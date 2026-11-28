using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Authentication;
using IdentityPlatform.Shared.Database;
using IdentityPlatform.Shared.Tenant;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using SalesPlattform.Backend.Authorization;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Strategy;
using SalesPlattform.Backend.Services;

var now = DateTimeOffset.UtcNow;
var assigned = Work("call-follow-up", "Eigener Lead", 35, now);
var renewal = Work("contract-renewal-critical", "Vertrag · Muster GmbH", 100, now.AddDays(2));
var expired = Work("contract-renewal", "Abgelaufener Vertrag", 100, now.AddDays(-1));
var snoozed = Work("call-follow-up", "Später", 150, now) with { AvailableFrom = now.AddDays(1) };
var items = new[] { renewal, assigned, expired, snoozed };
var personal = new HashSet<Guid> { assigned.Id, snoozed.Id };
Check(SalesStrategyTools.SelectItems(items, personal, now, 10, false).SequenceEqual([assigned, renewal]),
    "open assignments rank first; expired renewals and future snoozes are excluded");
Check(SalesStrategyTools.SelectItems(items, personal, now, 10, true).SequenceEqual([renewal]),
    "renewal ranking uses active renewal candidates");
Check(SalesStrategyTools.SelectItems(items, personal, now, 10, false, search: "muster").SequenceEqual([renewal]),
    "evidence lookup filters by name");
Check(SalesStrategyAgent.IsNextCallQuestion("welchen kunden sollte ich heute als erstes anrufen?")
    && SalesStrategyAgent.IsNextCallQuestion("Wen soll ich als Nächstes anrufen und warum?")
    && !SalesStrategyAgent.IsNextCallQuestion("Wen soll ich zuerst anrufen und kannst du ihm Aufgaben zuweisen?")
    && !SalesStrategyAgent.IsNextCallQuestion("Wen aus Team Nord soll ich zuerst anrufen?"),
    "only simple next-call questions bypass model calls");
Check(SalesStrategyAgent.FormatNextCall(null).Contains("keinen passenden")
    && SalesStrategyAgent.FormatNextCall(new SalesStrategySignal(assigned.Id, assigned.Title, "Anruf",
        "Prioritätsgrund", now, "normal", true, null)).Contains("Prioritätsgrund"),
    "quick answer uses the displayed priority reason and handles an empty list");

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, SyntheticIdentity>("test", _ => { });
builder.Services.AddAuthorization(options => options.AddPolicy("sales-access", policy => policy.RequireRole("sales-user")));
builder.Services.AddSingleton<IApplicationSettingsStore, SyntheticSettings>();
builder.Services.AddSingleton<IApplicationSettingsSecretStore, SyntheticGatewaySecret>();
builder.Services.Configure<ApplicationSettingsOptions>(options =>
{
    options.ApplicationKey = "sales-plattform";
    options.PlatformApiUrl = "http://synthetic-platform";
});
builder.Services.Configure<PlatformTenantDatabaseOptions>(options =>
{
    options.ApplicationKey = "sales-plattform";
    options.PlatformApiUrl = "http://synthetic-platform";
    options.RegistrationSecret = "synthetic";
});
var dbConnection = Environment.GetEnvironmentVariable("SALES_CHAT_TEST_POSTGRES");
builder.Services.AddHttpClient(IdentityPlatformServiceAuthenticationDefaults.ServiceClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new SyntheticPlatform(dbConnection));
builder.Services.AddHttpClient<PlatformTenantDatabaseBindingClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new SyntheticPlatform(dbConnection));
builder.Services.AddScoped<PlatformTenantDbContextFactory<SalesPlattformDbContext>>();
builder.Services.AddScoped<SalesTeamService>();
builder.Services.AddScoped<SalesTeamDirectory>();
builder.Services.AddScoped<SalesTaskPlanningService>();
builder.Services.AddScoped<TaskGoalProfileService>();
builder.Services.AddScoped<TenantAdminAccessService>();
builder.Services.AddSalesStrategy();
builder.Services.AddSingleton<ISalesChatModelFactory, SyntheticModelFactory>();
builder.Configuration["SalesAi:CompatibleEndpoint"] = "https://test-provider.invalid/v1";
builder.Configuration["SalesAi:CompatibleKey"] = "synthetic-operator-secret";
builder.Configuration["SalesAi:OpenAiKey"] = "synthetic-openai-operator-secret";

