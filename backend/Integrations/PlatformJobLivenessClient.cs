using IdentityPlatform.Shared.Jobs;

namespace SalesPlattform.Backend.Integrations;

/// <summary>Only an authoritative platform terminal status permits reconciliation of an old CRM run.</summary>
public sealed class PlatformJobLivenessClient(IPlatformJobRunStatusClient platform)
{
    public async Task<bool> IsActiveAsync(Guid runId, Guid tenantId, CancellationToken cancellationToken = default)
        => await platform.GetStatusAsync(runId, tenantId, cancellationToken) switch
        {
            PlatformJobRunStatus.Queued or PlatformJobRunStatus.Running => true,
            PlatformJobRunStatus.Succeeded or PlatformJobRunStatus.SucceededWithWarnings or PlatformJobRunStatus.Failed
                or PlatformJobRunStatus.Cancelled or PlatformJobRunStatus.Revoked => false,
            _ => throw new InvalidOperationException("Der Status eines vorherigen CRM-Imports ist derzeit nicht verifizierbar. Der vorhandene Lauf bleibt erhalten; bitte später erneut versuchen.")
        };
}
