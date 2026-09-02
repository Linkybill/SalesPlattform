using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdentityPlatform.Shared.Authorization;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

/// <summary>
/// Creates the first business projection for the SalesPlattform: a prioritized
/// worklist from the already synchronized CRM data.
/// </summary>
public sealed class WorklistService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    OwnerMappingService ownerMappings)
{
    // The platform factory opens the tenant-bound database session per request.
    // The service is scoped, so the active context only lives for one operation.
    private SalesPlattformDbContext? activeContext;
    private SalesPlattformDbContext db => activeContext
        ?? throw new InvalidOperationException("Für die Arbeitsliste ist keine Datenbank-Session geöffnet.");
    private static readonly string[] RuleCodes = ["R-05", "R-06", "R-07", "R-08", "R-09", "R-10", "R-12"];
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

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
                items.Select(ToDto).ToArray());
        }
        finally
        {
            activeContext = null;
        }
    }

    public async Task<WorklistItemDto?> CompleteAsync(
        Guid workItemId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        activeContext = session.Context;
        try
        {
            var item = await db.SalesWorkItems
                .Include(candidate => candidate.Owner)
                .Include(candidate => candidate.Relations)
                .SingleOrDefaultAsync(candidate => candidate.Id == workItemId, cancellationToken);
            if (item is null || !await CanAccessAsync(item, user, cancellationToken))
                return null;

            if (item.Status != WorkItemStatuses.Completed)
            {
                var now = DateTimeOffset.UtcNow;
                var actor = Actor(user);
                var before = SerializeState(item);
                item.Status = WorkItemStatuses.Completed;
                item.CompletedAt = now;
                item.CompletedBy = actor.Subject;
                item.SnoozedUntil = null;
                item.UpdatedAt = now;
                AddEvent(item, "completed", new { action = "complete" }, actor.Subject, now);
                AddAudit(actor, "work-item.completed", item, before, SerializeState(item), now);
                await db.SaveChangesAsync(cancellationToken);
            }

            return ToDto(item);
        }
        finally
        {
            activeContext = null;
        }
    }

    public async Task<WorklistItemDto?> SnoozeAsync(
        Guid workItemId,
        DateTimeOffset until,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (until <= now || until > now.AddDays(90))
            throw new ArgumentOutOfRangeException(nameof(until), "Ein Vorgang kann maximal 90 Tage zurückgestellt werden.");

        await using var session = await dbFactory.OpenAsync(cancellationToken);
        activeContext = session.Context;
        try
        {
            var item = await db.SalesWorkItems
                .Include(candidate => candidate.Owner)
                .Include(candidate => candidate.Relations)
                .SingleOrDefaultAsync(candidate => candidate.Id == workItemId, cancellationToken);
            if (item is null || !await CanAccessAsync(item, user, cancellationToken))
                return null;

            var actor = Actor(user);
            var before = SerializeState(item);
            item.Status = WorkItemStatuses.Snoozed;
            item.SnoozedUntil = until;
            item.UpdatedAt = now;
            AddEvent(item, "snoozed", new { action = "snooze", until }, actor.Subject, now);
            AddAudit(actor, "work-item.snoozed", item, before, SerializeState(item), now);
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
                || (item.Status == WorkItemStatuses.Snoozed
                    && item.SnoozedUntil <= DateTimeOffset.UtcNow));

    private async Task RefreshAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await RefreshGate.WaitAsync(cancellationToken);
        try
        {
            await RefreshCoreAsync(user, cancellationToken);
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    private async Task RefreshCoreAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        var now = DateTimeOffset.UtcNow;
        var actor = Actor(user);
        var ruleDefinitions = await EnsureRuleDefinitionsAsync(tenantId, actor, now, cancellationToken);
        var candidates = await FindCandidatesAsync(now, cancellationToken);
        var candidateIds = candidates.Select(candidate => candidate.Id(tenantId)).ToHashSet();
        var existing = await db.SalesWorkItems
            .Include(item => item.Relations)
            .Where(item => item.SourceRuleCode != null && RuleCodes.Contains(item.SourceRuleCode))
            .ToArrayAsync(cancellationToken);

        var ruleRun = new SalesRuleRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TriggerType = "worklist-refresh",
            Status = RuleRunStatuses.Running,
            StartedAt = now,
            RuleSetVersion = 1
        };
        db.SalesRuleRuns.Add(ruleRun);

        var createdCount = 0;
        var evaluationKeys = new HashSet<(string TargetType, Guid TargetId)>();
        foreach (var candidate in candidates)
        {
            var id = candidate.Id(tenantId);
            var item = existing.SingleOrDefault(existingItem => existingItem.Id == id);
            if (item is null)
            {
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
                    PriorityScore = CalculatePriority(candidate),
                    PriorityCalculatedAt = now,
                    SourceRuleCode = candidate.RuleCode,
                    SourceRuleRunId = ruleRun.Id,
                    RequiresApproval = candidate.RequiresApproval,
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
            else if (item.Status is WorkItemStatuses.Open or WorkItemStatuses.Snoozed or WorkItemStatuses.Resolved)
            {
                var keepSnooze = item.Status == WorkItemStatuses.Snoozed
                    && item.SnoozedUntil.HasValue
                    && item.SnoozedUntil > now;
                if (!keepSnooze)
                {
                    item.Status = WorkItemStatuses.Open;
                    item.SnoozedUntil = null;
                }

                item.WorkItemType = candidate.WorkItemType;
                item.Title = candidate.Title;
                item.Reason = candidate.Reason;
                item.OwnerId = candidate.OwnerId;
                item.DueAt = candidate.DueAt;
                item.PriorityScore = CalculatePriority(candidate);
                item.PriorityCalculatedAt = now;
                item.SourceRuleRunId = ruleRun.Id;
                item.RequiresApproval = candidate.RequiresApproval;
                item.UpdatedAt = now;

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

            // The existing schema identifies an evaluation by target per run.
            // Avoid a duplicate when two rules point to the same CRM entity.
            if (evaluationKeys.Add((candidate.TargetType, candidate.TargetId)))
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
                    WorkItemId = id,
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
        }

        foreach (var item in existing.Where(item => (item.Status is WorkItemStatuses.Open or WorkItemStatuses.Snoozed)
            && !candidateIds.Contains(item.Id)))
        {
            item.Status = WorkItemStatuses.Resolved;
            item.SnoozedUntil = null;
            item.UpdatedAt = now;
            AddEvent(item, "resolved", new { reason = "rule-no-longer-matches" }, actor.Subject, now);
        }

        ruleRun.Status = RuleRunStatuses.Succeeded;
        ruleRun.FinishedAt = DateTimeOffset.UtcNow;
        ruleRun.EvaluatedCount = candidates.Count;
        ruleRun.CreatedCount = createdCount;
        await db.SaveChangesAsync(cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var customers = await db.SalesCustomers.AsNoTracking().ToArrayAsync(cancellationToken);
        var leads = await db.SalesLeads.AsNoTracking().ToArrayAsync(cancellationToken);
        var deals = await db.SalesDeals
            .AsNoTracking()
            .Include(deal => deal.PipelineStage)
            .ToArrayAsync(cancellationToken);
        var contracts = await db.SalesContracts.AsNoTracking().ToArrayAsync(cancellationToken);
        var products = await db.SalesProducts
            .AsNoTracking()
            .Include(product => product.Category)
            .ToArrayAsync(cancellationToken);
        var appointments = await db.SalesAppointments
            .AsNoTracking()
            .Include(appointment => appointment.Relations)
            .ToArrayAsync(cancellationToken);
        var ownerChanges = await db.SalesOwnerChangeRequests.AsNoTracking().ToArrayAsync(cancellationToken);

        var customerNames = customers.ToDictionary(customer => customer.Id, customer => customer.Name);
        var dealNames = deals.ToDictionary(deal => deal.Id, deal => deal.Name);
        var leadNames = leads.ToDictionary(lead => lead.Id, lead => lead.Name);
        var productCategories = products
            .Where(product => product.IsActive && product.CategoryId.HasValue && product.Category is { IsActive: true })
            .GroupBy(product => product.CategoryId!.Value)
            .ToDictionary(group => group.Key, group => group.First().Category!.Name);
        var candidates = new List<WorklistCandidate>();

        foreach (var deal in deals.Where(deal => deal.IsActive
            && IsOpenStatus(deal.Status)
            && !(deal.PipelineStage?.IsTerminal ?? false)))
        {
            var lastActivity = deal.LastActivityAt ?? deal.SourceCreatedAt;
            if (!lastActivity.HasValue || lastActivity > now.AddDays(-30))
                continue;

            var dueAt = lastActivity.Value.AddDays(30);
            var title = deal.CustomerId is { } customerId && customerNames.TryGetValue(customerId, out var customerName)
                ? $"{deal.Name} · {customerName}"
                : deal.Name;
            candidates.Add(new WorklistCandidate(
                "deal-stalled", "R-05", "deal", deal.Id, deal.OwnerId, title,
                $"Offener Deal seit {FormatAge(lastActivity.Value, now)} ohne dokumentierte Aktivität.",
                dueAt, deal.Amount));
        }

        foreach (var contract in contracts.Where(contract => contract.IsActive
            && contract.EndAt is { } endAt
            && endAt >= now
            && endAt <= now.AddDays(90)
            && IsOpenStatus(contract.Status)))
        {
            var daysRemaining = Math.Max(0, (contract.EndAt!.Value - now).TotalDays);
            var critical = daysRemaining <= 30;
            var customerName = customerNames.GetValueOrDefault(contract.CustomerId) ?? "Unbekannter Kunde";
            candidates.Add(new WorklistCandidate(
                critical ? "contract-renewal-critical" : "contract-renewal", "R-06", "contract", contract.Id,
                contract.OwnerId, $"Vertrag verlängern · {customerName}",
                $"Vertragsende am {contract.EndAt.Value:dd.MM.yyyy}; noch {Math.Ceiling(daysRemaining):0} Tage.",
                contract.EndAt.Value, contract.RecurringAmount));
        }

        foreach (var customer in customers.Where(customer => customer.IsActive
            && (!customer.LastContactAt.HasValue || customer.LastContactAt <= now.AddMonths(-3))))
        {
            var dueAt = customer.LastContactAt?.AddMonths(3) ?? (customer.SourceCreatedAt ?? now).AddMonths(3);
            candidates.Add(new WorklistCandidate(
                "customer-stale", "R-07", "customer", customer.Id, customer.OwnerId,
                $"Kontakt aufnehmen · {customer.Name}",
                customer.LastContactAt.HasValue
                    ? $"Letzter Kontakt vor {FormatAge(customer.LastContactAt.Value, now)}."
                    : "Für den Kunden ist noch kein Kontakt dokumentiert.",
                dueAt, customer.LifetimeRevenue));
        }

        foreach (var lead in leads.Where(lead => lead.IsActive
            && IsOpenStatus(lead.Status)
            && !lead.FirstActivityAt.HasValue
            && !lead.LastContactAt.HasValue
            && !lead.LastPhoneCallAt.HasValue
            && lead.SourceCreatedAt is { } createdAt
            && createdAt <= now.AddHours(-1)))
        {
            var dueAt = lead.SourceCreatedAt!.Value.AddHours(1);
            candidates.Add(new WorklistCandidate(
                "lead-first-response", "R-09", "lead", lead.Id, lead.OwnerId,
                $"Lead qualifizieren · {lead.Name}",
                $"Neuer Lead seit {FormatAge(lead.SourceCreatedAt.Value, now)} ohne dokumentierte Aktivität.",
                dueAt, null));
        }

        foreach (var customer in customers.Where(customer => customer.IsActive && productCategories.Count > 0))
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

        foreach (var appointment in appointments.Where(appointment => appointment.IsActive
            && appointment.RescheduleCount >= 3
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
            "contract-renewal" => 70m,
            "ownership-change" => 50m,
            "appointment-rescheduled" => 45m,
            "customer-stale" => 30m,
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

    private static WorklistItemDto ToDto(SalesWorkItem item)
    {
        var relation = item.Relations.FirstOrDefault(relation => relation.RelationRole == "primary")
            ?? item.Relations.FirstOrDefault();
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
            item.CreatedAt,
            item.SnoozedUntil,
            item.RequiresApproval);
    }

    private static string PriorityBand(decimal score)
        => score >= 100 ? "critical" : score >= 70 ? "high" : score >= 40 ? "medium" : "low";

    private static bool IsOpenStatus(string? status)
        => !status.IsOneOf("won", "closed", "closed_won", "lost", "closed_lost", "converted", "cancelled", "canceled", "expired", "terminated", "rejected", "completed", "done", "archived");

    private static bool IsWonStatus(string? status)
        => status.IsOneOf("won", "closed_won", "converted");

    private static bool IsPendingOwnerChange(string? status)
        => status.IsOneOf("pending", "open", "requested", "proposed", "new");

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
        => JsonSerializer.Serialize(new { item.Status, item.CompletedAt, item.CompletedBy, item.SnoozedUntil, item.UpdatedAt });

    private static ActorInfo Actor(ClaimsPrincipal user)
        => new(
            user.FindFirstValue("sub"),
            user.FindFirstValue("name") ?? user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue(ClaimTypes.Email));

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
        new("R-05", "Deal ohne Aktivität", "Offener Deal ohne dokumentierte Aktivität seit mehr als 30 Tagen.", new { inactiveDays = 30 }),
        new("R-06", "Vertragsverlängerung", "Aktiver Vertrag endet innerhalb der nächsten 90 Tage.", new { horizonDays = 90, criticalDays = 30 }),
        new("R-07", "Kundenkontakt überfällig", "Kein Kontakt oder letzter Kontakt liegt mehr als drei Monate zurück.", new { inactiveMonths = 3 }),
        new("R-09", "Lead-Erstreaktion", "Neuer Lead ohne dokumentierte Aktivität nach einer Stunde.", new { responseHours = 1 }),
        new("R-08", "Zuständigkeit klären", "Offener Vorschlag zur Änderung der Zuständigkeit.", new { }),
        new("R-10", "Cross-Selling", "Aktiver Kunde ohne Deal oder Vertrag in einer Produktkategorie.", new { }),
        new("R-12", "Termin mehrfach verschoben", "Termin wurde mindestens dreimal verschoben.", new { minimumReschedules = 3 })
    ];

    private static readonly IReadOnlyDictionary<string, string> WorkItemTypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["contract-renewal-critical"] = "Vertrag läuft bald aus",
        ["lead-first-response"] = "Neuer Lead ohne Erstreaktion",
        ["deal-stalled"] = "Deal ohne Aktivität",
        ["contract-renewal"] = "Vertragsverlängerung",
        ["ownership-change"] = "Zuständigkeit klären",
        ["cross-sell"] = "Cross-Selling-Chance",
        ["appointment-rescheduled"] = "Termin mehrfach verschoben",
        ["customer-stale"] = "Kundenkontakt überfällig"
    };

    private static class WorkItemStatuses
    {
        public const string Open = "open";
        public const string Snoozed = "snoozed";
        public const string Completed = "completed";
        public const string Resolved = "resolved";
    }

    private static class RuleRunStatuses
    {
        public const string Succeeded = "succeeded";
        public const string Running = "running";
    }
}

public sealed record WorklistResponse(
    DateTimeOffset GeneratedAt,
    DateTimeOffset? LastRefreshAt,
    bool OwnerMatched,
    bool TeamView,
    IReadOnlyCollection<WorklistItemDto> Items);

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
    DateTimeOffset CreatedAt,
    DateTimeOffset? SnoozedUntil,
    bool RequiresApproval);

public sealed record SnoozeWorklistItemRequest(DateTimeOffset Until);

internal static class StringStatusExtensions
{
    public static bool IsOneOf(this string? value, params string[] candidates)
        => value is not null && candidates.Contains(value.Trim().ToLowerInvariant(), StringComparer.Ordinal);
}
