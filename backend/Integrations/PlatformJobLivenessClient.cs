using System.Net;
using System.Net.Http.Json;
using IdentityPlatform.Shared.Database;
using Microsoft.Extensions.Options;

namespace SalesPlattform.Backend.Integrations;

/// <summary>
/// Checks the platform job record that owns a CRM synchronization. The CRM
/// database record alone is not sufficient after a worker restart.
/// </summary>
public sealed class PlatformJobLivenessClient(
    HttpClient httpClient,
    IOptions<PlatformTenantDatabaseOptions> databaseOptions,
    ILogger<PlatformJobLivenessClient> logger)
{
    private const string RegistrationSecretHeader = "X-Identity-Platform-Registration-Secret";

    public async Task<bool?> IsActiveAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var options = databaseOptions.Value;
        var path = $"{options.PlatformApiUrl.TrimEnd('/')}/internal/job-runs/{runId:D}/status"
            + $"?applicationKey={Uri.EscapeDataString(options.ApplicationKey)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(RegistrationSecretHeader, options.RegistrationSecret);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return false;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Could not verify platform job run {RunId}; platform returned HTTP {StatusCode}.",
                    runId,
                    (int)response.StatusCode);
                return null;
            }

            var status = await response.Content.ReadFromJsonAsync<PlatformJobLivenessResponse>(cancellationToken);
            return status?.Status is "queued" or "running";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not verify platform job run {RunId}.", runId);
            return null;
        }
    }

    private sealed record PlatformJobLivenessResponse(string Status);
}
