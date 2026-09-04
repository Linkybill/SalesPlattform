using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using IdentityPlatform.Shared.Authorization;
using IdentityPlatform.Shared.Database;
using IdentityPlatform.Shared.Jobs;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Services;

/// <summary>
/// Creates the first business projection for the SalesPlattform: a prioritized
/// worklist from the already synchronized CRM data.
/// </summary>
public sealed class WorklistService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    OwnerMappingService ownerMappings,
    SalesApplicationSettingsService applicationSettings,
    SalesNotificationOutboxService notificationOutbox)
{
    // The platform factory opens the tenant-bound database session per request.
    // The service is scoped, so the active context only lives for one operation.
    private SalesPlattformDbContext? activeContext;
    private SalesPlattformDbContext db => activeContext
        ?? throw new InvalidOperationException("Für die Arbeitsliste ist keine Datenbank-Session geöffnet.");
    private static readonly string[] RuleCodes = [
        "R-01", "R-02", "R-03", "R-04", "R-05", "R-06", "R-07", "R-08", "R-09", "R-10",
        "R-11", "R-12", "R-13", "R-14", "R-15", "R-16", "R-17", "R-18"];
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    /// <summary>
    /// Evaluates the worklist immediately after a CRM synchronization. A full
    /// import evaluates the complete projection; an incremental import first
    /// resolves the changed source links and follows only the affected
    /// business relations.
    /// </summary>
    public async Task<WorklistEvaluationResult> EvaluateAfterSyncAsync(
        Guid tenantId,
        string mode,
        IReadOnlyCollection<CrmSynchronizationChange> changes,
        string? requestedBy,
        IPlatformJobProgressReporter? progressReporter = null,
        IPlatformJobLogger? jobLogger = null,
        CancellationToken cancellationToken = default)
    {
        await RefreshGate.WaitAsync(cancellationToken);
        try
        {
            await using var session = await dbFactory.OpenAsync(cancellationToken);
            activeContext = session.Context;
            var actor = new ActorInfo(requestedBy, requestedBy);
            var full = string.Equals(mode, CrmSyncModes.Full, StringComparison.OrdinalIgnoreCase);
            var evaluationProgress = new RuleEvaluationProgress(
                progressReporter,
                jobLogger);
            await evaluationProgress.ReportAsync(
                0,
                null,
                "Regelbewertung wird vorbereitet; betroffene Vorgänge werden ermittelt.",
                details: new
                {
                    phase = "rule-evaluation",
                    mode,
                    fullEvaluation = full,
                    changedRecords = changes.Count
                },
                force: true,
                cancellationToken);
            var scope = full
                ? await BuildFullEvaluationScopeAsync(changes, cancellationToken)
                : await BuildEvaluationScopeAsync(changes, cancellationToken);
            await ReplaceDeletedCrmTaskOccurrencesAsync(changes, actor, cancellationToken);
            if (scope.IsEmpty && !scope.IsFullEvaluation)
            {
                await evaluationProgress.ReportAsync(
                    0,
                    0,
                    "Regelbewertung abgeschlossen; für die Änderungen waren keine Regeln betroffen.",
                    details: new
                    {
                        phase = "rule-evaluation",
                        mode,
                        fullEvaluation = false,
                        changedRecords = changes.Count,
                        evaluated = 0,
                        remaining = 0
                    },
                    force: true,
                    cancellationToken);
                return new WorklistEvaluationResult(0, 0, 0, false);
            }

            return await RefreshCoreAsync(
                tenantId,
                actor,
                scope,
                full ? "crm-full-sync" : "crm-incremental-sync",
                evaluationProgress,
                cancellationToken);
        }
        finally
        {
            activeContext = null;
            RefreshGate.Release();
        }
    }

    public async Task<WorklistResponse> GetAsync(
        ClaimsPrincipal user,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        activeContext = session.Context;
        try
        {
            if (refresh)
                await RefreshAsync(user, cancellationToken);

            var teamView = IsSalesManager(user);
            var ownerId = teamView ? null : await ResolveOwnerIdAsync(user, cancellationToken);
            var ownerMatched = teamView || ownerId.HasValue;
            var query = OpenItemsQuery();
            if (!teamView)
            {
                query = query.Where(item => ownerId.HasValue
                    ? item.OwnerId == ownerId || item.OwnerId == null
                    : item.OwnerId == null);
            }

            var items = await query
                .OrderByDescending(item => item.PriorityScore)
                .ThenBy(item => item.DueAt)
                .ThenBy(item => item.CreatedAt)
                .Take(250)
                .ToArrayAsync(cancellationToken);

            var externalUrls = await ResolveExternalUrlsAsync(items, cancellationToken);
            var rules = await GetRuleNavigationAsync(items, cancellationToken);

            var latestRefresh = await db.SalesRuleRuns
                .AsNoTracking()
                .Where(run => run.Status == RuleRunStatuses.Succeeded)
                .OrderByDescending(run => run.FinishedAt ?? run.StartedAt)
                .Select(run => (DateTimeOffset?)(run.FinishedAt ?? run.StartedAt))
                .FirstOrDefaultAsync(cancellationToken);

            return new WorklistResponse(
                DateTimeOffset.UtcNow,
                latestRefresh,
                ownerMatched,
                teamView,
                rules,
                items.Select(item => ToDto(item, externalUrls)).ToArray());
        }
        finally
        {
            activeContext = null;
        }
    }

    public async Task<WorklistItemDto?> SnoozeAsync(
        Guid workItemId,
        SnoozeWorklistItemRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        activeContext = session.Context;
        try
        {
            var until = request.Tomorrow
                ? await TomorrowAtStartAsync(cancellationToken)
                : (request.Until
                    ?? throw new ArgumentException("Für das Zurückstellen muss ein Zeitpunkt angegeben werden.", nameof(request)));
            if (until <= now || until > now.AddDays(90))
                throw new ArgumentOutOfRangeException(nameof(until), "Ein Vorgang kann maximal 90 Tage zurückgestellt werden.");

            var item = await db.SalesWorkItems
                .Include(candidate => candidate.Owner)
                .Include(candidate => candidate.Relations)
                .SingleOrDefaultAsync(candidate => candidate.Id == workItemId, cancellationToken);
            if (item is null || !await CanAccessAsync(item, user, cancellationToken))
                return null;

            var actor = Actor(user);
            var before = SerializeState(item);
            var successorId = Guid.NewGuid();
            var nowChainId = item.WorkItemChainId == Guid.Empty ? item.Id : item.WorkItemChainId;
            item.Status = WorkItemStatuses.Closed;
            item.ClosureReason = "deferred";
            item.CompletedAt = now;
            item.CompletedBy = actor.Subject;
            item.AvailableFrom = null;
            item.SnoozedUntil = null;
            item.UpdatedAt = now;

            var successor = new SalesWorkItem
            {
                Id = successorId,
                TenantId = item.TenantId,
                WorkItemType = item.WorkItemType,
                Status = WorkItemStatuses.Scheduled,
                Title = item.Title,
                Reason = item.Reason,
                OwnerId = item.OwnerId,
                DueAt = until,
                AvailableFrom = until,
                PriorityScore = item.PriorityScore,
                PriorityCalculatedAt = item.PriorityCalculatedAt,
                SourceRuleCode = item.SourceRuleCode,
                SourceRuleRunId = item.SourceRuleRunId,
                RequiresApproval = item.RequiresApproval,
                WorkItemChainId = nowChainId,
                PreviousWorkItemId = item.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            foreach (var relation in item.Relations)
            {
                successor.Relations.Add(new SalesWorkItemRelation
                {
                    Id = Guid.NewGuid(),
                    TenantId = item.TenantId,
                    WorkItemId = successorId,
                    TargetType = relation.TargetType,
                    TargetId = relation.TargetId,
                    RelationRole = relation.RelationRole
                });
            }

            db.SalesWorkItems.Add(successor);
            AddEvent(item, "deferred", new { action = "defer", until, successorWorkItemId = successorId }, actor.Subject, now);
            AddEvent(successor, "scheduled", new { action = "successor-created", predecessorWorkItemId = item.Id, availableFrom = until }, actor.Subject, now);
            AddAudit(actor, "work-item.deferred", item, before, SerializeState(item), now);
            await db.SaveChangesAsync(cancellationToken);
            return ToDto(item);
        }
        finally
        {
            activeContext = null;
        }
    }

    private IQueryable<SalesWorkItem> OpenItemsQuery()
        => db.SalesWorkItems
            .AsNoTracking()
            .Include(item => item.Owner)
            .Include(item => item.Relations)
            .Where(item => item.Status == WorkItemStatuses.Open
                || (item.Status == WorkItemStatuses.Scheduled
                    && (item.AvailableFrom ?? item.SnoozedUntil) <= DateTimeOffset.UtcNow)
                || (item.Status == WorkItemStatuses.Snoozed
                    && (item.AvailableFrom ?? item.SnoozedUntil) <= DateTimeOffset.UtcNow));

    private async Task<DateTimeOffset> TomorrowAtStartAsync(CancellationToken cancellationToken)
    {
        var timeZoneId = await db.SalesWorkCalendars
            .AsNoTracking()
            .Where(calendar => calendar.IsDefault && calendar.IsActive)
            .OrderBy(calendar => calendar.Name)
            .Select(calendar => calendar.TimeZone)
            .FirstOrDefaultAsync(cancellationToken)
            ?? "Europe/Berlin";

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        var tomorrowAtStart = DateTime.SpecifyKind(
            localNow.Date.AddDays(1).AddHours(9),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(tomorrowAtStart, timeZone));
    }

    private async Task RefreshAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RefreshGate.WaitAsync(cancellationToken);
        try
        {
            await RefreshCoreAsync(
                TenantId(user),
                Actor(user),
                scope: null,
                "worklist-refresh",
                evaluationProgress: RuleEvaluationProgress.Disabled,
                cancellationToken);
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    private async Task ReplaceDeletedCrmTaskOccurrencesAsync(
        IReadOnlyCollection<CrmSynchronizationChange> changes,
        ActorInfo actor,
        CancellationToken cancellationToken)
    {
        var deletedActivityChanges = changes
            .Where(change => change.EntityType == CrmEntityTypes.Activity
                && string.Equals(change.ChangeKind, "deleted", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (deletedActivityChanges.Length == 0)
            return;

        var providerKeys = deletedActivityChanges.Select(change => change.ProviderKey).Distinct().ToArray();
        var connectionKeys = deletedActivityChanges.Select(change => change.ConnectionKey).Distinct().ToArray();
        var externalIds = deletedActivityChanges.Select(change => change.ExternalId).Distinct().ToArray();
        var deletedKeys = deletedActivityChanges
            .Select(change => (change.ProviderKey, change.ConnectionKey, change.EntityType, change.ExternalId))
            .ToHashSet();

        var deletedLinks = await db.IntegrationEntityLinks
            .AsNoTracking()
            .Where(link => providerKeys.Contains(link.ProviderKey)
                && connectionKeys.Contains(link.ConnectionKey)
                && link.EntityType == CrmEntityTypes.Activity
                && link.SourceDeletedAt != null
                && link.WorkItemId.HasValue
                && externalIds.Contains(link.ExternalId))
            .ToArrayAsync(cancellationToken);
        var workItemIds = deletedLinks
            .Where(link => deletedKeys.Contains((
                link.ProviderKey,
                link.ConnectionKey,
                link.EntityType,
                link.ExternalId)))
            .Select(link => link.WorkItemId!.Value)
            .Distinct()
            .ToArray();
        if (workItemIds.Length == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var items = await db.SalesWorkItems
            .Include(item => item.Relations)
            .Where(item => workItemIds.Contains(item.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var item in items)
        {
            if (!IsActiveStatus(item.Status))
                continue;

            var availableFrom = item.AvailableFrom ?? item.SnoozedUntil;
            var successorId = Guid.NewGuid();
            var chainId = item.WorkItemChainId == Guid.Empty ? item.Id : item.WorkItemChainId;
            item.Status = WorkItemStatuses.Closed;
            item.ClosureReason = "crm-task-deleted";
            item.CompletedAt = now;
            item.CompletedBy = actor.Subject;
            item.AvailableFrom = null;
            item.SnoozedUntil = null;
            item.UpdatedAt = now;

            var successor = new SalesWorkItem
            {
                Id = successorId,
                TenantId = item.TenantId,
                WorkItemType = item.WorkItemType,
                Status = availableFrom > now ? WorkItemStatuses.Scheduled : WorkItemStatuses.Open,
                Title = item.Title,
                Reason = item.Reason,
                OwnerId = item.OwnerId,
                DueAt = item.DueAt,
                AvailableFrom = availableFrom > now ? availableFrom : null,
                PriorityScore = item.PriorityScore,
                PriorityCalculatedAt = item.PriorityCalculatedAt,
                SourceRuleCode = item.SourceRuleCode,
                SourceRuleRunId = item.SourceRuleRunId,
                RequiresApproval = item.RequiresApproval,
                WorkItemChainId = chainId,
                PreviousWorkItemId = item.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            foreach (var relation in item.Relations)
            {
                successor.Relations.Add(new SalesWorkItemRelation
                {
                    Id = Guid.NewGuid(),
                    TenantId = item.TenantId,
                    WorkItemId = successorId,
                    TargetType = relation.TargetType,
                    TargetId = relation.TargetId,
                    RelationRole = relation.RelationRole
                });
            }

            db.SalesWorkItems.Add(successor);
            AddEvent(
                item,
                "crm-task-deleted",
                new { reason = "crm-task-deleted", successorWorkItemId = successorId },
                actor.Subject,
                now);
            AddEvent(
                successor,
                "crm-task-replacement-pending",
                new { predecessorWorkItemId = item.Id, availableFrom = successor.AvailableFrom },
                actor.Subject,
                now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<WorklistEvaluationResult> RefreshCoreAsync(
        Guid tenantId,
        ActorInfo actor,
        EvaluationScope? scope,
        string triggerType,
        RuleEvaluationProgress evaluationProgress,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var ruleConfiguration = await applicationSettings.GetRuleConfigurationAsync(
            tenantId,
            actor.Subject,
            cancellationToken);
        var ruleDefinitions = await EnsureRuleDefinitionsAsync(tenantId, actor, now, cancellationToken);
        var managementRecipients = await notificationOutbox.GetManagementRecipientsAsync(
            tenantId,
            actor.Subject,
            cancellationToken);
        var knownNotificationKeys = await db.SalesNotifications
            .AsNoTracking()
            .Select(notification => notification.NotificationKey)
            .ToHashSetAsync(cancellationToken);
        var candidates = (await FindCandidatesAsync(now, scope, ruleConfiguration, cancellationToken)).ToArray();
        var existing = await db.SalesWorkItems
            .Include(item => item.Relations)
            .Where(item => item.SourceRuleCode != null && RuleCodes.Contains(item.SourceRuleCode))
            .ToArrayAsync(cancellationToken);
        if (scope is not null && !scope.IsFullEvaluation)
            existing = existing.Where(scope.Matches).ToArray();
        var activeExisting = existing
            .Where(item => item.Status is WorkItemStatuses.Open
                or WorkItemStatuses.Scheduled
                or WorkItemStatuses.Snoozed)
            .ToArray();

        await evaluationProgress.ReportAsync(
            0,
            candidates.Length + activeExisting.Length,
            candidates.Length == 0 && activeExisting.Length == 0
                ? "Regelbewertung gestartet; es gibt keine zu prüfenden Vorgänge."
                : $"Regelbewertung gestartet: {candidates.Length:N0} Regeltreffer und {activeExisting.Length:N0} bestehende Vorgänge werden geprüft.",
            details: new
            {
                phase = "rule-evaluation",
                mode = triggerType,
                fullEvaluation = scope is null || scope.IsFullEvaluation,
                candidates = candidates.Length,
                existing = activeExisting.Length,
                evaluated = 0,
                remaining = candidates.Length + activeExisting.Length
            },
            force: true,
            cancellationToken);

        var ruleRun = new SalesRuleRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TriggerType = triggerType,
            Status = RuleRunStatuses.Running,
            StartedAt = now,
            RuleSetVersion = 1
        };
        db.SalesRuleRuns.Add(ruleRun);

        var createdCount = 0;
        var resolvedCount = 0;
        var candidateIds = new HashSet<Guid>();
        // The database identifies an evaluation by rule and target within a
        // rule run. Multiple rules may therefore match the same CRM entity,
        // while an accidental duplicate candidate for one rule is suppressed.
        var evaluationKeys = new HashSet<(string RuleCode, string TargetType, Guid TargetId)>();
        var ownersById = await db.SalesOwners
            .AsNoTracking()
            .ToDictionaryAsync(owner => owner.Id, cancellationToken);
        for (var candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            var candidate = candidates[candidateIndex];
            var stableId = candidate.Id(tenantId);
            var chainId = existing
                .FirstOrDefault(existingItem => existingItem.Id == stableId)
                ?.WorkItemChainId ?? stableId;
            var item = existing
                .Where(existingItem => existingItem.WorkItemChainId == chainId
                    && IsActiveStatus(existingItem.Status))
                .OrderByDescending(existingItem => existingItem.CreatedAt)
                .FirstOrDefault();
            if (item is null)
            {
                var id = existing.Any(existingItem => existingItem.Id == stableId)
                    ? Guid.NewGuid()
                    : stableId;
                item = new SalesWorkItem
                {
                    Id = id,
                    TenantId = tenantId,
                    WorkItemType = candidate.WorkItemType,
                    Status = WorkItemStatuses.Open,
                    Title = candidate.Title,
                    Reason = candidate.Reason,
                    OwnerId = candidate.OwnerId,
                    DueAt = candidate.DueAt,
                    AvailableFrom = null,
                    PriorityScore = CalculatePriority(candidate),
                    PriorityCalculatedAt = now,
                    SourceRuleCode = candidate.RuleCode,
                    SourceRuleRunId = ruleRun.Id,
                    RequiresApproval = candidate.RequiresApproval,
                    WorkItemChainId = chainId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                item.Relations.Add(new SalesWorkItemRelation
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    WorkItemId = id,
                    TargetType = candidate.TargetType,
                    TargetId = candidate.TargetId,
                    RelationRole = "primary"
                });
                db.SalesWorkItems.Add(item);
                existing = [.. existing, item];
                createdCount++;
            }
            else if (IsActiveStatus(item.Status))
            {
                var previousStatus = item.Status;
                var previousWorkItemType = item.WorkItemType;
                var previousTitle = item.Title;
                var previousReason = item.Reason;
                var previousOwnerId = item.OwnerId;
                var previousDueAt = item.DueAt;
                var previousAvailableFrom = item.AvailableFrom;
                var previousSnoozedUntil = item.SnoozedUntil;
                var previousRequiresApproval = item.RequiresApproval;
                var keepScheduled = item.Status == WorkItemStatuses.Scheduled
                    && item.AvailableFrom.HasValue
                    && item.AvailableFrom > now;
                var keepLegacySnooze = item.Status == WorkItemStatuses.Snoozed
                    && (item.AvailableFrom ?? item.SnoozedUntil) > now;
                if (!keepScheduled && !keepLegacySnooze)
                {
                    item.Status = WorkItemStatuses.Open;
                    item.AvailableFrom ??= item.SnoozedUntil;
                    item.SnoozedUntil = null;
                }

                item.WorkItemType = candidate.WorkItemType;
                item.Title = candidate.Title;
                item.Reason = candidate.Reason;
                item.OwnerId = candidate.OwnerId;
                if (item.PreviousWorkItemId is null)
                    item.DueAt = candidate.DueAt;
                item.PriorityScore = CalculatePriority(candidate);
                item.PriorityCalculatedAt = now;
                item.SourceRuleRunId = ruleRun.Id;
                item.RequiresApproval = candidate.RequiresApproval;

                // UpdatedAt drives the CRM task mirror. A rule run also
                // recalculates priority and records its run id, but those
                // internal changes must not cause a remote CRM PUT for every
                // active work item on every incremental/full evaluation.
                if (previousStatus != item.Status
                    || !string.Equals(previousWorkItemType, item.WorkItemType, StringComparison.Ordinal)
                    || !string.Equals(previousTitle, item.Title, StringComparison.Ordinal)
                    || !string.Equals(previousReason, item.Reason, StringComparison.Ordinal)
                    || previousOwnerId != item.OwnerId
                    || previousDueAt != item.DueAt
                    || previousAvailableFrom != item.AvailableFrom
                    || previousSnoozedUntil != item.SnoozedUntil
                    || previousRequiresApproval != item.RequiresApproval)
                {
                    item.UpdatedAt = now;
                }

                if (!item.Relations.Any(relation => relation.TargetType == candidate.TargetType
                    && relation.TargetId == candidate.TargetId))
                {
                    item.Relations.Add(new SalesWorkItemRelation
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        WorkItemId = item.Id,
                        TargetType = candidate.TargetType,
                        TargetId = candidate.TargetId,
                        RelationRole = "primary"
                    });
                }
            }

            candidateIds.Add(item.Id);

            notificationOutbox.EnqueueRuleNotification(
                db,
                item,
                new WorkItemNotification(
                    candidate.RuleCode,
                    candidate.TargetType,
                    candidate.TargetId,
                    candidate.RequiresApproval),
                candidate.OwnerId is { } ownerId && ownersById.TryGetValue(ownerId, out var owner)
                    ? owner
                    : null,
                managementRecipients,
                knownNotificationKeys,
                now);

            // Persist one evaluation for each rule/target combination. This
            // keeps the rule navigation and its counts complete when several
            // rules match the same CRM entity in one run.
            if (evaluationKeys.Add((candidate.RuleCode, candidate.TargetType, candidate.TargetId)))
            {
                db.SalesRuleEvaluations.Add(new SalesRuleEvaluation
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    RuleRunId = ruleRun.Id,
                    RuleDefinitionId = ruleDefinitions[candidate.RuleCode].Id,
                    TargetType = candidate.TargetType,
                    TargetId = candidate.TargetId,
                    Outcome = "matched",
                    WorkItemId = item.Id,
                    ExplanationJson = JsonSerializer.Serialize(new
                    {
                        candidate.WorkItemType,
                        candidate.DueAt,
                        candidate.Value,
                        candidate.Reason
                    }),
                    EvaluatedAt = now
                });
            }

            await evaluationProgress.ReportAsync(
                candidateIndex + 1,
                candidates.Length + activeExisting.Length,
                $"Regel {candidate.RuleCode} wird geprüft; {Math.Max(0, candidates.Length + activeExisting.Length - candidateIndex - 1):N0} Vorgänge verbleiben.",
                details: new
                {
                    phase = "rule-evaluation",
                    mode = triggerType,
                    ruleCode = candidate.RuleCode,
                    targetType = candidate.TargetType,
                    targetId = candidate.TargetId,
                    evaluated = candidateIndex + 1,
                    total = candidates.Length + activeExisting.Length,
                    remaining = Math.Max(0, candidates.Length + activeExisting.Length - candidateIndex - 1)
                },
                force: candidateIndex == 0 || candidateIndex == candidates.Length - 1,
                cancellationToken);
        }

        for (var existingIndex = 0; existingIndex < activeExisting.Length; existingIndex++)
        {
            var item = activeExisting[existingIndex];
            var remainsMatched = candidateIds.Contains(item.Id);
            if (!remainsMatched)
            {

            item.Status = WorkItemStatuses.Resolved;
            item.CompletedAt = null;
            item.CompletedBy = null;
            var targetDeleted = scope?.IsDeletedTarget(item) == true;
            item.ClosureReason = targetDeleted
                ? "target-deleted-in-crm"
                : "rule-no-longer-matches";
            item.AvailableFrom = null;
            item.SnoozedUntil = null;
            item.UpdatedAt = now;
            AddEvent(
                item,
                "resolved",
                new
                {
                    reason = item.ClosureReason,
                    targetDeleted
                },
                actor.Subject,
                now);
            resolvedCount++;
            }

            await evaluationProgress.ReportAsync(
                candidates.Length + existingIndex + 1,
                candidates.Length + activeExisting.Length,
                $"Bestehende Vorgänge werden abgeglichen; {Math.Max(0, activeExisting.Length - existingIndex - 1):N0} Vorgänge verbleiben.",
                details: new
                {
                    phase = "rule-evaluation",
                    mode = triggerType,
                    action = remainsMatched ? "keep-matching" : "resolve-non-matching",
                    evaluated = candidates.Length + existingIndex + 1,
                    total = candidates.Length + activeExisting.Length,
                    remaining = Math.Max(0, activeExisting.Length - existingIndex - 1),
                    resolved = resolvedCount
                },
                force: existingIndex == activeExisting.Length - 1,
                cancellationToken);
        }

        await evaluationProgress.ReportAsync(
            candidates.Length + activeExisting.Length,
            candidates.Length + activeExisting.Length,
            "Regelergebnisse werden gespeichert; die Arbeitsliste wird aktualisiert.",
            details: new
            {
                phase = "rule-evaluation",
                mode = triggerType,
                action = "persist",
                evaluated = candidates.Length + activeExisting.Length,
                total = candidates.Length + activeExisting.Length,
                remaining = 0
            },
            force: true,
            cancellationToken);

        ruleRun.Status = RuleRunStatuses.Succeeded;
        ruleRun.FinishedAt = DateTimeOffset.UtcNow;
        ruleRun.EvaluatedCount = candidates.Length;
        ruleRun.CreatedCount = createdCount;
        await db.SaveChangesAsync(cancellationToken);
        await evaluationProgress.ReportAsync(
            candidates.Length + activeExisting.Length,
            candidates.Length + activeExisting.Length,
            $"Regelbewertung abgeschlossen: {candidates.Length:N0} Treffer, {createdCount:N0} neue Vorgänge, {resolvedCount:N0} Vorgänge aufgelöst.",
            details: new
            {
                phase = "rule-evaluation",
                mode = triggerType,
                fullEvaluation = scope is null || scope.IsFullEvaluation,
                evaluated = candidates.Length + activeExisting.Length,
                total = candidates.Length + activeExisting.Length,
                remaining = 0,
                matched = candidates.Length,
                created = createdCount,
                resolved = resolvedCount
            },
            force: true,
            cancellationToken);
        return new WorklistEvaluationResult(
            candidates.Length,
            createdCount,
            resolvedCount,
            scope is null || scope.IsFullEvaluation);
    }

    private async Task<Dictionary<string, SalesRuleDefinition>> EnsureRuleDefinitionsAsync(
        Guid tenantId,
        ActorInfo actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = await db.SalesRuleDefinitions
            .Where(rule => rule.Version == 1 && RuleCodes.Contains(rule.Code))
            .ToDictionaryAsync(rule => rule.Code, cancellationToken);

        foreach (var definition in DefaultRules)
        {
            if (definitions.ContainsKey(definition.Code))
                continue;

            var entity = new SalesRuleDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = definition.Code,
                Name = definition.Name,
                Description = definition.Description,
                IsEnabled = true,
                AutomationMode = "worklist",
                Version = 1,
                ParametersJson = JsonSerializer.Serialize(definition.Parameters),
                UpdatedBy = actor.Subject,
                UpdatedAt = now
            };
            db.SalesRuleDefinitions.Add(entity);
            definitions.Add(entity.Code, entity);
        }

        return definitions;
    }

    private async Task<IReadOnlyCollection<WorklistCandidate>> FindCandidatesAsync(
        DateTimeOffset now,
        EvaluationScope? scope,
        SalesRuleConfiguration ruleConfiguration,
        CancellationToken cancellationToken)
    {
        IQueryable<SalesCustomer> customersQuery = db.SalesCustomers.AsNoTracking();
        IQueryable<SalesLead> leadsQuery = db.SalesLeads.AsNoTracking();
        IQueryable<SalesDeal> dealsQuery = db.SalesDeals.AsNoTracking().Include(deal => deal.PipelineStage);
        IQueryable<SalesContract> contractsQuery = db.SalesContracts.AsNoTracking();
        IQueryable<SalesDeal> paceDealsQuery = db.SalesDeals.AsNoTracking();
        IQueryable<SalesProduct> productsQuery = db.SalesProducts.AsNoTracking().Include(product => product.Category);
        IQueryable<SalesAppointment> appointmentsQuery = db.SalesAppointments.AsNoTracking().Include(appointment => appointment.Relations);
        IQueryable<SalesServiceCase> serviceCasesQuery = db.SalesServiceCases.AsNoTracking();
        IQueryable<SalesOffer> offersQuery = db.SalesOffers.AsNoTracking();
        IQueryable<SalesOrder> ordersQuery = db.SalesOrders.AsNoTracking();
        IQueryable<SalesInvoice> invoicesQuery = db.SalesInvoices.AsNoTracking();
        IQueryable<SalesOwnerChangeRequest> ownerChangesQuery = db.SalesOwnerChangeRequests.AsNoTracking();
        IQueryable<SalesOwner> ownersQuery = db.SalesOwners.AsNoTracking();
        IQueryable<SalesTarget> targetsQuery = db.SalesTargets.AsNoTracking();
        if (scope is not null && !scope.IsFullEvaluation)
        {
            var customerIds = scope.Ids(CrmEntityTypes.Customer);
            customersQuery = scope.AllCustomersForCrossSell
                ? customersQuery
                : customersQuery.Where(customer => customerIds.Contains(customer.Id));

            var leadIds = scope.Ids(CrmEntityTypes.Lead);
            leadsQuery = leadsQuery.Where(lead => leadIds.Contains(lead.Id));

            var dealIds = scope.Ids(CrmEntityTypes.Deal);
            dealsQuery = dealsQuery.Where(deal => dealIds.Contains(deal.Id));

            var contractIds = scope.Ids(CrmEntityTypes.Contract);
            contractsQuery = contractsQuery.Where(contract => contractIds.Contains(contract.Id));

            if (!scope.AllowsRule("R-10"))
                productsQuery = productsQuery.Where(_ => false);

            var appointmentIds = scope.Ids(CrmEntityTypes.Appointment);
            appointmentsQuery = appointmentsQuery.Where(appointment => appointmentIds.Contains(appointment.Id));

            var serviceCaseIds = scope.Ids(CrmEntityTypes.ServiceCase);
            serviceCasesQuery = serviceCasesQuery.Where(serviceCase => serviceCaseIds.Contains(serviceCase.Id));

            var offerIds = scope.Ids(CrmEntityTypes.Offer);
            offersQuery = offersQuery.Where(offer => offerIds.Contains(offer.Id));

            var orderIds = scope.Ids(CrmEntityTypes.Order);
            ordersQuery = ordersQuery.Where(order => orderIds.Contains(order.Id));

            var invoiceIds = scope.Ids(CrmEntityTypes.Invoice);
            invoicesQuery = invoicesQuery.Where(invoice => invoiceIds.Contains(invoice.Id));

            // Owner-change requests are platform workflow records, not CRM
            // source records. They are currently refreshed by a full/manual
            // evaluation; an incremental CRM change must not scan them.
            ownerChangesQuery = scope.AllowsRule("R-08")
                ? ownerChangesQuery
                : ownerChangesQuery.Where(_ => false);

            var paceOwnerIds = scope.OwnerIds.ToArray();
            targetsQuery = paceOwnerIds.Length == 0
                ? targetsQuery.Where(_ => false)
                : targetsQuery.Where(target => paceOwnerIds.Contains(target.OwnerId));
            paceDealsQuery = paceOwnerIds.Length == 0
                ? paceDealsQuery.Where(_ => false)
                : paceDealsQuery.Where(deal => deal.OwnerId.HasValue && paceOwnerIds.Contains(deal.OwnerId.Value));
        }

        var customers = await customersQuery.ToArrayAsync(cancellationToken);
        var leads = await leadsQuery.ToArrayAsync(cancellationToken);
        var deals = await dealsQuery.ToArrayAsync(cancellationToken);
        var paceDeals = (scope is null || scope.AllowsRule("R-11"))
            ? await paceDealsQuery.ToArrayAsync(cancellationToken)
            : Array.Empty<SalesDeal>();
        var contracts = await contractsQuery.ToArrayAsync(cancellationToken);
        var products = await productsQuery.ToArrayAsync(cancellationToken);
        var appointments = await appointmentsQuery.ToArrayAsync(cancellationToken);
        var serviceCases = await serviceCasesQuery.ToArrayAsync(cancellationToken);
        var offers = await offersQuery.ToArrayAsync(cancellationToken);
        var orders = await ordersQuery.ToArrayAsync(cancellationToken);
        var invoices = await invoicesQuery.ToArrayAsync(cancellationToken);
        var ownerChanges = await ownerChangesQuery.ToArrayAsync(cancellationToken);
        var owners = await ownersQuery.ToArrayAsync(cancellationToken);
        var targets = await targetsQuery.ToArrayAsync(cancellationToken);
        var workingCalendar = (scope is null || scope.AllowsRule("R-09"))
            ? await LoadWorkingCalendarAsync(cancellationToken)
            : null;

        var customerNames = customers.ToDictionary(customer => customer.Id, customer => customer.Name);
        var dealNames = deals.ToDictionary(deal => deal.Id, deal => deal.Name);
        var leadNames = leads.ToDictionary(lead => lead.Id, lead => lead.Name);
        var ownerNames = owners.ToDictionary(owner => owner.Id, owner => owner.DisplayName);
        var productCategories = products
            .Where(product => product.IsActive && product.CategoryId.HasValue && product.Category is { IsActive: true })
            .GroupBy(product => product.CategoryId!.Value)
            .ToDictionary(group => group.Key, group => group.First().Category!.Name);
        var candidates = new List<WorklistCandidate>();

        if (scope is null || scope.AllowsRule("R-05"))
        foreach (var deal in deals.Where(deal => deal.IsActive
            && IsOpenStatus(deal.Status)
            && !(deal.PipelineStage?.IsTerminal ?? false)))
        {
            var lastActivity = deal.LastActivityAt ?? deal.SourceCreatedAt;
            if (!lastActivity.HasValue || lastActivity > now.AddDays(-ruleConfiguration.DealInactiveDays))
                continue;

            var dueAt = lastActivity.Value.AddDays(ruleConfiguration.DealInactiveDays);
            var cockpitEscalation = lastActivity.Value <= now.AddDays(-ruleConfiguration.DealCockpitEscalationDays);
            var title = deal.CustomerId is { } customerId && customerNames.TryGetValue(customerId, out var customerName)
                ? $"{deal.Name} · {customerName}"
                : deal.Name;
            candidates.Add(new WorklistCandidate(
                "deal-stalled", "R-05", "deal", deal.Id, deal.OwnerId, title,
                cockpitEscalation
                    ? $"Offener Deal seit {FormatAge(lastActivity.Value, now)} ohne dokumentierte Aktivität; Cockpit-Handlungspunkt ab {ruleConfiguration.DealCockpitEscalationDays} Tagen."
                    : $"Offener Deal seit {FormatAge(lastActivity.Value, now)} ohne dokumentierte Aktivität (Grenzwert: {ruleConfiguration.DealInactiveDays} Tage).",
                dueAt, deal.Amount, null, cockpitEscalation));
        }

        if (scope is null || scope.AllowsRule("R-14"))
        foreach (var deal in deals.Where(deal => deal.IsActive
            && IsLostStatus(deal.Status)
            && IsReactivationLossReason(deal.LossReason)))
        {
            var lostAt = deal.ClosingAt ?? deal.SourceModifiedAt ?? deal.SourceCreatedAt;
            if (!lostAt.HasValue || lostAt > now.AddDays(-ruleConfiguration.LostDealReactivationAgeDays))
                continue;

            candidates.Add(new WorklistCandidate(
                "deal-reactivation", "R-14", "deal", deal.Id, deal.OwnerId,
                $"Verlorenen Deal reaktivieren · {deal.Name}",
                $"Verlorener Deal seit {FormatAge(lostAt.Value, now)}; Verlustgrund „{deal.LossReason}“ (Timing/Budget). Reaktivierung beim früheren Besitzer prüfen.",
                lostAt.Value.AddDays(ruleConfiguration.LostDealReactivationAgeDays),
                deal.Amount,
                null,
                true));
        }

        if (scope is null || scope.AllowsRule("R-06"))
        foreach (var contract in contracts.Where(contract => contract.IsActive
            && contract.EndAt is { } endAt
            && endAt >= now
            && endAt <= now.AddDays(ruleConfiguration.ContractRenewalHorizonDays)
            && IsOpenStatus(contract.Status)))
        {
            var daysRemaining = Math.Max(0, (contract.EndAt!.Value - now).TotalDays);
            var critical = daysRemaining <= ruleConfiguration.ContractCriticalDays;
            var customerName = customerNames.GetValueOrDefault(contract.CustomerId) ?? "Unbekannter Kunde";
            candidates.Add(new WorklistCandidate(
                critical ? "contract-renewal-critical" : "contract-renewal", "R-06", "contract", contract.Id,
                contract.OwnerId, $"Vertrag verlängern · {customerName}",
                $"Vertragsende am {contract.EndAt.Value:dd.MM.yyyy}; noch {Math.Ceiling(daysRemaining):0} Tage.",
                contract.EndAt.Value, contract.RecurringAmount));
        }

        if (scope is null || scope.AllowsRule("R-07"))
        foreach (var customer in customers.Where(customer => customer.IsActive
            && (!customer.LastContactAt.HasValue || customer.LastContactAt <= now.AddDays(-ruleConfiguration.ContactInactiveDays))))
        {
            var dueAt = customer.LastContactAt?.AddDays(ruleConfiguration.ContactInactiveDays)
                ?? (customer.SourceCreatedAt ?? now).AddDays(ruleConfiguration.ContactInactiveDays);
            candidates.Add(new WorklistCandidate(
                "customer-stale", "R-07", "customer", customer.Id, customer.OwnerId,
                $"Kontakt aufnehmen · {customer.Name}",
                customer.LastContactAt.HasValue
                    ? $"Letzter Kontakt vor {FormatAge(customer.LastContactAt.Value, now)}."
                    : "Für den Kunden ist noch kein Kontakt dokumentiert.",
                dueAt, customer.LifetimeRevenue));
        }

        if (scope is null || scope.AllowsRule("R-07"))
        foreach (var lead in leads.Where(lead => lead.IsActive
            && IsOpenStatus(lead.Status)
            && (!lead.LastContactAt.HasValue
                || lead.LastContactAt <= now.AddDays(-ruleConfiguration.ContactInactiveDays))))
        {
            var lastContact = lead.LastContactAt ?? lead.SourceCreatedAt ?? now;
            candidates.Add(new WorklistCandidate(
                "lead-reactivation", "R-07", "lead", lead.Id, lead.OwnerId,
                $"Lead reaktivieren · {lead.Name}",
                lead.LastContactAt.HasValue
                    ? $"Letzter Kontakt vor {FormatAge(lead.LastContactAt.Value, now)}; Reaktivierung erforderlich."
                    : "Für den Lead ist noch kein Kontakt dokumentiert; Reaktivierung erforderlich.",
                lastContact.AddDays(ruleConfiguration.ContactInactiveDays), null));
        }

        if (scope is null || scope.AllowsRule("R-01") || scope.AllowsRule("R-02")
            || scope.AllowsRule("R-03") || scope.AllowsRule("R-04") || scope.AllowsRule("R-09"))
        foreach (var lead in leads.Where(lead => lead.IsActive
            && IsOpenStatus(lead.Status)
            && (lead.CallsSinceConversation > 0
                || (!lead.FirstActivityAt.HasValue
                    && !lead.LastContactAt.HasValue
                    && !lead.LastPhoneCallAt.HasValue
                    && lead.SourceCreatedAt is not null
                    && CalculateWorkingHours(lead.SourceCreatedAt.Value, now, workingCalendar)
                        >= ruleConfiguration.LeadFirstResponseWorkingHours))))
        {
            if (lead.CallsSinceConversation > ruleConfiguration.CallNotReachableAfterAttempts
                && (scope is null || scope.AllowsRule("R-04")))
            {
                candidates.Add(new WorklistCandidate(
                    "call-not-reachable", "R-04", "lead", lead.Id, lead.OwnerId,
                    $"Erreichbarkeit klären · {lead.Name}",
                    $"{lead.CallsSinceConversation} Anrufversuche seit dem letzten Gespräch ohne qualifiziertes Gespräch. Nicht erreichbar nur vorschlagen; CRM-Status bleibt unverändert.",
                    lead.LastPhoneCallAt ?? now, null, null, true));
            }
            else if (lead.CallsSinceConversation >= ruleConfiguration.CallLongRunnerMinAttempts
                && lead.CallsSinceConversation <= ruleConfiguration.CallLongRunnerMaxAttempts
                && (scope is null || scope.AllowsRule("R-03")))
            {
                candidates.Add(new WorklistCandidate(
                    "call-long-runner", "R-03", "lead", lead.Id, lead.OwnerId,
                    $"Langläufer bearbeiten · {lead.Name}",
                    $"{lead.CallsSinceConversation} Anrufversuche seit dem letzten Gespräch ohne qualifiziertes Gespräch; Wiedervorlage im {ruleConfiguration.CallLongRunnerIntervalDays}-Tage-Intervall.",
                    (lead.LastPhoneCallAt ?? now).AddDays(ruleConfiguration.CallLongRunnerIntervalDays), null));
            }
            else if (lead.CallsSinceConversation >= 1
                && lead.CallsSinceConversation <= ruleConfiguration.CallEmailFollowUpAttempts)
            {
                var isEmailAttempt = lead.CallsSinceConversation == ruleConfiguration.CallEmailFollowUpAttempts;
                var ruleCode = isEmailAttempt ? "R-02" : "R-01";
                if (scope is null || scope.AllowsRule(ruleCode))
                {
                    candidates.Add(new WorklistCandidate(
                        isEmailAttempt ? "call-email-follow-up" : "call-follow-up",
                        ruleCode,
                        "lead",
                        lead.Id,
                        lead.OwnerId,
                        isEmailAttempt
                            ? $"E-Mail-Folgeaktion prüfen · {lead.Name}"
                            : $"Erneut anrufen · {lead.Name}",
                        isEmailAttempt
                            ? $"Der {ruleConfiguration.CallEmailFollowUpAttempts}. Anrufversuch blieb ohne qualifiziertes Gespräch. E-Mail-Vorlage prüfen und Wiedervorlage setzen."
                            : $"Anrufversuch {lead.CallsSinceConversation} seit dem letzten Gespräch blieb ohne qualifiziertes Gespräch.",
                        (lead.LastPhoneCallAt ?? now).AddDays(isEmailAttempt
                            ? ruleConfiguration.CallEmailFollowUpIntervalDays
                            : ruleConfiguration.CallFollowUpIntervalDays),
                        null,
                        null,
                        isEmailAttempt));
                }
            }
            else if (scope is null || scope.AllowsRule("R-09"))
            {
                var leadCreatedAt = lead.SourceCreatedAt!.Value;
                var dueAt = lead.ResponseDueAt
                    ?? AddWorkingHours(leadCreatedAt, ruleConfiguration.LeadFirstResponseWorkingHours, workingCalendar);
                var escalated = CalculateWorkingHours(leadCreatedAt, now, workingCalendar)
                    >= ruleConfiguration.LeadEscalationWorkingHours;
                candidates.Add(new WorklistCandidate(
                    "lead-first-response", "R-09", "lead", lead.Id, lead.OwnerId,
                    $"Lead qualifizieren · {lead.Name}",
                    escalated
                        ? $"Neuer Lead seit {FormatAge(leadCreatedAt, now)} ohne dokumentierte Aktivität; nach {ruleConfiguration.LeadEscalationWorkingHours} Arbeitsstunden an die Vertriebsleitung eskalieren."
                        : $"Neuer Lead seit {FormatAge(leadCreatedAt, now)} ohne dokumentierte Aktivität.",
                    dueAt, null, null, escalated));
            }
        }

        if ((scope is null || scope.AllowsRule("R-10")) && productCategories.Count > 0)
        foreach (var customer in customers.Where(customer => customer.IsActive
            && customer.LifetimeRevenue >= ruleConfiguration.CrossSellingMinimumCustomerValue
            && productCategories.Count > 0))
        {
            var acquiredCategoryIds = deals
                .Where(deal => deal.CustomerId == customer.Id && IsWonStatus(deal.Status))
                .Join(products.Where(product => product.CategoryId.HasValue), deal => deal.ProductId,
                    product => (Guid?)product.Id, (_, product) => product.CategoryId!.Value)
                .Concat(contracts
                    .Where(contract => contract.CustomerId == customer.Id && contract.IsActive)
                    .Join(products.Where(product => product.CategoryId.HasValue), contract => contract.ProductId,
                        product => (Guid?)product.Id, (_, product) => product.CategoryId!.Value))
                .ToHashSet();

            foreach (var category in productCategories.Where(category => !acquiredCategoryIds.Contains(category.Key)))
            {
                candidates.Add(new WorklistCandidate(
                    "cross-sell", "R-10", "customer", customer.Id, customer.OwnerId,
                    $"Cross-Selling · {customer.Name} · {category.Value}",
                    $"Aktiver Kunde ohne Produkt aus der Kategorie „{category.Value}“.",
                    now, customer.LifetimeRevenue, $"customer:{customer.Id:D}:category:{category.Key:D}"));
            }
        }

        if (scope is null || scope.AllowsRule("R-13"))
        foreach (var customer in customers.Where(customer => customer.IsActive
            && customer.LifetimeRevenue is > 0
            && customer.LifetimeRevenue >= ruleConfiguration.AccountCareMinimumRevenue
            && (!customer.LastPhoneCallAt.HasValue
                || customer.LastPhoneCallAt <= now.AddDays(-ruleConfiguration.AccountCareInactiveDays))))
        {
            var lastPhoneCall = customer.LastPhoneCallAt ?? customer.SourceCreatedAt ?? now;
            candidates.Add(new WorklistCandidate(
                "account-care", "R-13", "customer", customer.Id, customer.OwnerId,
                $"Account Care · {customer.Name}",
                customer.LastPhoneCallAt.HasValue
                    ? $"Letztes Telefonat vor {FormatAge(customer.LastPhoneCallAt.Value, now)}; Umsatzhistorie vorhanden ({customer.LifetimeRevenue:0.##})."
                    : $"Noch kein Telefonat dokumentiert; Umsatzhistorie vorhanden ({customer.LifetimeRevenue:0.##}).",
                lastPhoneCall.AddDays(ruleConfiguration.AccountCareInactiveDays), customer.LifetimeRevenue));
        }

        if (scope is null || scope.AllowsRule("R-08"))
        foreach (var deal in deals.Where(deal => deal.IsActive
            && IsOpenStatus(deal.Status)
            && IsOwnerChangeStage(deal.PipelineStage)))
        {
            var hasPendingRequest = ownerChanges.Any(request => request.TargetType == CrmEntityTypes.Deal
                && request.TargetId == deal.Id
                && IsPendingOwnerChange(request.Status));
            if (hasPendingRequest)
                continue;

            var title = deal.CustomerId is { } customerId && customerNames.TryGetValue(customerId, out var customerName)
                ? $"Zuständigkeit klären · {customerName}"
                : $"Zuständigkeit klären · {deal.Name}";
            candidates.Add(new WorklistCandidate(
                "ownership-change", "R-08", CrmEntityTypes.Deal, deal.Id, deal.OwnerId,
                title,
                $"Der Deal befindet sich in der Stufe „{deal.PipelineStage!.Name}“. Alten Besitzer, Kontakt und Wert prüfen; die Vertriebsleitung entscheidet.",
                now,
                deal.Amount,
                null,
                true));
        }

        if (scope is null || scope.AllowsRule("R-11"))
        {
            var today = DateOnly.FromDateTime(now.UtcDateTime.Date);
            var fiscalYear = await db.SalesFiscalYears
                .AsNoTracking()
                .Where(year => !year.IsClosed && year.StartsAt <= today && year.EndsAt >= today)
                .OrderByDescending(year => year.StartsAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (fiscalYear is not null)
            {
                var fiscalTargets = targets
                    .Where(target => target.FiscalYearId == fiscalYear.Id
                        && IsRevenueTargetType(target.TargetType)
                        && target.ValidFrom <= today
                        && (!target.ValidTo.HasValue || target.ValidTo >= today))
                    .ToArray();
                var targetByOwner = fiscalTargets
                    .GroupBy(target => target.OwnerId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Any(target => target.TargetPeriodId is null)
                            ? group.Where(target => target.TargetPeriodId is null).Sum(target => target.TargetValue)
                            : group.Sum(target => target.TargetValue));
                var elapsedDays = Math.Clamp(today.DayNumber - fiscalYear.StartsAt.DayNumber, 0, fiscalYear.EndsAt.DayNumber - fiscalYear.StartsAt.DayNumber + 1);
                var totalDays = Math.Max(1, fiscalYear.EndsAt.DayNumber - fiscalYear.StartsAt.DayNumber + 1);
                var timeShare = elapsedDays * 100m / totalDays;
                var achievedByOwner = paceDeals
                    .Where(deal => deal.IsActive
                        && deal.OwnerId.HasValue
                        && IsWonStatus(deal.Status)
                        && deal.Amount.HasValue
                        && deal.ClosingAt is { } closingAt
                        && DateOnly.FromDateTime(closingAt.UtcDateTime.Date) >= fiscalYear.StartsAt
                        && DateOnly.FromDateTime(closingAt.UtcDateTime.Date) <= fiscalYear.EndsAt)
                    .GroupBy(deal => deal.OwnerId!.Value)
                    .ToDictionary(group => group.Key, group => group.Sum(deal => deal.Amount!.Value));

                foreach (var (ownerId, annualTarget) in targetByOwner.Where(item => item.Value > 0))
                {
                    var achieved = achievedByOwner.GetValueOrDefault(ownerId);
                    var attainment = achieved / annualTarget * 100m;
                    var pace = attainment - timeShare;
                    if (pace >= -ruleConfiguration.TargetPaceGapPoints)
                        continue;

                    var ownerName = ownerNames.GetValueOrDefault(ownerId) ?? "Unbekannter Mitarbeiter";
                    candidates.Add(new WorklistCandidate(
                        "target-pace-gap", "R-11", CrmEntityTypes.Owner, ownerId, ownerId,
                        $"Ziel-Pace prüfen · {ownerName}",
                        $"Zielerreichung {attainment:0.##}% liegt {Math.Abs(pace):0.##} Punkte unter dem Zeitanteil ({timeShare:0.##}%). Team-Flag und Benachrichtigung an die Vertriebsleitung.",
                        now,
                        annualTarget,
                        $"owner:{ownerId:D}:fiscal-year:{fiscalYear.Id:D}",
                        true));
                }
            }
        }

        if (scope is null || scope.AllowsRule("R-12"))
        foreach (var appointment in appointments.Where(appointment => appointment.IsActive
            && appointment.RescheduleCount >= ruleConfiguration.AppointmentRescheduleCount
            && IsOpenStatus(appointment.Status)))
        {
            var target = appointment.Relations.FirstOrDefault();
            var targetName = target is null
                ? null
                : target.TargetType switch
                {
                    "customer" => customerNames.GetValueOrDefault(target.TargetId),
                    "deal" => dealNames.GetValueOrDefault(target.TargetId),
                    "lead" => leadNames.GetValueOrDefault(target.TargetId),
                    _ => null
                };
            candidates.Add(new WorklistCandidate(
                "appointment-rescheduled", "R-12", "appointment", appointment.Id, appointment.OwnerId,
                $"Termin absichern · {appointment.Subject ?? targetName ?? "ohne Betreff"}",
                $"Der Termin wurde bereits {appointment.RescheduleCount}× verschoben.",
                appointment.StartsAt, null));
        }

        if (scope is null || scope.AllowsRule("R-15"))
        foreach (var serviceCase in serviceCases.Where(serviceCase => serviceCase.IsActive
            && IsOpenStatus(serviceCase.Status)
            && (IsUrgentPriority(serviceCase.Priority)
                || serviceCase.DueAt <= now
                || (serviceCase.DueAt is null
                    && serviceCase.OpenedAt is { } openedAt
                    && openedAt <= now.AddDays(-ruleConfiguration.ServiceCaseResponseDays)))) )
        {
            var dueAt = serviceCase.DueAt
                ?? serviceCase.OpenedAt?.AddDays(ruleConfiguration.ServiceCaseResponseDays)
                ?? serviceCase.SourceCreatedAt
                ?? now;
            var customerName = serviceCase.CustomerId is { } customerId
                ? customerNames.GetValueOrDefault(customerId)
                : null;
            var urgent = IsUrgentPriority(serviceCase.Priority) || dueAt <= now;
            candidates.Add(new WorklistCandidate(
                "service-case-overdue", "R-15", CrmEntityTypes.ServiceCase, serviceCase.Id, serviceCase.OwnerId,
                $"Servicefall bearbeiten · {serviceCase.Subject}",
                urgent
                    ? $"Servicefall {serviceCase.Priority.ToLowerInvariant()} / {serviceCase.Status}; Frist {FormatAge(dueAt, now)} überschritten oder heute fällig.{(customerName is null ? string.Empty : $" Kunde: {customerName}.")}"
                    : $"Servicefall seit {FormatAge(serviceCase.OpenedAt ?? serviceCase.SourceCreatedAt ?? now, now)} ohne dokumentierte Bearbeitung.",
                dueAt,
                null,
                null,
                urgent));
        }

        if (scope is null || scope.AllowsRule("R-16"))
        foreach (var offer in offers.Where(offer => offer.IsActive
            && IsOpenStatus(offer.Status)
            && offer.SentAt is { } sentAt
            && sentAt <= now.AddDays(-ruleConfiguration.OfferFollowUpDays)))
        {
            var customerName = offer.CustomerId is { } customerId
                ? customerNames.GetValueOrDefault(customerId)
                : null;
            candidates.Add(new WorklistCandidate(
                "offer-follow-up", "R-16", CrmEntityTypes.Offer, offer.Id, offer.OwnerId,
                $"Angebot nachfassen · {offer.Name}",
                $"Gesendetes Angebot seit {FormatAge(offer.SentAt!.Value, now)} ohne Entscheidung; Folgekontakt prüfen.{(customerName is null ? string.Empty : $" Kunde: {customerName}.")}",
                offer.SentAt.Value.AddDays(ruleConfiguration.OfferFollowUpDays),
                offer.Amount));
        }

        if (scope is null || scope.AllowsRule("R-17"))
        foreach (var order in orders.Where(order => order.IsActive
            && IsOpenStatus(order.Status)
            && order.DeliveredAt is null
            && order.PromisedAt is { } promisedAt
            && promisedAt.AddDays(ruleConfiguration.OrderDeliveryEscalationDays) <= now))
        {
            var customerName = order.CustomerId is { } customerId
                ? customerNames.GetValueOrDefault(customerId)
                : null;
            candidates.Add(new WorklistCandidate(
                "order-overdue", "R-17", CrmEntityTypes.Order, order.Id, order.OwnerId,
                $"Lieferverzug prüfen · {order.Name}",
                $"Zugesagter Liefertermin war am {order.PromisedAt!.Value:dd.MM.yyyy}; Auftrag ist noch nicht abgeschlossen.{(customerName is null ? string.Empty : $" Kunde: {customerName}.")}",
                order.PromisedAt.Value.AddDays(ruleConfiguration.OrderDeliveryEscalationDays),
                order.Amount,
                null,
                true));
        }

        if (scope is null || scope.AllowsRule("R-18"))
        foreach (var invoice in invoices.Where(invoice => invoice.IsActive
            && IsOpenInvoice(invoice.Status)
            && invoice.OpenAmount.GetValueOrDefault(invoice.Amount ?? 0m) > 0
            && invoice.DueAt is { } dueAt
            && dueAt.AddDays(ruleConfiguration.InvoiceOverdueGraceDays) <= now))
        {
            var customerName = invoice.CustomerId is { } customerId
                ? customerNames.GetValueOrDefault(customerId)
                : null;
            var openAmount = invoice.OpenAmount.GetValueOrDefault(invoice.Amount ?? 0m);
            candidates.Add(new WorklistCandidate(
                "invoice-overdue", "R-18", CrmEntityTypes.Invoice, invoice.Id, invoice.OwnerId,
                $"Überfällige Rechnung prüfen · {invoice.Name}",
                $"Rechnung seit {FormatAge(invoice.DueAt!.Value, now)} überfällig; offener Betrag {openAmount:0.##} {invoice.Currency ?? "EUR"}.{(customerName is null ? string.Empty : $" Kunde: {customerName}.")}",
                invoice.DueAt.Value.AddDays(ruleConfiguration.InvoiceOverdueGraceDays),
                openAmount,
                null,
                true));
        }

        if (scope is null || scope.AllowsRule("R-08"))
        foreach (var customer in customers.Where(customer => customer.IsActive
            && customer.OwnerId.HasValue
            && customer.OwnerAssignedAt is { } ownerAssignedAt
            && ownerAssignedAt <= now.AddDays(-ruleConfiguration.OwnerChangeAfterDays)
            && (!customer.LastContactAt.HasValue
                || customer.LastContactAt <= now.AddDays(-ruleConfiguration.OwnerChangeNoContactDays))))
        {
            var hasPendingRequest = ownerChanges.Any(request => request.CustomerId == customer.Id
                && IsPendingOwnerChange(request.Status));
            if (hasPendingRequest)
                continue;

            var ownerName = ownerNames.GetValueOrDefault(customer.OwnerId!.Value) ?? "unbekannter Besitzer";
            candidates.Add(new WorklistCandidate(
                "ownership-change", "R-08", CrmEntityTypes.Customer, customer.Id, customer.OwnerId,
                $"Zuständigkeit klären · {customer.Name}",
                $"Kunde seit mehr als {ruleConfiguration.OwnerChangeAfterDays} Tagen bei {ownerName} und seit mehr als {ruleConfiguration.OwnerChangeNoContactDays} Tagen ohne Kontakt. Leitung entscheidet; kein automatischer Besitzerwechsel.",
                customer.LastContactAt?.AddDays(ruleConfiguration.OwnerChangeNoContactDays) ?? now,
                customer.LifetimeRevenue,
                null,
                true));
        }

        if (scope is null || scope.AllowsRule("R-08"))
        foreach (var request in ownerChanges.Where(request => IsPendingOwnerChange(request.Status)))
        {
            var targetName = request.CustomerId is { } customerId
                ? customerNames.GetValueOrDefault(customerId)
                : request.TargetType switch
                {
                    "deal" => dealNames.GetValueOrDefault(request.TargetId),
                    "lead" => leadNames.GetValueOrDefault(request.TargetId),
                    _ => null
                };
            candidates.Add(new WorklistCandidate(
                "ownership-change", "R-08", request.TargetType, request.TargetId,
                request.ProposedOwnerId ?? request.OldOwnerId,
                $"Zuständigkeit klären · {targetName ?? request.TargetType}",
                request.Reason, request.RequestedAt, null, null, true));
        }

        return candidates;
    }

    private async Task<EvaluationScope> BuildFullEvaluationScopeAsync(
        IReadOnlyCollection<CrmSynchronizationChange> changes,
        CancellationToken cancellationToken)
    {
        var scope = new EvaluationScope { IsFullEvaluation = true };
        var deletedChanges = changes
            .Where(change => string.Equals(change.ChangeKind, "deleted", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (deletedChanges.Length == 0)
            return scope;

        var providerKeys = deletedChanges.Select(change => change.ProviderKey).Distinct().ToArray();
        var connectionKeys = deletedChanges.Select(change => change.ConnectionKey).Distinct().ToArray();
        var entityTypes = deletedChanges.Select(change => change.EntityType).Distinct().ToArray();
        var externalIds = deletedChanges.Select(change => change.ExternalId).Distinct().ToArray();
        var deletedKeys = deletedChanges
            .Select(change => (change.ProviderKey, change.ConnectionKey, change.EntityType, change.ExternalId))
            .ToHashSet();
        var links = await db.IntegrationEntityLinks
            .AsNoTracking()
            .Where(link => providerKeys.Contains(link.ProviderKey)
                && connectionKeys.Contains(link.ConnectionKey)
                && entityTypes.Contains(link.EntityType)
                && externalIds.Contains(link.ExternalId))
            .Select(link => new
            {
                link.ProviderKey,
                link.ConnectionKey,
                link.EntityType,
                link.ExternalId,
                link.InternalEntityType,
                link.InternalEntityId
            })
            .ToArrayAsync(cancellationToken);

        foreach (var link in links.Where(link => deletedKeys.Contains((
                         link.ProviderKey,
                         link.ConnectionKey,
                         link.EntityType,
                         link.ExternalId))))
        {
            scope.MarkDeleted(link.InternalEntityType, link.InternalEntityId);
        }

        return scope;
    }

    private async Task<EvaluationScope> BuildEvaluationScopeAsync(
        IReadOnlyCollection<CrmSynchronizationChange> changes,
        CancellationToken cancellationToken)
    {
        var scope = new EvaluationScope();
        if (changes.Count == 0)
            return scope;

        var providerKeys = changes.Select(change => change.ProviderKey).Distinct().ToArray();
        var connectionKeys = changes.Select(change => change.ConnectionKey).Distinct().ToArray();
        var entityTypes = changes.Select(change => change.EntityType).Distinct().ToArray();
        var externalIds = changes.Select(change => change.ExternalId).Distinct().ToArray();
        var changedKeys = changes
            .Select(change => (change.ProviderKey, change.ConnectionKey, change.EntityType, change.ExternalId))
            .ToHashSet();
        var deletedKeys = changes
            .Where(change => string.Equals(change.ChangeKind, "deleted", StringComparison.OrdinalIgnoreCase))
            .Select(change => (change.ProviderKey, change.ConnectionKey, change.EntityType, change.ExternalId))
            .ToHashSet();
        var links = await db.IntegrationEntityLinks
            .AsNoTracking()
            .Where(link => providerKeys.Contains(link.ProviderKey)
                && connectionKeys.Contains(link.ConnectionKey)
                && entityTypes.Contains(link.EntityType)
                && externalIds.Contains(link.ExternalId))
            .Select(link => new
            {
                link.ProviderKey,
                link.ConnectionKey,
                link.EntityType,
                link.ExternalId,
                link.InternalEntityType,
                link.InternalEntityId
            })
            .ToArrayAsync(cancellationToken);

        foreach (var link in links.Where(link => changedKeys.Contains((
                         link.ProviderKey,
                         link.ConnectionKey,
                         link.EntityType,
                         link.ExternalId))))
        {
            if (deletedKeys.Contains((link.ProviderKey, link.ConnectionKey, link.EntityType, link.ExternalId)))
                scope.MarkDeleted(link.InternalEntityType, link.InternalEntityId);

            switch (link.InternalEntityType)
            {
                case CrmEntityTypes.Customer:
                    scope.Add(link.InternalEntityType, link.InternalEntityId, "R-07", "R-08", "R-10", "R-13");
                    break;
                case CrmEntityTypes.Lead:
                    scope.Add(link.InternalEntityType, link.InternalEntityId, "R-01", "R-02", "R-03", "R-04", "R-07", "R-09");
                    break;
                case CrmEntityTypes.Deal:
                    scope.Add(link.InternalEntityType, link.InternalEntityId, "R-05", "R-08", "R-11", "R-14");
                    break;
                case CrmEntityTypes.Contract:
                    scope.Add(link.InternalEntityType, link.InternalEntityId, "R-06");
                    break;
                case CrmEntityTypes.Activity:
                    scope.Add(link.InternalEntityType, link.InternalEntityId);
                    break;
                case CrmEntityTypes.Appointment:
                    scope.Add(link.InternalEntityType, link.InternalEntityId, "R-12");
                    break;
                case CrmEntityTypes.ServiceCase:
                    scope.Add(link.InternalEntityType, link.InternalEntityId, "R-15");
                    break;
                case CrmEntityTypes.Offer:
                    scope.Add(link.InternalEntityType, link.InternalEntityId, "R-16");
                    break;
                case CrmEntityTypes.Order:
                    scope.Add(link.InternalEntityType, link.InternalEntityId, "R-17");
                    break;
                case CrmEntityTypes.Invoice:
                    scope.Add(link.InternalEntityType, link.InternalEntityId, "R-18");
                    break;
                case CrmEntityTypes.Product:
                case CrmEntityTypes.ProductCategory:
                    // A product/category change can alter the cross-selling
                    // result for every active customer, but not the other
                    // rules. This is intentionally a narrow broadening.
                    scope.AllCustomersForCrossSell = true;
                    scope.EnableRule("R-10");
                    break;
                case CrmEntityTypes.Pipeline:
                case CrmEntityTypes.PipelineStage:
                case CrmEntityTypes.DealStageHistory:
                    scope.Add(link.InternalEntityType, link.InternalEntityId);
                    break;
                case CrmEntityTypes.Owner:
                    scope.Add(link.InternalEntityType, link.InternalEntityId, "R-11");
                    scope.OwnerIds.Add(link.InternalEntityId);
                    break;
            }
        }

        var leadIds = scope.Ids(CrmEntityTypes.Lead);
        if (leadIds.Length > 0)
        {
            var leadCustomers = await db.SalesLeads
                .AsNoTracking()
                .Where(lead => leadIds.Contains(lead.Id))
                .Select(lead => lead.CustomerId)
                .ToArrayAsync(cancellationToken);
            foreach (var customerId in leadCustomers.Where(id => id.HasValue).Select(id => id!.Value))
                scope.Add(CrmEntityTypes.Customer, customerId, "R-07", "R-08", "R-10", "R-13");
        }

        var activityIds = scope.Ids(CrmEntityTypes.Activity);
        if (activityIds.Length > 0)
        {
            var activityRelations = await db.SalesActivityRelations
                .AsNoTracking()
                .Where(relation => activityIds.Contains(relation.ActivityId))
                .ToArrayAsync(cancellationToken);
            foreach (var relation in activityRelations)
            {
                switch (relation.TargetType)
                {
                    case CrmEntityTypes.Customer:
                        scope.Add(relation.TargetType, relation.TargetId, "R-07", "R-08", "R-13");
                        break;
                    case CrmEntityTypes.Lead:
                        scope.Add(relation.TargetType, relation.TargetId, "R-01", "R-02", "R-03", "R-04", "R-07", "R-09");
                        break;
                    case CrmEntityTypes.Deal:
                        scope.Add(relation.TargetType, relation.TargetId, "R-05", "R-08", "R-11", "R-14");
                        break;
                }
            }
        }

        var ownerIds = scope.OwnerIds.ToArray();
        if (ownerIds.Length > 0)
        {
            var ownerCustomers = await db.SalesCustomers
                .AsNoTracking()
                .Where(customer => customer.OwnerId.HasValue && ownerIds.Contains(customer.OwnerId.Value))
                .Select(customer => customer.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var customerId in ownerCustomers)
                scope.Add(CrmEntityTypes.Customer, customerId, "R-07", "R-08", "R-10", "R-13");

            var ownerLeads = await db.SalesLeads
                .AsNoTracking()
                .Where(lead => lead.OwnerId.HasValue && ownerIds.Contains(lead.OwnerId.Value))
                .Select(lead => lead.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var leadId in ownerLeads)
                scope.Add(CrmEntityTypes.Lead, leadId, "R-01", "R-02", "R-03", "R-04", "R-07", "R-09");

            var ownerDeals = await db.SalesDeals
                .AsNoTracking()
                .Where(deal => deal.OwnerId.HasValue && ownerIds.Contains(deal.OwnerId.Value))
                .Select(deal => deal.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var dealId in ownerDeals)
                scope.Add(CrmEntityTypes.Deal, dealId, "R-05", "R-08", "R-11", "R-14");

            var ownerContracts = await db.SalesContracts
                .AsNoTracking()
                .Where(contract => contract.OwnerId.HasValue && ownerIds.Contains(contract.OwnerId.Value))
                .Select(contract => contract.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var contractId in ownerContracts)
                scope.Add(CrmEntityTypes.Contract, contractId, "R-06");

            var ownerAppointments = await db.SalesAppointments
                .AsNoTracking()
                .Where(appointment => appointment.OwnerId.HasValue && ownerIds.Contains(appointment.OwnerId.Value))
                .Select(appointment => appointment.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var appointmentId in ownerAppointments)
                scope.Add(CrmEntityTypes.Appointment, appointmentId, "R-12");

            var ownerServiceCases = await db.SalesServiceCases
                .AsNoTracking()
                .Where(serviceCase => serviceCase.OwnerId.HasValue && ownerIds.Contains(serviceCase.OwnerId.Value))
                .Select(serviceCase => serviceCase.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var serviceCaseId in ownerServiceCases)
                scope.Add(CrmEntityTypes.ServiceCase, serviceCaseId, "R-15");

            var ownerOffers = await db.SalesOffers
                .AsNoTracking()
                .Where(offer => offer.OwnerId.HasValue && ownerIds.Contains(offer.OwnerId.Value))
                .Select(offer => offer.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var offerId in ownerOffers)
                scope.Add(CrmEntityTypes.Offer, offerId, "R-16");

            var ownerOrders = await db.SalesOrders
                .AsNoTracking()
                .Where(order => order.OwnerId.HasValue && ownerIds.Contains(order.OwnerId.Value))
                .Select(order => order.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var orderId in ownerOrders)
                scope.Add(CrmEntityTypes.Order, orderId, "R-17");

            var ownerInvoices = await db.SalesInvoices
                .AsNoTracking()
                .Where(invoice => invoice.OwnerId.HasValue && ownerIds.Contains(invoice.OwnerId.Value))
                .Select(invoice => invoice.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var invoiceId in ownerInvoices)
                scope.Add(CrmEntityTypes.Invoice, invoiceId, "R-18");
        }

        var pipelineIds = scope.Ids(CrmEntityTypes.Pipeline);
        var stageIds = scope.Ids(CrmEntityTypes.PipelineStage);
        var historyIds = scope.Ids(CrmEntityTypes.DealStageHistory);
        var historyDealIds = historyIds.Length == 0
            ? Array.Empty<Guid>()
            : await db.SalesDealStageHistory
                .AsNoTracking()
                .Where(history => historyIds.Contains(history.Id))
                .Select(history => history.DealId)
                .ToArrayAsync(cancellationToken);
        var pipelineDealIds = pipelineIds.Length == 0 && stageIds.Length == 0
            ? Array.Empty<Guid>()
            : await db.SalesDeals
                .AsNoTracking()
                .Where(deal => (deal.PipelineId.HasValue && pipelineIds.Contains(deal.PipelineId.Value))
                    || (deal.PipelineStageId.HasValue && stageIds.Contains(deal.PipelineStageId.Value)))
                .Select(deal => deal.Id)
                .ToArrayAsync(cancellationToken);
        foreach (var dealId in historyDealIds.Concat(pipelineDealIds).Distinct())
                scope.Add(CrmEntityTypes.Deal, dealId, "R-05", "R-08", "R-11", "R-14");

        var customerIds = scope.Ids(CrmEntityTypes.Customer);
        if (customerIds.Length > 0)
        {
            var customerDeals = await db.SalesDeals
                .AsNoTracking()
                .Where(deal => deal.CustomerId.HasValue && customerIds.Contains(deal.CustomerId.Value))
                .Select(deal => deal.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var dealId in customerDeals)
                scope.Add(CrmEntityTypes.Deal, dealId, "R-05", "R-08", "R-11", "R-14");

            var customerContracts = await db.SalesContracts
                .AsNoTracking()
                .Where(contract => customerIds.Contains(contract.CustomerId))
                .Select(contract => contract.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var contractId in customerContracts)
                scope.Add(CrmEntityTypes.Contract, contractId, "R-06");

            var customerServiceCases = await db.SalesServiceCases
                .AsNoTracking()
                .Where(serviceCase => serviceCase.CustomerId.HasValue && customerIds.Contains(serviceCase.CustomerId.Value))
                .Select(serviceCase => serviceCase.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var serviceCaseId in customerServiceCases)
                scope.Add(CrmEntityTypes.ServiceCase, serviceCaseId, "R-15");

            var customerOffers = await db.SalesOffers
                .AsNoTracking()
                .Where(offer => offer.CustomerId.HasValue && customerIds.Contains(offer.CustomerId.Value))
                .Select(offer => offer.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var offerId in customerOffers)
                scope.Add(CrmEntityTypes.Offer, offerId, "R-16");

            var customerOrders = await db.SalesOrders
                .AsNoTracking()
                .Where(order => order.CustomerId.HasValue && customerIds.Contains(order.CustomerId.Value))
                .Select(order => order.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var orderId in customerOrders)
                scope.Add(CrmEntityTypes.Order, orderId, "R-17");

            var customerInvoices = await db.SalesInvoices
                .AsNoTracking()
                .Where(invoice => invoice.CustomerId.HasValue && customerIds.Contains(invoice.CustomerId.Value))
                .Select(invoice => invoice.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var invoiceId in customerInvoices)
                scope.Add(CrmEntityTypes.Invoice, invoiceId, "R-18");
        }

        var dealIds = scope.Ids(CrmEntityTypes.Deal);
        if (dealIds.Length > 0)
        {
            var dealCustomers = await db.SalesDeals
                .AsNoTracking()
                .Where(deal => dealIds.Contains(deal.Id))
                .Select(deal => deal.CustomerId)
                .ToArrayAsync(cancellationToken);
            foreach (var customerId in dealCustomers.Where(id => id.HasValue).Select(id => id!.Value))
                scope.Add(CrmEntityTypes.Customer, customerId, "R-07", "R-08", "R-10", "R-13");

            var dealOwners = await db.SalesDeals
                .AsNoTracking()
                .Where(deal => dealIds.Contains(deal.Id) && deal.OwnerId.HasValue)
                .Select(deal => deal.OwnerId!.Value)
                .ToArrayAsync(cancellationToken);
            foreach (var ownerId in dealOwners)
            {
                scope.OwnerIds.Add(ownerId);
                scope.Add(CrmEntityTypes.Owner, ownerId, "R-11");
            }

            var dealContracts = await db.SalesContracts
                .AsNoTracking()
                .Where(contract => contract.DealId.HasValue && dealIds.Contains(contract.DealId.Value))
                .Select(contract => contract.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var contractId in dealContracts)
                scope.Add(CrmEntityTypes.Contract, contractId, "R-06");

            var dealServiceCases = await db.SalesServiceCases
                .AsNoTracking()
                .Where(serviceCase => serviceCase.DealId.HasValue && dealIds.Contains(serviceCase.DealId.Value))
                .Select(serviceCase => serviceCase.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var serviceCaseId in dealServiceCases)
                scope.Add(CrmEntityTypes.ServiceCase, serviceCaseId, "R-15");

            var dealOffers = await db.SalesOffers
                .AsNoTracking()
                .Where(offer => offer.DealId.HasValue && dealIds.Contains(offer.DealId.Value))
                .Select(offer => offer.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var offerId in dealOffers)
                scope.Add(CrmEntityTypes.Offer, offerId, "R-16");

            var dealOrders = await db.SalesOrders
                .AsNoTracking()
                .Where(order => order.DealId.HasValue && dealIds.Contains(order.DealId.Value))
                .Select(order => order.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var orderId in dealOrders)
                scope.Add(CrmEntityTypes.Order, orderId, "R-17");

            var dealInvoices = await db.SalesInvoices
                .AsNoTracking()
                .Where(invoice => invoice.DealId.HasValue && dealIds.Contains(invoice.DealId.Value))
                .Select(invoice => invoice.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var invoiceId in dealInvoices)
                scope.Add(CrmEntityTypes.Invoice, invoiceId, "R-18");
        }

        var contractIds = scope.Ids(CrmEntityTypes.Contract);
        if (contractIds.Length > 0)
        {
            var contractCustomers = await db.SalesContracts
                .AsNoTracking()
                .Where(contract => contractIds.Contains(contract.Id))
                .Select(contract => new { contract.CustomerId, contract.DealId })
                .ToArrayAsync(cancellationToken);
            foreach (var contract in contractCustomers)
            {
                scope.Add(CrmEntityTypes.Customer, contract.CustomerId, "R-07", "R-08", "R-10", "R-13");
                if (contract.DealId.HasValue)
                    scope.Add(CrmEntityTypes.Deal, contract.DealId.Value, "R-05", "R-08", "R-11", "R-14");
            }
        }

        return scope;
    }

    private async Task<Guid?> ResolveOwnerIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
        => await ownerMappings.ResolveOwnerIdAsync(user, db, cancellationToken);

    private async Task<bool> CanAccessAsync(
        SalesWorkItem item,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (IsSalesManager(user))
            return true;

        var ownerId = await ResolveOwnerIdAsync(user, cancellationToken);
        return !item.OwnerId.HasValue || (ownerId.HasValue && item.OwnerId == ownerId);
    }

    private static bool IsSalesManager(ClaimsPrincipal user)
        => TenantApplicationRole.IsInRole(user, "sales-manager");

    private static decimal CalculatePriority(WorklistCandidate candidate)
    {
        var baseScore = candidate.WorkItemType switch
        {
            "contract-renewal-critical" => 100m,
            "lead-first-response" => 95m,
            "deal-stalled" => 80m,
            "call-not-reachable" => 100m,
            "call-email-follow-up" => 95m,
            "call-long-runner" => 80m,
            "call-follow-up" => 70m,
            "target-pace-gap" => 100m,
            "deal-reactivation" => 70m,
            "account-care" => 40m,
            "contract-renewal" => 70m,
            "ownership-change" => 50m,
            "appointment-rescheduled" => 45m,
            "customer-stale" => 30m,
            "service-case-overdue" => 100m,
            "invoice-overdue" => 100m,
            "order-overdue" => 90m,
            "offer-follow-up" => 70m,
            _ => 10m
        };
        var now = DateTimeOffset.UtcNow;
        var ageBonus = candidate.DueAt < now
            ? Math.Min((decimal)(now - candidate.DueAt).TotalDays * 0.5m, 30m)
            : 0m;
        var valueBonus = candidate.Value.HasValue
            ? Math.Min(candidate.Value.Value / 10_000m, 20m)
            : 0m;
        return Math.Round(baseScore + ageBonus + valueBonus, 2);
    }

    private async Task<WorklistExternalUrls> ResolveExternalUrlsAsync(
        IReadOnlyCollection<SalesWorkItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return new WorklistExternalUrls(
                new Dictionary<(string EntityType, Guid EntityId), string>(),
                new Dictionary<Guid, string>());

        var tenantIds = items
            .Select(item => item.TenantId)
            .Distinct()
            .ToArray();
        var targets = items
            .SelectMany(item => item.Relations)
            .Select(relation => (relation.TargetType, relation.TargetId))
            .Distinct()
            .ToArray();
        var targetUrls = new Dictionary<(string EntityType, Guid EntityId), string>();
        if (targets.Length > 0)
        {
            var targetIds = targets.Select(target => target.TargetId).Distinct().ToArray();
            var links = await db.IntegrationEntityLinks
                .AsNoTracking()
                .Where(link => tenantIds.Contains(link.TenantId)
                    && link.SourceDeletedAt == null
                    && link.ExternalUrl != null
                    && targetIds.Contains(link.InternalEntityId))
                .Select(link => new
                {
                    link.InternalEntityType,
                    link.InternalEntityId,
                    link.ExternalUrl,
                    link.LastSeenAt
                })
                .ToArrayAsync(cancellationToken);

            targetUrls = links
                .Where(link => targets.Contains((link.InternalEntityType, link.InternalEntityId))
                    && !string.IsNullOrWhiteSpace(link.ExternalUrl))
                .GroupBy(link => (link.InternalEntityType, link.InternalEntityId))
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(link => link.LastSeenAt).First().ExternalUrl!);
        }

        var workItemIds = items.Select(item => item.Id).ToArray();
        var taskLinks = await db.IntegrationEntityLinks
            .AsNoTracking()
            .Where(link => tenantIds.Contains(link.TenantId)
                && link.WorkItemId.HasValue
                && workItemIds.Contains(link.WorkItemId.Value)
                && link.EntityType == CrmEntityTypes.Activity
                && link.SourceDeletedAt == null
                && link.ExternalUrl != null)
            .Select(link => new
            {
                WorkItemId = link.WorkItemId!.Value,
                link.ExternalUrl,
                link.LastSeenAt
            })
            .ToArrayAsync(cancellationToken);
        var taskUrls = taskLinks
            .Where(link => !string.IsNullOrWhiteSpace(link.ExternalUrl))
            .GroupBy(link => link.WorkItemId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(link => link.LastSeenAt).First().ExternalUrl!);

        return new WorklistExternalUrls(targetUrls, taskUrls);
    }

    private static WorklistItemDto ToDto(
        SalesWorkItem item,
        WorklistExternalUrls? externalUrls = null)
    {
        var relation = item.Relations.FirstOrDefault(relation => relation.RelationRole == "primary")
            ?? item.Relations.FirstOrDefault();
        var externalUrl = relation is not null
            && externalUrls is not null
            && externalUrls.TargetUrls.TryGetValue((relation.TargetType, relation.TargetId), out var resolvedUrl)
                ? resolvedUrl
                : null;
        var crmTaskUrl = externalUrls is not null
            && externalUrls.TaskUrls.TryGetValue(item.Id, out var resolvedTaskUrl)
                ? resolvedTaskUrl
                : null;
        return new WorklistItemDto(
            item.Id,
            item.WorkItemType,
            WorkItemTypeNames.GetValueOrDefault(item.WorkItemType, item.WorkItemType),
            item.Status,
            item.Title,
            item.Reason,
            item.OwnerId,
            item.Owner?.DisplayName,
            item.DueAt,
            item.PriorityScore ?? 0,
            PriorityBand(item.PriorityScore ?? 0),
            item.SourceRuleCode,
            relation?.TargetType,
            relation?.TargetId,
            externalUrl,
            crmTaskUrl,
            item.CreatedAt,
            item.AvailableFrom,
            item.SnoozedUntil,
            item.RequiresApproval);
    }

    private sealed record WorklistExternalUrls(
        IReadOnlyDictionary<(string EntityType, Guid EntityId), string> TargetUrls,
        IReadOnlyDictionary<Guid, string> TaskUrls);

    private async Task<IReadOnlyCollection<WorklistRuleDto>> GetRuleNavigationAsync(
        IReadOnlyCollection<SalesWorkItem> items,
        CancellationToken cancellationToken)
    {
        var definitions = await db.SalesRuleDefinitions
            .AsNoTracking()
            .Where(rule => RuleCodes.Contains(rule.Code))
            .ToDictionaryAsync(rule => rule.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return RuleCodes
            .Select(code =>
            {
                var seed = DefaultRules.FirstOrDefault(rule => rule.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                var definition = definitions.GetValueOrDefault(code);
                return new WorklistRuleDto(
                    code,
                    definition?.Name ?? seed?.Name ?? code,
                    definition?.Description ?? seed?.Description,
                    items.Count(item => string.Equals(item.SourceRuleCode, code, StringComparison.OrdinalIgnoreCase)));
            })
            .ToArray();
    }

    private static string PriorityBand(decimal score)
        => score >= 100 ? "critical" : score >= 70 ? "high" : score >= 40 ? "medium" : "low";

    private static bool IsActiveStatus(string? status)
        => status is WorkItemStatuses.Open
            or WorkItemStatuses.Scheduled
            or WorkItemStatuses.Snoozed;

    private static bool IsOpenStatus(string? status)
        => !status.IsOneOf("won", "closed", "closed_won", "lost", "closed_lost", "converted", "cancelled", "canceled", "expired", "terminated", "rejected", "completed", "done", "archived");

    private static bool IsOpenInvoice(string? status)
        => !status.IsOneOf("paid", "paid_in_full", "settled", "closed", "cancelled", "canceled", "void", "written_off", "written-off");

    private static bool IsUrgentPriority(string? priority)
        => priority.IsOneOf("urgent", "critical", "high", "sehr hoch", "hoch");

    private static bool IsWonStatus(string? status)
        => status.IsOneOf("won", "closed_won", "converted");

    private static bool IsLostStatus(string? status)
        => status.IsOneOf("lost", "closed_lost");

    private static bool IsReactivationLossReason(string? lossReason)
        => lossReason is not null
            && (lossReason.Contains("timing", StringComparison.OrdinalIgnoreCase)
                || lossReason.Contains("budget", StringComparison.OrdinalIgnoreCase)
                || lossReason.Contains("zeit", StringComparison.OrdinalIgnoreCase)
                || lossReason.Contains("budget", StringComparison.OrdinalIgnoreCase));

    private static bool IsRevenueTargetType(string? targetType)
        => targetType is not null
            && (targetType.Contains("revenue", StringComparison.OrdinalIgnoreCase)
                || targetType.Contains("umsatz", StringComparison.OrdinalIgnoreCase)
                || targetType.Contains("sales", StringComparison.OrdinalIgnoreCase));

    private static bool IsOwnerChangeStage(SalesPipelineStage? stage)
        => stage is not null
            && (stage.Name.Contains("agent wechsel", StringComparison.OrdinalIgnoreCase)
                || stage.StageType.Contains("agent wechsel", StringComparison.OrdinalIgnoreCase)
                || stage.Key.Contains("agent wechsel", StringComparison.OrdinalIgnoreCase)
                || stage.Name.Contains("owner change", StringComparison.OrdinalIgnoreCase)
                || stage.StageType.Contains("owner change", StringComparison.OrdinalIgnoreCase)
                || stage.Key.Contains("owner change", StringComparison.OrdinalIgnoreCase));

    private static bool IsPendingOwnerChange(string? status)
        => status.IsOneOf("pending", "open", "requested", "proposed", "new");

    private async Task<SalesWorkCalendar?> LoadWorkingCalendarAsync(CancellationToken cancellationToken)
        => await db.SalesWorkCalendars
            .AsNoTracking()
            .Include(calendar => calendar.WorkingHours)
            .Include(calendar => calendar.Holidays)
            .Where(calendar => calendar.IsDefault && calendar.IsActive)
            .OrderBy(calendar => calendar.Name)
            .FirstOrDefaultAsync(cancellationToken);

    private static DateTimeOffset AddWorkingHours(
        DateTimeOffset start,
        decimal hours,
        SalesWorkCalendar? calendar)
    {
        if (hours <= 0)
            return start;
        if (calendar is null)
            return start.AddHours((double)hours);

        var timeZone = ResolveTimeZone(calendar.TimeZone);
        var cursor = start;
        var remaining = (double)hours;
        for (var day = 0; day < 3700 && remaining > 0; day++)
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(cursor, timeZone).DateTime.Date);
            foreach (var interval in WorkingIntervals(localDate, calendar, timeZone))
            {
                if (interval.End <= cursor)
                    continue;

                var intervalStart = interval.Start > cursor ? interval.Start : cursor;
                var availableHours = (interval.End - intervalStart).TotalHours;
                if (remaining <= availableHours)
                    return intervalStart.AddHours(remaining);

                remaining -= availableHours;
            }

            cursor = StartOfLocalDay(localDate.AddDays(1), timeZone);
        }

        return start.AddHours((double)hours);
    }

    private static double CalculateWorkingHours(
        DateTimeOffset start,
        DateTimeOffset end,
        SalesWorkCalendar? calendar)
    {
        if (end <= start)
            return 0;
        if (calendar is null)
            return (end - start).TotalHours;

        var timeZone = ResolveTimeZone(calendar.TimeZone);
        var totalHours = 0d;
        var localStart = TimeZoneInfo.ConvertTime(start, timeZone).Date;
        var localEnd = TimeZoneInfo.ConvertTime(end, timeZone).Date;
        for (var date = DateOnly.FromDateTime(localStart); date <= DateOnly.FromDateTime(localEnd); date = date.AddDays(1))
        {
            foreach (var interval in WorkingIntervals(date, calendar, timeZone))
            {
                var overlapStart = interval.Start > start ? interval.Start : start;
                var overlapEnd = interval.End < end ? interval.End : end;
                if (overlapEnd > overlapStart)
                    totalHours += (overlapEnd - overlapStart).TotalHours;
            }
        }

        return totalHours;
    }

    private static IReadOnlyCollection<(DateTimeOffset Start, DateTimeOffset End)> WorkingIntervals(
        DateOnly date,
        SalesWorkCalendar calendar,
        TimeZoneInfo timeZone)
    {
        var holiday = calendar.Holidays.FirstOrDefault(item => item.Date == date);
        if (holiday is not null && !holiday.IsWorkingDayOverride)
            return [];

        var hours = calendar.WorkingHours.FirstOrDefault(item => item.DayOfWeek == (int)date.DayOfWeek);
        if (hours is { IsWorkingDay: false })
            return [];

        var start = hours?.StartAt ?? (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? null
            : new TimeSpan(9, 0, 0));
        var end = hours?.EndAt ?? (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? null
            : new TimeSpan(17, 0, 0));
        if (!start.HasValue || !end.HasValue || end <= start)
            return [];

        var dayStart = AtLocalTime(date, start.Value, timeZone);
        var dayEnd = AtLocalTime(date, end.Value, timeZone);
        if (hours?.BreakStartAt is { } breakStart
            && hours.BreakEndAt is { } breakEnd
            && breakStart > start
            && breakEnd < end
            && breakEnd > breakStart)
        {
            return [
                (dayStart, AtLocalTime(date, breakStart, timeZone)),
                (AtLocalTime(date, breakEnd, timeZone), dayEnd)
            ];
        }

        return [(dayStart, dayEnd)];
    }

    private static DateTimeOffset StartOfLocalDay(DateOnly date, TimeZoneInfo timeZone)
        => AtLocalTime(date, TimeSpan.Zero, timeZone);

    private static DateTimeOffset AtLocalTime(DateOnly date, TimeSpan time, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.FromTimeSpan(time)), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static string FormatAge(DateTimeOffset from, DateTimeOffset now)
    {
        var days = Math.Max(0, (int)(now - from).TotalDays);
        return days == 0 ? "heute" : $"{days} Tagen";
    }

    private static Guid TenantId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId) && tenantId != Guid.Empty
            ? tenantId
            : throw new InvalidOperationException("Der Access Token enthält keine gültige tenant_id.");

    private void AddEvent(
        SalesWorkItem item,
        string eventType,
        object details,
        string? actorSubject,
        DateTimeOffset occurredAt)
        => db.SalesWorkItemEvents.Add(new SalesWorkItemEvent
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            WorkItemId = item.Id,
            WorkItem = item,
            EventType = eventType,
            DetailsJson = JsonSerializer.Serialize(details),
            ActorSubject = actorSubject,
            OccurredAt = occurredAt
        });

    private void AddAudit(
        ActorInfo actor,
        string action,
        SalesWorkItem item,
        string before,
        string after,
        DateTimeOffset occurredAt)
        => db.SalesAuditLogs.Add(new SalesAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            ActorSubject = actor.Subject,
            ActorDisplayName = actor.DisplayName,
            Action = action,
            EntityType = "sales_work_item",
            EntityId = item.Id,
            OccurredAt = occurredAt,
            BeforeJson = before,
            AfterJson = after
        });

    private static string SerializeState(SalesWorkItem item)
        => JsonSerializer.Serialize(new
        {
            item.Status,
            item.CompletedAt,
            item.CompletedBy,
            item.AvailableFrom,
            item.SnoozedUntil,
            item.WorkItemChainId,
            item.PreviousWorkItemId,
            item.ClosureReason,
            item.UpdatedAt
        });

    private static ActorInfo Actor(ClaimsPrincipal user)
        => new(
            user.FindFirstValue("sub"),
            user.FindFirstValue("name") ?? user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue(ClaimTypes.Email));

    private sealed class RuleEvaluationProgress(
        IPlatformJobProgressReporter? reporter,
        IPlatformJobLogger? logger)
    {
        private const decimal ProgressStart = 65m;
        private const decimal ProgressEnd = 85m;
        private const int MinimumItemDelta = 10;
        private static readonly TimeSpan MinimumReportInterval = TimeSpan.FromMilliseconds(750);
        private long lastReportedItems = -1;
        private long lastReportTimestamp;

        public static RuleEvaluationProgress Disabled { get; } = new(
            null,
            null);

        public async Task ReportAsync(
            int processed,
            int? total,
            string message,
            object? details,
            bool force,
            CancellationToken cancellationToken)
        {
            if (reporter is null && logger is null)
                return;

            var nowTimestamp = Stopwatch.GetTimestamp();
            var elapsed = lastReportTimestamp == 0
                ? MinimumReportInterval
                : Stopwatch.GetElapsedTime(lastReportTimestamp);
            var itemDelta = processed - lastReportedItems;
            if (!force
                && processed != total
                && itemDelta < MinimumItemDelta
                && elapsed < MinimumReportInterval)
            {
                return;
            }

            var progressPercent = total is null
                ? ProgressStart
                : total.Value == 0
                    ? ProgressEnd
                    : ProgressStart + Math.Clamp(
                        processed * (ProgressEnd - ProgressStart) / total.Value,
                        0,
                        ProgressEnd - ProgressStart);
            var detailsJson = details is null
                ? (JsonElement?)null
                : JsonSerializer.SerializeToElement(details);

            if (logger is not null)
            {
                await logger.InfoAsync(
                    message,
                    "Regelbewertung",
                    detailsJson,
                    cancellationToken);
            }

            if (reporter is not null)
            {
                await reporter.ReportAsync(
                    new PlatformJobProgress(
                        Step: "Regelbewertung",
                        Message: message,
                        ProgressPercent: progressPercent,
                        ItemsProcessed: Math.Max(0, processed),
                        ItemsTotal: total,
                        ItemsFailed: 0,
                        Details: detailsJson),
                    cancellationToken);
            }

            lastReportedItems = processed;
            lastReportTimestamp = nowTimestamp;
        }
    }

    private sealed class EvaluationScope
    {
        private readonly Dictionary<(string EntityType, Guid EntityId), HashSet<string>> targets = [];
        private readonly HashSet<string> enabledRules = [];

        public HashSet<Guid> OwnerIds { get; } = [];

        public bool AllCustomersForCrossSell { get; set; }

        public bool IsFullEvaluation { get; set; }

        private readonly HashSet<(string EntityType, Guid EntityId)> deletedTargets = [];

        public bool IsEmpty
            => !AllCustomersForCrossSell
                && enabledRules.Count == 0
                && targets.Values.All(rules => rules.Count == 0);

        public void Add(string entityType, Guid entityId, params string[] rules)
        {
            if (!targets.TryGetValue((entityType, entityId), out var targetRules))
            {
                targetRules = [];
                targets.Add((entityType, entityId), targetRules);
            }

            foreach (var rule in rules)
                targetRules.Add(rule);
        }

        public void EnableRule(string ruleCode)
            => enabledRules.Add(ruleCode);

        public void MarkDeleted(string entityType, Guid entityId)
            => deletedTargets.Add((entityType, entityId));

        public bool IsDeletedTarget(SalesWorkItem item)
            => item.Relations.Any(relation => deletedTargets.Contains((relation.TargetType, relation.TargetId)));

        public Guid[] Ids(string entityType)
            => targets.Keys
                .Where(target => target.EntityType == entityType && target.EntityId != Guid.Empty)
                .Select(target => target.EntityId)
                .Distinct()
                .ToArray();

        public bool AllowsRule(string ruleCode)
            => IsFullEvaluation
                || enabledRules.Contains(ruleCode)
                || AllCustomersForCrossSell && ruleCode == "R-10"
                || targets.Values.Any(rules => rules.Contains(ruleCode));

        public bool Matches(SalesWorkItem item)
            => IsFullEvaluation
                || item.SourceRuleCode is not null
                && item.Relations.Any(relation => Matches(
                    relation.TargetType,
                    relation.TargetId,
                    item.SourceRuleCode));

        public bool Matches(string targetType, Guid targetId, string ruleCode)
            => IsFullEvaluation
                || ruleCode == "R-10"
                && AllCustomersForCrossSell
                && targetType == CrmEntityTypes.Customer
                || targets.TryGetValue((targetType, targetId), out var rules)
                    && rules.Contains(ruleCode);
    }

    private sealed record WorklistCandidate(
        string WorkItemType,
        string RuleCode,
        string TargetType,
        Guid TargetId,
        Guid? OwnerId,
        string Title,
        string Reason,
        DateTimeOffset DueAt,
        decimal? Value,
        string? IdentityKey = null,
        bool RequiresApproval = false)
    {
        public Guid Id(Guid tenantId)
        {
            var identity = IdentityKey ?? $"{TargetType}|{TargetId:D}";
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{tenantId:D}|{RuleCode}|{identity}"));
            bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
            bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
            return new Guid(bytes);
        }
    }

    private sealed record ActorInfo(string? Subject, string? DisplayName);
    private sealed record RuleDefinitionSeed(string Code, string Name, string Description, object Parameters);

    private static readonly RuleDefinitionSeed[] DefaultRules =
    [
        new("R-01", "Anruf-Folgeaktion", "Anrufversuch ohne qualifiziertes Gespräch; nächster Versuch nach 14 Tagen.", new { maxAttempts = 5, intervalDays = 14 }),
        new("R-02", "E-Mail nach fünf Versuchen", "Fünfter Anrufversuch ohne qualifiziertes Gespräch; E-Mail-Vorlage und Wiedervorlage vorschlagen.", new { attempts = 5, intervalDays = 14 }),
        new("R-03", "Anruf-Langläufer", "Sechs bis zehn Anrufversuche ohne qualifiziertes Gespräch; 30-Tage-Intervall.", new { minAttempts = 6, maxAttempts = 10, intervalDays = 30 }),
        new("R-04", "Nicht erreichbar vorschlagen", "Mehr als zehn Anrufversuche ohne qualifiziertes Gespräch; keine automatische CRM-Statusänderung.", new { minAttempts = 11, requiresApproval = true }),
        new("R-05", "Deal ohne Aktivität", "Offener Deal ohne dokumentierte Aktivität seit mehr als 30 Tagen.", new { inactiveDays = 30 }),
        new("R-06", "Vertragsverlängerung", "Aktiver Vertrag endet innerhalb der nächsten 90 Tage.", new { horizonDays = 90, criticalDays = 30 }),
        new("R-07", "Kundenkontakt überfällig", "Kein Kontakt oder letzter Kontakt liegt mehr als 90 Tage zurück.", new { inactiveDays = 90 }),
        new("R-09", "Lead-Erstreaktion", "Neuer Lead ohne dokumentierte Aktivität nach einer Stunde.", new { responseHours = 1 }),
        new("R-08", "Zuständigkeit klären", "Offener Vorschlag zur Änderung der Zuständigkeit.", new { }),
        new("R-10", "Cross-Selling", "Aktiver Kunde ohne Deal oder Vertrag in einer Produktkategorie.", new { }),
        new("R-11", "Ziel-Pace", "Zielerreichung liegt mehr als 15 Punkte unter dem zeitanteiligen Ziel.", new { gapPoints = 15 }),
        new("R-12", "Termin mehrfach verschoben", "Termin wurde mindestens dreimal verschoben.", new { minimumReschedules = 3 }),
        new("R-13", "Account Care", "Aktiver Kunde mit Umsatzhistorie und mehr als 90 Tagen ohne Telefonat.", new { inactiveDays = 90 }),
        new("R-14", "Deal reaktivieren", "Verlorener Deal mit Timing- oder Budgetgrund und einem Alter von mehr als 90 Tagen kann reaktiviert werden.", new { ageDays = 90 }),
        new("R-15", "Servicefall bearbeiten", "Offener oder dringender Servicefall ohne rechtzeitige Bearbeitung.", new { responseDays = 2 }),
        new("R-16", "Angebot nachfassen", "Gesendetes Angebot ohne Entscheidung nach sieben Tagen.", new { followUpDays = 7 }),
        new("R-17", "Lieferverzug", "Offener Auftrag nach Überschreitung des zugesagten Liefertermins.", new { escalationDays = 1 }),
        new("R-18", "Rechnung überfällig", "Offene Rechnung nach dem Fälligkeitsdatum.", new { graceDays = 0 })
    ];

    private static readonly IReadOnlyDictionary<string, string> WorkItemTypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["contract-renewal-critical"] = "Vertrag läuft bald aus",
        ["lead-first-response"] = "Neuer Lead ohne Erstreaktion",
        ["call-follow-up"] = "Anruf-Folgeaktion",
        ["call-email-follow-up"] = "E-Mail nach fünf Anrufversuchen",
        ["call-long-runner"] = "Anruf-Langläufer",
        ["call-not-reachable"] = "Nicht erreichbar prüfen",
        ["target-pace-gap"] = "Zielabweichung prüfen",
        ["deal-stalled"] = "Deal ohne Aktivität",
        ["deal-reactivation"] = "Verlorenen Deal reaktivieren",
        ["contract-renewal"] = "Vertragsverlängerung",
        ["ownership-change"] = "Zuständigkeit klären",
        ["cross-sell"] = "Cross-Selling-Chance",
        ["appointment-rescheduled"] = "Termin mehrfach verschoben",
        ["customer-stale"] = "Kundenkontakt überfällig",
        ["lead-reactivation"] = "Lead reaktivieren",
        ["account-care"] = "Account Care",
        ["service-case-overdue"] = "Servicefall bearbeiten",
        ["offer-follow-up"] = "Angebot nachfassen",
        ["order-overdue"] = "Lieferverzug prüfen",
        ["invoice-overdue"] = "Überfällige Rechnung prüfen"
    };

    private static class WorkItemStatuses
    {
        public const string Open = "open";
        public const string Scheduled = "scheduled";
        public const string Snoozed = "snoozed";
        public const string Completed = "completed";
        public const string Resolved = "resolved";
        public const string Closed = "closed";
    }

    private static class RuleRunStatuses
    {
        public const string Succeeded = "succeeded";
        public const string Running = "running";
    }
}

public sealed record WorklistEvaluationResult(
    int EvaluatedCount,
    int CreatedCount,
    int ResolvedCount,
    bool FullEvaluation);

public sealed record WorklistResponse(
    DateTimeOffset GeneratedAt,
    DateTimeOffset? LastRefreshAt,
    bool OwnerMatched,
    bool TeamView,
    IReadOnlyCollection<WorklistRuleDto> Rules,
    IReadOnlyCollection<WorklistItemDto> Items);

public sealed record WorklistRuleDto(
    string Code,
    string Name,
    string? Description,
    int ItemCount);

public sealed record WorklistItemDto(
    Guid Id,
    string WorkItemType,
    string WorkItemTypeName,
    string Status,
    string Title,
    string? Reason,
    Guid? OwnerId,
    string? OwnerName,
    DateTimeOffset? DueAt,
    decimal PriorityScore,
    string PriorityBand,
    string? SourceRuleCode,
    string? TargetType,
    Guid? TargetId,
    string? ExternalUrl,
    string? CrmTaskUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AvailableFrom,
    DateTimeOffset? SnoozedUntil,
    bool RequiresApproval);

public sealed record SnoozeWorklistItemRequest(DateTimeOffset? Until = null, bool Tomorrow = false);

internal static class StringStatusExtensions
{
    public static bool IsOneOf(this string? value, params string[] candidates)
        => value is not null && candidates.Contains(value.Trim().ToLowerInvariant(), StringComparer.Ordinal);
}
