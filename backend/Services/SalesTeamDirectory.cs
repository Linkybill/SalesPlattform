using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace SalesPlattform.Backend.Services;

public sealed record SalesTeamUser(Guid Id, string Email)
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
public sealed record TeamDirectoryApplication(string AppKey, bool Enabled);
public sealed record TeamDirectoryMember(Guid Id, string Email, string Role, string Status,
    TeamDirectoryApplication[] Applications)
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
public sealed class TeamDirectoryUnavailableException : Exception;

public sealed class SalesTeamDirectory(IHttpClientFactory clients, IHttpContextAccessor context,
    IOptions<ApplicationSettingsOptions> options)
{
    private sealed record OwnMembership(Guid Id);
    private sealed record OwnTenant(Guid Id, OwnMembership Membership);

    public async Task<Guid> CurrentMembershipAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId) || tenantId == Guid.Empty)
            throw new UnauthorizedAccessException();
        var authorization = context.HttpContext?.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authorization)) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(options.Value.PlatformApiUrl)) throw new TeamDirectoryUnavailableException();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{options.Value.PlatformApiUrl.TrimEnd('/')}/api/tenant-portal/tenants");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        try
        {
            using var response = await clients.CreateClient(IdentityPlatformServiceAuthenticationDefaults.ServiceClientName).SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
            if (!response.IsSuccessStatusCode) throw new TeamDirectoryUnavailableException();
            var tenants = await response.Content.ReadFromJsonAsync<OwnTenant[]>(ct) ?? throw new TeamDirectoryUnavailableException();
            var id = tenants.SingleOrDefault(t => t.Id == tenantId)?.Membership.Id;
            return id is { } memberId && memberId != Guid.Empty ? memberId : throw new UnauthorizedAccessException();
        }
        catch (HttpRequestException) { throw new TeamDirectoryUnavailableException(); }
        catch (System.Text.Json.JsonException) { throw new TeamDirectoryUnavailableException(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TeamDirectoryUnavailableException(); }
    }

    public static SalesTeamUser[] EligibleUsers(IEnumerable<TeamDirectoryMember> members, string appKey)
        => members.Where(m => m.Id != Guid.Empty && m.Status == "Active"
                && !string.IsNullOrWhiteSpace(m.Email) && m.Email.Length <= 320
                && m.Applications.Any(a => a.AppKey == appKey && (a.Enabled || m.Role == "TenantAdmin")))
            .Select(m => new SalesTeamUser(m.Id, m.Email) { FirstName = m.FirstName, LastName = m.LastName })
            .OrderBy(m => m.Email).ToArray();

    public Task<SalesTeamUser[]> GetAsync(ClaimsPrincipal user, CancellationToken ct) => ReadAsync(user, "", ct);

    internal Task<SalesTeamUser[]> FindByNameAsync(ClaimsPrincipal user, string? firstName, string? lastName, CancellationToken ct)
        => ReadAsync(user, QueryHelpers.AddQueryString("/by-name", new Dictionary<string, string?>
        { ["firstName"] = firstName, ["lastName"] = lastName }), ct);

    private async Task<SalesTeamUser[]> ReadAsync(ClaimsPrincipal user, string suffix, CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId) || tenantId == Guid.Empty)
            throw new UnauthorizedAccessException();
        var authorization = context.HttpContext?.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authorization)) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(options.Value.PlatformApiUrl)) throw new TeamDirectoryUnavailableException();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{options.Value.PlatformApiUrl.TrimEnd('/')}/api/tenant-portal/tenants/{tenantId:D}/members{suffix}");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        try
        {
            using var response = await clients.CreateClient(IdentityPlatformServiceAuthenticationDefaults.ServiceClientName).SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
            if (!response.IsSuccessStatusCode) throw new TeamDirectoryUnavailableException();
            var members = await response.Content.ReadFromJsonAsync<TeamDirectoryMember[]>(ct)
                ?? throw new TeamDirectoryUnavailableException();
            return EligibleUsers(members, options.Value.ApplicationKey);
        }
        catch (HttpRequestException) { throw new TeamDirectoryUnavailableException(); }
        catch (System.Text.Json.JsonException) { throw new TeamDirectoryUnavailableException(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TeamDirectoryUnavailableException(); }
    }
}
