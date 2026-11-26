using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Services;
using SalesPlattform.Backend.Authorization;
using IdentityPlatform.Shared.ApplicationSettings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Net;

var checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
async Task Reject<T>(Func<Task> action) where T : Exception
{
    try { await action(); throw new Exception($"Expected {typeof(T).Name}"); }
    catch (T) { checks++; }
}
var tenant = Guid.NewGuid();
var builder = new DbContextOptionsBuilder<SalesPlattformDbContext>();
var postgres = args.Contains("--postgres");
if (postgres) builder.UseNpgsql("Host=127.0.0.1;Port=55439;Database=sales_team_test;Username=postgres;Password=synthetic-team-test");
else builder.UseInMemoryDatabase(Guid.NewGuid().ToString());
var options = builder.Options;
SalesPlattformDbContext Open(Guid id)
{
    var db = new SalesPlattformDbContext(options);
    ((IPlatformTenantDbContext)db).SetPlatformTenant(id, true);
    return db;
}
if (postgres)
{
    await using var schema = Open(tenant);
    await schema.Database.MigrateAsync();
    await schema.Database.MigrateAsync();
    Check(!(await schema.Database.GetPendingMigrationsAsync()).Any(), "Repeat startup migration has no pending changes.");
}
var items = TaskGoalProfileService.Types.Select((t, n) => new TaskGoalItemDto(t.Key, 25, 10, 6, n + 1)).ToArray();
var valid = new SaveTaskGoalProfileRequest(" Junior ", 240, items, null);
using (var payload = System.Text.Json.JsonDocument.Parse("""{"id":"test-lead","Company":"Test","Industry":"Logistik"}"""))
{
    var mapped = new SalesPlattform.Backend.Integrations.Zoho.ZohoCrmRecordMapper().Map(
        new SalesPlattform.Backend.Integrations.Abstractions.CrmExternalRecord("zoho", "Leads", "test-lead", payload.RootElement, null));
    Check(mapped is SalesPlattform.Backend.Integrations.Abstractions.CrmCanonicalLead { Industry: "Logistik" },
        "The Zoho Lead mapper retains the industry field.");
}
var httpContext = new DefaultHttpContext();
httpContext.Request.Headers.Authorization = "Bearer synthetic-test";
var access = new TenantAdminAccessService(new DeniedTenantClient(), new HttpContextAccessor { HttpContext = httpContext },
    Options.Create(new ApplicationSettingsOptions { PlatformApiUrl = "https://platform.example.test", ApplicationKey = "sales-plattform" }));
