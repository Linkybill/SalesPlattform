using System.Security.Claims;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using Microsoft.Extensions.Options;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoWebhookConfiguration(Uri? BaseUri, string Source, string? Error);

/// <summary>One tenant-app URL resolver for the worker and the read-only overview; no OAuth/secret reads.</summary>
public sealed class ZohoWebhookSettingsService(
    IApplicationSettingsStore settingsStore,
    IOptions<ApplicationSettingsOptions> applicationOptions,
    IOptions<ZohoOptions> zohoOptions,
    IHttpContextAccessor httpContextAccessor)
{
    public const string SettingKey = "zoho.webhookUrl";

    public async Task<ZohoWebhookConfiguration> ResolveCurrentAsync(CancellationToken cancellationToken = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (!Guid.TryParse(user?.FindFirstValue("tenant_id"), out var tenantId) || tenantId == Guid.Empty)
            throw new InvalidOperationException("Für die Hook-URL fehlt ein gültiger Tenant-Kontext.");
        var subject = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Für die Hook-URL fehlt eine gültige Subject-ID.");
        var context = new ApplicationSettingsContext(applicationOptions.Value.ApplicationKey, tenantId, Guid.Empty, subject);
        var settings = await settingsStore.LoadAsync(context, cancellationToken);
        var setting = settings.SingleOrDefault(x => x.Key.Equals(SettingKey, StringComparison.OrdinalIgnoreCase)
            && x.Scope == ApplicationSettingScopes.TenantApp && x.TenantId == tenantId);
        if (setting is not null && setting.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            return new(null, "tenantApp", "Die Mandanteneinstellung zoho.webhookUrl muss eine URL als Text enthalten.");
        var tenantValue = setting?.Value.ValueKind == JsonValueKind.String ? setting.Value.GetString() : null;
        var useTenantValue = !string.IsNullOrWhiteSpace(tenantValue);
        var value = useTenantValue ? tenantValue!.Trim() : zohoOptions.Value.WebhookUrl;
        var source = useTenantValue ? "tenantApp" : string.IsNullOrWhiteSpace(value) ? "missing" : "deployment";
        return ZohoCrmHookUpdateService.TryValidateWebhookUrl(value, out var uri, out var error)
            ? new(uri, source, null) : new(null, source, error);
    }
}