await using var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapSalesStrategy();
app.MapPost("/v1/chat/completions", (JsonElement request) => new
{
    id = "synthetic-response",
    @object = "chat.completion",
    created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    model = request.GetProperty("model").GetString(),
    choices = new[] { new { index = 0, message = new { role = "assistant", content = "Lokale Ollama-Antwort." }, finish_reason = "stop" } }
});
await app.StartAsync();
var address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
using var http = new HttpClient { BaseAddress = address };
Check((await http.GetAsync("/api/strategy/status")).StatusCode == HttpStatusCode.Unauthorized, "status requires authentication");
Check((await http.PostAsync("/api/strategy/hub/negotiate?negotiateVersion=1", null)).StatusCode == HttpStatusCode.Unauthorized,
    "SignalR requires authentication");
Check((await http.PostAsync("/api/strategy/mcp", JsonContent.Create(new { }))).StatusCode == HttpStatusCode.Unauthorized, "MCP requires authentication");

http.DefaultRequestHeaders.Add("X-Test-Identity", "sales");
http.DefaultRequestHeaders.Add(IdentityPlatformTenantContext.Header, Guid.NewGuid().ToString());
Check((await http.GetAsync("/api/strategy/status")).StatusCode == HttpStatusCode.Forbidden, "status rejects tenant mismatch");
Check((await http.PostAsync("/api/strategy/hub/negotiate?negotiateVersion=1", null)).StatusCode == HttpStatusCode.Forbidden,
    "SignalR rejects tenant mismatch");
Check((await http.PostAsync("/api/strategy/mcp", JsonContent.Create(new { }))).StatusCode == HttpStatusCode.Forbidden, "MCP rejects tenant mismatch");
http.DefaultRequestHeaders.Remove(IdentityPlatformTenantContext.Header);
var status = await http.GetFromJsonAsync<JsonElement>("/api/strategy/status");
Check(status.GetProperty("available").GetBoolean() && status.GetProperty("model").GetString() == "synthetic-model"
    && !status.GetRawText().Contains("synthetic-operator-secret"), "status exposes model but no provider secret");

var endpoint = new Uri(address, "/api/strategy/mcp");
await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new()
{
    Endpoint = endpoint,
    AdditionalHeaders = new Dictionary<string, string> { ["X-Test-Identity"] = "sales" }
}));
var tools = await mcp.ListToolsAsync();
Check(tools.Select(tool => tool.Name).Order().SequenceEqual(new[] { "sales_apply_change", "sales_call_priorities",
    "sales_daily_plan_options", "sales_explain_priority", "sales_find_people", "sales_find_users", "sales_list_tasks", "sales_list_user_tasks", "sales_prepare_assignment",
    "sales_prepare_daily_plan", "sales_renewal_risks" }), "MCP lists analysis and task tools");
var prompts = await mcp.ListPromptsAsync();
Check(prompts.Select(prompt => prompt.Name).Order().SequenceEqual(new[] { "sales_assignment", "sales_daily_plan", "sales_tasks" }),
    "MCP exposes discoverable skills");
var invalidEvidence = await mcp.CallToolAsync("sales_explain_priority", new Dictionary<string, object?>());
Check(invalidEvidence.IsError == true, "MCP dispatches and validates tool arguments");

