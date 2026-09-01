using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using IdentityPlatform.Shared.ApplicationSettings;
using Microsoft.Extensions.Options;

namespace SalesPlattform.Backend.Authorization;

public sealed class TenantAdminAccessService(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    IOptions<ApplicationSettingsOptions> configuredOptions)
{
    public async Task<bool> IsCurrentTenantAdminAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var tenantId = user.FindFirstValue("tenant_id");
        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.FirstOrDefault();
        var platformApiUrl = configuredOptions.Value.PlatformApiUrl;
        if (!Guid.TryParse(tenantId, out var parsedTenantId)
            || parsedTenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(authorization)
            || string.IsNullOrWhiteSpace(platformApiUrl))
        {
            return false;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{platformApiUrl.TrimEnd('/')}/api/application-context/"
                + $"{Uri.EscapeDataString(configuredOptions.Value.ApplicationKey)}/tenants");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);

        using var response = await httpClientFactory
            .CreateClient()
            .SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        var tenants = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<ApplicationTenantContext>>(
            cancellationToken: cancellationToken);
        return tenants?.Any(tenant =>
            tenant.Id == parsedTenantId
            && tenant.IsTenantAdmin) == true;
    }

    private sealed record ApplicationTenantContext(
        Guid Id,
        bool IsTenantAdmin);
}
