using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
// Inspect the serialized production request, not a test-only date formatter.
var buildNotification = Method(typeof(ZohoCrmAdapter), "BuildNotificationPayload");
var previousCulture = CultureInfo.CurrentCulture;
try
{
    foreach (var culture in new[] { "de-DE", "en-US", "ar-SA" })
    foreach (var module in new[] { "Calls", "Tasks", "Events" })
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var requestedAt = new DateTimeOffset(2026, 12, 27, 23, 45, 12, TimeSpan.Zero).AddTicks(1234567);
        const string callback = "https://sales.example.test/api/integrations/zoho/webhook?tenant_id=00000000-0000-0000-0000-000000000001";
        var payload = (JsonObject)buildNotification.Invoke(null,
            [callback, "synthetic-token", "123456789", module, requestedAt])!;
        using var json = JsonDocument.Parse(payload.ToJsonString());
        var watch = json.RootElement.GetProperty("watch")[0];
        var expiry = watch.GetProperty("channel_expiry").GetString();
        Check(expiry == "2027-01-03T22:45:12+00:00",
            $"{module}/{culture}: channel_expiry must use whole seconds and an explicit offset; got {expiry}.");
        Check(DateTimeOffset.TryParseExact(expiry, "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsedExpiry), "Expiry must match Zoho's datetime format.");
        Check(parsedExpiry > requestedAt && parsedExpiry < requestedAt.AddDays(7),
            "Expiry must remain in the future and below Zoho's seven-day maximum.");
        Check(watch.GetProperty("notify_url").GetString() == callback
            && watch.GetProperty("token").GetString() == "synthetic-token"
            && watch.GetProperty("channel_id").GetString() == "123456789", "Registration identity changed.");
        Check(watch.GetProperty("events").EnumerateArray().Select(x => x.GetString())
            .SequenceEqual(new[] { $"{module}.create", $"{module}.edit", $"{module}.delete" }),
            "Registration operations changed.");
        Check(!watch.TryGetProperty("notification_condition", out _) && !watch.TryGetProperty("field_selection", out _),
            "Registration must not restrict field changes.");
    }
}
finally { CultureInfo.CurrentCulture = previousCulture; }
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
// Read-back verification uses the provider parser and the same comparison returned by the API.
var parseNotifications = Method(typeof(ZohoCrmAdapter), "ParseNotificationSnapshots");
var evaluateNotification = Method(typeof(ZohoHookVerificationService), "Evaluate");
var expectedCallback = $"https://sales.example.test/api/integrations/zoho/webhook?tenant_id={tenantA:D}";
object NotificationRows(string? url = null, DateTimeOffset? expiry = null, string[]? events = null,
    string channel = "12345", string module = "Calls", bool duplicate = false,
    string? remoteToken = "never-return-provider-secret", object? conditions = null, object? fields = null)
{
    var row = new { channel_id = channel, resource_name = module, notify_url = url ?? expectedCallback,
        channel_expiry = expiry ?? now.AddDays(1), events = events ?? ["Calls.create", "Calls.edit", "Calls.delete"],
        token = remoteToken, notification_condition = conditions, fields, notify_on_related_action = false };
    var response = JsonSerializer.SerializeToElement(new { watch = duplicate ? new[] { row, row } : new[] { row } });
    return parseNotifications.Invoke(null, [response])!;
}
var localChannel = new IntegrationSubscription
{
    ProviderKey = "zoho", ConnectionKey = "default", Module = "Calls", EventsJson = "[]",
    ChannelId = "12345", Status = "active", ExpiresAt = now.AddDays(1), NotifyUrl = expectedCallback,
    VerificationTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("never-return-provider-secret")))
};
ZohoHookVerification Verify(object rows) => (ZohoHookVerification)evaluateNotification.Invoke(null,
    ["Calls", "12345", expectedCallback, rows, now, localChannel])!;