using var response = await http.PostAsJsonAsync("/api/strategy/ask", new { question = "Wen soll ich anrufen?" });
response.EnsureSuccessStatusCode();
var result = await response.Content.ReadFromJsonAsync<JsonElement>();
Check(result.GetProperty("answer").GetString() == "Synthetische Antwort.", "Microsoft Agent Framework returns model output");
var streamed = await InvokeChatHubAsync(http, "Wen soll ich anrufen?");
Check(streamed.Progress.Any(p => p.GetProperty("kind").GetString() == "status")
    && streamed.Progress.Where(p => p.GetProperty("kind").GetString() == "text").Count() >= 2
    && streamed.Result.GetProperty("answer").GetString() == "Synthetische Antwort."
    && !JsonSerializer.Serialize(streamed.Progress).Contains("synthetisches internes Modellmaterial"),
    "SignalR delivers status and public answer fragments while omitting reasoning content");
using var invalidApproval = await http.PostAsJsonAsync("/api/strategy/changes/apply", new { approval = "synthetic-invalid" });
Check(invalidApproval.StatusCode == HttpStatusCode.BadRequest, "forged proposals cannot be applied");
var settings = (SyntheticSettings)app.Services.GetRequiredService<IApplicationSettingsStore>();
settings.Enabled = false;
Check(!(await http.GetFromJsonAsync<JsonElement>("/api/strategy/status")).GetProperty("enabled").GetBoolean(),
    "disabled tenant has no chat");
using var disabledAsk = await http.PostAsJsonAsync("/api/strategy/ask", new { question = "Wen soll ich anrufen?" });
Check(disabledAsk.StatusCode == HttpStatusCode.Forbidden, "disabled tenant cannot invoke chat");
using var disabledMcp = await http.PostAsJsonAsync("/api/strategy/mcp", new { });
Check(disabledMcp.StatusCode == HttpStatusCode.Forbidden, "disabled tenant cannot access MCP");
Check((await http.PostAsync("/api/strategy/hub/negotiate?negotiateVersion=1", null)).StatusCode == HttpStatusCode.Forbidden,
    "disabled tenant cannot access SignalR");
settings.Enabled = true;
settings.Provider = "openai";
var openAi = await http.GetFromJsonAsync<JsonElement>("/api/strategy/status");
Check(openAi.GetProperty("available").GetBoolean() && openAi.GetProperty("provider").GetString() == "openai"
    && !openAi.GetRawText().Contains("operator-secret"), "OpenAI uses an operator credential");
builder.Configuration["SalesAi:OpenAiKey"] = null;
openAi = await http.GetFromJsonAsync<JsonElement>("/api/strategy/status");
Check(openAi.GetProperty("available").GetBoolean() && !openAi.GetRawText().Contains("vault-credential"),
    "OpenAI can use the platform Vault credential");
settings.Provider = "openai-compatible";
if (dbConnection is not null)
    await CheckOperationsAsync(dbConnection, http, endpoint);
builder.Configuration["SalesAi:CompatibleKey"] = null;
var disabled = await http.GetFromJsonAsync<JsonElement>("/api/strategy/status");
Check(!disabled.GetProperty("available").GetBoolean(), "a tenant setting cannot replace a missing operator key");
builder.Configuration["SalesAi:OllamaEndpoint"] = new Uri(address, "/v1").AbsoluteUri;
settings.Provider = "ollama";
settings.Model = null;
var ollamaStatus = await http.GetFromJsonAsync<JsonElement>("/api/strategy/status");
Check(ollamaStatus.GetProperty("model").GetString() == SalesModelSettings.DefaultOllamaModel,
    "a local Ollama model uses the operator's gateway key");
foreach (var selectable in new[] { "gemma4:26b", "gpt-oss:20b" })
{
    settings.OllamaModel = selectable;
    ollamaStatus = await http.GetFromJsonAsync<JsonElement>("/api/strategy/status");
    Check(ollamaStatus.GetProperty("model").GetString() == selectable, "tenant can switch the Ollama model in the portal");
}
using var localChat = new OpenAiSalesChatModelFactory().Create(new SalesModelSelection(
    "ollama", "gemma4:26b", "ollama-local", new Uri(address, "/v1")));
