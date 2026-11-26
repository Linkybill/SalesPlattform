using System.Net;
using System.Security.Claims;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations;
using SalesPlattform.Backend.Integrations.Repositories;
using SalesPlattform.Backend.Integrations.Zoho;

internal static class ImportRegressionChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var tenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<SalesPlattformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new SalesPlattformDbContext(options);
        ((IPlatformTenantDbContext)db).SetPlatformTenant(tenant, true);
        var first = Event();
        var second = Event();
        db.IntegrationWebhookEvents.AddRange(first, second);
        await db.SaveChangesAsync();
        var repository = new SalesCrmRepositoryFactory().Create(db);
        // A failed record must be discarded, but later events in the loaded batch
        // must still persist their successful completion.
        db.SalesOwners.Add(new SalesOwner { DisplayName = "uncommitted failed record" });
        repository.DetachRecordChanges();
        first.Status = "failed";
        first.AttemptCount = 1;
        db.IntegrationWebhookEvents.Update(first);
        await db.SaveChangesAsync();
        second.Status = "processed";
        second.AttemptCount = 1;
        second.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        check((await db.IntegrationWebhookEvents.SingleAsync(x => x.Id == second.Id)).Status == "processed",
            "A failure earlier in the batch must not lose later event completion.");
        check((await db.IntegrationWebhookEvents.SingleAsync(x => x.Id == first.Id)).AttemptCount == 1,
            "Failed event retry count must persist.");
        check(!await db.SalesOwners.AnyAsync(), "Failed canonical changes must not leak into the next event.");

        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("tenant_id", tenant.ToString()), new Claim("sub", "synthetic")], "test"));
        var settings = new SyntheticSettingsStore();
        foreach (var (key, value) in new[] { ("crm.integration", "zoho"), ("zoho.clientId", "synthetic") })
            settings.Records.Add(new(key, "tenantApp", tenant, null, JsonSerializer.SerializeToElement(value), DateTimeOffset.UtcNow));
        var configuration = new ZohoConfigurationService(null!, settings, new Secrets(),
            Options.Create(new ApplicationSettingsOptions { ApplicationKey = "sales-plattform" }), Options.Create(new ZohoOptions()), http);
        var cache = new ZohoAccessTokenCache();
        await cache.GetOrCreateAsync($"{tenant}:zoho:default", _ => Task.FromResult(
            (new ZohoAccessToken("synthetic", "https://zoho.example.test"), DateTimeOffset.UtcNow.AddHours(1))));
        using var handler = new Handler();
        var factory = new Factory(handler);
        var tokens = new ZohoTokenService(factory, configuration, null!, cache, http);
        var usage = new CrmApiUsageRecorder(null!, [new ZohoCrmApiUsageCostModel()], http, NullLogger<CrmApiUsageRecorder>.Instance);
        var adapter = new ZohoCrmAdapter(factory, tokens, usage, NullLogger<ZohoCrmAdapter>.Instance);
        var build = typeof(ZohoCrmAdapter).GetMethod("BuildNotificationRequest", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var watch = (Com.Zoho.Crm.API.Notifications.BodyWrapper)build.Invoke(null,
            [new Uri("https://sales.example.test/api/integrations/zoho/webhook"), tenant, "synthetic", "123456789", "Users", false, DateTimeOffset.UtcNow])!;
        check(watch.Watch.Single().Events.SequenceEqual(["users.all"]), "User subscriptions must use Zoho's documented lowercase users.all event.");
        foreach (var spelling in new[] { "Users", "users" })
        {
            var record = await adapter.GetRecordAsync(spelling, "123", ["full_name"]);
            check(record is { Module: "Users", ExternalId: "123" }, "User notifications must read the users response envelope.");
            check(handler.Paths.Last() == "/crm/v8/users/123", "User lookup must use the users endpoint without normal record fields.");
        }
        var task = await adapter.GetRecordAsync("Tasks", "Tasks:123", ["Subject"]);
        check(task is { Module: "Tasks", ExternalId: "Tasks:123" }, "Normal notifications must retain canonical activity IDs.");
        check(handler.Paths.Last() == "/crm/v8/Tasks/123?fields=Subject", "Normal record lookups must retain selected fields.");
        check(await adapter.GetRecordAsync("Tasks", "999", []) is null, "204 responses must remain empty lookups.");
        await adapter.EnsureNotificationsDisabledAsync("111", "Tasks");
        check(handler.Paths.TakeLast(2).SequenceEqual(new[] { "/crm/v8/actions/watch?channel_ids=111",
            "/crm/v8/actions/watch?channel_id=111&module=Tasks&page=1&per_page=200" }),
            "Absent channels require a confirming GET after an unconfirmed DELETE.");
        try
        {
            await adapter.EnsureNotificationsDisabledAsync("222", "Tasks");
            throw new Exception("Existing channel incorrectly treated as absent.");
        }
        catch (InvalidOperationException) { check(true, "An existing remote channel must block replacement after failed DELETE."); }
        try
        {
            await adapter.EnsureNotificationsDisabledAsync("333", "Tasks");
            throw new Exception("Unreadable channel incorrectly treated as absent.");
        }
        catch (InvalidOperationException) { check(true, "A malformed GET must never prove channel absence."); }

        static IntegrationWebhookEvent Event() => new()
        {
            Id = Guid.NewGuid(), ProviderKey = "zoho", ConnectionKey = "default", EventType = "Tasks.edit",
            PayloadJson = "{}", Status = "queued", ReceivedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            var body = path switch
            {
                "/crm/v8/org" => """{"org":[{"domain_name":"synthetic"}]}""",
                "/crm/v8/users/123" => """{"users":[{"id":"123","full_name":"Synthetic User","status":"active"}]}""",
                "/crm/v8/Tasks/123?fields=Subject" => """{"data":[{"id":"123","Subject":"Synthetic task"}]}""",
                "/crm/v8/Tasks/999" => null,
                "/crm/v8/actions/watch?channel_ids=111" or "/crm/v8/actions/watch?channel_ids=222" or "/crm/v8/actions/watch?channel_ids=333" => """{"watch":[]}""",
                "/crm/v8/actions/watch?channel_id=111&module=Tasks&page=1&per_page=200" => null,
                "/crm/v8/actions/watch?channel_id=222&module=Tasks&page=1&per_page=200" => """{"watch":[{"channel_id":"222","resource_name":"Tasks"}]}""",
                "/crm/v8/actions/watch?channel_id=333&module=Tasks&page=1&per_page=200" => "{}",
                _ => throw new InvalidOperationException("Unexpected request: " + path)
            };
            return Task.FromResult(new HttpResponseMessage(body is null ? HttpStatusCode.NoContent : HttpStatusCode.OK)
                { Content = body is null ? null : new StringContent(body) });
        }
    }
    private sealed class Secrets : IApplicationSettingsSecretStore
    {
        public Task<string?> GetAsync(ApplicationSettingsContext context, string key, string scope = "tenantApp", CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("synthetic");
        public Task SetAsync(ApplicationSettingsContext context, string key, string value, string scope = "tenantApp", string? updatedBy = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(ApplicationSettingsContext context, string key, string scope = "tenantApp", CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
