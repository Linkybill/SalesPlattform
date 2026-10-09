using System.Reflection;
using System.Text.Json;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations.Repositories;
using SalesPlattform.Backend.Integrations.Zoho;
using SalesPlattform.Backend.Services;

public static class ContactRegressions
{
    public static async Task Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var now = DateTimeOffset.UtcNow;
        var old = now.AddDays(-180);
        var mapper = new ZohoCrmRecordMapper();
        var options = new DbContextOptionsBuilder<SalesPlattformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var tenant = Guid.NewGuid();
        using var db = new SalesPlattformDbContext(options);
        ((IPlatformTenantDbContext)db).SetPlatformTenant(tenant, true);
        var repository = new SalesCrmRepositoryFactory().Create(db);
        var runId = Guid.NewGuid();
        CrmCanonicalRecord Map(string module, string id, object payload, CrmRecordRelation[]? relations = null)
            => mapper.Map(new("zoho", module, id, JsonSerializer.SerializeToElement(payload), null, relations));
        async Task Import(CrmCanonicalRecord record)
        {
            await repository.UpsertAsync(record, runId, 20, default);
            await repository.SaveChangesAsync(default);
        }
        var customerRecord = Map("Accounts", "account", new { Account_Name = "Synthetic customer", Created_Time = old });
        var leadRecord = (CrmCanonicalLead)Map("Leads", "lead", new
        {
            Full_Name = "Synthetic lead", Created_Time = old, Last_Contact = old, Last_Activity_Time = now
        });
        Check(leadRecord.LastContactAt == old, "Generic CRM modification/activity must not replace explicit contact.");
        var noContactLead = (CrmCanonicalLead)Map("Leads", "never", new { Full_Name = "Never contacted", Created_Time = now, Last_Activity_Time = now });
        Check(noContactLead.LastContactAt is null, "Last_Activity_Time alone is not a customer contact.");
        Check(mapper.GetPreferredFields("Leads").Contains("Last_Contact"), "Request the explicit contact field when available in schema.");
        await Import(customerRecord);
        await Import(leadRecord);
        await Import(noContactLead);
        var targets = new[] { new CrmRecordRelation("customer", "account"), new CrmRecordRelation("lead", "lead") };
        await Import(Map("Emails", "Emails:old", new { Subject = "Actual old email", time = old }, targets));
        var customer = await db.SalesCustomers.SingleAsync();
        var lead = await db.SalesLeads.SingleAsync(value => value.Name == "Synthetic lead");
        Check(customer.LastContactAt == old && lead.LastContactAt == old, "An email is a genuine contact.");
        await Import(Map("Tasks", "Tasks:today", new { Subject = "Open CRM follow-up", Due_Date = now, Status = "Not Started" }, targets));
        await Import(Map("Tasks", "Tasks:future", new { Subject = "Future mirrored task", Due_Date = now.AddDays(90), Status = "Not Started" }, targets));
        Check(customer.LastContactAt == old && lead.LastContactAt == old, "CRM follow-ups must not hide dormant customers/leads.");
        await Import(Map("Calls", "Calls:missed", new { Call_Start_Time = now.AddDays(-1), Call_Duration = 0, Call_Result = "No Answer" }, targets));
        Check(customer.LastContactAt == old && lead.LastContactAt == old, "An unsuccessful call is not a conversation.");
        await Import(Map("Emails", "Emails:future", new { Subject = "Future communication", time = now.AddDays(10) }, targets));
        Check(customer.LastContactAt == old && lead.LastContactAt == old, "Future communication must not count as completed contact.");
        await Import(Map("Emails", "Emails:deleted", new { Subject = "Deleted email", time = now.AddDays(-1) }, targets));
        await repository.MarkDeletedAsync(new("zoho", "Emails", "activity", "Emails:deleted", now), runId, default);
        await repository.SaveChangesAsync(default);

