using System.Security.Claims;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Installations;
using Microsoft.Extensions.Options;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoWebhookConfiguration(Uri? BaseUri, string Source, string? Error);

/// <summary>One tenant-app URL resolver for the worker and the read-only overview; no OAuth/secret reads.</summary>
public sealed class ZohoWebhookSettingsService(
    IApplicationSettingsStore settingsStore,
    IOptions<ApplicationSettingsOptions> applicationOptions,
    IHttpContextAccessor httpContextAccessor,
    InstallationRuntimeOptions? installation = null)
{
    public const string SettingKey = "zoho.webhookUrl";
    public const string AddressModeKey = "zoho.webhookAddressMode";

    public async Task<ZohoWebhookConfiguration> ResolveCurrentAsync(CancellationToken cancellationToken = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (!Guid.TryParse(user?.FindFirstValue("tenant_id"), out var tenantId) || tenantId == Guid.Empty)
            throw new InvalidOperationException("Für die Hook-URL fehlt ein gültiger Tenant-Kontext.");
        var subject = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Für die Hook-URL fehlt eine gültige Subject-ID.");
        var context = new ApplicationSettingsContext(applicationOptions.Value.ApplicationKey, tenantId, Guid.Empty, subject);
        var settings = await settingsStore.LoadAsync(context, cancellationToken);
        var modeSetting = settings.SingleOrDefault(x => x.Key.Equals(AddressModeKey, StringComparison.OrdinalIgnoreCase)
            && x.Scope == ApplicationSettingScopes.TenantApp && x.TenantId == tenantId);
        if (modeSetting is not null && modeSetting.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            return new(null, "tenantApp", "Die Mandanteneinstellung zoho.webhookAddressMode muss custom oder installation enthalten.");
        var mode = modeSetting?.Value.ValueKind == JsonValueKind.String ? modeSetting.Value.GetString() : null;
        // Existing explicit relay addresses retain their meaning until the tenant deliberately chooses installation.
        if (string.IsNullOrWhiteSpace(mode)) mode = "custom";
        if (mode is not ("custom" or "installation"))
            return new(null, "tenantApp", "Unbekannter Webhook-Adressmodus; custom oder installation auswählen.");
        if (mode == "installation")
        {
            if (installation is null || installation.InstallationId == Guid.Empty
                || !Uri.TryCreate(installation.PublicOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https"
                || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
                return new(null, "installation", "Die Installation besitzt keine gültige öffentliche HTTPS-Origin. Installation prüfen; kein Rückfall auf eine andere Callback-Adresse.");
            var callback = origin.GetLeftPart(UriPartial.Authority) + ZohoCrmHookUpdateService.WebhookPath;
            return ZohoCrmHookUpdateService.TryValidateWebhookUrl(callback, out var directUri, out var directError)
                ? new(directUri, "installation", null) : new(null, "installation", directError);
        }
        var setting = settings.SingleOrDefault(x => x.Key.Equals(SettingKey, StringComparison.OrdinalIgnoreCase)
            && x.Scope == ApplicationSettingScopes.TenantApp && x.TenantId == tenantId);
        if (setting is not null && setting.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            return new(null, "tenantApp", "Die Mandanteneinstellung zoho.webhookUrl muss eine URL als Text enthalten.");
        var tenantValue = setting?.Value.ValueKind == JsonValueKind.String ? setting.Value.GetString() : null;
        if (string.IsNullOrWhiteSpace(tenantValue))
            return new(null, "missing", "In den Sales-AppSettings unter Zoho Webhook-URL (zoho.webhookUrl) die öffentliche HTTP(S)-URL dieses Mandanten zum /api/integrations/zoho/webhook eintragen.");
        return ZohoCrmHookUpdateService.TryValidateWebhookUrl(tenantValue.Trim(), out var uri, out var error)
            ? new(uri, "tenantApp", null) : new(null, "tenantApp", error);
    }
}
