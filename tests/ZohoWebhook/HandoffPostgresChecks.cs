using System.Text.RegularExpressions;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SalesPlattform.Backend.Data;

internal static class HandoffPostgresChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var connectionString = Environment.GetEnvironmentVariable("SALES_HANDOFF_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (connection.Host != "127.0.0.1" || !Regex.IsMatch(connection.Database ?? "", "^sales_handoff_validation_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Handoff migration checks require a new named loopback-only disposable database.");
        const string previous = "20260903220620_RemoveCrmContacts";
        const string migration = "20261104132530_AddInstallationHookHandoff";
        var tenant = Guid.NewGuid(); var otherTenant = Guid.NewGuid(); var checks = 0;
        void Verify(bool condition, string message) { check(condition, message); checks++; }
        SalesPlattformDbContext Open(Guid owner)
        {
            var db = new SalesPlattformDbContext(new DbContextOptionsBuilder<SalesPlattformDbContext>().UseNpgsql(connectionString).Options);
            ((IPlatformTenantDbContext)db).SetPlatformTenant(owner, true);
            return db;
        }
        await using var db = Open(tenant);
        await using (var probe = new NpgsqlConnection(connectionString))
        {
            await probe.OpenAsync();
            await using var empty = new NpgsqlCommand("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public'", probe);
            Verify((long)(await empty.ExecuteScalarAsync())! == 0, "The migration fixture must start from its own empty database.");
        }
        await db.GetService<IMigrator>().MigrateAsync(previous);
        var subscription = new IntegrationSubscription { Id = Guid.NewGuid(), TenantId = tenant, ProviderKey = "zoho", ConnectionKey = "default",
            Module = "Leads", ChannelId = "123456789", VerificationTokenHash = new string('A', 64), EventsJson = "[\"Leads.edit\"]",
            NotifyUrl = "https://source.synthetic.test/webhook", Status = "active", ExpiresAt = DateTimeOffset.UtcNow.AddDays(2) };
        var webhook = new IntegrationWebhookEvent { Id = Guid.NewGuid(), TenantId = tenant, ProviderKey = "zoho", ConnectionKey = "default",
            EventType = "Leads.edit", PayloadJson = "{\"synthetic\":\"retained-business-payload\"}", Status = "queued", ReceivedAt = DateTimeOffset.UtcNow };
        db.AddRange(subscription, webhook); await db.SaveChangesAsync();
        await using (var other = Open(otherTenant))
        {
            other.IntegrationSubscriptions.Add(new() { Id = Guid.NewGuid(), TenantId = otherTenant, ProviderKey = "zoho", ConnectionKey = "default",
                Module = "Leads", ChannelId = "987654321", VerificationTokenHash = new string('B', 64), EventsJson = "[\"Leads.edit\"]",
                NotifyUrl = "https://foreign.synthetic.test/webhook", Status = "active" });
            await other.SaveChangesAsync();
        }
        await db.Database.MigrateAsync();
        Verify(!db.Database.HasPendingModelChanges(), "The scaffolded model and runtime model must agree.");
        Verify((await db.Database.GetAppliedMigrationsAsync()).Last() == migration, "The additive handoff migration must be applied.");
        db.ChangeTracker.Clear();
        var retained = await db.IntegrationSubscriptions.SingleAsync();
        Verify(retained.Id == subscription.Id && retained.ChannelId == subscription.ChannelId && retained.VerificationTokenHash == subscription.VerificationTokenHash
            && retained.NotifyUrl == subscription.NotifyUrl, "Migration must preserve existing callback identity and location.");
        Verify((await db.IntegrationWebhookEvents.SingleAsync()).PayloadJson.Contains("retained-business-payload", StringComparison.Ordinal),
            "Migration must preserve existing inbox payloads.");
        var handoff = new IntegrationHookHandoff { Id = Guid.NewGuid(), TenantId = tenant, TransitionId = Guid.NewGuid(), InstallationId = Guid.NewGuid(),
            PlacementGeneration = 8, ProviderKey = "zoho", ConnectionKey = "default", AddressMode = "installation",
            TargetBaseUrl = "https://destination.synthetic.test/webhook", CreatedAt = DateTimeOffset.UtcNow };
        var channel = new IntegrationHookHandoffChannel { Id = Guid.NewGuid(), TenantId = tenant, HandoffId = handoff.Id,
            Module = retained.Module, ChannelId = retained.ChannelId, VerificationTokenHash = retained.VerificationTokenHash, EventsJson = retained.EventsJson,
            PreviousNotifyUrl = retained.NotifyUrl, TargetNotifyUrl = handoff.TargetBaseUrl };
        handoff.Channels.Add(channel); db.Add(handoff); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var pending = await db.IntegrationHookHandoffs.Include(x => x.Channels).SingleAsync();
        Verify(pending.Status == "pending" && pending.Channels.Single().VerifiedAt is null, "The durable pending inventory must survive a new read.");
        Verify(pending.Channels.Single().VerificationTokenHash == subscription.VerificationTokenHash, "The journal retains only the existing verification hash.");
        await using (var other = Open(otherTenant))
        {
            Verify(!await other.IntegrationHookHandoffs.AnyAsync() && !await other.IntegrationHookHandoffChannels.AnyAsync(), "Foreign tenant reads cannot see local handoff rows.");
            Verify((await other.IntegrationSubscriptions.SingleAsync()).ChannelId == "987654321", "Migration must also retain other tenants' original subscriptions.");
        }
        await using (var duplicate = Open(tenant))
        {
            duplicate.Add(new IntegrationHookHandoff { Id = Guid.NewGuid(), TenantId = tenant, TransitionId = handoff.TransitionId, InstallationId = handoff.InstallationId,
                PlacementGeneration = 8, ProviderKey = "zoho", ConnectionKey = "default", AddressMode = "installation", TargetBaseUrl = handoff.TargetBaseUrl,
                CreatedAt = DateTimeOffset.UtcNow });
            var rejected = false;
            try { await duplicate.SaveChangesAsync(); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { rejected = true; }
            Verify(rejected, "A retried transition cannot create a second local handoff journal.");
        }
        await using (var sql = new NpgsqlConnection(connectionString))
        {
            await sql.OpenAsync();
            await using var deletion = new NpgsqlCommand("DELETE FROM integration_hook_handoffs WHERE \"Id\"=@id", sql);
            deletion.Parameters.AddWithValue("id", handoff.Id);
            var rejected = false;
            try { await deletion.ExecuteNonQueryAsync(); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ForeignKeyViolation) { rejected = true; }
            Verify(rejected, "The Restrict FK prevents deleting a journal with retained channel evidence.");
        }
        var rollbackRejected = false;
        try { await db.GetService<IMigrator>().MigrateAsync(previous); }
        catch (NotSupportedException) { rollbackRejected = true; }
        Verify(rollbackRejected, "Down migration must refuse loss of retained handoff evidence.");
        Verify((await db.Database.GetAppliedMigrationsAsync()).Last() == migration, "Rejected rollback must retain migration history.");
        Verify(await db.IntegrationHookHandoffs.CountAsync() == 1 && await db.IntegrationHookHandoffChannels.CountAsync() == 1,
            "Rejected rollback and duplicate insert must preserve the original journal and channel.");
        Verify(await db.IntegrationWebhookEvents.CountAsync() == 1 && (await db.IntegrationSubscriptions.SingleAsync()).ChannelId == "123456789",
            "All recovery checks must retain the original business inbox and callback identity.");
        Console.WriteLine($"Sales handoff PostgreSQL: {checks} checks passed; isolated database retained.");
    }
}
