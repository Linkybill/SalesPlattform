using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SalesPlattform.Backend.Authorization;
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
            ZohoCrmAdapter adapter,
            ZohoSchemaCacheService schemaCache,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await adapter.TestConnectionAsync(cancellationToken);
                var schema = await schemaCache.GetCachedAsync(cancellationToken);
                return Results.Ok(result with
                {
                    AvailableModules = schema?.AvailableModules ?? []
                });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        protectedGroup.MapGet("/metadata", async (
            string module,
            ZohoSchemaCacheService schemaCache,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(module))
                return Results.BadRequest(new { message = "Das Zoho-Modul ist erforderlich." });
            try
            {
                var schema = await schemaCache.GetCachedAsync(cancellationToken);
                if (schema is null)
                {
                    return Results.Conflict(new
                    {
                        message = "Noch kein Zoho-Schema-Cache vorhanden. Bitte zuerst den manuellen Job 'Zoho-Schema cachen' starten."
                    });
                }

                var fields = schema.GetFields(module);
                return Results.Ok(new { module, fields });
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

        endpoints.MapPost("/api/integrations/zoho/webhook", async (
            HttpContext httpContext,
            JsonElement payload,
            ZohoWebhookReceiver receiver,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(httpContext.Request.Query["tenant_id"], out var tenantId)
                || tenantId == Guid.Empty)
            {
                return Results.BadRequest(new { message = "Der Zoho-Webhook enthält keinen gültigen Tenant-Kontext." });
            }

            // Zoho cannot send the platform JWT. The tenant is taken only from
            // the provider-generated callback URL and the request is accepted
            // only after the subscription token is verified against that tenant.
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_id", tenantId.ToString("D")),
                new Claim("sub", "system:zoho-webhook")
            ], "zoho-webhook"));
            try
            {
                var receipt = await receiver.ReceiveAsync(payload, cancellationToken);
                return Results.Accepted(value: receipt);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        }).AllowAnonymous();

        return endpoints;
    }
}

public sealed record ZohoOAuthCompleteRequest(
    string Code,
    string State);