var localResult = await localChat.GetResponseAsync([new ChatMessage(ChatRole.User, "Hallo")]);
Check(localResult.Text == "Lokale Ollama-Antwort.", "real model adapter uses the local OpenAI-compatible route");
builder.Configuration["SalesAi:OllamaEndpoint"] = "http://public.invalid/v1";
var insecureOllama = await http.GetFromJsonAsync<JsonElement>("/api/strategy/status");
Check(!insecureOllama.GetProperty("available").GetBoolean(), "untrusted cleartext endpoint is rejected");
using var badRequest = await http.PostAsJsonAsync("/api/strategy/ask", new { question = "  " });
Check(badRequest.StatusCode == HttpStatusCode.BadRequest, "blank prompt rejected");
Console.WriteLine("Sales Strategy: MCP transport, authorization, model settings and Agent Framework checks passed.");

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
}

static WorklistItemDto Work(string type, string title, decimal priority, DateTimeOffset dueAt)
    => new(Guid.NewGuid(), type, type, "open", title, null, null, null, dueAt, priority, "normal",
        null, type.StartsWith("contract-", StringComparison.Ordinal) ? "contract" : "lead", Guid.NewGuid(), null,
        null, dueAt, null, null, false);

static async Task<(JsonElement Result, List<JsonElement> Progress)> InvokeChatHubAsync(HttpClient http, string question)
{
    using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var ct = timer.Token;
    const string hub = "/api/strategy/hub";
    using var negotiate = await http.PostAsync(hub + "/negotiate?negotiateVersion=1", null, ct);
    negotiate.EnsureSuccessStatusCode();
    var connection = await negotiate.Content.ReadFromJsonAsync<JsonElement>(ct);
    var transport = hub + "?id=" + Uri.EscapeDataString(connection.GetProperty("connectionToken").GetString()!);
    using var connect = await http.GetAsync(transport, ct);
    connect.EnsureSuccessStatusCode();
    async Task SendAsync(object message)
    {
        using var posted = await http.PostAsync(transport,
            new StringContent(JsonSerializer.Serialize(message) + "\u001e", Encoding.UTF8, "text/plain"), ct);
        posted.EnsureSuccessStatusCode();
    }
    try
    {
        await SendAsync(new { protocol = "json", version = 1 });
        _ = await http.GetStringAsync(transport, ct);
        await SendAsync(new { type = 1, invocationId = "1", target = "Ask", arguments = new[] { new { question } } });
        var updates = new List<JsonElement>();
        while (true)
        {
            var batch = await http.GetStringAsync(transport, ct);
            foreach (var frame in batch.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                using var json = JsonDocument.Parse(frame);
                var message = json.RootElement;
                if (message.GetProperty("type").GetInt32() == 1 && message.GetProperty("target").GetString() == "Progress")
                    updates.Add(message.GetProperty("arguments")[0].Clone());
                if (message.GetProperty("type").GetInt32() == 3)
                    return message.TryGetProperty("result", out var result) ? (result.Clone(), updates)
                        : throw new InvalidOperationException(message.GetProperty("error").GetString());
            }
        }
    }
    finally
    {
        using var disconnected = await http.DeleteAsync(transport);
    }
}

