using System.Security.Claims;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record StoredZohoConnection(
    Guid Id,
    string ApiDomain,
    DateTimeOffset ConnectedAt,
    DateTimeOffset? LastTokenRefreshAt,
    DateTimeOffset? LastSyncAt);

public sealed record ZohoConnectionStatus(
    bool Connected,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastTokenRefreshAt,
    DateTimeOffset? LastSyncAt,
    string? ApiDomain);

public sealed class ZohoConnectionStore(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    IApplicationSettingsSecretStore secrets,
    IOptions<ApplicationSettingsOptions> settingsOptions,
    IHttpContextAccessor httpContextAccessor)
{
    private const string ProviderKey = "zoho";
    private const string ConnectionKey = "default";
    private const string RefreshTokenSettingKey = "integration.zoho.default.refresh-token";

    private readonly ApplicationSettingsOptions settings = settingsOptions.Value;

    public async Task<StoredZohoConnection?> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var context = ResolveContext(httpContextAccessor.HttpContext?.User);
        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var connection = await session.Context.IntegrationConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProviderKey == ProviderKey
                && item.ConnectionKey == ConnectionKey
                && item.IsActive, cancellationToken);
        var refreshToken = await secrets.GetAsync(
            context,
            RefreshTokenSettingKey,
            ApplicationSettingScopes.TenantApp,
            cancellationToken);
        return connection is null || string.IsNullOrWhiteSpace(refreshToken)
            ? null
            : new StoredZohoConnection(
                connection.Id,
                connection.ApiDomain,
                connection.ConnectedAt,
                connection.LastTokenRefreshAt,
                connection.LastSyncAt);
    }

    public async Task<ZohoConnectionStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = await GetActiveAsync(cancellationToken);
        return connection is null
            ? new ZohoConnectionStatus(false, null, null, null, null)
            : new ZohoConnectionStatus(
                true,
                connection.ConnectedAt,
                connection.LastTokenRefreshAt,
                connection.LastSyncAt,
                connection.ApiDomain);
    }

    public async Task<StoredZohoConnection> StoreRefreshTokenAsync(
        string refreshToken,
        string apiDomain,
        string updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("Zoho lieferte keinen gültigen Refresh Token.");

        var context = ResolveContext(httpContextAccessor.HttpContext?.User);
        await secrets.SetAsync(
            context,
            RefreshTokenSettingKey,
            refreshToken,
            ApplicationSettingScopes.TenantApp,
            updatedBy,
            cancellationToken);

        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var connection = await session.Context.IntegrationConnections
            .SingleOrDefaultAsync(item => item.ProviderKey == ProviderKey
                && item.ConnectionKey == ConnectionKey, cancellationToken);
        if (connection is null)
        {
            connection = new IntegrationConnection
            {
                Id = Guid.NewGuid(),
                ProviderKey = ProviderKey,
                ConnectionKey = ConnectionKey,
                DisplayName = "Zoho CRM",
                ApiDomain = apiDomain,
                ConnectedAt = DateTimeOffset.UtcNow,
                IsActive = true
            };
            session.Context.IntegrationConnections.Add(connection);
        }
        else
        {
            connection.ApiDomain = apiDomain;
            connection.IsActive = true;
        }

        await session.Context.SaveChangesAsync(cancellationToken);
        return new StoredZohoConnection(
            connection.Id,
            connection.ApiDomain,
            connection.ConnectedAt,
            connection.LastTokenRefreshAt,
            connection.LastSyncAt);
    }

    public async Task<string> GetRefreshTokenAsync(
        CancellationToken cancellationToken = default)
    {
        var context = ResolveContext(httpContextAccessor.HttpContext?.User);
        var refreshToken = await secrets.GetAsync(
            context,
            RefreshTokenSettingKey,
            ApplicationSettingScopes.TenantApp,
            cancellationToken);
        return string.IsNullOrWhiteSpace(refreshToken)
            ? throw new InvalidOperationException("Für diesen Mandanten ist kein Zoho-Refresh-Token hinterlegt.")
            : refreshToken;
    }

    public async Task MarkTokenRefreshedAsync(
        string apiDomain,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var connection = await session.Context.IntegrationConnections
            .SingleOrDefaultAsync(item => item.ProviderKey == ProviderKey
                && item.ConnectionKey == ConnectionKey
                && item.IsActive, cancellationToken);
        if (connection is null)
            return;

        connection.ApiDomain = apiDomain;
        connection.LastTokenRefreshAt = DateTimeOffset.UtcNow;
        await session.Context.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateOAuthStateAsync(
        string stateHash,
        string userSubject,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        session.Context.IntegrationOAuthStates.Add(new IntegrationOAuthState
        {
            Id = Guid.NewGuid(),
            ProviderKey = ProviderKey,
            StateHash = stateHash,
            UserSubject = userSubject,
            ExpiresAt = expiresAt
        });
        await session.Context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ConsumeOAuthStateAsync(
        string stateHash,
        string userSubject,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var state = await session.Context.IntegrationOAuthStates
            .SingleOrDefaultAsync(item => item.ProviderKey == ProviderKey
                && item.StateHash == stateHash
                && item.UserSubject == userSubject
                && item.ConsumedAt == null
                && item.ExpiresAt >= DateTimeOffset.UtcNow, cancellationToken);
        if (state is null)
            return false;

        state.ConsumedAt = DateTimeOffset.UtcNow;
        await session.Context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task MarkSyncAsync(
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var connection = await session.Context.IntegrationConnections
            .SingleOrDefaultAsync(item => item.ProviderKey == ProviderKey
                && item.ConnectionKey == ConnectionKey
                && item.IsActive, cancellationToken);
        if (connection is null)
            return;

        connection.LastSyncAt = DateTimeOffset.UtcNow;
        await session.Context.SaveChangesAsync(cancellationToken);
    }

    private ApplicationSettingsContext ResolveContext(ClaimsPrincipal? user)
    {
        if (user is null)
            throw new InvalidOperationException("Für die App-Datenbank ist ein angemeldeter Benutzer erforderlich.");

        var tenantId = user.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantId, out var parsedTenantId) || parsedTenantId == Guid.Empty)
            throw new InvalidOperationException("Der Access Token enthält keine gültige tenant_id.");

        var userId = user.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidOperationException("Der angemeldete Benutzer besitzt keine gültige Subject-ID.");

        return new ApplicationSettingsContext(
            settings.ApplicationKey,
            parsedTenantId,
            Guid.Empty,
            userId);
    }
}
