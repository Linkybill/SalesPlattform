using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Services;

var checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
async Task Reject<T>(Func<Task> action) where T : Exception
{
    try { await action(); throw new Exception($"Expected {typeof(T).Name}"); }
    catch (T) { checks++; }
}
var tenant = Guid.NewGuid();
var first = new SalesTeamUser(Guid.NewGuid(), "first@example.test");
var second = new SalesTeamUser(Guid.NewGuid(), "second@example.test");
var other = new SalesTeamUser(Guid.NewGuid(), "other@example.test");
SalesTeamUser[] users = [first, second];
var postgres = args.Contains("--postgres");
var builder = new DbContextOptionsBuilder<SalesPlattformDbContext>();
if (postgres) builder.UseNpgsql("Host=127.0.0.1;Port=55439;Database=sales_team_test;Username=postgres;Password=synthetic-team-test");
else builder.UseInMemoryDatabase(Guid.NewGuid().ToString());
var options = builder.Options;
SalesPlattformDbContext Open(Guid id)
{
    var db = new SalesPlattformDbContext(options);
    ((IPlatformTenantDbContext)db).SetPlatformTenant(id, true);
    return db;
}
if (postgres) { await using var schema = Open(tenant); await schema.Database.MigrateAsync(); }
var eligible = SalesTeamDirectory.EligibleUsers([
    new(first.Id, first.Email, "Member", "Active", [new("sales-plattform", true)]),
    new(second.Id, second.Email, "TenantAdmin", "Active", [new("sales-plattform", false)]),
    new(Guid.NewGuid(), "blocked@example.test", "Member", "Active", [new("sales-plattform", false)]),
    new(Guid.NewGuid(), "inactive@example.test", "TenantAdmin", "Inactive", [new("sales-plattform", true)]),
    new(Guid.NewGuid(), "other-app@example.test", "Member", "Active", [new("other", true)]),
], "sales-plattform");
Check(eligible.Select(u => u.Id).ToHashSet().SetEquals([first.Id, second.Id]), "Only active Sales users and tenant admins are selectable.");
foreach (var invalid in new SaveSalesTeamRequest[] {
    new(" ", [first.Id], null), new(new string('x',201),[first.Id],null), new("Team",[],null),
    new("Team",[first.Id,first.Id],null),new("Team",[other.Id],null),new("Team",[Guid.Empty],null) })
    await Reject<ArgumentException>(() => { SalesTeamService.Validate(invalid, users); return Task.CompletedTask; });

SalesTeamDto created;
await using (var db = Open(tenant))
{
    created = await SalesTeamService.SaveToDatabaseAsync(db, null, new("  Vertrieb Nord  ", [first.Id, second.Id], null), users, default);
    Check(created.Name == "Vertrieb Nord" && created.Members.Length == 2, "Create normalizes name and saves both members.");
    Check(await db.SalesTeamUserMembers.AllAsync(m => m.TenantId == tenant), "Membership tenant is set.");
}
await using (var db = Open(Guid.NewGuid()))
{
    Check(!await db.SalesTeams.AnyAsync() && !await db.SalesTeamUserMembers.AnyAsync(), "Other tenant cannot list teams or members.");
    await Reject<KeyNotFoundException>(() => SalesTeamService.SaveToDatabaseAsync(db, created.Id, new("Foreign",[first.Id],created.Revision),users,default));
}
await using (var db = Open(tenant))
{
    await Reject<ArgumentException>(() => SalesTeamService.SaveToDatabaseAsync(db, created.Id, new("Changed",[first.Id,other.Id],created.Revision),users,default));
    Check((await db.SalesTeams.Include(t => t.UserMembers).SingleAsync()).UserMembers.Count == 2, "Invalid member cannot partially update team.");
}
await using (var db = Open(tenant))
{
    var edited = await SalesTeamService.SaveToDatabaseAsync(db, created.Id, new("Vertrieb Süd",[second.Id],created.Revision),users,default);
    Check(edited.Revision != created.Revision && edited.Members.Single().Id == second.Id, "Editing replaces membership and revision.");
    Check(await db.SalesTeamUserMembers.CountAsync() == 1, "Removed memberships are deleted.");
}
await using (var db = Open(tenant))
{
    var saved = await db.SalesTeams.AsNoTracking().SingleAsync();
    var edited = await SalesTeamService.SaveToDatabaseAsync(db, created.Id, new("Vertrieb Süd",[first.Id,second.Id],saved.Revision),users,default);
    Check(edited.Members.Length == 2 && await db.SalesTeamUserMembers.CountAsync() == 2, "Existing team can add a member again.");
}
await using (var db = Open(tenant))
{
    await Reject<SalesTeamConflictException>(() => SalesTeamService.SaveToDatabaseAsync(db, created.Id, new("Stale",[first.Id],created.Revision),users,default));
    Check((await db.SalesTeams.SingleAsync()).Name == "Vertrieb Süd", "Stale edit preserves saved version.");
    var another = await SalesTeamService.SaveToDatabaseAsync(db,null,new("Zweites Team",[second.Id],null),users,default);
    Check(another.Members.Single().Id == second.Id, "Person may belong to multiple teams.");
}
await using (var a = Open(tenant))
await using (var b = Open(tenant))
{
    var one = await a.SalesTeams.SingleAsync(t => t.Id == created.Id);
    var two = await b.SalesTeams.SingleAsync(t => t.Id == created.Id);
    one.Name = "Concurrent winner"; one.Revision = Guid.NewGuid(); await a.SaveChangesAsync();
    two.Name = "Concurrent loser"; two.Revision = Guid.NewGuid();
    await Reject<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
}
Console.WriteLine($"Teams: {checks} checks passed.");