static async Task CheckOperationsAsync(string connectionString, HttpClient http, Uri endpoint)
{
    var tenant = SyntheticPlatform.TenantId;
    var task = Guid.NewGuid();
    var availableTask = Guid.NewGuid();
    var lead = Guid.NewGuid();
    var team = Guid.NewGuid();
    var otherTenant = Guid.NewGuid();
    var foreignTask = Guid.NewGuid();
    await using var db = new SalesPlattformDbContext(new DbContextOptionsBuilder<SalesPlattformDbContext>().UseNpgsql(connectionString).Options);
    await db.Database.MigrateAsync();
    ((IPlatformTenantDbContext)db).SetPlatformTenant(tenant, true);
    db.SalesTeams.Add(new SalesTeam { Id = team, TenantId = tenant, Key = "quality", Name = "Team Qualitätsprüfung" });
    db.SalesTeamUserMembers.Add(new SalesTeamUserMember { TenantId = tenant, TeamId = team, MembershipId = SyntheticPlatform.First,
        Email = "first@example.test" });
    db.SalesWorkItems.Add(new SalesWorkItem { Id = task, TenantId = tenant, WorkItemChainId = task, Status = "open",
        WorkItemType = "call-follow-up", Title = "Musterfirma anrufen" });
    db.SalesTaskAssignments.Add(new SalesTaskAssignment { TenantId = tenant, WorkItemId = task, MembershipId = SyntheticPlatform.First,
        Title = "Musterfirma anrufen", TaskType = "follow-up", AssignedOn = SalesTaskPlanningService.LocalDay(DateTimeOffset.UtcNow),
        TargetId = Guid.NewGuid(), TargetType = "customer" });
    db.SalesLeads.Add(new SalesLead { Id = lead, TenantId = tenant, Name = "Neuer Kontakt", Phone = "123", Status = "new" });
    db.SalesWorkItems.Add(new SalesWorkItem { Id = availableTask, TenantId = tenant, WorkItemChainId = availableTask,
        Status = "open", WorkItemType = "lead-first-response", Title = "Neuen Kontakt anrufen", Relations =
            [new SalesWorkItemRelation { TenantId = tenant, TargetType = "lead", TargetId = lead, RelationRole = "primary" }] });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    ((IPlatformTenantDbContext)db).SetPlatformTenant(otherTenant, true);
    db.SalesWorkItems.Add(new SalesWorkItem { Id = foreignTask, TenantId = otherTenant, WorkItemChainId = foreignTask, Status = "open",
        WorkItemType = "call-follow-up", Title = "Fremdmandant" });
    db.SalesTaskAssignments.Add(new SalesTaskAssignment { TenantId = otherTenant, WorkItemId = foreignTask, MembershipId = SyntheticPlatform.First,
        Title = "Fremdmandant", TaskType = "follow-up", AssignedOn = SalesTaskPlanningService.LocalDay(DateTimeOffset.UtcNow),
        TargetId = Guid.NewGuid(), TargetType = "customer" });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    ((IPlatformTenantDbContext)db).SetPlatformTenant(tenant, true);

    async Task<McpClient> Connect(string identity) => await McpClient.CreateAsync(new HttpClientTransport(new()
    {
        Endpoint = endpoint, AdditionalHeaders = new Dictionary<string, string>
        { ["X-Test-Identity"] = identity, ["Authorization"] = identity == "sales-admin" ? "Bearer synthetic" : "Bearer member" }
    }));
    await using var member = await Connect("sales");
    await using var admin = await Connect("sales-admin");
    var mine = await member.CallToolAsync("sales_list_tasks", new Dictionary<string, object?>());
    Check(mine.IsError != true && JsonSerializer.Serialize(mine).Contains("Musterfirma")
        && !JsonSerializer.Serialize(mine).Contains("Fremdmandant"), "MCP shows only own tenant memberships and tasks");
    var denied = await member.CallToolAsync("sales_list_tasks", new Dictionary<string, object?> { ["teamId"] = team });
    Check(denied.IsError == true, "ordinary members cannot read other users through team filters");
    var teamTasks = await admin.CallToolAsync("sales_list_tasks", new Dictionary<string, object?> { ["teamId"] = team });
    Check(teamTasks.IsError != true && JsonSerializer.Serialize(teamTasks).Contains("first@example.test"), "admins can read team assignments");
    var byName = await admin.CallToolAsync("sales_list_user_tasks", new Dictionary<string, object?>
        { ["firstName"] = "Sina", ["lastName"] = "Muster" });
    Check(byName.IsError != true && byName.StructuredContent is { } named
        && named.GetProperty("users")[0].GetProperty("id").GetGuid() == SyntheticPlatform.First
        && named.GetProperty("tasks")[0].GetProperty("workItemId").GetGuid() == task,
        "tasks can be retrieved directly by real first and last name, independently of email");
    var ambiguous = await admin.CallToolAsync("sales_list_user_tasks", new Dictionary<string, object?> { ["lastName"] = "Muster" });
    Check(ambiguous.StructuredContent is { } choices && choices.GetProperty("users").GetArrayLength() == 2
        && choices.GetProperty("tasks").GetArrayLength() == 0, "shared last name returns choices before reading tasks");
    var privateLookup = await member.CallToolAsync("sales_find_users", new Dictionary<string, object?> { ["firstName"] = "Sina" });
    Check(privateLookup.IsError == true, "ordinary members cannot search the Sales user directory");
    var draft = await admin.CallToolAsync("sales_prepare_assignment", new Dictionary<string, object?>
        { ["workItemIds"] = new[] { task }, ["recipientId"] = SyntheticPlatform.Second });
    Check(draft.IsError != true && draft.StructuredContent.HasValue, "MCP previews assignment");
    var approval = draft.StructuredContent!.Value.GetProperty("approval").GetString();
    Check(await db.SalesTaskAssignments.AsNoTracking().Select(a => a.MembershipId).SingleAsync() == SyntheticPlatform.First,
        "preview does not change the assignee");
    var stolen = await member.CallToolAsync("sales_apply_change", new Dictionary<string, object?> { ["approval"] = approval });
    Check(stolen.IsError == true, "a different user cannot apply the draft");
    var saved = await admin.CallToolAsync("sales_apply_change", new Dictionary<string, object?> { ["approval"] = approval });
    Check(saved.IsError != true && await db.SalesTaskAssignments.AsNoTracking().Select(a => a.MembershipId).SingleAsync() == SyntheticPlatform.Second,
        "confirmation reassigns the task");
    var replay = await admin.CallToolAsync("sales_apply_change", new Dictionary<string, object?> { ["approval"] = approval });
    Check(replay.IsError == true, "stale approval cannot overwrite changed assignment");
    var available = await admin.CallToolAsync("sales_list_tasks", new Dictionary<string, object?>
        { ["onlyUnassigned"] = true });
    Check(available.IsError != true && JsonSerializer.Serialize(available).Contains("Neuen Kontakt")
        && !JsonSerializer.Serialize(available).Contains("Musterfirma"), "unassigned filter excludes existing assignments");
    var newDraft = await admin.CallToolAsync("sales_prepare_assignment", new Dictionary<string, object?>
        { ["workItemIds"] = new[] { availableTask }, ["recipientId"] = SyntheticPlatform.Second });
    var newApproval = newDraft.StructuredContent?.GetProperty("approval").GetString();
    var newSaved = await admin.CallToolAsync("sales_apply_change", new Dictionary<string, object?> { ["approval"] = newApproval });
    Check(newSaved.IsError != true && await db.SalesTaskAssignments.AnyAsync(a => a.WorkItemId == availableTask &&
        a.TaskType == "cold-call" && a.MembershipId == SyntheticPlatform.Second), "manual first assignment keeps the inferred task type");

    http.DefaultRequestHeaders.Remove("X-Test-Identity");
    http.DefaultRequestHeaders.Add("X-Test-Identity", "sales-admin");
    http.DefaultRequestHeaders.Add("Authorization", "Bearer synthetic");
    var liveDraft = await InvokeChatHubAsync(http, "Tagesprofil im Chat");
    var draftPlan = liveDraft.Result.GetProperty("changes").EnumerateArray().Single();
    Check(liveDraft.Progress.Any(p => p.GetProperty("kind").GetString() == "step"
        && p.GetProperty("text").GetString() == "Tagesprofil vorbereiten"),
        "SignalR shows the actual tool invoked for the chat");
    Check(!await db.SalesTaskGoalPlans.AnyAsync(), "a model tool call only drafts a plan");
    using var confirmation = await http.PostAsJsonAsync("/api/strategy/changes/apply",
        new { approval = draftPlan.GetProperty("approval").GetString() });
    confirmation.EnsureSuccessStatusCode();
    var plan = await db.SalesTaskGoalPlans.AsNoTracking().SingleAsync();
    Check(plan.StartsOn == plan.EndsOn && plan.Audience.ToHashSet().SetEquals([SyntheticPlatform.First, SyntheticPlatform.Second]),
        "a confirmed chat draft saves today's plan for the eligible tenant audience");
}

