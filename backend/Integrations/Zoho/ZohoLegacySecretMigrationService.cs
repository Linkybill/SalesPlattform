using System.Net;
using System.Net.Http.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Authentication;
using IdentityPlatform.Shared.Registration;
using Microsoft.Extensions.Options;

namespace SalesPlattform.Backend.Integrations.Zoho;

/// <summary>
/// Migrates the legacy platform-owned Zoho client secret into the
/// application-owned tenant database. The legacy endpoint is only reachable
/// with the application service identity and is used until the old value has
/// been removed.
/// </summary>
public sealed class ZohoLegacySecretMigrationService(
    IApplicationSettingsSecretStore localSecrets,
    IOptions<IdentityPlatformApplicationOptions> identityPlatformOptions,
    IHttpClientFactory httpClientFactory,
    ILogger<ZohoLegacySecretMigrationService> logger)
{
    private const string ClientSecretKey = "zoho.clientSecret";

    public async Task<string?> GetOrMigrateClientSecretAsync(
        ApplicationSettingsContext context,
        string updatedBy,
        CancellationToken cancellationToken = default)
    {
        var localSecret = await localSecrets.GetAsync(
            context,
            ClientSecretKey,
            ApplicationSettingScopes.TenantApp,
            cancellationToken);
        var options = identityPlatformOptions.Value;
        if (!string.IsNullOrWhiteSpace(localSecret))
        {
            // The portal may already have copied the value into the app DB.
            // Remove a duplicate legacy platform value on the next Zoho call.
            await TryDeleteLegacySecretAsync(context, options, cancellationToken);
            return localSecret;
        }

        if (string.IsNullOrWhiteSpace(options.PlatformApiUrl))
        {
            return null;
        }

        var legacyUrl = BuildLegacyUrl(context, options);
        using var request = new HttpRequestMessage(HttpMethod.Get, legacyUrl);

        using var response = await httpClientFactory
            .CreateClient(IdentityPlatformServiceAuthenticationDefaults.ServiceClientName)
            .SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Das Legacy-Zoho-Secret konnte nicht gelesen werden (HTTP {(int)response.StatusCode}).");
        }

        var payload = await response.Content.ReadFromJsonAsync<LegacySecretResponse>(
            cancellationToken: cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Value))
            return null;

        await localSecrets.SetAsync(
            context,
            ClientSecretKey,
            payload.Value,
            ApplicationSettingScopes.TenantApp,
            updatedBy,
            cancellationToken);

        await TryDeleteLegacySecretAsync(context, options, cancellationToken);

        return payload.Value;
    }

    private async Task TryDeleteLegacySecretAsync(
        ApplicationSettingsContext context,
        IdentityPlatformApplicationOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.PlatformApiUrl))
        {
            return;
        }

        try
        {
            using var deleteRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                BuildLegacyUrl(context, options));
            using var deleteResponse = await httpClientFactory
                .CreateClient(IdentityPlatformServiceAuthenticationDefaults.ServiceClientName)
                .SendAsync(deleteRequest, cancellationToken);
            if (!deleteResponse.IsSuccessStatusCode
                && deleteResponse.StatusCode != HttpStatusCode.NotFound)
            {
                logger.LogWarning(
                    "The legacy Zoho client secret for tenant {TenantId} could not be deleted (HTTP {StatusCode}).",
                    context.TenantId,
                    (int)deleteResponse.StatusCode);
            }
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "The legacy Zoho client secret for tenant {TenantId} could not be deleted.",
                context.TenantId);
        }
    }

    private static string BuildLegacyUrl(
        ApplicationSettingsContext context,
        IdentityPlatformApplicationOptions options)
        => $"{options.PlatformApiUrl!.TrimEnd('/')}/internal/application-context/"
            + $"{Uri.EscapeDataString(context.ApplicationKey)}/tenants/{context.TenantId:D}/settings/"
            + $"{Uri.EscapeDataString(ClientSecretKey)}/legacy-secret";

    private sealed record LegacySecretResponse(string Value);
}
