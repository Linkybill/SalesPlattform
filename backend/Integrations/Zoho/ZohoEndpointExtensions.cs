using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Zoho;

public static class ZohoEndpointExtensions
{
    public static IEndpointRouteBuilder MapZohoIntegrationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var protectedGroup = endpoints.MapGroup("/api/integrations/zoho")
            .RequireAuthorization("sales-user");

        protectedGroup.MapGet("/oauth/start", async (
            ClaimsPrincipal user,
            ZohoOAuthService oauth,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await oauth.StartAsync(user, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        protectedGroup.MapPost("/oauth/complete", async (
            ZohoOAuthCompleteRequest request,
            ClaimsPrincipal user,
            ZohoOAuthService oauth,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await oauth.CompleteAsync(
                    request.Code,
                    request.State,
                    user,
                    cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        protectedGroup.MapGet("/status", async (
            ZohoConnectionStore connections,
            CancellationToken cancellationToken) =>
            Results.Ok(await connections.GetStatusAsync(cancellationToken)));

        protectedGroup.MapGet("/test-connection", async (
            ICrmAdapter adapter,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await adapter.TestConnectionAsync(cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        protectedGroup.MapGet("/metadata", async (
            string module,
            ICrmAdapter adapter,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(module))
                return Results.BadRequest(new { message = "Das Zoho-Modul ist erforderlich." });
            try
            {
                var fields = await adapter.GetFieldsAsync(module, cancellationToken);
                return Results.Ok(new { module, fields });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        protectedGroup.MapPost("/sync", async (
            ZohoSyncRequest? request,
            ZohoSyncService sync,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await sync.SyncAsync(request?.Modules, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        endpoints.MapGet("/api/integrations/zoho/oauth/callback", (
            string? code,
            string? state,
            string? error,
            string? error_description,
            ZohoOAuthService oauth) =>
            Results.Redirect(oauth.BuildFrontendCallbackUrl(
                code,
                state,
                error,
                error_description)))
            .AllowAnonymous();

        return endpoints;
    }
}

public sealed record ZohoOAuthCompleteRequest(
    string Code,
    string State);

public sealed record ZohoSyncRequest(
    IReadOnlyCollection<string>? Modules);
