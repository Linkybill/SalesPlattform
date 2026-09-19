using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Options;
using IdentityPlatform.Shared.ApplicationSettings;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Zoho;

var checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    checks++;
}
static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException($"Missing production helper: {name}.");
var validateUrl = Method(typeof(ZohoCrmHookUpdateService), "TryValidateWebhookUrl");
var tenantUrl = Method(typeof(ZohoCrmHookUpdateService), "BuildTenantWebhookUrl");
foreach (var url in new[] { "https://176.9.57.203:3003/api/integrations/zoho/webhook",
    "https://sales.example.com/api/integrations/zoho/webhook",
    "https://router.example.com/apps/sales-plattform/api/integrations/zoho/webhook" })
{
    object?[] arguments = [url, null, null];
    Check((bool)validateUrl.Invoke(null, arguments)!, "Public callback rejected.");
    var tenant = Guid.NewGuid();
    var callback = (string)tenantUrl.Invoke(null, [arguments[1], tenant])!;
    Check(callback == $"{url}?tenant_id={tenant:D}", "Callback must contain exactly the selected tenant.");
}
foreach (var url in new string?[] { null, "", "/api/integrations/zoho/webhook", "ftp://example.com/api/integrations/zoho/webhook",
    "https://localhost:3003/api/integrations/zoho/webhook", "https://127.0.0.1/api/integrations/zoho/webhook",
    "https://[::1]/api/integrations/zoho/webhook", "https://sales.local/api/integrations/zoho/webhook",
    "https://example.com/", "https://example.com/api/integrations/zoho/webhook?tenant_id=other",
    "https://user:password@example.com/api/integrations/zoho/webhook", "https://example.com/api/integrations/zoho/webhook#x" })
{
    object?[] arguments = [url, null, null];
    Check(!(bool)validateUrl.Invoke(null, arguments)!, "Invalid/local callback must not be registered.");
}
var readTenant = Method(typeof(ZohoEndpointExtensions), "TryReadWebhookTenant");
var id = Guid.NewGuid();
bool TenantAccepted(string query, StringValues routed)
{
    var context = new DefaultHttpContext();
    context.Request.QueryString = new(query);
    context.Request.Headers["X-Tenant-Id"] = routed;
    return (bool)readTenant.Invoke(null, [context.Request, Guid.Empty])!;
}
Check(TenantAccepted($"?tenant_id={id:D}", id.ToString("D")), "Matching router tenant rejected.");
Check(!TenantAccepted($"?tenant_id={id:D}", StringValues.Empty), "Router tenant is required.");
Check(!TenantAccepted($"?tenant_id={id:D}", Guid.NewGuid().ToString("D")), "Cross-tenant callback accepted.");
Check(!TenantAccepted($"?tenant_id={id:D}&tenant_id={id:D}", id.ToString("D")), "Duplicate query accepted.");
Check(!TenantAccepted($"?tenant_id={id:D}", new StringValues([id.ToString("D"), id.ToString("D")])), "Duplicate headers accepted.");
Check(!TenantAccepted("", id.ToString("D")), "Missing query accepted.");
Check(!TenantAccepted($"?tenant_id={Guid.Empty:D}", Guid.Empty.ToString("D")), "Empty tenant accepted.");

var validToken = Method(typeof(ZohoWebhookReceiver), "HasValidSubscriptionToken");
var now = DateTimeOffset.UtcNow;
const string token = "synthetic-webhook-test-token";
var subscription = new IntegrationSubscription
{
    ProviderKey = "zoho", ConnectionKey = "default", Module = "Leads", EventsJson = "[]",
    ChannelId = "test-channel", NotifyUrl = "https://example.com/api/integrations/zoho/webhook",
    Status = "active", ExpiresAt = now.AddHours(1),
    VerificationTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))
};
bool Accept(IntegrationSubscription? value, string supplied = token)
    => (bool)validToken.Invoke(null, [value, supplied, now])!;
