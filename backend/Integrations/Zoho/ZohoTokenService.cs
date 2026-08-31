using System.Text.Json;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoAccessToken(string Value, string ApiDomain);

public sealed class ZohoTokenService(
    IHttpClientFactory httpClientFactory,
    ZohoConfigurationService configurationService,
    ZohoConnectionStore connectionStore)
{
    public async Task<ZohoAccessToken> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationService.ResolveCurrentAsync(cancellationToken);
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
        await connectionStore.MarkTokenRefreshedAsync(
            token.ApiDomain ?? configuration.ApiUrl,
            cancellationToken);
        return new ZohoAccessToken(
            token.AccessToken,
            (token.ApiDomain ?? configuration.ApiUrl).TrimEnd('/'));
    }

    private static async Task<ZohoTokenResponse> ReadTokenResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Zoho OAuth antwortete mit HTTP {(int)response.StatusCode}: {body}");

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
    }
}
