using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations.Zoho;

namespace SalesPlattform.Backend.Services;

/// <summary>
/// Mirrors active Sales work-item occurrences as CRM Tasks. The CRM RemoteId
/// belongs to one occurrence only; a replacement occurrence gets a new link.
/// </summary>
public sealed class CrmTaskMirrorService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ZohoCrmAdapter adapter,
    ILogger<CrmTaskMirrorService> logger)
{
    private static readonly string[] ActiveStatuses = ["open", "scheduled", "snoozed"];

    public async Task EnsureActiveTasksAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var db = session.Context;
        var now = DateTimeOffset.UtcNow;
        var items = await db.SalesWorkItems
            .Include(item => item.Relations)
            .Where(item => item.TenantId == tenantId && ActiveStatuses.Contains(item.Status))
            .OrderBy(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);
        if (items.Length == 0)
            return;

        var workItemIds = items.Select(item => item.Id).ToArray();
        var existingLinks = (await db.IntegrationEntityLinks
            .Where(link => link.TenantId == tenantId
                && link.ProviderKey == adapter.ProviderKey
                && link.ConnectionKey == "default"
                && link.EntityType == CrmEntityTypes.Activity
                && link.WorkItemId.HasValue
                && workItemIds.Contains(link.WorkItemId.Value)
                && link.SourceDeletedAt == null)
            .ToArrayAsync(cancellationToken))
            .GroupBy(link => link.WorkItemId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(link => link.LastSeenAt).First());

        foreach (var item in items)
        {
            if (existingLinks.ContainsKey(item.Id))
                continue;

            var relation = item.Relations.FirstOrDefault(candidate => candidate.RelationRole == "primary")
                ?? item.Relations.FirstOrDefault();
            var targetLink = relation is null
                ? null
                : await db.IntegrationEntityLinks
                    .AsNoTracking()
                    .Where(link => link.TenantId == tenantId
                        && link.ProviderKey == adapter.ProviderKey
                        && link.ConnectionKey == "default"
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
                        && link.ConnectionKey == "default"
                        && link.InternalEntityType == CrmEntityTypes.Owner
                        && link.InternalEntityId == item.OwnerId.Value
                        && link.SourceDeletedAt == null)
                    .OrderByDescending(link => link.LastSeenAt)
                    .Select(link => link.ExternalId)
                    .FirstOrDefaultAsync(cancellationToken);

            if (relation is not null && targetLink is null)
            {
                logger.LogWarning(
                    "CRM-Aufgabe für Arbeitsvorgang {WorkItemId} wird zurückgestellt, weil das Ziel {TargetType}/{TargetId} keine aktive Remote-ID besitzt.",
                    item.Id,
                    relation.TargetType,
                    relation.TargetId);
                continue;
            }

            CrmTaskWriteResult created;
            try
            {
                created = await adapter.CreateTaskAsync(
                    new CrmTaskWriteRequest(
                        item.Title,
                        item.DueAt,
                        item.Reason,
                        ownerExternalId,
                        relation?.TargetType,
                        targetLink?.ExternalId),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
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
                OccurredAt = item.DueAt ?? now,
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
        }
    }
}
