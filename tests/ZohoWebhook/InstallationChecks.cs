using IdentityPlatform.Shared.Jobs;
using SalesPlattform.Backend.Integrations;

internal static class InstallationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var run = Guid.NewGuid(); var tenant = Guid.NewGuid();
        var source = new StatusSource(); var client = new PlatformJobLivenessClient(source);
        foreach (var status in new[] { PlatformJobRunStatus.Queued, PlatformJobRunStatus.Running })
        {
            source.Status = status;
            check(await client.IsActiveAsync(run, tenant), "A confirmed active CRM run must prevent a second synchronization.");
        }
        foreach (var status in new[] { PlatformJobRunStatus.Succeeded, PlatformJobRunStatus.SucceededWithWarnings,
            PlatformJobRunStatus.Failed, PlatformJobRunStatus.Cancelled, PlatformJobRunStatus.Revoked })
        {
            source.Status = status;
            check(!await client.IsActiveAsync(run, tenant), "Only a confirmed terminal CRM run may be reconciled as inactive.");
        }
        source.Status = PlatformJobRunStatus.Unknown;
        var refused = false;
        try { await client.IsActiveAsync(run, tenant); } catch (InvalidOperationException) { refused = true; }
        check(refused, "Unknown/unavailable platform status must stop reconciliation instead of abandoning the local run.");
        check(source.LastRun == run && source.LastTenant == tenant, "CRM liveness must preserve the exact run and tenant scope.");
    }

    private sealed class StatusSource : IPlatformJobRunStatusClient
    {
        public PlatformJobRunStatus Status { get; set; }
        public Guid LastRun { get; private set; }
        public Guid LastTenant { get; private set; }
        public Task<PlatformJobRunStatus> GetStatusAsync(Guid runId, Guid tenantId, CancellationToken cancellationToken = default)
        { LastRun = runId; LastTenant = tenantId; return Task.FromResult(Status); }
    }
}
