using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations;

namespace SalesPlattform.Backend.Services;

public sealed record CrmTaskMirrorFailure(Guid WorkItemId, string Message);

public sealed record CrmTaskMirrorDecision(
    Guid WorkItemId,
    string Subject,
    string Action,
    string? ExternalId,
    string? Message);

public sealed record CrmTaskMirrorResult(
    int ActiveItems,
    int Created,
    int Updated,
    int Unchanged,
    int BaselineEstablished,
    int Failed,
    int Skipped,
    IReadOnlyCollection<CrmTaskMirrorFailure> Failures,
    IReadOnlyCollection<CrmTaskMirrorDecision> Decisions)
{
    public bool HasWarnings => Failed > 0 || Skipped > 0;
}

public sealed record CrmTaskProjection(
    string Subject,
    string? DueDate,
    string? Description,
    string? OwnerExternalId,
    string? TargetEntityType,
    string? TargetExternalId);

/// <summary>
/// Mirrors active Sales work-item occurrences as CRM Tasks. The CRM RemoteId
/// belongs to one occurrence only; a replacement occurrence gets a new link.
/// </summary>
public sealed class CrmTaskMirrorService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    CrmAdapterRegistry adapters,
    ILogger<CrmTaskMirrorService> logger)
{
    private static readonly string[] ActiveStatuses = ["open", "scheduled", "snoozed"];

    public async Task<CrmTaskMirrorResult> EnsureActiveTasksAsync(
        Guid tenantId,
        string? providerKey = null,
        string connectionKey = "default",
        CancellationToken cancellationToken = default)
    {
        var adapter = providerKey is null
            ? await adapters.ResolveCurrentAsync(cancellationToken)
            : adapters.Resolve(providerKey);
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var db = session.Context;
        var now = DateTimeOffset.UtcNow;
        var items = await db.SalesWorkItems
            .Include(item => item.Relations)
            .Where(item => item.TenantId == tenantId && ActiveStatuses.Contains(item.Status))
            .OrderBy(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);
        if (items.Length == 0)
            return new CrmTaskMirrorResult(0, 0, 0, 0, 0, 0, 0, [], []);

        var timeZone = await ResolveTenantTimeZoneAsync(db, cancellationToken);
        var workItemIds = items.Select(item => item.Id).ToArray();
        var existingLinks = (await db.IntegrationEntityLinks
            .Where(link => link.TenantId == tenantId
                && link.ProviderKey == adapter.ProviderKey
                && link.ConnectionKey == connectionKey
                && link.EntityType == CrmEntityTypes.Activity
                && link.WorkItemId.HasValue
                && workItemIds.Contains(link.WorkItemId.Value)
                && link.SourceDeletedAt == null)
            .ToArrayAsync(cancellationToken))
            .GroupBy(link => link.WorkItemId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(link => link.LastSeenAt).First());
        var createdCount = 0;
        var updatedCount = 0;
        var unchangedCount = 0;
        var baselineEstablishedCount = 0;
        var failedCount = 0;
        var skippedCount = 0;
        var outboundStateChanged = false;
        var failures = new List<CrmTaskMirrorFailure>();
        var decisions = new List<CrmTaskMirrorDecision>(items.Length);

        foreach (var item in items)
        {
            var relation = item.Relations.FirstOrDefault(candidate => candidate.RelationRole == "primary")
                ?? item.Relations.FirstOrDefault();
            var targetLink = relation is null
                ? null
                : await db.IntegrationEntityLinks
                    .AsNoTracking()
                    .Where(link => link.TenantId == tenantId
                        && link.ProviderKey == adapter.ProviderKey
                        && link.ConnectionKey == connectionKey
                        && link.InternalEntityType == relation.TargetType
                        && link.InternalEntityId == relation.TargetId
                        && link.SourceDeletedAt == null)
                    .OrderByDescending(link => link.LastSeenAt)
                    .FirstOrDefaultAsync(cancellationToken);
            var ownerExternalId = item.OwnerId is null
                ? null
                : await db.IntegrationEntityLinks
                    .AsNoTracking()
                    .Where(link => link.TenantId == tenantId
                        && link.ProviderKey == adapter.ProviderKey
                        && link.ConnectionKey == connectionKey
                        && link.InternalEntityType == CrmEntityTypes.Owner
                        && link.InternalEntityId == item.OwnerId.Value
                        && link.SourceDeletedAt == null)
                    .OrderByDescending(link => link.LastSeenAt)
                    .Select(link => link.ExternalId)
                    .FirstOrDefaultAsync(cancellationToken);

            if (relation is not null && targetLink is null)
            {
                skippedCount++;
                var message = $"Ziel {relation.TargetType}/{relation.TargetId} besitzt keine aktive CRM-Remote-ID.";
                failures.Add(new CrmTaskMirrorFailure(item.Id, message));
                decisions.Add(new CrmTaskMirrorDecision(
                    item.Id,
                    item.Title,
                    "skipped",
                    null,
                    message));
                logger.LogWarning(
                    "CRM-Aufgabe für Arbeitsvorgang {WorkItemId} wird zurückgestellt, weil {Message}",
                    item.Id,
                    message);
                continue;
            }

            var request = new CrmTaskWriteRequest(
                item.Title,
                ResolveCrmDueAt(item, now, timeZone),
                item.Reason,
                ownerExternalId,
                relation?.TargetType,
                targetLink?.ExternalId);
            var projection = BuildProjection(request);
            var projectionJson = JsonSerializer.Serialize(projection);

            if (existingLinks.TryGetValue(item.Id, out var existingLink))
            {
                if (existingLink.LastOutboundTaskProjectionJson is null)
                {
                    // Existing links predate the outbound projection marker.
                    // Their task was already created from the current model;
                    // establish a baseline without causing a one-time write
                    // burst. Future content changes are compared exactly.
                    existingLink.LastOutboundTaskProjectionJson = projectionJson;
                    outboundStateChanged = true;
                    baselineEstablishedCount++;
                    decisions.Add(new CrmTaskMirrorDecision(
                        item.Id,
                        item.Title,
                        "baseline-established",
                        existingLink.ExternalId,
                        "Bestehender CRM-Task wurde als Ausgangsstand übernommen; kein Update gesendet."));
                    continue;
                }

                if (string.Equals(
                        existingLink.LastOutboundTaskProjectionJson,
                        projectionJson,
                        StringComparison.Ordinal))
                {
                    unchangedCount++;
                    decisions.Add(new CrmTaskMirrorDecision(
                        item.Id,
                        item.Title,
                        "unchanged",
                        existingLink.ExternalId,
                        "Task-Inhalt unverändert; kein Update gesendet."));
                    continue;
                }

                try
                {
                    await adapter.UpdateTaskAsync(existingLink.ExternalId, request, cancellationToken);
                    existingLink.LastSeenAt = now;
                    existingLink.LastOutboundTaskProjectionJson = projectionJson;
                    outboundStateChanged = true;
                    updatedCount++;
                    decisions.Add(new CrmTaskMirrorDecision(
                        item.Id,
                        item.Title,
                        "updated",
                        existingLink.ExternalId,
                        "Task-Inhalt geändert; Update gesendet."));
                }
                catch (CrmApiRateLimitException exception)
                {
                    failedCount++;
                    var message = exception.Message;
                    failures.Add(new CrmTaskMirrorFailure(item.Id, message));
                    decisions.Add(new CrmTaskMirrorDecision(
                        item.Id,
                        item.Title,
                        "failed",
                        existingLink.ExternalId,
                        message));
                    logger.LogWarning(
                        exception,
                        "CRM-Aufgaben-Abgleich wegen Zoho-API-Limit nach Arbeitsvorgang {WorkItemId} beendet.",
                        item.Id);
                    break;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failedCount++;
                    var message = exception.Message;
                    failures.Add(new CrmTaskMirrorFailure(item.Id, message));
                    decisions.Add(new CrmTaskMirrorDecision(
                        item.Id,
                        item.Title,
                        "failed",
                        existingLink.ExternalId,
                        message));
                    logger.LogError(
                        exception,
                        "CRM-Aufgabe für Arbeitsvorgang {WorkItemId} konnte nicht aktualisiert werden.",
                        item.Id);
                }

                continue;
            }

            CrmTaskWriteResult created;
            try
            {
                created = await adapter.CreateTaskAsync(request, cancellationToken);
            }
            catch (CrmApiRateLimitException exception)
            {
                failedCount++;
                var message = exception.Message;
                failures.Add(new CrmTaskMirrorFailure(item.Id, message));
                decisions.Add(new CrmTaskMirrorDecision(
                    item.Id,
                    item.Title,
                    "failed",
                    null,
                    message));
                logger.LogWarning(
                    exception,
                    "CRM-Aufgaben-Abgleich wegen Zoho-API-Limit nach Arbeitsvorgang {WorkItemId} beendet.",
                    item.Id);
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failedCount++;
                var message = exception.Message;
                failures.Add(new CrmTaskMirrorFailure(item.Id, message));
                decisions.Add(new CrmTaskMirrorDecision(
                    item.Id,
                    item.Title,
                    "failed",
                    null,
                    message));
                logger.LogError(
                    exception,
                    "CRM-Aufgabe für Arbeitsvorgang {WorkItemId} konnte nicht angelegt werden.",
                    item.Id);
                continue;
            }

            var activity = new SalesActivity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ActivityType = "task",
                Subject = item.Title,
                OccurredAt = request.DueAt ?? now,
                OwnerId = item.OwnerId,
                SourceCreatedAt = now,
                SourceModifiedAt = now,
                LastSeenAt = now
            };
            foreach (var itemRelation in item.Relations)
            {
                activity.Relations.Add(new SalesActivityRelation
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ActivityId = activity.Id,
                    TargetType = itemRelation.TargetType,
                    TargetId = itemRelation.TargetId,
                    RelationRole = itemRelation.RelationRole
                });
            }

            db.SalesActivities.Add(activity);
            db.IntegrationEntityLinks.Add(new IntegrationEntityLink
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProviderKey = created.Provider,
                ConnectionKey = created.ConnectionKey,
                EntityType = CrmEntityTypes.Activity,
                ExternalId = created.ExternalId,
                ExternalUrl = created.ExternalUrl,
                InternalEntityType = CrmEntityTypes.Activity,
                InternalEntityId = activity.Id,
                WorkItemId = item.Id,
                LastOutboundTaskProjectionJson = projectionJson,
                LastSeenAt = now
            });
            db.IntegrationRawRecords.Add(new IntegrationRawRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProviderKey = created.Provider,
                ConnectionKey = created.ConnectionKey,
                EntityType = CrmEntityTypes.Activity,
                ExternalId = created.ExternalId,
                PayloadJson = created.Payload.GetRawText(),
                FirstSeenAt = now,
                LastSeenAt = now,
                SyncedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            createdCount++;
            outboundStateChanged = true;
            decisions.Add(new CrmTaskMirrorDecision(
                item.Id,
                item.Title,
                "created",
                created.ExternalId,
                "CRM-Task angelegt."));
        }

        if (outboundStateChanged)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new CrmTaskMirrorResult(
            items.Length,
            createdCount,
            updatedCount,
            unchangedCount,
            baselineEstablishedCount,
            failedCount,
            skippedCount,
            failures.Take(10).ToArray(),
            decisions);
    }

    private static CrmTaskProjection BuildProjection(CrmTaskWriteRequest request)
        => new(
            request.Subject,
            request.DueAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            NormalizeExternalId(request.OwnerExternalId),
            NormalizeTargetType(request.TargetEntityType),
            NormalizeExternalId(request.TargetExternalId));

    private static string? NormalizeTargetType(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? NormalizeExternalId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var separator = trimmed.LastIndexOf(':');
        return separator >= 0 && separator < trimmed.Length - 1
            ? trimmed[(separator + 1)..]
            : trimmed;
    }

    private static DateTimeOffset ResolveCrmDueAt(
        SalesWorkItem item,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var deferredUntil = item.Status is "scheduled" or "snoozed"
            ? item.AvailableFrom ?? item.SnoozedUntil
            : null;
        if (deferredUntil is not null && deferredUntil > now)
            return deferredUntil.Value;

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localToday = DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified);
        return new DateTimeOffset(localToday, localNow.Offset);
    }

    private static async Task<TimeZoneInfo> ResolveTenantTimeZoneAsync(
        SalesPlattformDbContext db,
        CancellationToken cancellationToken)
    {
        var timeZoneId = await db.SalesWorkCalendars
            .AsNoTracking()
            .Where(calendar => calendar.IsDefault && calendar.IsActive)
            .OrderBy(calendar => calendar.Name)
            .Select(calendar => calendar.TimeZone)
            .FirstOrDefaultAsync(cancellationToken)
            ?? "Europe/Berlin";

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
