using System.Net.Http.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace SalesPlattform.Backend.Services.Mail;

/// <summary>
/// SMTP transport used by the first notification delivery implementation.
/// Mailpit uses the default values (mailpit:1025, no TLS, no authentication).
/// Office 365 SMTP OAuth2 can be configured later without changing the outbox
/// or rule engine; a Microsoft Graph provider can also be added beside this
/// provider through the same interface.
/// </summary>
public sealed class SmtpSalesMailDeliveryProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<SmtpSalesMailDeliveryProvider> logger) : ISalesMailDeliveryProvider
{
    public string Key => "smtp";

    public async Task SendAsync(
        SalesMailDeliveryMessage message,
        SalesMailSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (message.Recipients.Count == 0)
            throw new InvalidOperationException("Für die Benachrichtigung ist kein Empfänger hinterlegt.");

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(settings.FromName, settings.From));
        foreach (var recipient in message.Recipients)
            mimeMessage.To.Add(MailboxAddress.Parse(recipient));
        mimeMessage.Subject = message.Subject;
        mimeMessage.Body = new BodyBuilder { HtmlBody = message.BodyHtml }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 30_000 };
        await client.ConnectAsync(
            settings.Host,
            settings.Port,
            SocketOptions(settings.Security),
            cancellationToken);

        if (settings.Authentication.Equals("basic", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(settings.Username)
                || string.IsNullOrWhiteSpace(settings.Password))
            {
                throw new InvalidOperationException(
                    "SMTP Basic Authentication benötigt Benutzername und Passwort in den Mandanten-App-Settings.");
            }

            await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
        }
        else if (settings.Authentication.Equals("oauth2", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(settings.Username))
                throw new InvalidOperationException("SMTP OAuth2 benötigt die Mailbox-Adresse als Benutzername.");

            var accessToken = await GetOAuthAccessTokenAsync(settings, cancellationToken);
            await client.AuthenticateAsync(
                new SaslMechanismOAuth2(settings.Username, accessToken),
                cancellationToken);
        }
        else if (!settings.Authentication.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SMTP Authentication muss none, basic oder oauth2 sein.");
        }

        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        logger.LogInformation(
            "Sales notification sent via {SmtpHost}:{SmtpPort} to {RecipientCount} recipient(s).",
            settings.Host,
            settings.Port,
            message.Recipients.Count);
    }

    private async Task<string> GetOAuthAccessTokenAsync(
        SalesMailSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.OAuthTenantId)
            || string.IsNullOrWhiteSpace(settings.OAuthClientId)
            || string.IsNullOrWhiteSpace(settings.OAuthClientSecret))
        {
            throw new InvalidOperationException(
                "SMTP OAuth2 benötigt OAuth-Tenant-ID, Client-ID und Client-Secret.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(settings.OAuthTenantId)}/oauth2/v2.0/token");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.OAuthClientId,
            ["client_secret"] = settings.OAuthClientSecret,
            ["grant_type"] = "client_credentials",
            ["scope"] = "https://outlook.office365.com/.default"
        });

        using var response = await httpClientFactory
            .CreateClient()
            .SendAsync(request, cancellationToken);
        var tokenResponse = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken);
        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
        {
            var detail = tokenResponse?.ErrorDescription
                ?? response.ReasonPhrase
                ?? "Unbekannter OAuth2-Fehler.";
            throw new HttpRequestException($"Office-365-OAuth2-Token konnte nicht abgerufen werden: {detail}");
        }

        return tokenResponse.AccessToken;
    }

    private static SecureSocketOptions SocketOptions(string security)
        => security.ToLowerInvariant() switch
        {
            "starttls" => SecureSocketOptions.StartTls,
            "ssl" or "sslonconnect" => SecureSocketOptions.SslOnConnect,
            "none" => SecureSocketOptions.None,
            _ => throw new InvalidOperationException(
                "SMTP Security muss none, starttls oder ssl sein.")
        };

    private sealed record OAuthTokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("error_description")] string? ErrorDescription);
}
