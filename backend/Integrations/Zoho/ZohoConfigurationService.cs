using System.Security.Claims;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using Microsoft.Extensions.Options;
using SalesPlattform.Backend.Integrations;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoTenantConfiguration(
    string ClientId,
    string ClientSecret,
    string AccountsUrl,
    string ApiUrl,
    string RedirectUri,
    string FrontendCallbackUrl,
    IReadOnlyCollection<string> Scopes,
    int OAuthStateLifetimeMinutes);

/// <summary>
/// Resolves Zoho settings through the shared application-settings resolver.
/// Tenant-app secrets are decrypted only inside the SalesPlattform backend by
/// the app-local application-settings store.
/// </summary>
public sealed class ZohoConfigurationService(
        ApplicationSettingsResolver resolver,
        IApplicationSettingsStore localSettings,
        IOptions<ApplicationSettingsOptions> settingsOptions,
        IOptions<ZohoOptions> zohoOptions,
        ZohoLegacySecretMigrationService legacySecretMigration,
        IHttpContextAccessor httpContextAccessor)
{
    private readonly ZohoOptions options = zohoOptions.Value;

    public Task<ZohoTenantConfiguration> ResolveCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var user = httpContext?.User
            ?? throw new InvalidOperationException("Für die Zoho-Konfiguration ist ein angemeldeter Benutzer erforderlich.");
        if (string.IsNullOrWhiteSpace(httpContext.Request.Headers.Authorization.FirstOrDefault())
            && Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId)
            && !string.IsNullOrWhiteSpace(user.FindFirstValue("sub")))
        {
            return ResolveBackgroundAsync(tenantId, user.FindFirstValue("sub")!, cancellationToken);
        }

        return ResolveAsync(user, cancellationToken);
    }

    private async Task<ZohoTenantConfiguration> ResolveBackgroundAsync(
        Guid tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        var context = new ApplicationSettingsContext(
            settingsOptions.Value.ApplicationKey,
            tenantId,
            Guid.Empty,
            userId);
        var local = await localSettings.LoadAsync(context, cancellationToken);
        var selectedIntegration = ReadString(local, "crm.integration") ?? "none";
        if (!string.Equals(selectedIntegration, CrmProviders.Zoho, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Zoho CRM ist für diesen Mandanten nicht als CRM-Integration ausgewählt.");

        var clientId = ReadRequiredString(local, "zoho.clientId");
        var clientSecret = await legacySecretMigration.GetOrMigrateClientSecretAsync(
            context,
            userId,
            cancellationToken)
            ?? throw new InvalidOperationException("Die Einstellung 'zoho.clientSecret' ist für diesen Mandanten nicht konfiguriert.");
        var dataCenter = ReadString(local, "zoho.datacenter") ?? "eu";
        var (accountsUrl, apiUrl) = ResolveDataCenter(dataCenter);
        options.ValidateForOAuth();
        return new ZohoTenantConfiguration(
            clientId,
            clientSecret,
            accountsUrl,
            apiUrl,
            options.RedirectUri,
            options.FrontendCallbackUrl,
            options.GetScopes(),
            options.OAuthStateLifetimeMinutes);
    }

    public async Task<ZohoTenantConfiguration> ResolveAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId))
            throw new InvalidOperationException("Der Access Token enthält keine gültige tenant_id.");

        var userId = user.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidOperationException("Der angemeldete Benutzer besitzt keine gültige Subject-ID.");

        var settings = new ApplicationSettingsContext(
            settingsOptions.Value.ApplicationKey,
            tenantId,
            Guid.Empty,
            userId);
        var effective = await resolver.ResolveEffectiveUserAsync(settings, cancellationToken);

        var selectedIntegration = ReadString(effective, "crm.integration") ?? "none";
        if (!string.Equals(selectedIntegration, CrmProviders.Zoho, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Zoho CRM ist für diesen Mandanten nicht als CRM-Integration ausgewählt.");

        var clientId = ReadRequiredString(effective, "zoho.clientId");
        var clientSecret = await legacySecretMigration.GetOrMigrateClientSecretAsync(
            settings,
            userId,
            cancellationToken)
            ?? throw new InvalidOperationException("Die Einstellung 'zoho.clientSecret' ist für diesen Mandanten nicht konfiguriert.");
        var dataCenter = ReadString(effective, "zoho.datacenter") ?? "eu";
        await MirrorBackgroundSettingsAsync(settings, effective, userId, cancellationToken);
        var (accountsUrl, apiUrl) = ResolveDataCenter(dataCenter);
        options.ValidateForOAuth();
        return new ZohoTenantConfiguration(
            clientId,
            clientSecret,
            accountsUrl,
            apiUrl,
            options.RedirectUri,
            options.FrontendCallbackUrl,
            options.GetScopes(),
            options.OAuthStateLifetimeMinutes);
    }

    private async Task MirrorBackgroundSettingsAsync(
        ApplicationSettingsContext context,
        EffectiveApplicationSettings effective,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        // Interactive requests can resolve tenant-app settings from the
        // platform. Background workers deliberately have no user bearer token,
        // so keep the non-secret CRM connection values in the app-owned tenant
        // database as well. The client secret is handled exclusively by the
        // secret store and is never copied through this method.
        foreach (var key in new[] { "crm.integration", "zoho.datacenter", "zoho.clientId" })
        {
            var setting = effective.Settings.FirstOrDefault(item =>
                string.Equals(item.Definition.Key, key, StringComparison.OrdinalIgnoreCase));
            if (setting?.EffectiveValue is not JsonElement value)
                continue;

            await localSettings.SetAsync(
                context,
                key,
                ApplicationSettingScopes.TenantApp,
                value.Clone(),
                updatedBy,
                cancellationToken);
        }
    }

    private static string ReadRequiredString(
        EffectiveApplicationSettings settings,
        string key)
        => ReadString(settings, key)
            ?? throw new InvalidOperationException($"Die Einstellung '{key}' ist für diesen Mandanten nicht konfiguriert.");

    private static string? ReadString(
        EffectiveApplicationSettings settings,
        string key)
        => settings.Settings.FirstOrDefault(item =>
                string.Equals(item.Definition.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.EffectiveValue?.GetString() is { Length: > 0 } value
                ? value
                : null;

    private static string ReadRequiredString(
        IReadOnlyCollection<ApplicationSettingValueRecord> settings,
        string key)
        => ReadString(settings, key)
            ?? throw new InvalidOperationException($"Die Einstellung '{key}' ist für diesen Mandanten nicht konfiguriert.");

    private static string? ReadString(
        IReadOnlyCollection<ApplicationSettingValueRecord> settings,
        string key)
    {
        var setting = settings.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        return setting is not null
            && setting.Value.ValueKind == System.Text.Json.JsonValueKind.String
            && setting.Value.GetString() is { Length: > 0 } value
                ? value
                : null;
    }

    private static (string AccountsUrl, string ApiUrl) ResolveDataCenter(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "com" or "us" => ("https://accounts.zoho.com", "https://www.zohoapis.com"),
            "eu" => ("https://accounts.zoho.eu", "https://www.zohoapis.eu"),
            "in" => ("https://accounts.zoho.in", "https://www.zohoapis.in"),
            "au" => ("https://accounts.zoho.com.au", "https://www.zohoapis.com.au"),
            "jp" => ("https://accounts.zoho.jp", "https://www.zohoapis.jp"),
            "cn" => ("https://accounts.zoho.com.cn", "https://www.zohoapis.com.cn"),
            _ => throw new InvalidOperationException($"Der Zoho-Datacenter '{value}' wird nicht unterstützt.")
        };
}
