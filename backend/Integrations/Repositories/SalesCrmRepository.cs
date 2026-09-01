using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;

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
        if (includeItems) query = query.Include(item => item.Items);
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
        if (includeItems) query = query.Include(item => item.Items);
        return await query
            .Where(item => item.ProviderKey == providerKey
                && item.ConnectionKey == connectionKey
                && (item.Status == "queued" || item.Status == "running"))
            .OrderBy(item => item.QueuedAt)
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
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow
        };
        run.Items.Add(item);
        db.IntegrationSyncRunItems.Add(item);
        return Task.FromResult(item);
    }

    public async Task UpsertAsync(
        CrmCanonicalRecord record,
        Guid syncRunId,
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
            raw.SyncRunId = syncRunId;
            raw.SyncedAt = DateTimeOffset.UtcNow;
        }

        switch (record)
        {
            case CrmCanonicalCustomer customer:
                await UpsertCustomerAsync(customer, cancellationToken);
                break;
            case CrmCanonicalDeal deal:
                await UpsertDealAsync(deal, cancellationToken);
                break;
            case CrmCanonicalLead lead:
                await UpsertLeadAsync(lead, cancellationToken);
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
            Message = exception.Message[..Math.Min(exception.Message.Length, 4000)],
            Retryable = false,
            Attempt = 1,
            OccurredAt = DateTimeOffset.UtcNow
        });

    public void DetachRecordChanges()
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(entry =>
                     entry.Entity is not IntegrationSyncRun
                     && entry.Entity is not IntegrationSyncRunItem
                     && entry.Entity is not IntegrationSyncError))
        {
            entry.State = EntityState.Detached;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => db.SaveChangesAsync(cancellationToken);

    private async Task UpsertCustomerAsync(
        CrmCanonicalCustomer record,
        CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        SalesCustomer customer;
        if (link is null)
        {
            customer = new SalesCustomer { Id = Guid.NewGuid(), Name = record.Name };
            db.SalesCustomers.Add(customer);
            link = NewLink(record, CrmEntityTypes.Customer, customer.Id);
            db.IntegrationEntityLinks.Add(link);
        }
        else
        {
            customer = await db.SalesCustomers
                .SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Die interne Kundenentität {link.InternalEntityId} fehlt.");
        }

        customer.Name = record.Name;
        customer.Industry = record.Industry;
        customer.PostalCode = record.PostalCode;
        customer.City = record.City;
        customer.CountryCode = record.CountryCode;
        customer.Status = record.Status ?? customer.Status;
        customer.SourceCreatedAt = record.CreatedAt;
        customer.SourceModifiedAt = record.ModifiedAt;
        link.LastSeenAt = DateTimeOffset.UtcNow;
    }

    private async Task UpsertDealAsync(
        CrmCanonicalDeal record,
        CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        SalesDeal deal;
        if (link is null)
        {
            deal = new SalesDeal { Id = Guid.NewGuid(), Name = record.Name };
            db.SalesDeals.Add(deal);
            link = NewLink(record, CrmEntityTypes.Deal, deal.Id);
            db.IntegrationEntityLinks.Add(link);
        }
        else
        {
            deal = await db.SalesDeals
                .SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Die interne Dealentität {link.InternalEntityId} fehlt.");
        }

        deal.CustomerId = record.CustomerExternalId is null
            ? null
            : await FindInternalIdAsync(record, CrmEntityTypes.Customer, record.CustomerExternalId, cancellationToken);
        deal.Name = record.Name;
        deal.Amount = record.Amount;
        deal.Currency = record.Currency;
        deal.NeedsReview = record.PipelineKey is not null
            || record.StageKey is not null
            || record.ProductName is not null;
        deal.DurationMonths = record.DurationMonths;
        deal.ContractEndAt = record.ContractEndAt;
        deal.ClosingAt = record.ClosingAt;
        deal.Status = record.Status ?? deal.Status;
        deal.LossReason = record.LossReason;
        deal.LastActivityAt = record.LastActivityAt;
        deal.SourceCreatedAt = record.CreatedAt;
        deal.SourceModifiedAt = record.ModifiedAt;
        link.LastSeenAt = DateTimeOffset.UtcNow;
    }

    private async Task UpsertLeadAsync(
        CrmCanonicalLead record,
        CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(record, cancellationToken);
        SalesLead lead;
        if (link is null)
        {
            lead = new SalesLead { Id = Guid.NewGuid(), Name = record.Name };
            db.SalesLeads.Add(lead);
            link = NewLink(record, CrmEntityTypes.Lead, lead.Id);
            db.IntegrationEntityLinks.Add(link);
        }
        else
        {
            lead = await db.SalesLeads
                .SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Die interne Leadentität {link.InternalEntityId} fehlt.");
        }

        lead.Name = record.Name;
        lead.CompanyName = record.CompanyName;
        lead.Email = record.Email;
        lead.Phone = record.Phone;
        lead.Status = record.Status ?? lead.Status;
        lead.Source = record.Source;
        lead.LastContactAt = record.LastContactAt;
        if (record.TotalCallAttempts.HasValue) lead.TotalCallAttempts = record.TotalCallAttempts.Value;
        lead.SourceCreatedAt = record.CreatedAt;
        lead.SourceModifiedAt = record.ModifiedAt;
        link.LastSeenAt = DateTimeOffset.UtcNow;
    }

    private async Task<IntegrationEntityLink?> FindLinkAsync(
        CrmCanonicalRecord record,
        CancellationToken cancellationToken)
        => await db.IntegrationEntityLinks
            .SingleOrDefaultAsync(item => item.ProviderKey == record.ProviderKey
                && item.ConnectionKey == record.ConnectionKey
                && item.EntityType == record.EntityType
                && item.ExternalId == record.ExternalId, cancellationToken);

    private async Task<Guid?> FindInternalIdAsync(
        CrmCanonicalRecord record,
        string entityType,
        string externalId,
        CancellationToken cancellationToken)
        => await db.IntegrationEntityLinks
            .Where(item => item.ProviderKey == record.ProviderKey
                && item.ConnectionKey == record.ConnectionKey
                && item.EntityType == entityType
                && item.ExternalId == externalId)
            .Select(item => (Guid?)item.InternalEntityId)
            .SingleOrDefaultAsync(cancellationToken);

    private static IntegrationEntityLink NewLink(
        CrmCanonicalRecord record,
        string entityType,
        Guid internalEntityId)
        => new()
        {
            Id = Guid.NewGuid(),
            ProviderKey = record.ProviderKey,
            ConnectionKey = record.ConnectionKey,
            EntityType = entityType,
            ExternalId = record.ExternalId,
            InternalEntityType = entityType,
            InternalEntityId = internalEntityId
        };
}
