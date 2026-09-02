using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoAccessToken(string Value, string ApiDomain);

public sealed class ZohoTokenService(
    IHttpClientFactory httpClientFactory,
    ZohoConfigurationService configurationService,
    ZohoConnectionStore connectionStore,
    ZohoAccessTokenCache tokenCache,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<ZohoAccessToken> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationService.ResolveCurrentAsync(cancellationToken);
        var cacheKey = ResolveCacheKey();
        try
        {
            return await tokenCache.GetOrCreateAsync(
                cacheKey,
                tokenCancellationToken => RefreshAsync(configuration, tokenCancellationToken),
                cancellationToken);
        }
        catch (ZohoOAuthRateLimitException exception)
        {
            // Do not immediately repeat a refresh request for every following
            // module/page of the same import. Zoho explicitly asks callers to
            // wait after this response.
            tokenCache.SetFailure(cacheKey, exception.Message, TimeSpan.FromMinutes(2));
            throw;
        }
    }

    public void InvalidateCachedToken()
    {
        if (httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id") is { Length: > 0 } tenantId)
            tokenCache.Invalidate($"{tenantId}:zoho:default");
    }

    private async Task<(ZohoAccessToken Token, DateTimeOffset ExpiresAt)> RefreshAsync(
        ZohoTenantConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var refreshToken = await connectionStore.GetRefreshTokenAsync(cancellationToken);
        using var response = await httpClientFactory.CreateClient().PostAsync(
            $"{configuration.AccountsUrl}/oauth/v2/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["client_id"] = configuration.ClientId,
                ["client_secret"] = configuration.ClientSecret,
                ["grant_type"] = "refresh_token"
            }),
            cancellationToken);
        var token = await ReadTokenResponseAsync(response, cancellationToken);
        var apiDomain = (token.ApiDomain ?? configuration.ApiUrl).TrimEnd('/');
        await connectionStore.MarkTokenRefreshedAsync(apiDomain, cancellationToken);

        // Zoho normally returns 3600 seconds. Keep a safety margin so a long
        // import never starts a request with a token that is about to expire.
        var lifetimeSeconds = token.ExpiresIn > 0 ? token.ExpiresIn : 3600;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, lifetimeSeconds - 60));
        return (new ZohoAccessToken(token.AccessToken, apiDomain), expiresAt);
    }

    private string ResolveCacheKey()
    {
        var tenantId = httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id");
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new InvalidOperationException("Der Access Token enthält keine gültige tenant_id.");
        return $"{tenantId}:zoho:default";
    }

    private static async Task<ZohoTokenResponse> ReadTokenResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (body.Contains("too many requests continuously", StringComparison.OrdinalIgnoreCase))
            {
                throw new ZohoOAuthRateLimitException(
                    "Zoho OAuth ist vorübergehend gedrosselt. Bitte warten Sie einige Minuten und erneuern Sie danach die Verbindung.");
            }

            throw new InvalidOperationException($"Zoho OAuth antwortete mit HTTP {(int)response.StatusCode}: {body}");
        }

        var token = JsonSerializer.Deserialize<ZohoTokenResponse>(body);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            throw new InvalidOperationException("Zoho OAuth lieferte keinen Access Token.");
        return token;
    }

    private sealed class ZohoTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("api_domain")]
        public string? ApiDomain { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class ZohoOAuthRateLimitException(string message)
        : InvalidOperationException(message);
}
