using System.Security.Claims;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Tenant;
using Microsoft.Extensions.Options;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations;

public sealed class CrmProviderNotConfiguredException()
    : InvalidOperationException("Für diesen Mandanten ist keine CRM-Integration ausgewählt.");

public sealed class CrmProviderSelectionService(
    IApplicationSettingsStore settingsStore,
    IOptions<ApplicationSettingsOptions> settingsOptions,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<string> GetSelectedProviderAsync(
        CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Für die CRM-Synchronisation fehlt der Tenant-Kontext.");
        var tenant = IdentityPlatformTenantContext.Resolve(httpContext);
        if (!tenant.IsValid || tenant.TenantId is null)
            throw new InvalidOperationException(tenant.Error ?? "Für die CRM-Synchronisation fehlt eine gültige Tenant-ID.");

        var subject = httpContext.User.FindFirstValue("sub")
            ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "system:crm-sync";
        var context = new ApplicationSettingsContext(
            settingsOptions.Value.ApplicationKey,
            tenant.TenantId.Value,
            Guid.Empty,
            subject);
        var settings = await settingsStore.LoadAsync(context, cancellationToken);
        var provider = settings.FirstOrDefault(item =>
            string.Equals(item.Key, "crm.integration", StringComparison.OrdinalIgnoreCase));
        var selected = provider?.Value.ValueKind == System.Text.Json.JsonValueKind.String
            ? provider.Value.GetString()?.Trim().ToLowerInvariant()
            : null;
        return string.IsNullOrWhiteSpace(selected) || selected == "none"
            ? throw new CrmProviderNotConfiguredException()
            : selected;
    }
}

public sealed class CrmSynchronizationAdapterRegistry(
    IEnumerable<ICrmSynchronizationAdapter> adapters,
    CrmProviderSelectionService selection)
{
    private readonly IReadOnlyDictionary<string, ICrmSynchronizationAdapter> registered = adapters
        .ToDictionary(adapter => adapter.ProviderKey, StringComparer.OrdinalIgnoreCase);

    public async Task<ICrmSynchronizationAdapter> ResolveCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var provider = await selection.GetSelectedProviderAsync(cancellationToken);
        return registered.TryGetValue(provider, out var adapter)
            ? adapter
            : throw new InvalidOperationException(
                $"Für die CRM-Integration '{provider}' ist kein Adapter im Backend registriert.");
    }
}
