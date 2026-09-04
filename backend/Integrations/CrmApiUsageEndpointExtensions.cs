using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SalesPlattform.Backend.Authorization;

namespace SalesPlattform.Backend.Integrations;

public static class CrmApiUsageEndpointExtensions
{
    public static IEndpointRouteBuilder MapCrmApiUsageEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/integrations/usage", async (
            int? hours,
            ClaimsPrincipal user,
            TenantAdminAccessService tenantAdminAccess,
            CrmApiUsageService usage,
            CancellationToken cancellationToken) =>
        {
            if (!await tenantAdminAccess.IsCurrentTenantAdminAsync(user, cancellationToken))
                return Results.Forbid();

            try
            {
                return Results.Ok(await usage.GetAsync(hours ?? 24, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization("sales-access");

        endpoints.MapGet("/api/integrations/usage/calls", async (
            int? hours,
            Guid? runId,
            string? origin,
            string? requestedBy,
            string? correlationId,
            int? offset,
            int? limit,
            ClaimsPrincipal user,
            TenantAdminAccessService tenantAdminAccess,
            CrmApiUsageService usage,
            CancellationToken cancellationToken) =>
        {
            if (!await tenantAdminAccess.IsCurrentTenantAdminAsync(user, cancellationToken))
                return Results.Forbid();

            try
            {
                return Results.Ok(await usage.GetCallsAsync(
                    hours ?? 24,
                    runId,
                    origin,
                    requestedBy,
                    correlationId,
                    offset ?? 0,
                    limit ?? 100,
                    cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization("sales-access");

        return endpoints;
    }
}