Check(Accept(subscription), "Valid subscription token rejected.");
Check(!Accept(null), "Unknown subscription accepted.");
Check(!Accept(subscription, "incorrect"), "Incorrect token accepted.");
Check(!Accept(subscription, ""), "Empty token accepted.");
subscription.ExpiresAt = now;
Check(!Accept(subscription), "Expired subscription accepted.");
subscription.ExpiresAt = null;
Check(!Accept(subscription), "Missing expiry accepted.");
subscription.ExpiresAt = now.AddHours(1);
subscription.Status = "inactive";
Check(!Accept(subscription), "Inactive subscription accepted.");
subscription.Status = "active";
subscription.VerificationTokenHash = "malformed";
Check(!Accept(subscription), "Malformed stored hash must fail closed, not throw.");
var keep = Method(typeof(ZohoCrmHookUpdateService), "CanKeepSubscription");
bool Keep(string url) => (bool)keep.Invoke(null, [subscription, url, now])!;
subscription.ExpiresAt = now.AddDays(5);
Check(Keep(subscription.NotifyUrl), "Valid current callback should not consume another registration call.");
Check(!Keep("https://new.example.com/api/integrations/zoho/webhook"), "Changed callback URL must force re-registration.");
subscription.ExpiresAt = now.AddHours(35);
Check(!Keep(subscription.NotifyUrl), "Expiring subscription must renew before the next daily run.");
subscription.ExpiresAt = now.AddDays(5);
subscription.Status = "failed";
Check(!Keep(subscription.NotifyUrl), "Failed subscription must not suppress renewal.");
var payloadType = typeof(ZohoWebhookReceiver).Assembly.GetType("SalesPlattform.Backend.Integrations.Zoho.ZohoWebhookPayload")!;
var parse = payloadType.GetMethod("Parse")!;
var parseStored = payloadType.GetMethod("ParseStored")!;
var raw = JsonSerializer.Serialize(new { module = "Leads", operation = "update", channel_id = "test-channel",
    token, ids = new[] { "123", "123", "456" }, arbitrary_secret = "never-persist-this" });
var parsed = parse.Invoke(null, [raw])!;
var stored = (string)payloadType.GetMethod("ToStoredJson")!.Invoke(parsed, null)!;
Check(!stored.Contains(token) && !stored.Contains("token") && !stored.Contains("never-persist-this"), "Queue must not persist token or arbitrary fields.");
var fromStored = parseStored.Invoke(null, [stored])!;
Check((string)payloadType.GetProperty("Operation")!.GetValue(fromStored)! == "edit", "Stored operation changed.");
Check(((IReadOnlyCollection<string>)payloadType.GetProperty("RecordIds")!.GetValue(fromStored)!).SequenceEqual(["123", "456"]), "Stored ids must survive deduplication.");
Check(parseStored.Invoke(null, [raw]) is not null, "Legacy queue payload must remain readable.");
void Rejected(Action action, string message)
{
    try { action(); throw new Exception(message); }
    catch (TargetInvocationException error) when (error.InnerException is ArgumentException or InvalidOperationException) { checks++; }
}
Rejected(() => parse.Invoke(null, [stored]), "Inbound callback must still require a token.");
Rejected(() => parse.Invoke(null, [raw.Replace("update", "unsupported")]), "Unsupported operations must be rejected.");
var safeError = Method(typeof(ZohoWebhookOverviewService), "SafeError");
foreach (var code in new[] { "OAUTH_SCOPE_MISMATCH", "INVALID_DATA", "HTTP 429", "HTTP 401", "private CRM record" })
{
    var safe = (string)safeError.Invoke(null, [$"{code} token={token} customer=private-client"] )!;
    Check(!safe.Contains(token) && !safe.Contains("private-client") && !safe.Contains("private CRM record"), "Overview/log error leaked raw data.");
    if (code != "private CRM record") Check(safe.Contains(code), "Safe diagnostic code lost.");
}
Check(safeError.Invoke(null, [null]) is null, "Empty error should stay empty.");
var subscriptionStatus = Method(typeof(ZohoWebhookOverviewService), "SubscriptionStatus");
Check((string)subscriptionStatus.Invoke(null, ["active", now, now])! == "expired", "Expired subscription must not be shown as active.");
Check((string)subscriptionStatus.Invoke(null, ["active", null, now])! == "unknown-expiry", "Unknown expiry must not appear healthy.");
Check((string)subscriptionStatus.Invoke(null, ["failed", now.AddDays(1), now])! == "failed", "Failure status lost.");
Check((string)subscriptionStatus.Invoke(null, ["active", now.AddDays(1), now])! == "active", "Valid subscription incorrectly expired.");
var validateQuery = Method(typeof(ZohoWebhookOverviewService), "ValidateQuery");
foreach (var status in new[] { "", "queued", "processing", "processed", "failed" })
{
    validateQuery.Invoke(null, ["Leads", status, 1, 25]); checks++;
}
foreach (object?[] query in new object?[][] { [null, null, 0, 25], [null, null, 10001, 25],
    [null, null, 1, 0], [null, null, 1, 101], ["Leads.%", null, 1, 25], [null, "secret", 1, 25] })
    Rejected(() => validateQuery.Invoke(null, query), "Unbounded or invalid overview query accepted.");
