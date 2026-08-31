using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoAuthorizationStart(
    string AuthorizationUrl,
    DateTimeOffset ExpiresAt);

public sealed record ZohoConnectionResult(
    bool Connected,
    string Provider,
    string ApiDomain);

public sealed class ZohoOAuthService(
    IHttpClientFactory httpClientFactory,
    IOptions<ZohoOptions> configuredOptions,
    ZohoConfigurationService configurationService,
    ZohoConnectionStore connectionStore)
{
    private readonly ZohoOptions options = configuredOptions.Value;

    public async Task<ZohoAuthorizationStart> StartAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationService.ResolveAsync(user, cancellationToken);
        var subject = user.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("Der angemeldete Benutzer besitzt keine gültige Subject-ID.");

        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(configuration.OAuthStateLifetimeMinutes);
        await connectionStore.CreateOAuthStateAsync(
            Hash(state),
            subject,
            expiresAt,
            cancellationToken);

        var authorizationUrl = QueryHelpers.AddQueryString(
            $"{configuration.AccountsUrl.TrimEnd('/')}/oauth/v2/auth",
            new Dictionary<string, string?>
            {
                ["scope"] = string.Join(',', configuration.Scopes),
                ["client_id"] = configuration.ClientId,
                ["response_type"] = "code",
                ["access_type"] = "offline",
                ["redirect_uri"] = configuration.RedirectUri,
                ["state"] = state
            });
        return new ZohoAuthorizationStart(authorizationUrl, expiresAt);
    }

    public async Task<ZohoConnectionResult> CompleteAsync(
        string code,
        string state,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationService.ResolveAsync(user, cancellationToken);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            throw new InvalidOperationException("Zoho OAuth benötigt Code und State.");

        var subject = user.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("Der angemeldete Benutzer besitzt keine gültige Subject-ID.");

        if (!await connectionStore.ConsumeOAuthStateAsync(
                Hash(state),
                subject,
                cancellationToken))
        {
            throw new InvalidOperationException("Der Zoho-OAuth-State ist ungültig, abgelaufen oder wurde bereits verwendet.");
        }

        using var response = await httpClientFactory.CreateClient().PostAsync(
            $"{configuration.AccountsUrl}/oauth/v2/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = configuration.ClientId,
                ["client_secret"] = configuration.ClientSecret,
                ["redirect_uri"] = configuration.RedirectUri,
                ["grant_type"] = "authorization_code"
            }),
            cancellationToken);
        var token = await ReadTokenResponseAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException("Zoho lieferte keinen Refresh Token.");

        var connection = await connectionStore.StoreRefreshTokenAsync(
            token.RefreshToken,
            token.ApiDomain ?? configuration.ApiUrl,
            subject,
            cancellationToken);
        return new ZohoConnectionResult(
            true,
            "zoho",
            connection.ApiDomain);
    }

    public string BuildFrontendCallbackUrl(
        string? code,
        string? state,
        string? error,
        string? errorDescription)
    {
        var parameters = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(code)) parameters["zoho_code"] = code;
        if (!string.IsNullOrWhiteSpace(state)) parameters["zoho_state"] = state;
        if (!string.IsNullOrWhiteSpace(error)) parameters["zoho_error"] = error;
        if (!string.IsNullOrWhiteSpace(errorDescription)) parameters["zoho_error_description"] = errorDescription;
        return QueryHelpers.AddQueryString(options.FrontendCallbackUrl, parameters);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("api_domain")]
        public string? ApiDomain { get; set; }
    }
}