Check(Verify(NotificationRows()).Status == "verified", "Valid provider registration should verify.");
Check(Verify(NotificationRows(events: ["Calls.all"])).Status == "verified", "All-operation registration should verify.");
Check(Verify(NotificationRows(expiry: now)).Status == "expired", "Expired provider entry must not verify.");
Check(Verify(NotificationRows(events: ["Calls.edit"])).Status == "events-mismatch", "Missing create/delete must be visible.");
Check(Verify(NotificationRows(url: expectedCallback.Replace(tenantA.ToString("D"), tenantB.ToString("D")))).Status == "url-mismatch", "Foreign tenant URL must not verify.");
Check(Verify(NotificationRows(url: expectedCallback.Replace("sales.example.test", "wrong.example.test"))).Status == "url-mismatch", "Wrong host must not verify.");
Check(Verify(NotificationRows(channel: "99999")).Status == "not-found", "Foreign channel must not be displayed or verify.");
Check(Verify(NotificationRows(module: "Tasks")).Status == "not-found", "Foreign module must not verify.");
Check(Verify(NotificationRows(duplicate: true)).Status == "ambiguous", "Duplicate entries must not verify.");
Check(Verify(NotificationRows()).Filters?.Status == "none", "Explicit null conditions must mean no filters.");
Check(Verify(NotificationRows(conditions: Array.Empty<object>())).Filters?.Status == "none", "Empty conditions must mean no filters.");
Check(Verify(NotificationRows()).TokenCheck?.Status == "match", "Valid hashes must match irrespective of hex casing.");
Check(Verify(NotificationRows()).LocalChannelCheck?.Status == "ready", "Active matching channel must be ready.");
Check(Verify(NotificationRows()).NotifyOnRelatedAction == false, "Related-action switch must be displayed independently.");
Check(Verify(NotificationRows(remoteToken: "other-token")).TokenCheck?.Status == "mismatch", "Token mismatch must be explicit.");
Check(Verify(NotificationRows(remoteToken: "other-token")).Status != "verified", "Mismatching token must block success.");
foreach (var absent in new string?[] { null, "", new string('x', 51) })
    Check(Verify(NotificationRows(remoteToken: absent)).TokenCheck?.Status == "remote-missing", "Missing/invalid provider token must block success.");
var correctHash = localChannel.VerificationTokenHash;
foreach (var invalidHash in new[] { "", "invalid", "00" })
{
    localChannel.VerificationTokenHash = invalidHash;
    Check(Verify(NotificationRows()).Status == "token-not-verified", "Missing/malformed local hashes must fail closed.");
}
localChannel.VerificationTokenHash = correctHash;
foreach (var state in new[] { "inactive", "failed", "Active" })
{
    localChannel.Status = state;
    Check(Verify(NotificationRows()).LocalChannelCheck?.Status == "inactive", "Match receiver's exact active-status check.");
    Check(Verify(NotificationRows()).Status == "local-not-ready", "Inactive local channels must block success.");
}
localChannel.Status = "active";
foreach (var expiry in new DateTimeOffset?[] { null, now, now.AddSeconds(-1) })
{
    localChannel.ExpiresAt = expiry;
    Check(Verify(NotificationRows()).Status == "local-not-ready", "Unknown/expired local lifetime must block success.");
}
localChannel.ExpiresAt = now.AddDays(1);
localChannel.ChannelId = "changed-during-check";
Check(Verify(NotificationRows()).LocalChannelCheck?.Status == "changed", "Concurrent renewal must not verify stale state.");
Check(Verify(NotificationRows()).TokenCheck?.Status == "not-checked", "Do not compare tokens across different channels.");
localChannel.ChannelId = "12345";
var noLocal = (ZohoHookVerification)evaluateNotification.Invoke(null,
    ["Calls", "12345", expectedCallback, NotificationRows(), now, null])!;
Check(noLocal.Status == "local-not-ready" && noLocal.LocalChannelCheck?.Status == "missing", "Removed local channel must block success.");
object Field(string name) => new { field = new { api_name = name, id = "private-field-id" }, group = (object?)null };
object Condition(object selection) => new { type = "field_selection", module = new { api_name = "Calls" }, field_selection = selection };
var filters = new[] { Condition(new { group_operator = "and", group = new[] { Field("Call_Duration"),
    new { group_operator = "or", group = new[] { Field("Subject"), Field("Call_Start_Time") } } } }) };