sealed class SyntheticIdentity(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logs, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logs, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = Request.Headers["X-Test-Identity"].ToString();
        if (identity is not ("sales" or "sales-admin")) return Task.FromResult(AuthenticateResult.NoResult());
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", identity), new Claim(ClaimTypes.Role, "sales-user"),
            new Claim(IdentityPlatformTenantContext.TenantClaim, SyntheticPlatform.TenantId.ToString())
        ], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(user, Scheme.Name)));
    }
}

sealed class SyntheticSettings : IApplicationSettingsStore
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "openai-compatible";
    public string? Model { get; set; } = "synthetic-model";
    public string? OllamaModel { get; set; }

    public Task<IReadOnlyCollection<ApplicationSettingValueRecord>> LoadAsync(ApplicationSettingsContext context, CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, object>
        {
            ["sales.ai.enabled"] = Enabled,
            ["sales.ai.provider"] = Provider,
            ["sales.ai.openAiKey"] = "synthetic-openai-key",
            ["sales.ai.compatibleKey"] = "synthetic-tenant-key"
        };
        if (Model is not null) values["sales.ai.model"] = Model;
        if (OllamaModel is not null) values["sales.ai.ollamamodel"] = OllamaModel;
        return Task.FromResult<IReadOnlyCollection<ApplicationSettingValueRecord>>(values.Select(pair =>
            new ApplicationSettingValueRecord(pair.Key, "tenantApp", context.TenantId, null, JsonSerializer.SerializeToElement(pair.Value),
                DateTimeOffset.UtcNow)).ToArray());
    }

    public Task SetAsync(ApplicationSettingsContext context, string key, string scope, JsonElement value, string? updatedBy, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task DeleteAsync(ApplicationSettingsContext context, string key, string scope, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

sealed class SyntheticGatewaySecret : IApplicationSettingsSecretStore
{
    public Task<string?> GetAsync(ApplicationSettingsContext context, string key, string scope, CancellationToken cancellationToken = default)
        => Task.FromResult(scope == ApplicationSettingScopes.PlatformApp ? key switch
        {
            "sales.ai.ollamagatewaykey" => "synthetic-gateway-secret",
            "sales.ai.openaikey" => "synthetic-vault-credential",
            _ => null
        } : null);
    public Task SetAsync(ApplicationSettingsContext context, string key, string value, string scope, string? updatedBy = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(ApplicationSettingsContext context, string key, string scope, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

sealed class SyntheticModelFactory : ISalesChatModelFactory
{
    public IChatClient Create(SalesModelSelection selection)
    {
        if (selection.Endpoint?.Host != "test-provider.invalid" || selection.Provider != "openai-compatible"
            || selection.Model != "synthetic-model" || selection.ApiKey != "synthetic-operator-secret")
            throw new InvalidOperationException("Incorrect model provider, endpoint or credential.");
        return new SyntheticModel();
    }
}

sealed class SyntheticModel : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (options?.MaxOutputTokens != 600 || options.Tools?.OfType<AIFunction>().Count() != 10
            || options.Tools.OfType<AIFunction>().Any(f => f.Name == "sales_apply_change"))
            throw new InvalidOperationException("Agent tool bindings or output limit are incorrect.");
        if (!messages.Any(m => m.Text == "Tagesprofil im Chat"))
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Synthetische Antwort.")));

        var toolResult = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().SingleOrDefault();
        if (toolResult is not null)
        {
            var serialized = JsonSerializer.SerializeToElement(toolResult.Result);
            CheckApprovalHidden(serialized);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Bitte bestätige das Tagesprofil.")));
        }
        var args = new Dictionary<string, object?>
        {
            ["name"] = "Tagesprofil aus Chat",
            ["goals"] = JsonSerializer.SerializeToElement(TaskGoalProfileService.Types.Select(t => new DailyTaskGoal(t.Key, 2))),
            ["industries"] = JsonSerializer.SerializeToElement(Array.Empty<string>())
        };
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("synthetic-change", "sales_prepare_daily_plan", args)])));
    }

    private static void CheckApprovalHidden(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Object && result.EnumerateObject().Any(p =>
            p.Name.Equals("approval", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String))
            throw new InvalidOperationException("The model must never receive an approval token.");
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var answer = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("synthetisches internes Modellmaterial")]);
        foreach (var content in answer.Messages.SelectMany(m => m.Contents))
        {
            if (content is TextContent text && text.Text.Length > 10)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, text.Text[..10]);
                await Task.Delay(30, cancellationToken);
                yield return new ChatResponseUpdate(ChatRole.Assistant, text.Text[10..]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [content]);
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

