using IdentityPlatform.Shared.Tenant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SalesPlattform.Backend.Integrations.Zoho;

[Authorize("sales-user")]
public sealed class ZohoSyncJobHub(ZohoSyncService syncService) : Hub
{
    public async Task<ZohoSyncJobSnapshot> Watch(Guid runId)
    {
        var http = Context.GetHttpContext() ?? throw new HubException("HTTP-Kontext fehlt.");
        var tenant = IdentityPlatformTenantContext.Resolve(http);
        if (!tenant.IsValid || tenant.TenantId is null)
            throw new HubException("Der Tenant-Kontext ist ungültig.");

        var snapshot = await syncService.GetSnapshotAsync(runId, Context.ConnectionAborted);
        if (snapshot is null)
            throw new HubException("Der Importauftrag wurde nicht gefunden.");

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(runId), Context.ConnectionAborted);
        return snapshot;
    }

    public static string Group(Guid runId) => $"sales-zoho-sync:{runId:D}";
}