var filtered = Verify(NotificationRows(conditions: filters));
Check(filtered.Status == "filters-present", "Field filters must not appear as unrestricted success.");
Check(filtered.Filters!.Conditions.Single() == "Calls: (Call_Duration UND (Subject ODER Call_Start_Time))", "Preserve safe field names and boolean grouping.");
Check(Verify(NotificationRows(conditions: new[] { Condition(Field("Subject")) })).Filters?.Status == "present", "Single-field selection supported.");
foreach (var invalidConditions in new object[] { "private-raw-secret", new[] { Condition(Field("<script>secret</script>")) },
    new[] { new { type = "unexpected", secret = "private-raw-secret" } },
    new[] { Condition(new { group_operator = "xor", group = new[] { Field("Subject") } }) },
    Enumerable.Repeat(Condition(Field("Subject")), 11).ToArray() })
{
    var invalidFilterResult = Verify(NotificationRows(conditions: invalidConditions));
    Check(invalidFilterResult.Status == "filters-unknown", "Unsupported filter shapes must not be silently ignored.");
    Check(!JsonSerializer.Serialize(invalidFilterResult).Contains("private-raw-secret"), "Filter raw data leaked.");
}
object deep = Field("Subject");
for (var depth = 0; depth < 8; depth++) deep = new { group_operator = "and", group = new[] { deep } };
Check(Verify(NotificationRows(conditions: new[] { Condition(deep) })).Status == "filters-unknown", "Deep filter nesting must be bounded.");
Check(Verify(NotificationRows(fields: new[] { "legacy-field" })).Status == "filters-unknown", "Nonempty legacy fields must not be called unrestricted.");
var missingFilters = JsonSerializer.SerializeToElement(new { watch = new[] { new { channel_id = "12345", resource_name = "Calls",
    notify_url = expectedCallback, channel_expiry = now.AddDays(1), events = new[] { "Calls.all" }, token = "never-return-provider-secret" } } });
Check(Verify(parseNotifications.Invoke(null, [missingFilters])!).Status == "filters-unknown", "Absent filter metadata is unknown, not no filters.");
var safeDiagnostic = JsonSerializer.Serialize(filtered);
Check(!safeDiagnostic.Contains(correctHash, StringComparison.OrdinalIgnoreCase)
    && !safeDiagnostic.Contains("never-return-provider-secret") && !safeDiagnostic.Contains("private-field-id"), "Diagnostic must never contain hashes, tokens or raw field IDs.");
var parsedRows = NotificationRows();
Check(!JsonSerializer.Serialize(parsedRows).Contains(correctHash, StringComparison.OrdinalIgnoreCase), "Internal snapshot serialization must omit token hashes.");
var missingExpiry = JsonSerializer.SerializeToElement(new { watch = new[] { new { channel_id = "12345", resource_name = "Calls",
    notify_url = expectedCallback, channel_expiry = "invalid", events = new[] { "Calls.all" } } } });
Check(Verify(parseNotifications.Invoke(null, [missingExpiry])!).Status == "expiry-unknown", "Malformed expiry must not verify.");
Check(Verify(parseNotifications.Invoke(null, [JsonSerializer.SerializeToElement(new { watch = Array.Empty<object>() })])!).Status == "not-found", "Empty response must not verify.");
Rejected(() => parseNotifications.Invoke(null, [JsonSerializer.SerializeToElement(new { error = "bad" })]), "Malformed response must fail closed.");
var secretUrl = expectedCallback.Replace("https://", "https://private-user:private-pass@") + "&token=private-query#private-fragment";
var sanitized = JsonSerializer.Serialize(Verify(NotificationRows(url: secretUrl, events: ["Calls.create", "private-event-secret"])));
foreach (var secret in new[] { "private-user", "private-pass", "private-query", "private-fragment", "private-event-secret", "never-return-provider-secret" })
    Check(!sanitized.Contains(secret), "Verification leaked a provider secret.");
var safeCallback = Method(typeof(ZohoHookVerificationService), "SafeCallbackUrl");
Check(!((string)safeCallback.Invoke(null, ["https://sales.example.test/private-path-secret?token=private-query"])!).Contains("private-path-secret"), "Unexpected provider path must be hidden.");
Check(safeCallback.Invoke(null, ["javascript:private-secret"]) is null, "Non-HTTP callback must not be displayed.");
Check(!JsonSerializer.Serialize(Verify(NotificationRows(channel: "other", url: "https://foreign.example.test/private"))).Contains("foreign.example.test"), "Foreign channel URL leaked.");
var safeFailure = Method(typeof(ZohoHookVerificationService), "SafeFailure");
foreach (var (rawError, expectedStatus) in new[] { ("OAUTH_SCOPE_MISMATCH", "scope-missing"), ("HTTP 401", "authentication-error"),
    ("HTTP 429", "rate-limited"), ("private-failure", "check-failed") })
{
    var failure = (ZohoHookVerification)safeFailure.Invoke(null, ["Calls", new InvalidOperationException(rawError + " private-token"), now])!;
    Check(failure.Status == expectedStatus, "Incorrect safe provider diagnostic.");
    Check(!JsonSerializer.Serialize(failure).Contains("private-token"), "Provider exception leaked credentials.");
}
await ReplacementChecks.Run(Check);
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