var guarded = new TaskGoalProfileService(null!, access);
await Reject<UnauthorizedAccessException>(() => guarded.SaveAsync(null, valid, new ClaimsPrincipal(), default));
var claimedAdmin = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tenant.ToString()), new Claim(ClaimTypes.Role, "sales-manager")], "test"));
await Reject<UnauthorizedAccessException>(() => guarded.SaveAsync(null, valid, claimedAdmin, default));
foreach (var invalid in new[] {
    valid with { Name = " " }, valid with { Name = new string('x', 201) },
    valid with { DailyCapacityMinutes = 0 }, valid with { DailyCapacityMinutes = 1441 },
    valid with { Items = null! }, valid with { Items = [] },
    valid with { Items = [null!, ..items.Skip(1)] },
    valid with { Items = [items[0], items[0], ..items.Skip(2)] },
    valid with { Items = [items[0] with { TaskType = "arbitrary-type" }, ..items.Skip(1)] },
    valid with { Items = [items[0] with { SharePercent = 101 }, ..items.Skip(1)] },
    valid with { Items = [items[0] with { MinutesPerTask = 0 }, ..items.Skip(1)] },
    valid with { Items = [items[0] with { DailyTarget = -1 }, ..items.Skip(1)] },
    valid with { Items = [items[0] with { Priority = 5 }, ..items.Skip(1)] },
    valid with { Items = items.Select(i => i with { DailyTarget = 0 }).ToArray() },
}) await Reject<ArgumentException>(() => { TaskGoalProfileService.Validate(invalid); return Task.CompletedTask; });
TaskGoalProfileDto created;
await using (var db = Open(tenant))
{
    created = await TaskGoalProfileService.SaveToDatabaseAsync(db, null, valid, default);
    Check(created.Name == "Junior" && created.Items.Length == 4 && created.Items.Sum(i => i.DailyTarget) == 24, "Create saves typed goals and normalized name.");
    Check(await db.TaskGoalProfileItems.AllAsync(i => i.TenantId == tenant), "Every item has the current tenant.");
}
await using (var db = Open(Guid.NewGuid()))
{
    Check(!await db.TaskGoalProfiles.AnyAsync() && !await db.TaskGoalProfileItems.AnyAsync(), "Tenant cannot read another tenant's profiles or goals.");
    await Reject<KeyNotFoundException>(() => TaskGoalProfileService.SaveToDatabaseAsync(db, created.Id, valid with { Revision = created.Revision }, default));
}
await using (var db = Open(tenant))
{
    var changed = System.Text.Json.JsonSerializer.Deserialize<SaveTaskGoalProfileRequest>(
        System.Text.Json.JsonSerializer.Serialize(new { Name = "Senior", Revision = created.Revision,
            Items = items.Select((item, index) => new { item.TaskType, DailyTarget = index == 0 ? 50 : item.DailyTarget }) }))!;
    var edited = await TaskGoalProfileService.SaveToDatabaseAsync(db, created.Id, changed, default);
    Check(edited.Revision != created.Revision && edited.Items[0].DailyTarget == 50, "A count-only request updates the daily target.");
    Check(edited.DailyCapacityMinutes == 240 && edited.Items[0].SharePercent == 25, "A count-only edit preserves previous planning data.");
    Check(await db.TaskGoalProfileItems.CountAsync() == 4, "Editing updates existing goals without duplicates.");
}
await using (var db = Open(tenant))
{
    await Reject<TaskGoalProfileConflictException>(() => TaskGoalProfileService.SaveToDatabaseAsync(db, created.Id, valid with { Revision = created.Revision }, default));
    await Reject<TaskGoalProfileConflictException>(() => TaskGoalProfileService.SaveToDatabaseAsync(db, created.Id, valid, default));
    Check((await db.TaskGoalProfiles.SingleAsync()).Name == "Senior", "Stale request leaves persisted data unchanged.");
    await Reject<ArgumentException>(() => TaskGoalProfileService.SaveToDatabaseAsync(db, null, valid with { Revision = created.Revision }, default));
}
await using (var a = Open(tenant))
await using (var b = Open(tenant))
{
    var first = await a.TaskGoalProfiles.SingleAsync();
    var second = await b.TaskGoalProfiles.SingleAsync();
    first.Revision = Guid.NewGuid(); await a.SaveChangesAsync();
    second.Revision = Guid.NewGuid();
    await Reject<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
}
if (postgres)
{
    await using var foreign = Open(Guid.NewGuid());
    foreign.TaskGoalProfileItems.Add(new TaskGoalProfileItem { Id = Guid.NewGuid(), TenantId = foreign.CurrentTenantId,
        ProfileId = created.Id, TaskType = "cold-call", MinutesPerTask = 10 });
    await Reject<DbUpdateException>(() => foreign.SaveChangesAsync());
}
await using (var db = Open(tenant))
{
    var member = Guid.NewGuid();
    var otherMember = Guid.NewGuid();
    var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    var day = DateOnly.FromDateTime(now.DateTime);
    string Config(int target, string[] industries) => System.Text.Json.JsonSerializer.Serialize(
        new SalesTaskGoalConfiguration(TaskGoalProfileService.Types.Select(t =>
            new DailyTaskGoal(t.Key, t.Key == "cold-call" ? target : 0)).ToArray(), industries));

    var personal = new SalesTaskGoalPlan { Id = Guid.NewGuid(), TenantId = tenant, MembershipId = member,
        Name = "Persönlich", Configuration = Config(3, ["Handel"]) };
    var period = new SalesTaskGoalPlan { Id = Guid.NewGuid(), TenantId = tenant, StartsOn = day, EndsOn = day,
        Name = "Tagesaktion", Audience = [member, otherMember], Configuration = Config(1, ["BAUGEWERBE", "Logistik"]) };
    db.SalesTaskGoalPlans.AddRange(personal, period);

    Guid AddLead(string industry, string status = "new", string type = "lead-first-response")
    {
        var id = Guid.NewGuid();
        var workId = Guid.NewGuid();
        db.SalesLeads.Add(new SalesLead { Id = id, TenantId = tenant, Name = "Testlead", Phone = "123",
            Industry = industry, Status = status, SourceCreatedAt = now.AddDays(-1) });
        db.SalesWorkItems.Add(new SalesWorkItem { Id = workId, TenantId = tenant, Title = "Testaufgabe",
            WorkItemType = type, WorkItemChainId = workId, Status = "open", CreatedAt = now, UpdatedAt = now,
            Relations = [new SalesWorkItemRelation { Id = Guid.NewGuid(), TenantId = tenant, TargetId = id,
                TargetType = "lead", RelationRole = "primary" }] });
        return id;
    }

    var construction = AddLead("Baugewerbe");
    var logistics = AddLead("Logistik", type: "call-follow-up");
    var retail = AddLead("Handel");
    var unknownIndustry = AddLead("");
    AddLead("Baugewerbe", "disqualified");
    AddLead("Baugewerbe", type: "lead-reactivation");
    await db.SaveChangesAsync();
    Check(await SalesTaskPlanningService.DistributeToDatabaseAsync(db, now, default) == 2,
        "Active period fills one daily goal for each eligible recipient.");
    var assignments = await db.SalesTaskAssignments.AsNoTracking().ToArrayAsync();
    Check(assignments.Select(a => a.TargetId).ToHashSet().SetEquals([construction, logistics])
        && assignments.All(a => a.PlanId == period.Id && a.TaskType == "cold-call"),
        "The override replaces the personal sector filter and skips disqualified and unproven reactivation leads.");
    Check(assignments.Select(a => a.MembershipId).ToHashSet().SetEquals([member, otherMember]), "Each recipient gets a distinct candidate.");
    Check(await SalesTaskPlanningService.DistributeToDatabaseAsync(db, now, default) == 0, "Running again leaves assignments unchanged.");

    var trackedAssignment = assignments.Single(a => a.TargetId == construction);
    var zohoActivity = new SalesActivity { Id = Guid.NewGuid(), TenantId = tenant, ActivityType = "task", OccurredAt = now };
    var sync = new IntegrationSyncRun { Id = Guid.NewGuid(), TenantId = tenant, ProviderKey = "zoho", ConnectionKey = "default" };
    db.AddRange(zohoActivity, sync, new IntegrationEntityLink { Id = Guid.NewGuid(), TenantId = tenant,
        ProviderKey = "zoho", EntityType = "activity", ExternalId = "crm-task-test", InternalEntityType = "activity",
        InternalEntityId = zohoActivity.Id, WorkItemId = trackedAssignment.WorkItemId });
    await db.SaveChangesAsync();
    var repository = new SalesPlattform.Backend.Integrations.Repositories.SalesCrmRepositoryFactory().Create(db);
    var mapper = new SalesPlattform.Backend.Integrations.Zoho.ZohoCrmRecordMapper();
    async Task ImportTask(string status, DateTimeOffset modifiedAt)
    {
        using var payload = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new { Status = status }));
        var record = new SalesPlattform.Backend.Integrations.Abstractions.CrmExternalRecord("zoho", "Tasks", "crm-task-test", payload.RootElement, modifiedAt);
        await repository.UpsertAsync(mapper.Map(record), sync.Id, 20, default);
        await db.SaveChangesAsync();
    }
    async Task<DateTimeOffset?> CompletedAt() => await db.SalesTaskAssignments.AsNoTracking()
        .Where(a => a.Id == trackedAssignment.Id).Select(a => a.CrmCompletedAt).SingleAsync();
    var completed = now.AddMinutes(1);
    await ImportTask("Completed", completed);
    Check(await CompletedAt() == completed, "Zoho task completion updates the linked assignment.");
    await ImportTask("In Progress", now);
    Check(await CompletedAt() == completed, "An older event cannot undo completion.");
    await ImportTask("Completed", now.AddMinutes(2));
    Check(await CompletedAt() == completed, "Later updates to a completed task preserve the original completion date.");
    await ImportTask("In Progress", now.AddMinutes(3));
    Check(await CompletedAt() is null, "Reopening in CRM reopens the assignment status in Sales.");
    Check(await SalesTaskPlanningService.DistributeToDatabaseAsync(db, now.AddDays(1), default) == 1,
        "Default personal goals resume after the inclusive end date.");
    Check(await db.SalesTaskAssignments.AnyAsync(a => a.AssignedOn == day.AddDays(1) && a.TargetId == retail && a.PlanId == personal.Id),
        "Personal filter and source plan are persisted for the next day.");
    personal.Configuration = Config(1, []);
    await db.SaveChangesAsync();
    Check(await SalesTaskPlanningService.DistributeToDatabaseAsync(db, now.AddDays(2), default) == 1
        && await db.SalesTaskAssignments.AnyAsync(a => a.TargetId == unknownIndustry),
        "An empty filter includes otherwise eligible targets without an industry.");

    var futureDate = day.AddDays(3);
    SalesTeamUser[] audience = [new(member, "planner@example.test")];
    var overrideGoals = TaskGoalProfileService.Types.Select(t => new DailyTaskGoal(t.Key, 0)).ToArray();
    var request = new SaveSalesTaskPlan("Überschreiben", overrideGoals, ["Handel"], null, futureDate, futureDate);
    var added = await SalesTaskPlanningService.SaveToDatabaseAsync(db, null, null, request, audience, day, default);
    await Reject<ArgumentException>(() => SalesTaskPlanningService.SaveToDatabaseAsync(db, null, null, request, audience, day, default));
    var edited = await SalesTaskPlanningService.SaveToDatabaseAsync(db, null, added.Id,
        request with { Name = "Geändertes Ziel", Revision = added.Revision }, audience, day, default);
    Check(edited.Id == added.Id && edited.Name == "Geändertes Ziel" && edited.Revision != added.Revision,
        "Editing updates the same period and reserves its date.");
    await Reject<SalesTaskPlanConflictException>(() => SalesTaskPlanningService.SaveToDatabaseAsync(db, null, added.Id,
        request with { Revision = added.Revision }, audience, day, default));
    var disabled = await SalesTaskPlanningService.DeactivatePeriodInDatabaseAsync(db, added.Id, now, default);
    Check(disabled.DeactivatedAt == now && disabled.Name == edited.Name, "Deactivation retains the saved historical entry.");
    await Reject<SalesTaskPlanConflictException>(() => SalesTaskPlanningService.SaveToDatabaseAsync(db, null, added.Id,
        request with { Revision = disabled.Revision }, audience, day, default));
    var afterDeactivation = AddLead("Handel");
    await db.SaveChangesAsync();
    Check(await SalesTaskPlanningService.DistributeToDatabaseAsync(db, now.AddDays(3), default) == 1
        && await db.SalesTaskAssignments.AnyAsync(a => a.TargetId == afterDeactivation && a.PlanId == personal.Id),
        "The scheduler resumes personal goals when an overlapping period was deactivated.");
    var replacement = await SalesTaskPlanningService.SaveToDatabaseAsync(db, null, null, request, audience, day, default);
    Check(replacement.Id != disabled.Id && replacement.StartsOn == added.StartsOn, "A deactivated period does not block a replacement.");
}
await using (var foreign = Open(Guid.NewGuid()))
{
    Check(!await foreign.SalesTaskGoalPlans.AnyAsync() && !await foreign.SalesTaskAssignments.AnyAsync(),
        "Another tenant cannot read the plans or assignments.");
}
Console.WriteLine($"Task goal profiles: {checks} checks passed ({(postgres ? "PostgreSQL" : "InMemory")}).");

sealed class DeniedTenantClient : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new DeniedTenantHandler());
    private sealed class DeniedTenantHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json") });
    }
}