foreach (var dto in new[] { typeof(ZohoWebhookOverview), typeof(ZohoHookSubscriptionView), typeof(ZohoHookEventView) })
    Check(!dto.GetProperties().Any(x => x.Name.Contains("Token") || x.Name.Contains("Payload") || x.Name == "NotifyUrl"), "Public DTO contains sensitive fields.");
// Same resolver instance, different tenants/hosts. No cache or user-scope override may leak between them.
var tenantA = Guid.NewGuid();
var tenantB = Guid.NewGuid();
var tenantC = Guid.NewGuid();
var settingsStore = new SyntheticSettingsStore();
var accessor = new HttpContextAccessor();
var deploymentUrl = "https://shared.example.test/api/integrations/zoho/webhook";
var options = new ZohoOptions { WebhookUrl = deploymentUrl };
var settingsReader = new ZohoWebhookSettingsService(settingsStore,
    Options.Create(new ApplicationSettingsOptions { ApplicationKey = "sales-plattform" }), Options.Create(options), accessor);
void SelectTenant(Guid tenant, bool interactive = false)
{
    accessor.HttpContext = new DefaultHttpContext();
    accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
        new Claim("tenant_id", tenant.ToString("D")), new Claim("sub", interactive ? "synthetic-user" : "system:platform-job")], "test"));
    if (interactive) accessor.HttpContext.Request.Headers.Authorization = "Bearer synthetic";
}
void Put(Guid tenant, object? value, string scope = "tenantApp")
{
    settingsStore.Records.RemoveAll(x => x.TenantId == tenant && x.Scope == scope);
    settingsStore.Records.Add(new("zoho.webhookurl", scope, tenant, scope == "appUser" ? "synthetic-user" : null,
        JsonSerializer.SerializeToElement(value), now)); // Shared normalizes persisted keys to lowercase.
}
const string urlA = "https://sales.customer-a.example/api/integrations/zoho/webhook";
const string urlB = "https://sales.customer-b.example/api/integrations/zoho/webhook";
Put(tenantA, urlA);
Put(tenantB, urlB);
Put(tenantA, "https://wrong-user.example/api/integrations/zoho/webhook", "appUser");
SelectTenant(tenantA);
var a = await settingsReader.ResolveCurrentAsync();
Check(a.BaseUri?.AbsoluteUri == urlA && a.Source == "tenantApp", "Tenant A must use its own URL, not the deployment or user value.");
Check((string)tenantUrl.Invoke(null, [a.BaseUri, tenantA])! == $"{urlA}?tenant_id={tenantA:D}", "Tenant A registration callback wrong.");
SelectTenant(tenantB, interactive: true);
var b = await settingsReader.ResolveCurrentAsync();
Check(b.BaseUri?.AbsoluteUri == urlB && b.Source == "tenantApp", "Tenant B overview must use B's frontend, not A's.");
Check((string)tenantUrl.Invoke(null, [b.BaseUri, tenantB])! == $"{urlB}?tenant_id={tenantB:D}", "Tenant B registration callback wrong.");
SelectTenant(tenantA, interactive: true);
Check((await settingsReader.ResolveCurrentAsync()).BaseUri == a.BaseUri, "Interactive and background must resolve the same tenant URL.");
Put(tenantA, "https://new.customer-a.example/api/integrations/zoho/webhook");
var changed = await settingsReader.ResolveCurrentAsync();
Check(changed.BaseUri != a.BaseUri, "Settings edits must take effect without restart or OAuth reconnect.");
subscription.Status = "active";
subscription.ExpiresAt = now.AddDays(5);
subscription.NotifyUrl = (string)tenantUrl.Invoke(null, [a.BaseUri, tenantA])!;
Check(!Keep((string)tenantUrl.Invoke(null, [changed.BaseUri, tenantA])!), "Tenant URL edit must force registration renewal.");
Put(tenantA, "https://secret:password@invalid.example/api/integrations/zoho/webhook");
var invalid = await settingsReader.ResolveCurrentAsync();
Check(invalid.BaseUri is null && invalid.Source == "tenantApp" && invalid.Error is not null, "Invalid tenant value must not fall back to a shared URL.");
Check(!invalid.Error!.Contains("password"), "URL validation error leaked credentials.");
Put(tenantA, 123);
Check((await settingsReader.ResolveCurrentAsync()).BaseUri is null, "Wrong setting type must fail closed.");
Put(tenantA, " ");
var fallback = await settingsReader.ResolveCurrentAsync();
Check(fallback.BaseUri?.AbsoluteUri == deploymentUrl && fallback.Source == "deployment", "Empty override should use the explicit deployment default.");
SelectTenant(tenantC);
Check((await settingsReader.ResolveCurrentAsync()).Source == "deployment", "Missing override must not borrow another tenant's URL.");
options.WebhookUrl = "";
Check((await settingsReader.ResolveCurrentAsync()) is { BaseUri: null, Source: "missing", Error: not null }, "Missing URL must be visible.");
SelectTenant(Guid.Empty);
try { await settingsReader.ResolveCurrentAsync(); throw new Exception("Empty tenant accepted."); }
catch (InvalidOperationException) { checks++; }
Check(settingsStore.Contexts.All(x => x.ApplicationKey == "sales-plattform" && x.TenantId != Guid.Empty), "Settings context lost app/tenant boundary.");
Check(settingsStore.Contexts.Any(x => x.TenantId == tenantA) && settingsStore.Contexts.Any(x => x.TenantId == tenantB), "Both tenant contexts must be read independently.");
Console.WriteLine($"Zoho webhook: {checks} checks passed (synthetic data; no live CRM calls).");

sealed class SyntheticSettingsStore : IApplicationSettingsStore
{
    public List<ApplicationSettingValueRecord> Records { get; } = [];
    public List<ApplicationSettingsContext> Contexts { get; } = [];
    public Task<IReadOnlyCollection<ApplicationSettingValueRecord>> LoadAsync(ApplicationSettingsContext context, CancellationToken cancellationToken = default)
    {
        Contexts.Add(context);
        // Deliberately includes foreign and appUser rows to assert defensive tenantApp selection.
        return Task.FromResult<IReadOnlyCollection<ApplicationSettingValueRecord>>(Records.ToArray());
    }
    public Task SetAsync(ApplicationSettingsContext context, string key, string scope, JsonElement value, string? updatedBy, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Overview/resolver must not write settings.");
    public Task DeleteAsync(ApplicationSettingsContext context, string key, string scope, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Overview/resolver must not delete settings.");
}