        // Simulate the old task-derived values. The repair must move them backwards.
        customer.LastContactAt = now.AddDays(90);
        lead.LastContactAt = now.AddDays(90);
        await repository.SaveChangesAsync(default);
        var otherTenant = Guid.NewGuid();
        using (var other = new SalesPlattformDbContext(options))
        {
            ((IPlatformTenantDbContext)other).SetPlatformTenant(otherTenant, true);
            var otherCustomerId = Guid.NewGuid();
            other.SalesCustomers.Add(new() { Id = otherCustomerId, Name = "Other tenant", LastContactAt = now });
            other.IntegrationEntityLinks.Add(new()
            {
                Id = Guid.NewGuid(), ProviderKey = "zoho", ConnectionKey = "default", EntityType = "customer",
                ExternalId = "account", InternalEntityType = "customer", InternalEntityId = otherCustomerId
            });
            await other.SaveChangesAsync();
        }
        await repository.RebuildContactMarkersAsync("zoho", "default", [leadRecord, noContactLead], now, default);
        await repository.SaveChangesAsync(default);
        Check(customer.LastContactAt == old && lead.LastContactAt == old, "Repair must discard tasks, deleted and future contacts.");
        Check(await db.SalesCustomers.CountAsync() == 1, "Tenant isolation must remain active during marker repair.");
        using (var other = new SalesPlattformDbContext(options))
        {
            ((IPlatformTenantDbContext)other).SetPlatformTenant(otherTenant, true);
            Check((await other.SalesCustomers.SingleAsync()).LastContactAt == now, "Repair must not modify another tenant's contact markers.");
        }
        var worklist = new WorklistService(null!, null!, null!, null!);
        typeof(WorklistService).GetField("activeContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(worklist, db);
        var ruleConstructor = typeof(SalesRuleConfiguration).GetConstructors().Single(c => c.GetParameters().Length > 1);
        var configuration = (SalesRuleConfiguration)ruleConstructor.Invoke(ruleConstructor.GetParameters()
            .Select(parameter => parameter.ParameterType == typeof(decimal) ? (object)0m : 90).ToArray());
        async Task<Guid[]> DormantTargets()
        {
            var method = typeof(WorklistService).GetMethod("FindCandidatesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var task = (Task)method.Invoke(worklist, [now, null, configuration, CancellationToken.None])!;
            await task;
            return ((System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!)
                .Cast<object>().Where(candidate => (string)candidate.GetType().GetProperty("RuleCode")!.GetValue(candidate)! == "R-07")
                .Select(candidate => (Guid)candidate.GetType().GetProperty("TargetId")!.GetValue(candidate)!).ToArray();
        }
        var dormant = await DormantTargets();
        Check(dormant.Contains(customer.Id) && dormant.Contains(lead.Id), "The actual R-07 evaluator must recover old customers and leads after repair.");
        await repository.RebuildContactMarkersAsync("zoho", "default", [leadRecord, noContactLead], now, default);
        await repository.SaveChangesAsync(default);
        Check(customer.LastContactAt == old && lead.LastContactAt == old, "Repair is idempotent.");
        await Import(leadRecord);
        Check(lead.LastContactAt == old, "Subsequent lead imports cannot restore Last_Activity_Time pollution.");
        await Import(Map("Calls", "Calls:conversation", new { Call_Start_Time = now.AddHours(-1), Call_Duration = 30, Call_Result = "Connected" }, targets));
        Check(customer.LastContactAt == now.AddHours(-1) && lead.LastContactAt == now.AddHours(-1), "Qualified conversations remove dormancy.");
        dormant = await DormantTargets();
        Check(!dormant.Contains(customer.Id) && !dormant.Contains(lead.Id), "The actual R-07 evaluator must exclude recently contacted entities.");

        var dueMethod = typeof(WorklistService).GetMethod("ContactReactivationDueAt", BindingFlags.Static | BindingFlags.NonPublic)!;
        DateTimeOffset Due(DateTimeOffset? last, DateTimeOffset? created)
            => (DateTimeOffset)dueMethod.Invoke(null, [last, created, 90, now])!;
        Check(Due(null, now) == now, "Never contacted must be due immediately, not in 90 days.");
        Check(Due(null, null) == now && Due(null, now.AddDays(1)) == now, "Missing/future creation dates cannot defer reactivation.");
        Check(Due(old, old) == old.AddDays(90), "Existing contacts retain the configured inactivity threshold.");
        var successful = new IntegrationSyncRun
        {
            Id = Guid.NewGuid(), ProviderKey = "zoho", ConnectionKey = "default", Mode = "full", Status = "running",
            Items = new[] { "Accounts", "Leads", "Calls", "Emails" }.Select(module =>
                new IntegrationSyncRunItem { Id = Guid.NewGuid(), Module = module, Status = "succeeded" }).ToList()
        };
        Check(ZohoSyncService.CanRebuildContactMarkers(successful), "Complete successful full imports permit repair.");
        successful.RecordsFailed = 1;
        Check(!ZohoSyncService.CanRebuildContactMarkers(successful), "Any import error blocks destructive reconstruction.");
        successful.RecordsFailed = 0;
        successful.Mode = "incremental";
        Check(!ZohoSyncService.CanRebuildContactMarkers(successful), "Incremental imports cannot clear historical markers.");
        successful.Mode = "full";
        successful.Items.Last().Status = "failed";
        Check(!ZohoSyncService.CanRebuildContactMarkers(successful), "Failed email imports cannot clear markers.");
        successful.Items.Last().Status = "skipped";
        Check(!ZohoSyncService.CanRebuildContactMarkers(successful), "Skipped modules cannot count as complete.");
        successful.Items.Remove(successful.Items.Last());
        Check(!ZohoSyncService.CanRebuildContactMarkers(successful), "Partial module selection cannot clear markers.");

        // Compile the production contact query for PostgreSQL, including tenant/date filters.
        using var sqlDb = new SalesPlattformDbContext(new DbContextOptionsBuilder<SalesPlattformDbContext>()
            .UseNpgsql("Host=unused.invalid;Database=synthetic;Username=test").Options);
        ((IPlatformTenantDbContext)sqlDb).SetPlatformTenant(tenant, true);
        var sqlRepository = new SalesCrmRepositoryFactory().Create(sqlDb);
        var contactQuery = (IQueryable<SalesActivity>)sqlRepository.GetType()
            .GetMethod("ContactActivities", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(sqlRepository, [now])!;
        var sql = contactQuery.ToQueryString();
        Check(sql.Contains(tenant.ToString()) && sql.Contains("email") && sql.Contains("call"), "PostgreSQL contact query must retain tenant and communication filters.");
        Console.WriteLine($"Contact reactivation: {checks} checks passed (synthetic tenant database and PostgreSQL translation; no live CRM writes).");
    }
}
