using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Zoho;

internal static class DiagnosticQueueChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<SalesPlattformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new SalesPlattformDbContext(options);
        var tenant = Guid.NewGuid();
        ((IPlatformTenantDbContext)db).SetPlatformTenant(tenant, true);
        // Exercise the real persistence path with synthetic storage; tenant setting resolution
        // and the HTTP routing guard are covered separately.
        var receiver = new ZohoWebhookReceiver(null!, null!, NullLogger<ZohoWebhookReceiver>.Instance);
        var receive = typeof(ZohoWebhookReceiver).GetMethod("ReceiveCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task<ZohoWebhookReceipt> Receive(JsonElement payload, bool diagnostic = true)
            => (Task<ZohoWebhookReceipt>)receive.Invoke(receiver, [db, payload, diagnostic, CancellationToken.None])!;
        JsonElement Payload(string? channel = null, string? token = null, string module = "Leads")
            => JsonSerializer.SerializeToElement(new { module, operation = "update", ids = new[] { "123", "123" },
                channel_id = channel, token, private_data = "never-store-this" });

        var payload = Payload();
        var receipt = await Receive(payload);
        check(receipt is { Queued: true, Status: "queued", Authenticated: false },
            "Diagnostic events without channel/token must actually queue without claiming authentication.");
        var row = await db.IntegrationWebhookEvents.SingleAsync();
        check(row.Id == receipt.EventId && row.Status == "queued" && row.EventType == "Leads.edit",
            "The receipt must identify the persisted normal queue row.");
        check(row.TenantId == tenant, "Diagnostic events must belong to the routed tenant.");
        await using (var otherTenantDb = new SalesPlattformDbContext(options))
        {
            ((IPlatformTenantDbContext)otherTenantDb).SetPlatformTenant(Guid.NewGuid(), true);
            check(!await otherTenantDb.IntegrationWebhookEvents.AnyAsync(), "Diagnostic events must not leak to another tenant.");
        }
        check(!row.PayloadJson.Contains("token") && !row.PayloadJson.Contains("never-store-this"),
            "Diagnostic queue must strip secrets and arbitrary fields.");
        var payloadType = typeof(ZohoWebhookReceiver).Assembly.GetType("SalesPlattform.Backend.Integrations.Zoho.ZohoWebhookPayload")!;
        var stored = payloadType.GetMethod("ParseStored")!.Invoke(null, [row.PayloadJson])!;
        check(((IReadOnlyCollection<string>)payloadType.GetProperty("RecordIds")!.GetValue(stored)!).SequenceEqual(["123"]),
            "The worker must be able to read diagnostic payloads with no channel/token.");
        var duplicate = await Receive(payload);
        check(!duplicate.Queued && !duplicate.Authenticated && duplicate.EventId == row.Id
            && await db.IntegrationWebhookEvents.CountAsync() == 1, "Diagnostic retries must not create duplicate rows.");
        row.Status = "processed";
        await db.SaveChangesAsync();
        check((await Receive(payload)).Status == "processed", "Duplicates must report the existing processing status.");

        foreach (var bad in new[] { "{}", "[]", "null", "{\"module\":\"Leads\",\"operation\":\"edit\"}",
            "{\"module\":\"Leads\",\"operation\":\"unknown\",\"ids\":[\"123\"]}" })
        {
            try { await Receive(JsonSerializer.Deserialize<JsonElement>(bad)); throw new Exception("Invalid event queued."); }
            catch (InvalidOperationException) { check(true, "Invalid diagnostic payload rejected."); }
        }
        try { await Receive(Payload(module: "Contacts")); throw new Exception("Contacts queued."); }
        catch (InvalidOperationException) { check(true, "Contacts exclusion must apply in diagnostic mode."); }
        try { await Receive(payload, diagnostic: false); throw new Exception("Missing credentials accepted outside diagnostics."); }
        catch (InvalidOperationException) { check(true, "Disabling diagnostic mode restores required credentials."); }
        try { await Receive(Payload("unknown", "incorrect"), diagnostic: false); throw new Exception("Unknown subscription accepted."); }
        catch (UnauthorizedAccessException) { check(true, "Normal mode still rejects unverified subscriptions."); }
        check(await db.IntegrationWebhookEvents.CountAsync() == 1, "Rejected events must never persist.");
        check((await Receive(Payload("unknown", "incorrect"))).Queued,
            "Diagnostic mode must bypass verification even for supplied invalid credentials.");
        check(!(await db.IntegrationWebhookEvents.ToListAsync()).Any(x => x.PayloadJson.Contains("incorrect")),
            "Supplied diagnostic tokens must not enter storage.");

        db.IntegrationSubscriptions.Add(new IntegrationSubscription
        {
            ProviderKey = "zoho", ConnectionKey = "default", Module = "Leads", ChannelId = "valid-channel",
            EventsJson = "[]", NotifyUrl = "https://sales.example.test/api/integrations/zoho/webhook",
            Status = "active", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            VerificationTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("valid-token")))
        });
        await db.SaveChangesAsync();
        check(await Receive(Payload("valid-channel", "valid-token"), diagnostic: false)
            is { Queued: true, Authenticated: true }, "Verified callbacks must retain normal authenticated queueing.");
    }
}
