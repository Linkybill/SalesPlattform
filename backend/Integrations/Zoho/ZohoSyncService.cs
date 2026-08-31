using System.Text.Json;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoSyncResult(
    Guid RunId,
    string Status,
    int RecordsRead,
    int RecordsWritten,
    int RecordsFailed,
    IReadOnlyCollection<string> Modules);

public sealed class ZohoSyncService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ICrmAdapter adapter,
    ZohoConnectionStore connectionStore,
    ILogger<ZohoSyncService> logger)
{
    private static readonly IReadOnlyDictionary<string, string[]> PreferredFields =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accounts"] =
            [
                "id", "Account_Name", "Name", "Industry", "Billing_Code", "Billing_City",
                "Billing_Country", "Billing_State", "Billing_Street", "Owner",
                "Account_Status", "Created_Time", "Modified_Time"
            ],
            ["Deals"] =
            [
                "id", "Deal_Name", "Account_Name", "Amount", "Currency", "Stage",
                "Pipeline", "Product_Name", "Product", "Contract_Term",
                "Contract_End_Date", "Closing_Date", "Owner", "Reason_for_Loss__s",
                "Last_Activity_Time", "Created_Time", "Modified_Time"
            ],
            ["Leads"] =
            [
                "id", "Full_Name", "Last_Name", "Company", "Email", "Phone",
                "Lead_Status", "Lead_Source", "Last_Activity_Time", "Last_Call",
                "Call_Attempts", "Owner", "Created_Time", "Modified_Time"
            ]
        };

    public async Task<ZohoSyncResult> SyncAsync(
        IReadOnlyCollection<string>? requestedModules,
        CancellationToken cancellationToken = default)
    {
        var modules = (requestedModules is { Count: > 0 }
                ? requestedModules
                : ["Accounts", "Deals", "Leads"])
            .Select(module => module.Trim())
            .Where(module => module.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (modules.Length == 0)
            throw new InvalidOperationException("Es wurde kein Zoho-Modul für den Import angegeben.");

        var availableModules = await adapter.GetModulesAsync(cancellationToken);
        var unavailable = modules
            .Where(module => !availableModules.Contains(module, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (unavailable.Length > 0)
        {
            throw new InvalidOperationException(
                $"Diese Zoho-Module sind nicht verfügbar: {string.Join(", ", unavailable)}.");
        }

        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var db = session.Context;
        var run = new IntegrationSyncRun
        {
            Id = Guid.NewGuid(),
            ProviderKey = CrmProviders.Zoho,
            Mode = "full",
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow
        };
        db.IntegrationSyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            foreach (var module in OrderModules(modules))
            {
                var fields = await ResolveFieldsAsync(module, cancellationToken);
                var records = await adapter.GetRecordsAsync(module, fields, cancellationToken);
                run.RecordsRead += records.Count;
                foreach (var record in records)
                {
                    try
                    {
                        await UpsertRecordAsync(db, record, cancellationToken);
                        run.RecordsWritten++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        run.RecordsFailed++;
                        logger.LogError(
                            exception,
                            "Zoho record {Module}/{ExternalId} could not be imported.",
                            record.Module,
                            record.ExternalId);
                    }
                }

                var entityType = EntityTypeForModule(module);
                var cursor = await db.IntegrationSyncCursors
                    .SingleOrDefaultAsync(item => item.ProviderKey == CrmProviders.Zoho
                        && item.EntityType == entityType, cancellationToken);
                if (cursor is null)
                {
                    cursor = new IntegrationSyncCursor
                    {
                        Id = Guid.NewGuid(),
                        ProviderKey = CrmProviders.Zoho,
                        EntityType = entityType
                    };
                    db.IntegrationSyncCursors.Add(cursor);
                }

                cursor.LastModifiedAt = records
                    .Where(record => record.ModifiedAt.HasValue)
                    .Select(record => record.ModifiedAt)
                    .Max();
                cursor.LastExternalId = records.LastOrDefault()?.ExternalId;
                cursor.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            run.Status = run.RecordsFailed == 0 ? "succeeded" : "completed_with_errors";
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Error = run.RecordsFailed == 0
                ? null
                : $"{run.RecordsFailed} Datensätze konnten nicht importiert werden.";
            await db.SaveChangesAsync(cancellationToken);
            await connectionStore.MarkSyncAsync(cancellationToken);
            return new ZohoSyncResult(
                run.Id,
                run.Status,
                run.RecordsRead,
                run.RecordsWritten,
                run.RecordsFailed,
                modules);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Status = "failed";
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Error = exception.Message[..Math.Min(exception.Message.Length, 4000)];
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task<IReadOnlyCollection<string>> ResolveFieldsAsync(
        string module,
        CancellationToken cancellationToken)
    {
        var metadata = await adapter.GetFieldsAsync(module, cancellationToken);
        var actualNames = metadata
            .Select(field => field.ApiName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        var preferred = PreferredFields.TryGetValue(module, out var fields)
            ? fields
            : [];
        return preferred
            .Concat(actualNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
    }

    private async Task UpsertRecordAsync(
        SalesPlattformDbContext db,
        CrmExternalRecord record,
        CancellationToken cancellationToken)
    {
        var entityType = EntityTypeForModule(record.Module);
        var raw = await db.IntegrationRawRecords
            .SingleOrDefaultAsync(item => item.ProviderKey == CrmProviders.Zoho
                && item.EntityType == entityType
                && item.ExternalId == record.ExternalId, cancellationToken);
        if (raw is null)
        {
            raw = new IntegrationRawRecord
            {
                Id = Guid.NewGuid(),
                ProviderKey = CrmProviders.Zoho,
                EntityType = entityType,
                ExternalId = record.ExternalId,
                PayloadJson = record.Payload.GetRawText(),
                ExternalModifiedAt = record.ModifiedAt,
                SyncedAt = DateTimeOffset.UtcNow
            };
            db.IntegrationRawRecords.Add(raw);
        }
        else
        {
            raw.PayloadJson = record.Payload.GetRawText();
            raw.ExternalModifiedAt = record.ModifiedAt;
            raw.SyncedAt = DateTimeOffset.UtcNow;
        }

        switch (record.Module.ToLowerInvariant())
        {
            case "accounts":
                await UpsertCustomerAsync(db, record, cancellationToken);
                break;
            case "deals":
                await UpsertDealAsync(db, record, cancellationToken);
                break;
            case "leads":
                await UpsertLeadAsync(db, record, cancellationToken);
                break;
            default:
                throw new InvalidOperationException(
                    $"Der Import des Moduls '{record.Module}' ist noch nicht implementiert.");
        }
    }

    private static async Task UpsertCustomerAsync(
        SalesPlattformDbContext db,
        CrmExternalRecord record,
        CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(db, CrmEntityTypes.Customer, record.ExternalId, cancellationToken);
        SalesCustomer customer;
        if (link is null)
        {
            customer = new SalesCustomer
            {
                Id = Guid.NewGuid(),
                Name = ZohoFieldReader.String(record.Payload, "Account_Name", "Name") ?? record.ExternalId
            };
            db.SalesCustomers.Add(customer);
            link = new IntegrationEntityLink
            {
                Id = Guid.NewGuid(),
                ProviderKey = CrmProviders.Zoho,
                EntityType = CrmEntityTypes.Customer,
                ExternalId = record.ExternalId,
                InternalEntityType = CrmEntityTypes.Customer,
                InternalEntityId = customer.Id
            };
            db.IntegrationEntityLinks.Add(link);
        }
        else
        {
            customer = await db.SalesCustomers
                .SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Die interne Kundenentität {link.InternalEntityId} fehlt.");
        }

        customer.Name = ZohoFieldReader.String(record.Payload, "Account_Name", "Name") ?? customer.Name;
        customer.Industry = ZohoFieldReader.String(record.Payload, "Industry");
        customer.PostalCode = ZohoFieldReader.String(record.Payload, "Billing_Code", "Zip", "Postal_Code");
        customer.City = ZohoFieldReader.String(record.Payload, "Billing_City", "City");
        customer.Country = ZohoFieldReader.String(record.Payload, "Billing_Country", "Country");
        customer.OwnerExternalId = ZohoFieldReader.LookupId(record.Payload, "Owner");
        customer.Status = ZohoFieldReader.String(record.Payload, "Account_Status", "Status");
        customer.SourceCreatedAt = ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "CreatedTime");
        customer.SourceModifiedAt = record.ModifiedAt;
        link.LastSeenAt = DateTimeOffset.UtcNow;
    }

    private static async Task UpsertDealAsync(
        SalesPlattformDbContext db,
        CrmExternalRecord record,
        CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(db, CrmEntityTypes.Deal, record.ExternalId, cancellationToken);
        SalesDeal deal;
        if (link is null)
        {
            deal = new SalesDeal
            {
                Id = Guid.NewGuid(),
                Name = ZohoFieldReader.String(record.Payload, "Deal_Name", "Name") ?? record.ExternalId
            };
            db.SalesDeals.Add(deal);
            link = new IntegrationEntityLink
            {
                Id = Guid.NewGuid(),
                ProviderKey = CrmProviders.Zoho,
                EntityType = CrmEntityTypes.Deal,
                ExternalId = record.ExternalId,
                InternalEntityType = CrmEntityTypes.Deal,
                InternalEntityId = deal.Id
            };
            db.IntegrationEntityLinks.Add(link);
        }
        else
        {
            deal = await db.SalesDeals
                .SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Die interne Dealentität {link.InternalEntityId} fehlt.");
        }

        var accountExternalId = ZohoFieldReader.LookupId(record.Payload, "Account_Name", "Account");
        deal.CustomerId = accountExternalId is null
            ? null
            : await FindInternalIdAsync(db, CrmEntityTypes.Customer, accountExternalId, cancellationToken);
        deal.Name = ZohoFieldReader.String(record.Payload, "Deal_Name", "Name") ?? deal.Name;
        deal.Amount = ZohoFieldReader.Decimal(record.Payload, "Amount");
        deal.Currency = ZohoFieldReader.String(record.Payload, "Currency", "Currency_Code");
        deal.PipelineKey = ZohoFieldReader.String(record.Payload, "Pipeline");
        deal.StageKey = ZohoFieldReader.String(record.Payload, "Stage");
        deal.ProductName = ZohoFieldReader.String(record.Payload, "Product_Name", "Product", "Produkt");
        deal.DurationMonths = ZohoFieldReader.Decimal(record.Payload, "Contract_Term", "Duration_Months", "Laufzeit");
        deal.ContractEndAt = ZohoFieldReader.Date(record.Payload, "Contract_End_Date", "Vertragsende");
        deal.ClosingAt = ZohoFieldReader.Date(record.Payload, "Closing_Date", "closing_date");
        deal.Status = ZohoFieldReader.String(record.Payload, "Stage", "Status");
        deal.LossReason = ZohoFieldReader.String(record.Payload, "Reason_for_Loss__s", "Loss_Reason", "verlustgrund");
        deal.OwnerExternalId = ZohoFieldReader.LookupId(record.Payload, "Owner");
        deal.LastActivityAt = ZohoFieldReader.DateTimeOffset(record.Payload, "Last_Activity_Time");
        deal.SourceCreatedAt = ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "CreatedTime");
        deal.SourceModifiedAt = record.ModifiedAt;
        link.LastSeenAt = DateTimeOffset.UtcNow;
    }

    private static async Task UpsertLeadAsync(
        SalesPlattformDbContext db,
        CrmExternalRecord record,
        CancellationToken cancellationToken)
    {
        var link = await FindLinkAsync(db, CrmEntityTypes.Lead, record.ExternalId, cancellationToken);
        SalesLead lead;
        if (link is null)
        {
            lead = new SalesLead
            {
                Id = Guid.NewGuid(),
                Name = ZohoFieldReader.String(record.Payload, "Full_Name", "Last_Name", "Company")
                    ?? record.ExternalId
            };
            db.SalesLeads.Add(lead);
            link = new IntegrationEntityLink
            {
                Id = Guid.NewGuid(),
                ProviderKey = CrmProviders.Zoho,
                EntityType = CrmEntityTypes.Lead,
                ExternalId = record.ExternalId,
                InternalEntityType = CrmEntityTypes.Lead,
                InternalEntityId = lead.Id
            };
            db.IntegrationEntityLinks.Add(link);
        }
        else
        {
            lead = await db.SalesLeads
                .SingleOrDefaultAsync(item => item.Id == link.InternalEntityId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Die interne Leadentität {link.InternalEntityId} fehlt.");
        }

        lead.Name = ZohoFieldReader.String(record.Payload, "Full_Name", "Last_Name", "Company") ?? lead.Name;
        lead.CompanyName = ZohoFieldReader.String(record.Payload, "Company");
        lead.Email = ZohoFieldReader.String(record.Payload, "Email");
        lead.Phone = ZohoFieldReader.String(record.Payload, "Phone");
        lead.Status = ZohoFieldReader.String(record.Payload, "Lead_Status", "Status");
        lead.Source = ZohoFieldReader.String(record.Payload, "Lead_Source", "LeadSource");
        lead.LastContactAt = ZohoFieldReader.DateTimeOffset(
            record.Payload,
            "Last_Call",
            "Last_Activity_Time",
            "Last_Contact");
        lead.CallAttempts = ZohoFieldReader.Int32(record.Payload, "Call_Attempts", "anrufversuche") ?? lead.CallAttempts;
        lead.OwnerExternalId = ZohoFieldReader.LookupId(record.Payload, "Owner");
        lead.SourceCreatedAt = ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "CreatedTime");
        lead.SourceModifiedAt = record.ModifiedAt;
        link.LastSeenAt = DateTimeOffset.UtcNow;
    }

    private static async Task<IntegrationEntityLink?> FindLinkAsync(
        SalesPlattformDbContext db,
        string entityType,
        string externalId,
        CancellationToken cancellationToken)
        => await db.IntegrationEntityLinks
            .SingleOrDefaultAsync(item => item.ProviderKey == CrmProviders.Zoho
                && item.EntityType == entityType
                && item.ExternalId == externalId, cancellationToken);

    private static async Task<Guid?> FindInternalIdAsync(
        SalesPlattformDbContext db,
        string entityType,
        string externalId,
        CancellationToken cancellationToken)
        => await db.IntegrationEntityLinks
            .Where(item => item.ProviderKey == CrmProviders.Zoho
                && item.EntityType == entityType
                && item.ExternalId == externalId)
            .Select(item => (Guid?)item.InternalEntityId)
            .SingleOrDefaultAsync(cancellationToken);

    private static string EntityTypeForModule(string module)
        => module.ToLowerInvariant() switch
        {
            "accounts" => CrmEntityTypes.Customer,
            "deals" => CrmEntityTypes.Deal,
            "leads" => CrmEntityTypes.Lead,
            _ => module.ToLowerInvariant()
        };

    private static IEnumerable<string> OrderModules(IEnumerable<string> modules)
    {
        var order = new[] { "Accounts", "Deals", "Leads" };
        return modules.OrderBy(module =>
            Array.FindIndex(order, item => string.Equals(item, module, StringComparison.OrdinalIgnoreCase)) switch
            {
                -1 => order.Length,
                var index => index
            });
    }
}
