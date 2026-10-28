using System.Security.Claims;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using Microsoft.Extensions.Options;

namespace SalesPlattform.Backend.Integrations.Zoho;

/// <summary>Tenant opt-in to queue test callbacks without subscription verification.</summary>
public sealed class ZohoWebhookDiagnosticService(
    IApplicationSettingsStore settingsStore,
    IOptions<ApplicationSettingsOptions> applicationOptions,
    IHttpContextAccessor httpContextAccessor)
{
    public const string SettingKey = "zoho.webhookDiagnostic";

    // Called only after trusted router and callback tenant validation.
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (!Guid.TryParse(user?.FindFirstValue("tenant_id"), out var tenantId) || tenantId == Guid.Empty)
            throw new InvalidOperationException("Für die Hook-Diagnose fehlt ein gültiger Tenant-Kontext.");
        var subject = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Für die Hook-Diagnose fehlt eine gültige Subject-ID.");
        var context = new ApplicationSettingsContext(applicationOptions.Value.ApplicationKey, tenantId, Guid.Empty, subject);
        var records = await settingsStore.LoadAsync(context, cancellationToken);
        var values = records.Where(x => x.Key.Equals(SettingKey, StringComparison.OrdinalIgnoreCase)
            && x.Scope == ApplicationSettingScopes.TenantApp && x.TenantId == tenantId).Take(2).ToArray();
        return values.Length == 1 && values[0].Value.ValueKind == JsonValueKind.True;
    }
}
