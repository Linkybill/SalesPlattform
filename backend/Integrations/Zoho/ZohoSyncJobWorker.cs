using System.Security.Claims;
using IdentityPlatform.Shared.Tenant;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoSyncJobWorker(
    ZohoSyncJobStore store,
    IServiceScopeFactory scopeFactory,
    IHubContext<ZohoSyncJobHub> hub,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ZohoSyncJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await store.ConsumeAsync(ProcessAsync, stoppingToken);
    }

    private async Task ProcessAsync(
        ZohoSyncJobWorkItem workItem,
        CancellationToken stoppingToken)
    {
        var previousHttpContext = httpContextAccessor.HttpContext;
        var backgroundHttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(IdentityPlatformTenantContext.TenantClaim, workItem.TenantId.ToString("D")),
                new Claim("sub", workItem.UserSubject),
                new Claim(ClaimTypes.NameIdentifier, workItem.UserSubject)
            ],
            authenticationType: "SalesPlattformBackgroundJob"))
        };
        backgroundHttpContext.Request.Headers[IdentityPlatformTenantContext.Header] = workItem.TenantId.ToString("D");
        httpContextAccessor.HttpContext = backgroundHttpContext;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<ZohoSyncService>();
            await syncService.RunAsync(workItem, PublishAsync, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Zoho import job {RunId} was cancelled during application shutdown.", workItem.RunId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Zoho import job {RunId} failed in the background worker.", workItem.RunId);
        }
        finally
        {
            httpContextAccessor.HttpContext = previousHttpContext;
        }
    }

    private async Task PublishAsync(ZohoSyncJobSnapshot snapshot)
    {
        try
        {
            await hub.Clients
                .Group(ZohoSyncJobHub.Group(snapshot.RunId))
                .SendAsync("jobUpdated", snapshot);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Could not publish Zoho import update for job {RunId}.", snapshot.RunId);
        }
    }
}
