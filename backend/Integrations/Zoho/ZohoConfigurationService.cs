using System.Security.Claims;
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
    IOptions<ApplicationSettingsOptions> settingsOptions,
    IOptions<ZohoOptions> zohoOptions,
    IHttpContextAccessor httpContextAccessor)
{
    private readonly ZohoOptions options = zohoOptions.Value;

    public Task<ZohoTenantConfiguration> ResolveCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var user = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("Für die Zoho-Konfiguration ist ein angemeldeter Benutzer erforderlich.");
        return ResolveAsync(user, cancellationToken);
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
        var clientSecret = ReadRequiredString(effective, "zoho.clientSecret");
        var dataCenter = ReadString(effective, "zoho.datacenter") ?? "eu";
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
