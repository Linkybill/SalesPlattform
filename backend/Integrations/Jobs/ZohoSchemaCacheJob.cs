using IdentityPlatform.Shared.Jobs;
using SalesPlattform.Backend.Integrations.Zoho;

namespace SalesPlattform.Backend.Integrations.Jobs;

public sealed class ZohoSchemaCacheJob(
    ZohoSchemaCacheService schemaCache) : IPlatformJob
{
    public Task<PlatformJobResult> ExecuteAsync(
        PlatformJobExecutionContext context,
        CancellationToken cancellationToken)
        => schemaCache.RefreshAsync(context, cancellationToken);
}
