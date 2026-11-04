using System.Security.Claims;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Database;
using IdentityPlatform.Shared.Installations;
using IdentityPlatform.Shared.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Zoho;

internal static class HandoffChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var tenant = Guid.NewGuid();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("tenant_id", tenant.ToString("D")), new Claim("sub", "synthetic-admin")], "test"));
        var settings = new SyntheticSettingsStore();
        void Set(string key, object? value, Guid? scopeTenant = null, string scope = "tenantApp")
        {
            settings.Records.RemoveAll(x => x.Key == key && x.TenantId == (scopeTenant ?? tenant) && x.Scope == scope);
            settings.Records.Add(new(key, scope, scopeTenant ?? tenant, null, JsonSerializer.SerializeToElement(value), DateTimeOffset.UtcNow));
        }
        const string relay = "https://relay.customer.test/api/integrations/zoho/webhook";
        var runtime = new InstallationRuntimeOptions { InstallationId = Guid.NewGuid(), PublicOrigin = "https://sales.destination.test:8443" };
        var resolver = new ZohoWebhookSettingsService(settings, Options.Create(new ApplicationSettingsOptions { ApplicationKey = "sales-plattform" }), http, runtime);
        Set(ZohoWebhookSettingsService.SettingKey, relay);
        check((await resolver.ResolveCurrentAsync()).BaseUri?.AbsoluteUri == relay, "Legacy configured relay must remain custom even in installation mode.");
        Set(ZohoWebhookSettingsService.AddressModeKey, "installation", Guid.NewGuid());
        Set(ZohoWebhookSettingsService.AddressModeKey, "installation", scope: "appUser");
        check((await resolver.ResolveCurrentAsync()).Source == "tenantApp", "Foreign tenant/app-user address modes must not override the tenant-app setting.");
        Set(ZohoWebhookSettingsService.AddressModeKey, "installation");
        check((await resolver.ResolveCurrentAsync()) is { Source: "installation", Error: null } direct
            && direct.BaseUri?.AbsoluteUri == "https://sales.destination.test:8443/api/integrations/zoho/webhook", "Explicit installation mode must derive the checked direct origin.");
        runtime.PublicOrigin = "https://sales.destination.test/other";
        check((await resolver.ResolveCurrentAsync()).BaseUri is null, "Invalid installation origin must not fall back to the configured relay.");
        runtime.PublicOrigin = "https://sales.destination.test";
        Set(ZohoWebhookSettingsService.AddressModeKey, "custom");
        check((await resolver.ResolveCurrentAsync()).BaseUri?.AbsoluteUri == relay, "Explicit custom mode retains the customer address.");
        Set(ZohoWebhookSettingsService.AddressModeKey, "guess");
        check((await resolver.ResolveCurrentAsync()).BaseUri is null, "Unknown address modes fail closed.");
        Set(ZohoWebhookSettingsService.AddressModeKey, "installation");
        var legacy = new ZohoWebhookSettingsService(settings, Options.Create(new ApplicationSettingsOptions { ApplicationKey = "sales-plattform" }), http);
        check((await legacy.ResolveCurrentAsync()).BaseUri is null, "Installation address mode requires an installation; no localhost fallback.");

        var denied = new ZohoHookHandoffService(null!, null!, resolver, http, runtime, null);
        var untrustedContext = new PlatformJobExecutionContext(Guid.NewGuid(), tenant, "installationplacementchanged", "system:test",
            JsonSerializer.SerializeToElement(new { transitionId = Guid.NewGuid(), placementGeneration = 42 }), null!, null!);
        check((await denied.ResumeAsync(untrustedContext, default))?.Count == 1,
            "Editable parameters or a trigger label cannot authorize a handoff without a signed placement and trusted job claim.");

        const string originalUrl = "https://sales.source.test/api/integrations/zoho/webhook?tenant_id=00000000-0000-0000-0000-000000000001";
        const string destinationUrl = "https://sales.destination.test/api/integrations/zoho/webhook?tenant_id=00000000-0000-0000-0000-000000000001";
        const string eventsJson = "[\"Leads.create\",\"Leads.edit\",\"Leads.delete\"]";
        var hash = ZohoCrmHookUpdateService.HashToken("synthetic-verification-token");
        var expires = DateTimeOffset.UtcNow.AddDays(2);
        (IntegrationHookHandoffChannel Intent, IntegrationSubscription Subscription) Pair() => (new()
        {
            Id = Guid.NewGuid(), TenantId = tenant, HandoffId = Guid.NewGuid(), Module = "Leads", ChannelId = "123456789",
            EventsJson = eventsJson, VerificationTokenHash = hash, PreviousNotifyUrl = originalUrl, TargetNotifyUrl = destinationUrl
        }, new()
        {
            Id = Guid.NewGuid(), TenantId = tenant, ProviderKey = "zoho", ConnectionKey = "default", Module = "Leads",
            ChannelId = "123456789", EventsJson = eventsJson, VerificationTokenHash = hash, NotifyUrl = originalUrl,
            Status = "active", ExpiresAt = expires
        });
        ZohoNotificationSnapshot Remote(string url) => new("123456789", "Leads", url, expires,
            ["Leads.delete", "Leads.edit", "Leads.create"], hash, new("none", "None", []), false);

        var pair = Pair(); var remote = Remote(originalUrl); var order = new List<string>(); var patches = 0;
        Task<IReadOnlyList<ZohoNotificationSnapshot>> Read(CancellationToken _) { order.Add("read"); return Task.FromResult<IReadOnlyList<ZohoNotificationSnapshot>>([remote]); }
        Task Save(CancellationToken _) { order.Add(pair.Intent.VerifiedAt is null ? "intent" : "complete"); return Task.CompletedTask; }
        Task Patch(IReadOnlyList<string> events, DateTimeOffset expiry, CancellationToken _)
        {
            check(pair.Intent.PatchAttemptedAt.HasValue && order.Last() == "intent", "Persist the intent before the provider mutation.");
            check(expiry == expires && events.Count == 3, "Preserve existing expiry and events.");
            patches++; order.Add("patch"); remote = remote with { NotifyUrl = destinationUrl }; return Task.CompletedTask;
        }
        check(await ZohoHookHandoffChannelFlow.ExecuteAsync(pair.Intent, pair.Subscription, Read, Patch, Save, default) is null,
            "A confirmed PATCH followed by exact GET verification must complete.");
        check(order.SequenceEqual(["read", "intent", "patch", "read", "complete"]), "Handoff ordering must be read, durable intent, PATCH, verify, commit.");
        check(pair.Subscription.ChannelId == "123456789" && pair.Subscription.VerificationTokenHash == hash
            && pair.Subscription.NotifyUrl == destinationUrl, "A handoff changes only callback location and verified metadata, never channel/token identity.");
        var payload = ZohoCrmAdapter.BuildNotificationHandoff("123456789", "Leads", destinationUrl, remote.Events, expires).GetProperty("watch")[0];
        check(!payload.TryGetProperty("token", out _) && !payload.TryGetProperty("notification_condition", out _)
            && payload.GetProperty("channel_id").GetString() == "123456789", "PATCH must omit token and filters so Zoho retains them.");

        pair = Pair(); remote = Remote(originalUrl); order.Clear(); patches = 0;
        Task LostReply(IReadOnlyList<string> events, DateTimeOffset expiry, CancellationToken ct)
        { patches++; remote = remote with { NotifyUrl = destinationUrl }; throw new TimeoutException("Synthetic lost reply"); }
        check(await ZohoHookHandoffChannelFlow.ExecuteAsync(pair.Intent, pair.Subscription, Read, LostReply, Save, default) == "provider-unavailable"
            && pair.Intent.VerifiedAt is null && pair.Subscription.NotifyUrl == originalUrl, "Lost PATCH replies must leave an unconfirmed local intent.");
        check(await ZohoHookHandoffChannelFlow.ExecuteAsync(pair.Intent, pair.Subscription, Read, LostReply, Save, default) is null && patches == 1,
            "Retry after a lost reply must GET the same channel, recognize completion and never repeat the external write.");

        foreach (var mutation in new Func<ZohoNotificationSnapshot, ZohoNotificationSnapshot>[] {
            x => x with { ChannelId = "987" }, x => x with { TokenHash = new string('F', 64) },
            x => x with { Events = ["Leads.edit"] }, x => x with { NotifyUrl = "https://unrelated.test/" },
            x => x with { Filters = new("present", "Filtered", []) } })
        {
            pair = Pair(); remote = mutation(Remote(originalUrl)); patches = 0;
            check(await ZohoHookHandoffChannelFlow.ExecuteAsync(pair.Intent, pair.Subscription, Read, Patch, Save, default) == "channel-conflict"
                && patches == 0, "A conflicting external channel must not be overwritten or replaced.");
        }
        pair = Pair(); remote = Remote(originalUrl); patches = 0;
        Task ScopeMissing(IReadOnlyList<string> _, DateTimeOffset __, CancellationToken ___) => throw new InvalidOperationException("OAUTH_SCOPE_MISMATCH private-provider-body");
        check(await ZohoHookHandoffChannelFlow.ExecuteAsync(pair.Intent, pair.Subscription, Read, ScopeMissing, Save, default) == "scope-missing"
            && pair.Intent.VerifiedAt is null && ZohoHookHandoffService.Message("scope-missing").Contains("UPDATE"), "Old OAuth grants require explicit reauthorization without replacing channels.");
        pair = Pair(); remote = Remote(originalUrl); patches = 0;
        Task BrokenSave(CancellationToken _) => throw new IOException("Synthetic commit failure");
        check(await ZohoHookHandoffChannelFlow.ExecuteAsync(pair.Intent, pair.Subscription, Read, Patch, BrokenSave, default) == "provider-unavailable"
            && patches == 0, "A failed intent commit must prevent the external PATCH.");
        pair = Pair(); remote = Remote(originalUrl);
        using var cancelled = new CancellationTokenSource();
        Task CancelAfterWrite(IReadOnlyList<string> _, DateTimeOffset __, CancellationToken ct)
        { remote = remote with { NotifyUrl = destinationUrl }; cancelled.Cancel(); throw new OperationCanceledException(ct); }
        var cancelledObserved = false;
        try { await ZohoHookHandoffChannelFlow.ExecuteAsync(pair.Intent, pair.Subscription, Read, CancelAfterWrite, Save, cancelled.Token); }
        catch (OperationCanceledException) { cancelledObserved = true; }
        check(cancelledObserved && pair.Intent.PatchAttemptedAt.HasValue && pair.Intent.VerifiedAt is null, "Drain cancellation retains the pending exact-channel intent.");
        check(await ZohoHookHandoffChannelFlow.ExecuteAsync(pair.Intent, pair.Subscription, Read, Patch, Save, default) is null,
            "A later admitted execution can confirm an externally applied callback after cancellation.");

        var options = new DbContextOptionsBuilder<SalesPlattformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var db = new SalesPlattformDbContext(options))
        {
            ((IPlatformTenantDbContext)db).SetPlatformTenant(tenant, true);
            db.IntegrationHookHandoffs.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, TransitionId = Guid.NewGuid(),
                InstallationId = runtime.InstallationId, PlacementGeneration = 2, ProviderKey = "zoho", ConnectionKey = "default",
                AddressMode = "custom", TargetBaseUrl = relay, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await using (var other = new SalesPlattformDbContext(options))
        {
            ((IPlatformTenantDbContext)other).SetPlatformTenant(Guid.NewGuid(), true);
            check(!await other.IntegrationHookHandoffs.AnyAsync(), "Local handoff intents must retain the existing tenant isolation.");
        }
    }
}
