using System.Text.Json;
using IdentityPlatform.Shared.Tenant;
using Microsoft.AspNetCore.Http.Connections;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using SalesPlattform.Backend.Services;

namespace SalesPlattform.Backend.Strategy;

public static class SalesStrategyEndpoints
{
    internal const string TenantPolicy = "sales-strategy-tenant";
    private const string AiEnabledPolicy = "sales-ai-enabled";

    public static void AddSalesStrategy(this IServiceCollection services)
    {
        services.AddScoped<SalesStrategyTools>();
        services.AddScoped<SalesOperationsTools>();
        services.AddDataProtection();
        services.AddSignalR().AddHubOptions<SalesChatHub>(options => options.MaximumReceiveMessageSize = 96 * 1024);
        services.AddScoped<SalesModelSettings>();
        services.AddScoped<SalesStrategyAgent>();
        services.AddSingleton<ISalesChatModelFactory, OpenAiSalesChatModelFactory>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(TenantPolicy, policy => policy.RequireAssertion(context =>
                context.Resource is HttpContext http && HasVerifiedTenant(http)));
            options.AddPolicy(AiEnabledPolicy, policy => policy.RequireAssertion(async context =>
                context.Resource is HttpContext http && HasVerifiedTenant(http)
                && await http.RequestServices.GetRequiredService<SalesModelSettings>()
                    .IsEnabledAsync(Tenant(http), http.User, http.RequestAborted)));
        });
        services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<SalesStrategyTools>()
            .WithTools<SalesOperationsTools>()
            .WithPrompts<SalesChatSkills>();
    }

    public static void MapSalesStrategy(this WebApplication app)
    {
        app.MapMcp("/api/strategy/mcp").RequireAuthorization("sales-access", TenantPolicy, AiEnabledPolicy);
        app.MapHub<SalesChatHub>("/api/strategy/hub", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.LongPolling.PollTimeout = TimeSpan.FromSeconds(15);
            options.CloseOnAuthenticationExpiration = true;
        }).RequireAuthorization("sales-access", TenantPolicy, AiEnabledPolicy);
        var group = app.MapGroup("/api/strategy").RequireAuthorization("sales-access", TenantPolicy);
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (SalesTaskPlanConflictException error) { return Results.Conflict(new { message = error.Message }); }
            catch (TeamDirectoryUnavailableException) { return Results.Problem("Benutzerverzeichnis derzeit nicht erreichbar.", statusCode: 503); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (Exception error) when (error is McpException or ArgumentException)
            { return Results.BadRequest(new { message = error.Message }); }
        });

        group.MapGet("/calls", (SalesStrategyTools tools, CancellationToken ct) => tools.GetCallsAsync(cancellationToken: ct));
        group.MapGet("/renewals", (SalesStrategyTools tools, CancellationToken ct) => tools.GetRenewalsAsync(cancellationToken: ct));
        group.MapGet("/status", async (HttpContext http, SalesModelSettings settings, CancellationToken ct) =>
        {
            var status = await settings.ResolveAsync(Tenant(http), http.User, ct);
            return Results.Ok(new { status.Enabled, available = status.Selection is not null,
                provider = status.Selection?.Provider, model = status.Selection?.Model });
        });
        group.MapPost("/ask", async (SalesQuestion input, HttpContext http, SalesModelSettings settings,
            SalesStrategyAgent agent, ILogger<SalesStrategyAgent> logger, CancellationToken ct) =>
        {
            if (input.ValidationError() is { } validation)
                return Results.BadRequest(new { message = validation });
            var status = await settings.ResolveAsync(Tenant(http), http.User, ct);
            if (!status.Enabled) return Results.Forbid();
            if (status.Selection is not { } selection)
                return Results.Problem("Bitte den Vertriebsassistenten in den Mandanteneinstellungen konfigurieren.", statusCode: 503);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(selection.Provider == "ollama" ? 120 : 45));
            try { return Results.Ok(await agent.RespondAsync(selection, input.Question!.Trim(), input.History ?? [], deadline.Token)); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.Problem("Der Modellaufruf hat das Zeitlimit erreicht.", statusCode: 504);
            }
            catch (Exception error) when (error is System.ClientModel.ClientResultException or HttpRequestException or JsonException)
            {
                logger.LogWarning("Sales model request failed for {Provider} ({ErrorType}).", selection.Provider, error.GetType().Name);
                return Results.Problem("Der Modellanbieter konnte die Frage nicht beantworten.", statusCode: 502);
            }
        });
        group.MapPost("/changes/apply", async (SalesChangeApproval input, SalesOperationsTools tools, CancellationToken ct) =>
            Results.Ok(await tools.ApplyAsync(input.Approval ?? "", ct))).RequireAuthorization(AiEnabledPolicy);
    }

    internal static Guid Tenant(HttpContext http) => IdentityPlatformTenantContext.Resolve(http).TenantId!.Value;

    internal static bool HasVerifiedTenant(HttpContext http)
        => Guid.TryParse(http.User.FindFirst(IdentityPlatformTenantContext.TenantClaim)?.Value, out var claim)
            && claim != Guid.Empty && IdentityPlatformTenantContext.TryGetTenantId(http, out var resolved) && claim == resolved;

    private sealed record SalesChangeApproval(string? Approval);
}
