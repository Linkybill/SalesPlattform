using IdentityPlatform.Shared.Jobs;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Jobs;

public sealed class CrmFullImportJob(
    CrmSynchronizationService synchronization) : IPlatformJob
{
    public Task<PlatformJobResult> ExecuteAsync(
        PlatformJobExecutionContext context,
        CancellationToken cancellationToken)
        => synchronization.ExecuteAsync(CrmSyncModes.Full, context, cancellationToken);
}
