using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations;

namespace SalesPlattform.Backend.Integrations.Repositories;

public sealed class SalesCrmRepositoryFactory : ISalesCrmRepositoryFactory
{
    public ISalesCrmRepository Create(SalesPlattformDbContext db)
        => new SalesCrmRepository(db);
}

internal sealed class SalesCrmRepository(SalesPlattformDbContext db) : ISalesCrmRepository
{
    public Task<bool> HasActiveSyncRunAsync(
        string providerKey,
        string connectionKey,
        CancellationToken cancellationToken)
        => db.IntegrationSyncRuns
            .AsNoTracking()
            .AnyAsync(item => item.ProviderKey == providerKey
                && item.ConnectionKey == connectionKey
                && (item.Status == "queued" || item.Status == "running"), cancellationToken);

    public void AddSyncRun(IntegrationSyncRun run)
        => db.IntegrationSyncRuns.Add(run);

    public async Task<IntegrationSyncRun?> GetSyncRunAsync(
        Guid runId,
        bool includeItems,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        IQueryable<IntegrationSyncRun> query = db.IntegrationSyncRuns;
        if (asNoTracking) query = query.AsNoTracking();
        if (includeItems)
        {
            query = query
                .Include(item => item.Items)
                    .ThenInclude(item => item.Errors)
                .Include(item => item.Errors);
            query = query.AsSplitQuery();
        }
        return await query.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<IntegrationSyncRun>> GetActiveSyncRunsAsync(
        string providerKey,
        string connectionKey,
        bool includeItems,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        IQueryable<IntegrationSyncRun> query = db.IntegrationSyncRuns;
        if (asNoTracking) query = query.AsNoTracking();
        if (includeItems)
        {
            query = query
                .Include(item => item.Items)
                    .ThenInclude(item => item.Errors)
                .Include(item => item.Errors);
            query = query.AsSplitQuery();
        }
        return await query
            .Where(item => item.ProviderKey == providerKey
                && item.ConnectionKey == connectionKey
                && (item.Status == "queued" || item.Status == "running"))
            .OrderBy(item => item.QueuedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<IntegrationSyncRun>> GetRecentSyncRunsAsync(
        string providerKey,
        string connectionKey,
        int limit,
        bool includeItems,
        CancellationToken cancellationToken)
    {
        IQueryable<IntegrationSyncRun> query = db.IntegrationSyncRuns.AsNoTracking();
        if (includeItems)
        {
            query = query
                .Include(item => item.Items)
                    .ThenInclude(item => item.Errors)
                .Include(item => item.Errors);
            query = query.AsSplitQuery();
        }
        return await query
            .Where(item => item.ProviderKey == providerKey
                && item.ConnectionKey == connectionKey)
            .OrderByDescending(item => item.QueuedAt)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    public Task<IntegrationSyncRunItem> GetOrCreateSyncRunItemAsync(
        IntegrationSyncRun run,
        string module,
        CancellationToken cancellationToken)
    {
        var item = run.Items.SingleOrDefault(candidate =>
            string.Equals(candidate.Module, module, StringComparison.OrdinalIgnoreCase));
        if (item is not null) return Task.FromResult(item);

        item = new IntegrationSyncRunItem
        {
            Id = Guid.NewGuid(),
            SyncRunId = run.Id,
            Module = module,
            Status = "queued"
        };
        run.Items.Add(item);
        db.IntegrationSyncRunItems.Add(item);
        return Task.FromResult(item);
    }

    public async Task UpsertAsync(
        CrmCanonicalRecord record,
        Guid syncRunId,
        int callConversationThresholdSeconds,
        CancellationToken cancellationToken)
    {
        var raw = await db.IntegrationRawRecords
            .SingleOrDefaultAsync(item => item.ProviderKey == record.ProviderKey
                && item.ConnectionKey == record.ConnectionKey
                && item.EntityType == record.EntityType
                && item.ExternalId == record.ExternalId, cancellationToken);
        if (raw is null)
        {
            raw = new IntegrationRawRecord
            {
                Id = Guid.NewGuid(),
                ProviderKey = record.ProviderKey,
                ConnectionKey = record.ConnectionKey,
                EntityType = record.EntityType,
                ExternalId = record.ExternalId,
                PayloadJson = record.Payload.GetRawText(),
                ExternalModifiedAt = record.ModifiedAt,
                FirstSeenAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
                SyncRunId = syncRunId,
                SyncedAt = DateTimeOffset.UtcNow
            };
            db.IntegrationRawRecords.Add(raw);
        }
        else
        {
            raw.PayloadJson = record.Payload.GetRawText();
            raw.ExternalModifiedAt = record.ModifiedAt;
            raw.LastSeenAt = DateTimeOffset.UtcNow;
            raw.SourceDeletedAt = null;
            raw.SyncRunId = syncRunId;
            raw.SyncedAt = DateTimeOffset.UtcNow;
        }

        switch (record)
        {
            case CrmCanonicalOwner owner:
                await UpsertOwnerAsync(owner, cancellationToken);
                break;
            case CrmCanonicalCustomer customer:
                await UpsertCustomerAsync(customer, cancellationToken);
                break;
            case CrmCanonicalLead lead:
                await UpsertLeadAsync(lead, cancellationToken);
                break;
            case CrmCanonicalProductCategory category:
                await UpsertProductCategoryAsync(category, cancellationToken);
                break;
            case CrmCanonicalProduct product:
                await UpsertProductAsync(product, cancellationToken);
                break;
            case CrmCanonicalPipeline pipeline:
                await UpsertPipelineAsync(pipeline, cancellationToken);
                break;
            case CrmCanonicalPipelineStage stage:
                await UpsertPipelineStageAsync(stage, cancellationToken);
                break;
            case CrmCanonicalDeal deal:
                await UpsertDealAsync(deal, cancellationToken);
                break;
            case CrmCanonicalDealStageHistory history:
                await UpsertDealStageHistoryAsync(history, cancellationToken);
                break;
            case CrmCanonicalContract contract:
                await UpsertContractAsync(contract, cancellationToken);
                break;
            case CrmCanonicalActivity activity:
                await UpsertActivityAsync(activity, callConversationThresholdSeconds, cancellationToken);
                break;
            case CrmCanonicalAppointment appointment:
                await UpsertAppointmentAsync(appointment, cancellationToken);
                break;
            case CrmCanonicalServiceCase serviceCase:
                await UpsertServiceCaseAsync(serviceCase, cancellationToken);
                break;
            case CrmCanonicalOffer offer:
                await UpsertOfferAsync(offer, cancellationToken);
                break;
            case CrmCanonicalOrder order:
                await UpsertOrderAsync(order, cancellationToken);
                break;
            case CrmCanonicalInvoice invoice:
                await UpsertInvoiceAsync(invoice, cancellationToken);
                break;
            default:
                throw new InvalidOperationException(
                    $"Das kanonische CRM-Objekt '{record.EntityType}' wird noch nicht unterstützt.");
        }
    }

    public async Task<IntegrationSyncCursor> GetOrCreateCursorAsync(
        string providerKey,
        string connectionKey,
        string entityType,
        CancellationToken cancellationToken)
    {
        var cursor = await db.IntegrationSyncCursors
            .SingleOrDefaultAsync(item => item.ProviderKey == providerKey
                && item.ConnectionKey == connectionKey
                && item.EntityType == entityType, cancellationToken);
        if (cursor is not null) return cursor;

        cursor = new IntegrationSyncCursor
        {
            Id = Guid.NewGuid(),
            ProviderKey = providerKey,
            ConnectionKey = connectionKey,
            EntityType = entityType
        };
        db.IntegrationSyncCursors.Add(cursor);
        return cursor;
    }

    public async Task<IReadOnlyCollection<string>> GetExternalIdsAsync(
        string providerKey,
        string connectionKey,
        string entityType,
        CancellationToken cancellationToken)
        => await db.IntegrationEntityLinks
            .AsNoTracking()
            .Where(item => item.ProviderKey == providerKey
                && item.ConnectionKey == connectionKey
                && item.EntityType == entityType
                && item.SourceDeletedAt == null)
            .Select(item => item.ExternalId)
            .ToArrayAsync(cancellationToken);

    public async Task<bool> MarkDeletedAsync(
        CrmDeletedRecord record,
        Guid syncRunId,
        CancellationToken cancellationToken)
    {
        var link = await db.IntegrationEntityLinks
            .SingleOrDefaultAsync(item => item.ProviderKey == record.Provider
                && item.ConnectionKey == record.ConnectionKey
                && item.EntityType == record.EntityType
                && item.ExternalId == record.ExternalId, cancellationToken);
        if (link is null || link.SourceDeletedAt is not null)
            return false;

        link.SourceDeletedAt = record.DeletedAt;
        var raw = await db.IntegrationRawRecords
            .SingleOrDefaultAsync(item => item.ProviderKey == record.Provider
                && item.ConnectionKey == record.ConnectionKey
                && item.EntityType == record.EntityType
                && item.ExternalId == record.ExternalId, cancellationToken);
        if (raw is not null)
        {
            raw.SourceDeletedAt = record.DeletedAt;
            raw.SyncRunId = syncRunId;
            raw.SyncedAt = DateTimeOffset.UtcNow;
        }

        switch (link.InternalEntityType)
        {
            case CrmEntityTypes.Owner:
                var owner = await db.SalesOwners.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (owner is not null) { owner.IsActive = false; owner.SourceDeletedAt = record.DeletedAt; }
                break;
            case CrmEntityTypes.Customer:
                var customer = await db.SalesCustomers.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (customer is not null) { customer.IsActive = false; customer.SourceDeletedAt = record.DeletedAt; }
                break;
            case CrmEntityTypes.Lead:
                var lead = await db.SalesLeads.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (lead is not null) { lead.IsActive = false; lead.SourceDeletedAt = record.DeletedAt; }
                break;
            case CrmEntityTypes.Product:
                var product = await db.SalesProducts.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (product is not null) { product.IsActive = false; product.SourceDeletedAt = record.DeletedAt; }
                break;
            case CrmEntityTypes.ProductCategory:
                var category = await db.SalesProductCategories.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (category is not null) category.IsActive = false;
                break;
            case CrmEntityTypes.Pipeline:
                var pipeline = await db.SalesPipelines.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (pipeline is not null) pipeline.IsActive = false;
                break;
            case CrmEntityTypes.PipelineStage:
                var stage = await db.SalesPipelineStages.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (stage is not null) stage.IsActive = false;
                break;
            case CrmEntityTypes.Deal:
                var deal = await db.SalesDeals.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (deal is not null) { deal.IsActive = false; deal.SourceDeletedAt = record.DeletedAt; }
                break;
            case CrmEntityTypes.Activity:
                var activity = await db.SalesActivities.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (activity is not null) activity.SourceDeletedAt = record.DeletedAt;
                break;
            case CrmEntityTypes.Appointment:
                var appointment = await db.SalesAppointments.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (appointment is not null) { appointment.IsActive = false; appointment.SourceDeletedAt = record.DeletedAt; }
                break;
            case CrmEntityTypes.ServiceCase:
                var serviceCase = await db.SalesServiceCases.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (serviceCase is not null) { serviceCase.IsActive = false; serviceCase.SourceDeletedAt = record.DeletedAt; }
                break;
            case CrmEntityTypes.Offer:
                var offer = await db.SalesOffers.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (offer is not null) { offer.IsActive = false; offer.SourceDeletedAt = record.DeletedAt; }
                break;
            case CrmEntityTypes.Order:
                var order = await db.SalesOrders.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (order is not null) { order.IsActive = false; order.SourceDeletedAt = record.DeletedAt; }
                break;
            case CrmEntityTypes.Invoice:
                var invoice = await db.SalesInvoices.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken);
                if (invoice is not null) { invoice.IsActive = false; invoice.SourceDeletedAt = record.DeletedAt; }
                break;
        }

        return true;
    }

    public async Task BackfillLeadActivityMarkersAsync(
        CancellationToken cancellationToken)
    {
        var activityMarkers = await db.SalesActivityRelations
            .AsNoTracking()
            .Where(relation => relation.TargetType == CrmEntityTypes.Lead)
            .Join(
                db.SalesActivities.AsNoTracking().Where(activity => activity.SourceDeletedAt == null),
                relation => relation.ActivityId,
                activity => activity.Id,
                (relation, activity) => new
                {
                    LeadId = relation.TargetId,
                    activity.OccurredAt
                })
            .GroupBy(item => item.LeadId)
            .Select(group => new
            {
                LeadId = group.Key,
                FirstActivityAt = group.Min(item => item.OccurredAt)
            })
            .ToArrayAsync(cancellationToken);

        if (activityMarkers.Length == 0)
            return;

        var leadIds = activityMarkers.Select(marker => marker.LeadId).ToArray();
        var leads = await db.SalesLeads
            .Where(lead => leadIds.Contains(lead.Id))
            .ToDictionaryAsync(lead => lead.Id, cancellationToken);

        foreach (var marker in activityMarkers)
        {
            if (leads.TryGetValue(marker.LeadId, out var lead))
                RegisterLeadActivity(lead, marker.FirstActivityAt);
        }
    }

    public async Task RecalculateLeadCallCountersAsync(
        IReadOnlyCollection<CrmSynchronizationChange> changes,
        int callConversationThresholdSeconds,
        CancellationToken cancellationToken)
    {
        var activityChanges = changes
            .Where(change => change.EntityType == CrmEntityTypes.Activity)
            .ToArray();
        if (activityChanges.Length == 0)
            return;

        var providerKeys = activityChanges.Select(change => change.ProviderKey).Distinct().ToArray();
        var connectionKeys = activityChanges.Select(change => change.ConnectionKey).Distinct().ToArray();
        var externalIds = activityChanges.Select(change => change.ExternalId).Distinct().ToArray();
        var changedKeys = activityChanges
            .Select(change => (change.ProviderKey, change.ConnectionKey, change.ExternalId))
            .ToHashSet();
        var activityLinks = await db.IntegrationEntityLinks
            .AsNoTracking()
            .Where(link => providerKeys.Contains(link.ProviderKey)
                && connectionKeys.Contains(link.ConnectionKey)
                && link.EntityType == CrmEntityTypes.Activity
                && externalIds.Contains(link.ExternalId))
            .Select(link => new
            {
                link.ProviderKey,
                link.ConnectionKey,
                link.ExternalId,
                link.InternalEntityId
            })
            .ToArrayAsync(cancellationToken);
        var changedActivityIds = activityLinks
            .Where(link => changedKeys.Contains((link.ProviderKey, link.ConnectionKey, link.ExternalId)))
            .Select(link => link.InternalEntityId)
            .Distinct()
            .ToArray();
        if (changedActivityIds.Length == 0)
            return;

        var leadIds = await db.SalesActivityRelations
            .AsNoTracking()
            .Where(relation => changedActivityIds.Contains(relation.ActivityId)
                && relation.TargetType == CrmEntityTypes.Lead)
            .Select(relation => relation.TargetId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (leadIds.Length == 0)
            return;

        var calls = await db.SalesActivityRelations
            .AsNoTracking()
            .Where(relation => leadIds.Contains(relation.TargetId)
                && relation.TargetType == CrmEntityTypes.Lead)
            .Join(
                db.SalesActivities.AsNoTracking().Where(activity => activity.ActivityType == "call"
                    && activity.SourceDeletedAt == null),
                relation => relation.ActivityId,
                activity => activity.Id,
                (relation, activity) => new
                {
                    activity.Id,
                    relation.TargetId,
                    activity.OccurredAt,
                    activity.DurationSeconds,
                    activity.ConnectionStatus,
                    activity.Result
                })
            .OrderByDescending(item => item.OccurredAt)
            .ToArrayAsync(cancellationToken);
        var activityIds = calls
            .Select(call => call.Id)
            .Distinct()
            .ToArray();
        var activities = await db.SalesActivities
            .Where(activity => activityIds.Contains(activity.Id))
            .ToDictionaryAsync(activity => activity.Id, cancellationToken);
        var classifiedCalls = calls
            .Select(call => new
            {
                call.TargetId,
                call.OccurredAt,
                CountsAsConversation = CallQualification.IsConversation(
                    call.DurationSeconds,
                    call.ConnectionStatus,
                    call.Result,
                    callConversationThresholdSeconds)
            })
            .ToArray();
        foreach (var call in calls)
        {
            if (!activities.TryGetValue(call.Id, out var activity))
                continue;

            activity.CountsAsConversation = call.DurationSeconds.HasValue
                ? CallQualification.IsConversation(
                    call.DurationSeconds,
                    call.ConnectionStatus,
                    call.Result,
                    callConversationThresholdSeconds)
                : null;
            activity.ConversationClass = CallQualification.ConversationClass(
                true,
                call.DurationSeconds,
                call.ConnectionStatus,
                call.Result,
                callConversationThresholdSeconds);
        }
        var leads = await db.SalesLeads
            .Where(lead => leadIds.Contains(lead.Id))
            .ToDictionaryAsync(lead => lead.Id, cancellationToken);

        foreach (var group in classifiedCalls.GroupBy(call => call.TargetId))
        {
            if (!leads.TryGetValue(group.Key, out var lead))
                continue;

            var orderedCalls = group.OrderByDescending(call => call.OccurredAt).ToArray();
            lead.TotalCallAttempts = orderedCalls.Length;
            var callsSinceConversation = 0;
            foreach (var call in orderedCalls)
            {
                if (call.CountsAsConversation == true)
                    break;
                callsSinceConversation++;
            }
            lead.CallsSinceConversation = callsSinceConversation;
        }

        foreach (var leadId in leadIds.Where(leadId => !classifiedCalls.Any(call => call.TargetId == leadId)))
        {
            if (leads.TryGetValue(leadId, out var lead))
            {
                lead.TotalCallAttempts = 0;
                lead.CallsSinceConversation = 0;
            }
        }
    }

    public void AddSyncError(
        Guid syncRunId,
        Guid syncRunItemId,
        string module,
        string? externalId,
        Exception exception)
        => db.IntegrationSyncErrors.Add(new IntegrationSyncError
        {
            Id = Guid.NewGuid(),
            SyncRunId = syncRunId,
            SyncRunItemId = syncRunItemId,
            Module = module,
            ExternalId = externalId,
            ErrorCode = exception.GetType().Name,
            Message = IntegrationErrorFormatter.Describe(exception),
            Retryable = false,
            Attempt = 1,
            OccurredAt = DateTimeOffset.UtcNow
        });

    public Task ClearSyncErrorsAsync(
        Guid syncRunId,
        CancellationToken cancellationToken)
        => db.IntegrationSyncErrors
            .Where(error => error.SyncRunId == syncRunId)
            .ExecuteDeleteAsync(cancellationToken);

    public void DetachRecordChanges()
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(entry =>
                     entry.Entity is not IntegrationSyncRun
                     && entry.Entity is not IntegrationSyncRunItem
                     && entry.Entity is not IntegrationSyncError
                     && entry.Entity is not IntegrationSyncCursor))
        {
            entry.State = EntityState.Detached;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => db.SaveChangesAsync(cancellationToken);

    private async Task UpsertOwnerAsync(CrmCanonicalOwner record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var owner = link is null
            ? new SalesOwner { Id = Guid.NewGuid(), DisplayName = record.DisplayName }
            : await db.SalesOwners.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Owner", link.InternalEntityId);
        if (link is null)
        {
            db.SalesOwners.Add(owner);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Owner, owner.Id));
        }
        owner.DisplayName = record.DisplayName;
        owner.Email = record.Email;
        owner.IsActive = record.IsActive;
        owner.SourceCreatedAt = record.CreatedAt;
        owner.SourceModifiedAt = record.ModifiedAt;
        owner.LastSeenAt = DateTimeOffset.UtcNow;
    }

    private async Task UpsertCustomerAsync(CrmCanonicalCustomer record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var customer = link is null
            ? new SalesCustomer { Id = Guid.NewGuid(), Name = record.Name }
            : await db.SalesCustomers.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Kunde", link.InternalEntityId);
        var previousStatus = customer.Status;
        var previousOwnerId = customer.OwnerId;
        var isNewCustomer = link is null;
        if (link is null)
        {
            db.SalesCustomers.Add(customer);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Customer, customer.Id));
        }

        customer.Name = record.Name;
        customer.LegalName = record.LegalName;
        customer.TaxNumber = record.TaxNumber;
        customer.WebsiteDomain = record.WebsiteDomain;
        customer.Industry = record.Industry;
        customer.PostalCode = record.PostalCode;
        customer.City = record.City;
        customer.RegionCode = record.RegionCode;
        customer.CountryCode = record.CountryCode;
        customer.AddressLine1 = record.AddressLine1;
        customer.HouseNumber = record.HouseNumber;
        if (record.Latitude.HasValue && record.Longitude.HasValue)
        {
            customer.Latitude = record.Latitude;
            customer.Longitude = record.Longitude;
            customer.GeocodingStatus = "crm";
        }
        customer.OwnerId = await FindInternalIdAsync(record, CrmEntityTypes.Owner, record.OwnerExternalId, cancellationToken);
        if (isNewCustomer || previousOwnerId != customer.OwnerId || !customer.OwnerAssignedAt.HasValue)
            customer.OwnerAssignedAt = record.ModifiedAt ?? record.CreatedAt ?? DateTimeOffset.UtcNow;
        customer.Status = record.Status ?? customer.Status;
        customer.IsActive = true;
        customer.NeedsReview = string.IsNullOrWhiteSpace(customer.CountryCode)
            || string.IsNullOrWhiteSpace(customer.PostalCode)
            || string.IsNullOrWhiteSpace(customer.Industry);
        customer.SourceCreatedAt = record.CreatedAt;
        customer.SourceModifiedAt = record.ModifiedAt;
        customer.LastSeenAt = DateTimeOffset.UtcNow;
        SetLinkSeen(link, record);

        if (isNewCustomer || !string.Equals(previousStatus, customer.Status, StringComparison.OrdinalIgnoreCase))
        {
            db.SalesCustomerStatusHistories.Add(new SalesCustomerStatusHistory
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                Status = customer.Status,
                ValidFrom = record.ModifiedAt ?? record.CreatedAt ?? DateTimeOffset.UtcNow
            });
        }
    }

    private async Task UpsertLeadAsync(CrmCanonicalLead record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var lead = link is null
            ? new SalesLead { Id = Guid.NewGuid(), Name = record.Name }
            : await db.SalesLeads.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Lead", link.InternalEntityId);
        if (link is null)
        {
            db.SalesLeads.Add(lead);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Lead, lead.Id));
        }

        lead.Name = record.Name;
        lead.CompanyName = record.CompanyName;
        lead.CustomerId = await FindInternalIdAsync(record, CrmEntityTypes.Customer, record.CustomerExternalId, cancellationToken);
        lead.OwnerId = await FindInternalIdAsync(record, CrmEntityTypes.Owner, record.OwnerExternalId, cancellationToken);
        lead.Email = record.Email;
        lead.NormalizedEmail = NormalizeEmail(record.Email);
        lead.Phone = record.Phone;
        lead.NormalizedPhone = NormalizePhone(record.Phone);
        lead.Status = record.Status ?? lead.Status;
        lead.Source = record.Source;
        if (record.LastContactAt is { } lastContactAt)
        {
            if (lead.LastContactAt is null || lead.LastContactAt < lastContactAt)
                lead.LastContactAt = lastContactAt;
            RegisterLeadActivity(lead, lastContactAt);
        }
        if (record.LastPhoneCallAt is { } lastPhoneCallAt)
        {
            if (lead.LastPhoneCallAt is null || lead.LastPhoneCallAt < lastPhoneCallAt)
                lead.LastPhoneCallAt = lastPhoneCallAt;
            RegisterLeadActivity(lead, lastPhoneCallAt);
        }
        if (record.CallsSinceConversation.HasValue) lead.CallsSinceConversation = record.CallsSinceConversation.Value;
        if (record.TotalCallAttempts.HasValue) lead.TotalCallAttempts = record.TotalCallAttempts.Value;
        lead.IsActive = true;
        lead.SourceCreatedAt = record.CreatedAt;
        lead.SourceModifiedAt = record.ModifiedAt;
        lead.LastSeenAt = DateTimeOffset.UtcNow;
        SetLinkSeen(link, record);
    }

    private async Task UpsertProductCategoryAsync(CrmCanonicalProductCategory record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var category = link is null
            ? new SalesProductCategory { Id = Guid.NewGuid(), Key = record.Key, Name = record.Name }
            : await db.SalesProductCategories.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Produktkategorie", link.InternalEntityId);
        if (link is null)
        {
            db.SalesProductCategories.Add(category);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.ProductCategory, category.Id));
        }
        category.Key = record.Key;
        category.Name = record.Name;
        category.IsActive = true;
        SetLinkSeen(link, record);
    }

    private async Task UpsertProductAsync(CrmCanonicalProduct record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var product = link is null
            ? new SalesProduct { Id = Guid.NewGuid(), Key = record.Key, Name = record.Name }
            : await db.SalesProducts.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Produkt", link.InternalEntityId);
        if (link is null)
        {
            db.SalesProducts.Add(product);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Product, product.Id));
        }

        product.Key = record.Key;
        product.Name = record.Name;
        product.Description = record.Description;
        product.IsActive = record.IsActive;
        product.CategoryId = await ResolveProductCategoryAsync(record, cancellationToken);
        product.SourceCreatedAt = record.CreatedAt;
        product.SourceModifiedAt = record.ModifiedAt;
        product.LastSeenAt = DateTimeOffset.UtcNow;
        SetLinkSeen(link, record);
    }

    private async Task<Guid?> ResolveProductCategoryAsync(CrmCanonicalProduct record, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(record.CategoryName) && string.IsNullOrWhiteSpace(record.CategoryExternalId))
            return null;
        var externalId = record.CategoryExternalId ?? $"name:{record.CategoryName!.Trim().ToLowerInvariant()}";
        var link = await db.IntegrationEntityLinks.SingleOrDefaultAsync(item => item.ProviderKey == record.ProviderKey
            && item.ConnectionKey == record.ConnectionKey
            && item.EntityType == CrmEntityTypes.ProductCategory
            && item.ExternalId == externalId, cancellationToken);
        if (link is not null) return link.InternalEntityId;

        var category = new SalesProductCategory
        {
            Id = Guid.NewGuid(),
            Key = externalId,
            Name = record.CategoryName ?? externalId,
            IsActive = true
        };
        db.SalesProductCategories.Add(category);
        db.IntegrationEntityLinks.Add(new IntegrationEntityLink
        {
            Id = Guid.NewGuid(),
            ProviderKey = record.ProviderKey,
            ConnectionKey = record.ConnectionKey,
            EntityType = CrmEntityTypes.ProductCategory,
            ExternalId = externalId,
            ExternalUrl = record.ExternalUrl,
            InternalEntityType = CrmEntityTypes.ProductCategory,
            InternalEntityId = category.Id,
            LastSeenAt = DateTimeOffset.UtcNow
        });
        return category.Id;
    }

    private async Task UpsertPipelineAsync(CrmCanonicalPipeline record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var pipeline = link is null
            ? new SalesPipeline { Id = Guid.NewGuid(), Key = record.Key, Name = record.Name }
            : await db.SalesPipelines.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Pipeline", link.InternalEntityId);
        if (link is null)
        {
            db.SalesPipelines.Add(pipeline);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Pipeline, pipeline.Id));
        }
        pipeline.Key = record.Key;
        pipeline.Name = record.Name;
        pipeline.Description = record.Description;
        pipeline.SortOrder = record.SortOrder;
        pipeline.IsActive = true;
        pipeline.SourceCreatedAt = record.CreatedAt;
        pipeline.SourceModifiedAt = record.ModifiedAt;
        SetLinkSeen(link, record);
    }

    private async Task UpsertPipelineStageAsync(CrmCanonicalPipelineStage record, CancellationToken cancellationToken)
    {
        var pipelineId = await ResolvePipelineIdAsync(record, record.PipelineExternalId, null, cancellationToken);
        if (!pipelineId.HasValue)
            throw new InvalidOperationException($"Die Pipeline '{record.PipelineExternalId}' der Stufe '{record.Name}' fehlt.");

        var link = await FindLinkAsync(record, cancellationToken);
        var stage = link is null
            ? new SalesPipelineStage { Id = Guid.NewGuid(), PipelineId = pipelineId.Value, Key = record.Key, Name = record.Name, StageType = record.StageType }
            : await db.SalesPipelineStages.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Pipeline-Stufe", link.InternalEntityId);
        if (link is null)
        {
            db.SalesPipelineStages.Add(stage);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.PipelineStage, stage.Id));
        }
        stage.PipelineId = pipelineId.Value;
        stage.Key = record.Key;
        stage.Name = record.Name;
        stage.StageType = record.StageType;
        stage.SortOrder = record.SortOrder;
        stage.Probability = record.Probability;
        stage.IsTerminal = record.IsTerminal;
        stage.IsActive = true;
        stage.SourceModifiedAt = record.ModifiedAt;
        SetLinkSeen(link, record);
    }

    private async Task UpsertDealAsync(CrmCanonicalDeal record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var deal = link is null
            ? new SalesDeal { Id = Guid.NewGuid(), Name = record.Name }
            : await db.SalesDeals.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Deal", link.InternalEntityId);
        if (link is null)
        {
            db.SalesDeals.Add(deal);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Deal, deal.Id));
        }

        deal.CustomerId = await FindInternalIdAsync(record, CrmEntityTypes.Customer, record.CustomerExternalId, cancellationToken);
        deal.OwnerId = await FindInternalIdAsync(record, CrmEntityTypes.Owner, record.OwnerExternalId, cancellationToken);
        deal.PipelineId = await ResolvePipelineIdAsync(record, record.PipelineExternalId, record.PipelineKey, cancellationToken);
        deal.PipelineStageId = await ResolveStageIdAsync(record, deal.PipelineId, record.StageExternalId, record.StageKey, cancellationToken);
        deal.ProductId = await ResolveProductIdAsync(record, record.ProductExternalId, record.ProductName, cancellationToken);
        deal.Name = record.Name;
        deal.Amount = record.Amount;
        deal.Currency = record.Currency;
        deal.NeedsReview = !deal.CustomerId.HasValue || !deal.PipelineStageId.HasValue || !deal.ProductId.HasValue;
        deal.DurationMonths = record.DurationMonths;
        deal.ContractStartAt = record.ContractStartAt;
        deal.ContractEndAt = record.ContractEndAt;
        deal.ClosingAt = record.ClosingAt;
        deal.Status = NormalizeDealStatus(record.Status);
        deal.LossReason = record.LossReason;
        deal.LastActivityAt = record.LastActivityAt;
        deal.IsActive = true;
        deal.SourceCreatedAt = record.CreatedAt;
        deal.SourceModifiedAt = record.ModifiedAt;
        deal.LastSeenAt = DateTimeOffset.UtcNow;
        SetLinkSeen(link, record);

        if (deal.Status == "won" && deal.CustomerId.HasValue)
        {
            await UpsertContractAsync(new CrmCanonicalContract(
                record.ProviderKey,
                record.ConnectionKey,
                $"deal:{record.ExternalId}",
                record.Payload,
                record.CreatedAt,
                record.ModifiedAt,
                record.CustomerExternalId ?? string.Empty,
                record.ExternalId,
                record.ProductExternalId,
                record.OwnerExternalId,
                record.Name,
                "active",
                record.ContractStartAt,
                record.ContractEndAt,
                record.DurationMonths,
                record.Amount,
                record.Currency)
                { ExternalUrl = record.ExternalUrl },
                deal.CustomerId.Value,
                deal.Id,
                cancellationToken);
        }
    }

    private async Task UpsertDealStageHistoryAsync(CrmCanonicalDealStageHistory record, CancellationToken cancellationToken)
    {
        var dealId = await FindInternalIdAsync(record, CrmEntityTypes.Deal, record.DealExternalId, cancellationToken)
            ?? throw new InvalidOperationException($"Der Deal '{record.DealExternalId}' der Stage-Historie fehlt.");
        var link = await FindLinkAsync(record, cancellationToken);
        var history = link is null
            ? new SalesDealStageHistory { Id = Guid.NewGuid(), DealId = dealId, StageKeySnapshot = record.StageKeySnapshot, EnteredAt = record.EnteredAt }
            : await db.SalesDealStageHistory.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Deal-Stage-Historie", link.InternalEntityId);
        if (link is null)
        {
            db.SalesDealStageHistory.Add(history);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.DealStageHistory, history.Id));
        }
        var deal = await db.SalesDeals.SingleOrDefaultAsync(item => item.Id == dealId, cancellationToken);
        history.DealId = dealId;
        history.PipelineId = await ResolvePipelineIdAsync(record, record.PipelineExternalId, null, cancellationToken)
            ?? deal?.PipelineId;
        history.PipelineStageId = await ResolveStageIdAsync(record, history.PipelineId, record.StageExternalId, record.StageKeySnapshot, cancellationToken);
        history.StageKeySnapshot = record.StageKeySnapshot;
        history.EnteredAt = record.EnteredAt;
        history.ExitedAt = record.ExitedAt;
        history.SourceObservedAt = record.ModifiedAt;
        history.SourceEventKey = record.ExternalId;
        SetLinkSeen(link, record);
    }

    private async Task UpsertContractAsync(CrmCanonicalContract record, CancellationToken cancellationToken)
    {
        var customerId = await FindInternalIdAsync(record, CrmEntityTypes.Customer, record.CustomerExternalId, cancellationToken);
        var dealId = await FindInternalIdAsync(record, CrmEntityTypes.Deal, record.DealExternalId, cancellationToken);
        if (!customerId.HasValue || !dealId.HasValue)
            return;

        await UpsertContractAsync(record, customerId.Value, dealId.Value, cancellationToken);
    }

    private async Task UpsertContractAsync(
        CrmCanonicalContract record,
        Guid customerId,
        Guid dealId,
        CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var contract = link is null
            ? new SalesContract { Id = Guid.NewGuid(), CustomerId = customerId, Status = record.Status }
            : await db.SalesContracts.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Vertrag", link.InternalEntityId);
        if (link is null)
        {
            db.SalesContracts.Add(contract);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Contract, contract.Id));
        }
        contract.CustomerId = customerId;
        contract.DealId = dealId;
        contract.ProductId = await FindInternalIdAsync(record, CrmEntityTypes.Product, record.ProductExternalId, cancellationToken);
        contract.OwnerId = await FindInternalIdAsync(record, CrmEntityTypes.Owner, record.OwnerExternalId, cancellationToken);
        contract.ContractNumber = record.ContractNumber;
        contract.Status = record.Status;
        contract.StartAt = record.StartAt;
        contract.EndAt = record.EndAt;
        contract.DurationMonths = record.DurationMonths;
        contract.RecurringAmount = record.RecurringAmount;
        contract.Currency = record.Currency;
        contract.IsActive = string.Equals(record.Status, "active", StringComparison.OrdinalIgnoreCase);
        contract.SourceModifiedAt = record.ModifiedAt;
        contract.LastSeenAt = DateTimeOffset.UtcNow;
        SetLinkSeen(link, record);
    }

    private async Task UpsertActivityAsync(
        CrmCanonicalActivity record,
        int callConversationThresholdSeconds,
        CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var activity = link is null
            ? new SalesActivity { Id = Guid.NewGuid(), ActivityType = record.ActivityType, OccurredAt = record.OccurredAt }
            : await db.SalesActivities.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Aktivität", link.InternalEntityId);
        if (link is null)
        {
            db.SalesActivities.Add(activity);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Activity, activity.Id));
        }
        activity.ActivityType = record.ActivityType;
        activity.Subject = record.Subject;
        activity.OccurredAt = record.OccurredAt;
        activity.DurationSeconds = record.DurationSeconds;
        activity.Direction = record.Direction;
        activity.ConnectionStatus = record.ConnectionStatus;
        activity.ConversationClass = CallQualification.ConversationClass(
            record.ActivityType == "call",
            record.DurationSeconds,
            record.ConnectionStatus,
            record.Result,
            callConversationThresholdSeconds) ?? record.ConversationClass;
        activity.CountsAsConversation = record.ActivityType == "call"
            ? record.DurationSeconds.HasValue
                ? CallQualification.IsConversation(
                    record.DurationSeconds,
                    record.ConnectionStatus,
                    record.Result,
                    callConversationThresholdSeconds)
                : null
            : record.CountsAsConversation;
        activity.Result = record.Result;
        activity.OwnerId = await FindInternalIdAsync(record, CrmEntityTypes.Owner, record.OwnerExternalId, cancellationToken);
        activity.SourceCreatedAt = record.CreatedAt;
        activity.SourceModifiedAt = record.ModifiedAt;
        activity.LastSeenAt = DateTimeOffset.UtcNow;
        activity.SourceDeletedAt = null;
        SetLinkSeen(link, record);

        foreach (var relation in record.Relations)
        {
            var target = await ResolveRelationAsync(record, relation, cancellationToken);
            if (!target.HasValue) continue;
            var exists = await db.SalesActivityRelations.AnyAsync(item => item.ActivityId == activity.Id
                && item.TargetType == target.Value.Type
                && item.TargetId == target.Value.Id, cancellationToken);
            if (!exists)
            {
                db.SalesActivityRelations.Add(new SalesActivityRelation
                {
                    Id = Guid.NewGuid(),
                    ActivityId = activity.Id,
                    TargetType = target.Value.Type,
                    TargetId = target.Value.Id,
                    RelationRole = relation.Role
                });
            }
            await TouchTargetAsync(
                target.Value.Type,
                target.Value.Id,
                activity.OccurredAt,
                record.ActivityType == "call",
                record.ActivityType != "call" || activity.CountsAsConversation == true,
                cancellationToken);
        }
    }

    private async Task UpsertAppointmentAsync(CrmCanonicalAppointment record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var appointment = link is null
            ? new SalesAppointment { Id = Guid.NewGuid(), StartsAt = record.StartsAt, EndsAt = record.EndsAt, Status = record.Status }
            : await db.SalesAppointments.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Termin", link.InternalEntityId);
        if (link is null)
        {
            db.SalesAppointments.Add(appointment);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Appointment, appointment.Id));
        }
        appointment.Subject = record.Subject;
        appointment.StartsAt = record.StartsAt;
        appointment.EndsAt = record.EndsAt;
        appointment.Status = record.Status;
        appointment.AppointmentType = record.AppointmentType;
        appointment.OwnerId = await FindInternalIdAsync(record, CrmEntityTypes.Owner, record.OwnerExternalId, cancellationToken);
        appointment.OriginalStartsAt = record.OriginalStartsAt;
        appointment.RescheduleCount = record.RescheduleCount;
        appointment.SourceCreatedAt = record.CreatedAt;
        appointment.SourceModifiedAt = record.ModifiedAt;
        appointment.LastSeenAt = DateTimeOffset.UtcNow;
        SetLinkSeen(link, record);

        foreach (var relation in record.Relations)
        {
            var target = await ResolveRelationAsync(record, relation, cancellationToken);
            if (!target.HasValue) continue;
            if (!await db.SalesAppointmentRelations.AnyAsync(item => item.AppointmentId == appointment.Id
                && item.TargetType == target.Value.Type
                && item.TargetId == target.Value.Id, cancellationToken))
            {
                db.SalesAppointmentRelations.Add(new SalesAppointmentRelation
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointment.Id,
                    TargetType = target.Value.Type,
                    TargetId = target.Value.Id,
                    RelationRole = relation.Role
                });
            }
        }
        var changedAt = record.ModifiedAt ?? record.StartsAt;
        if (!await db.SalesAppointmentStatusHistories.AnyAsync(item => item.AppointmentId == appointment.Id
            && item.Status == record.Status
            && item.ChangedAt == changedAt, cancellationToken))
        {
            db.SalesAppointmentStatusHistories.Add(new SalesAppointmentStatusHistory
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id,
                Status = record.Status,
                ChangedAt = changedAt,
                OriginalStartsAt = record.OriginalStartsAt,
                Source = CrmProviders.Zoho
            });
        }
    }

    private async Task UpsertServiceCaseAsync(CrmCanonicalServiceCase record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var serviceCase = link is null
            ? new SalesServiceCase { Id = Guid.NewGuid(), Subject = record.Subject, Status = record.Status, Priority = record.Priority }
            : await db.SalesServiceCases.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Service-Fall", link.InternalEntityId);
        if (link is null)
        {
            db.SalesServiceCases.Add(serviceCase);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.ServiceCase, serviceCase.Id));
        }

        serviceCase.Subject = record.Subject;
        serviceCase.Description = record.Description;
        serviceCase.Status = record.Status;
        serviceCase.Priority = record.Priority;
        serviceCase.Origin = record.Origin;
        serviceCase.Reason = record.Reason;
        serviceCase.CustomerId = await FindInternalIdAsync(record, CrmEntityTypes.Customer, record.CustomerExternalId, cancellationToken);
        serviceCase.DealId = await FindInternalIdAsync(record, CrmEntityTypes.Deal, record.DealExternalId, cancellationToken);
        serviceCase.OwnerId = await FindInternalIdAsync(record, CrmEntityTypes.Owner, record.OwnerExternalId, cancellationToken);
        serviceCase.OpenedAt = record.OpenedAt;
        serviceCase.DueAt = record.DueAt;
        serviceCase.ResolvedAt = record.ResolvedAt;
        serviceCase.IsActive = true;
        serviceCase.SourceCreatedAt = record.CreatedAt;
        serviceCase.SourceModifiedAt = record.ModifiedAt;
        serviceCase.LastSeenAt = DateTimeOffset.UtcNow;
        SetLinkSeen(link, record);
    }

    private async Task UpsertOfferAsync(CrmCanonicalOffer record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var offer = link is null
            ? new SalesOffer { Id = Guid.NewGuid(), Name = record.Name, Status = record.Status }
            : await db.SalesOffers.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Angebot", link.InternalEntityId);
        if (link is null)
        {
            db.SalesOffers.Add(offer);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Offer, offer.Id));
        }

        offer.Name = record.Name;
        offer.OfferNumber = record.OfferNumber;
        offer.Status = record.Status;
        offer.Amount = record.Amount;
        offer.Currency = record.Currency;
        offer.CustomerId = await FindInternalIdAsync(record, CrmEntityTypes.Customer, record.CustomerExternalId, cancellationToken);
        offer.DealId = await FindInternalIdAsync(record, CrmEntityTypes.Deal, record.DealExternalId, cancellationToken);
        offer.OwnerId = await FindInternalIdAsync(record, CrmEntityTypes.Owner, record.OwnerExternalId, cancellationToken);
        offer.IssuedAt = record.IssuedAt;
        offer.SentAt = record.SentAt;
        offer.ValidUntil = record.ValidUntil;
        offer.IsActive = true;
        offer.SourceCreatedAt = record.CreatedAt;
        offer.SourceModifiedAt = record.ModifiedAt;
        offer.LastSeenAt = DateTimeOffset.UtcNow;
        SetLinkSeen(link, record);
    }

    private async Task UpsertOrderAsync(CrmCanonicalOrder record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var order = link is null
            ? new SalesOrder { Id = Guid.NewGuid(), Name = record.Name, Status = record.Status }
            : await db.SalesOrders.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Auftrag", link.InternalEntityId);
        if (link is null)
        {
            db.SalesOrders.Add(order);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Order, order.Id));
        }

        order.Name = record.Name;
        order.OrderNumber = record.OrderNumber;
        order.Status = record.Status;
        order.Amount = record.Amount;
        order.Currency = record.Currency;
        order.CustomerId = await FindInternalIdAsync(record, CrmEntityTypes.Customer, record.CustomerExternalId, cancellationToken);
        order.OfferId = await FindInternalIdAsync(record, CrmEntityTypes.Offer, record.OfferExternalId, cancellationToken);
        order.DealId = await FindInternalIdAsync(record, CrmEntityTypes.Deal, record.DealExternalId, cancellationToken);
        order.OwnerId = await FindInternalIdAsync(record, CrmEntityTypes.Owner, record.OwnerExternalId, cancellationToken);
        order.OrderedAt = record.OrderedAt;
        order.PromisedAt = record.PromisedAt;
        order.DeliveredAt = record.DeliveredAt;
        order.IsActive = true;
        order.SourceCreatedAt = record.CreatedAt;
        order.SourceModifiedAt = record.ModifiedAt;
        order.LastSeenAt = DateTimeOffset.UtcNow;
        SetLinkSeen(link, record);
    }

    private async Task UpsertInvoiceAsync(CrmCanonicalInvoice record, CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        var invoice = link is null
            ? new SalesInvoice { Id = Guid.NewGuid(), Name = record.Name, Status = record.Status }
            : await db.SalesInvoices.SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw MissingEntity("Rechnung", link.InternalEntityId);
        if (link is null)
        {
            db.SalesInvoices.Add(invoice);
            db.IntegrationEntityLinks.Add(NewLink(record, CrmEntityTypes.Invoice, invoice.Id));
        }

        invoice.Name = record.Name;
        invoice.InvoiceNumber = record.InvoiceNumber;
        invoice.Status = record.Status;
        invoice.Amount = record.Amount;
        invoice.OpenAmount = record.OpenAmount;
        invoice.Currency = record.Currency;
        invoice.CustomerId = await FindInternalIdAsync(record, CrmEntityTypes.Customer, record.CustomerExternalId, cancellationToken);
        invoice.OrderId = await FindInternalIdAsync(record, CrmEntityTypes.Order, record.OrderExternalId, cancellationToken);
        invoice.DealId = await FindInternalIdAsync(record, CrmEntityTypes.Deal, record.DealExternalId, cancellationToken);
        invoice.OwnerId = await FindInternalIdAsync(record, CrmEntityTypes.Owner, record.OwnerExternalId, cancellationToken);
        invoice.IssuedAt = record.IssuedAt;
        invoice.DueAt = record.DueAt;
        invoice.PaidAt = record.PaidAt;
        invoice.IsActive = true;
        invoice.SourceCreatedAt = record.CreatedAt;
        invoice.SourceModifiedAt = record.ModifiedAt;
        invoice.LastSeenAt = DateTimeOffset.UtcNow;
        SetLinkSeen(link, record);
    }

    private async Task<Guid?> ResolvePipelineIdAsync(CrmCanonicalRecord record, string? externalId, string? name, CancellationToken cancellationToken)
    {
        var byLink = await FindInternalIdAsync(record, CrmEntityTypes.Pipeline, externalId, cancellationToken);
        if (byLink.HasValue) return byLink;
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(externalId)) return null;
        return await db.SalesPipelines
            .Where(item => item.Key == name || item.Name == name || item.Key == externalId || item.Name == externalId)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid?> ResolveStageIdAsync(CrmCanonicalRecord record, Guid? pipelineId, string? externalId, string? name, CancellationToken cancellationToken)
    {
        var byLink = await FindInternalIdAsync(record, CrmEntityTypes.PipelineStage, externalId, cancellationToken);
        if (byLink.HasValue) return byLink;
        if (!pipelineId.HasValue) return null;
        return await db.SalesPipelineStages
            .Where(item => item.PipelineId == pipelineId.Value
                && (item.Key == name || item.Name == name || item.Key == externalId || item.Name == externalId))
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid?> ResolveProductIdAsync(CrmCanonicalRecord record, string? externalId, string? name, CancellationToken cancellationToken)
    {
        var byLink = await FindInternalIdAsync(record, CrmEntityTypes.Product, externalId, cancellationToken);
        if (byLink.HasValue) return byLink;
        if (string.IsNullOrWhiteSpace(name)) return null;
        return await db.SalesProducts
            .Where(item => item.Key == name || item.Name == name)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<(string Type, Guid Id)?> ResolveRelationAsync(
        CrmCanonicalRecord record,
        CrmRecordRelation relation,
        CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            relation.EntityType,
            CrmEntityTypes.Customer,
            CrmEntityTypes.Lead,
            CrmEntityTypes.Deal
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var id = await FindInternalIdAsync(record, candidate, relation.ExternalId, cancellationToken);
            if (id.HasValue) return (candidate, id.Value);
        }
        return null;
    }

    private async Task TouchTargetAsync(
        string targetType,
        Guid targetId,
        DateTimeOffset occurredAt,
        bool isPhoneCall,
        bool countsAsContact,
        CancellationToken cancellationToken)
    {
        switch (targetType)
        {
            case CrmEntityTypes.Customer:
                var customer = await db.SalesCustomers.SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken);
                if (customer is not null)
                {
                    if (countsAsContact)
                        customer.LastContactAt = customer.LastContactAt is null || customer.LastContactAt < occurredAt ? occurredAt : customer.LastContactAt;
                    if (isPhoneCall)
                        customer.LastPhoneCallAt = customer.LastPhoneCallAt is null || customer.LastPhoneCallAt < occurredAt
                            ? occurredAt
                            : customer.LastPhoneCallAt;
                }
                break;
            case CrmEntityTypes.Deal:
                var deal = await db.SalesDeals.SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken);
                if (deal is not null && (deal.LastActivityAt is null || deal.LastActivityAt < occurredAt)) deal.LastActivityAt = occurredAt;
                break;
            case CrmEntityTypes.Lead:
                var lead = await db.SalesLeads.SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken);
                if (lead is not null)
                {
                    RegisterLeadActivity(lead, occurredAt);
                    if (countsAsContact && (lead.LastContactAt is null || lead.LastContactAt < occurredAt))
                        lead.LastContactAt = occurredAt;
                    if (isPhoneCall && (lead.LastPhoneCallAt is null || lead.LastPhoneCallAt < occurredAt)) lead.LastPhoneCallAt = occurredAt;
                }
                break;
        }
    }

    private static void RegisterLeadActivity(SalesLead lead, DateTimeOffset occurredAt)
    {
        if (!lead.FirstActivityAt.HasValue || occurredAt < lead.FirstActivityAt.Value)
            lead.FirstActivityAt = occurredAt;
    }

    private async Task<Guid?> FindInternalIdAsync(
        CrmCanonicalRecord record,
        string entityType,
        string? externalId,
        CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(externalId)
            ? null
            : await db.IntegrationEntityLinks
                .Where(item => item.ProviderKey == record.ProviderKey
                    && item.ConnectionKey == record.ConnectionKey
                    && item.EntityType == entityType
                    && item.ExternalId == externalId)
                .Select(item => (Guid?)item.InternalEntityId)
                .SingleOrDefaultAsync(cancellationToken);

    private Task<IntegrationEntityLink?> FindLinkAsync(CrmCanonicalRecord record, CancellationToken cancellationToken)
        => db.IntegrationEntityLinks.SingleOrDefaultAsync(item => item.ProviderKey == record.ProviderKey
            && item.ConnectionKey == record.ConnectionKey
            && item.EntityType == record.EntityType
            && item.ExternalId == record.ExternalId, cancellationToken);

    private static IntegrationEntityLink NewLink(CrmCanonicalRecord record, string entityType, Guid internalEntityId)
        => new()
        {
            Id = Guid.NewGuid(),
            ProviderKey = record.ProviderKey,
            ConnectionKey = record.ConnectionKey,
            EntityType = entityType,
            ExternalId = record.ExternalId,
            ExternalUrl = record.ExternalUrl,
            InternalEntityType = entityType,
            InternalEntityId = internalEntityId,
            LastSeenAt = DateTimeOffset.UtcNow
        };

    private static void SetLinkSeen(IntegrationEntityLink? link, CrmCanonicalRecord record)
    {
        if (link is null) return;

        link.LastSeenAt = DateTimeOffset.UtcNow;
        link.SourceDeletedAt = null;
        if (!string.IsNullOrWhiteSpace(record.ExternalUrl))
            link.ExternalUrl = record.ExternalUrl;
    }

    private static InvalidOperationException MissingEntity(string type, Guid id)
        => new($"Die interne {type}-Entität {id} fehlt.");

    private static string NormalizeDealStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "open";
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("won") || normalized.Contains("gewonnen")) return "won";
        if (normalized.Contains("lost") || normalized.Contains("verloren")) return "lost";
        return "open";
    }

    private static string? NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? NormalizePhone(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(char.IsDigit).ToArray());
}