sealed class SyntheticPlatform(string? connectionString) : HttpMessageHandler
{
    internal static readonly Guid TenantId = Guid.Parse("09851496-157a-4576-85d3-8487cbab367a");
    internal static readonly Guid First = Guid.NewGuid();
    internal static readonly Guid Second = Guid.NewGuid();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        object body = uri.AbsolutePath switch
        {
            "/api/database/bindings/sales-plattform/backend" => new PlatformTenantDatabaseBinding(TenantId,
                PlatformTenantDatabaseStrategy.SharedDatabase, connectionString ?? "", "sales_chat", null, "Current", null),
            "/api/application-context/sales-plattform/tenants" => new[] { new { id = TenantId, isTenantAdmin = true } },
            "/api/tenant-portal/tenants" => new[] { new { id = TenantId, membership = new { id = First } } },
            _ when uri.AbsolutePath.EndsWith("/members/by-name") => new[]
            {
                new TeamDirectoryMember(First, "first@example.test", "TenantAdmin", "Active", [new("sales-plattform", true)])
                    { FirstName = "Sina", LastName = "Muster" },
                new TeamDirectoryMember(Second, "second@example.test", "Member", "Active", [new("sales-plattform", true)])
                    { FirstName = "Tom", LastName = "Muster" }
            }.Where(m => !uri.Query.Contains("firstName=") || uri.Query.Contains("firstName=" + m.FirstName)).ToArray(),
            _ when uri.AbsolutePath.EndsWith("/members") => new[]
            {
                new TeamDirectoryMember(First, "first@example.test", "TenantAdmin", "Active", [new("sales-plattform", true)]),
                new TeamDirectoryMember(Second, "second@example.test", "TenantMember", "Active", [new("sales-plattform", true)])
            },
            _ => throw new InvalidOperationException($"Unexpected test URL: {uri.AbsolutePath}")
        };
        if (uri.AbsolutePath == "/api/application-context/sales-plattform/tenants" &&
            request.Headers.Authorization?.Parameter == "member")
            body = new[] { new { id = TenantId, isTenantAdmin = false } };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
    }
}
