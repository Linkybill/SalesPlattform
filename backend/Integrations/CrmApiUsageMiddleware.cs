using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations;

/// <summary>
/// Flushes usage observations made by CRM adapters during regular HTTP
/// requests. Platform jobs use their explicit run scope so their events retain
/// the job run ID.
/// </summary>
public sealed class CrmApiUsageMiddleware(
    RequestDelegate next,
    ILogger<CrmApiUsageMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        CrmApiUsageRecorder usage)
    {
        IDisposable? scope = null;
        if (Guid.TryParse(httpContext.User.FindFirstValue("tenant_id"), out var tenantId)
            && tenantId != Guid.Empty)
        {
            scope = usage.BeginScope(
                tenantId,
                requestedBy: httpContext.User.FindFirstValue("sub"),
                origin: CrmApiUsageOrigins.UserInterface,
                correlationId: httpContext.TraceIdentifier);
        }

        try
        {
            await next(httpContext);
        }
        finally
        {
            try
            {
                await usage.FlushAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "CRM-API-Verbrauch konnte nach dem HTTP-Request nicht gespeichert werden.");
            }
            scope?.Dispose();
        }
    }
}

public static class CrmApiUsageMiddlewareExtensions
{
    public static IApplicationBuilder UseCrmApiUsage(
        this IApplicationBuilder application)
        => application.UseMiddleware<CrmApiUsageMiddleware>();
}
