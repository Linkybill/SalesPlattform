using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using Microsoft.Extensions.Options;

namespace SalesPlattform.Backend.Services.Mail;

public static class SalesMailSettingKeys
{
    public const string Provider = "sales.notifications.mail.provider";
    public const string Enabled = "sales.notifications.mail.enabled";
    public const string Host = "sales.notifications.mail.host";
    public const string Port = "sales.notifications.mail.port";
    public const string Security = "sales.notifications.mail.security";
    public const string Authentication = "sales.notifications.mail.authentication";
    public const string Username = "sales.notifications.mail.username";
    public const string Password = "sales.notifications.mail.password";
    public const string From = "sales.notifications.mail.from";
    public const string FromName = "sales.notifications.mail.fromName";
    public const string OAuthTenantId = "sales.notifications.mail.oauthTenantId";
    public const string OAuthClientId = "sales.notifications.mail.oauthClientId";
    public const string OAuthClientSecret = "sales.notifications.mail.oauthClientSecret";
    public const string ManagementRecipients = "sales.notifications.managementRecipients";
}

public sealed record SalesMailSettings(
    bool Enabled,
    string Provider,
    string Host,
    int Port,
    string Security,
    string Authentication,
    string? Username,
    string? Password,
    string From,
    string FromName,
    string? OAuthTenantId,
    string? OAuthClientId,
    string? OAuthClientSecret);

public sealed record SalesMailDeliveryMessage(
    IReadOnlyCollection<string> Recipients,
    string Subject,
    string BodyHtml);

public interface ISalesMailDeliveryProvider
{
    string Key { get; }

    Task SendAsync(
        SalesMailDeliveryMessage message,
        SalesMailSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class SalesMailDeliveryProviderRegistry(
    IEnumerable<ISalesMailDeliveryProvider> providers)
{
    private readonly IReadOnlyDictionary<string, ISalesMailDeliveryProvider> providers = providers
        .GroupBy(provider => provider.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

    public ISalesMailDeliveryProvider Resolve(string providerKey)
        => providers.TryGetValue(providerKey, out var provider)
            ? provider
            : throw new InvalidOperationException(
                $"Der Mailprovider '{providerKey}' ist in der SalesPlattform nicht registriert.");
}

/// <summary>
/// Reads the notification transport from the tenant app settings. The shared
/// application-settings store encrypts tenant-app values in the tenant
/// database; settings marked as secret in the manifest are additionally hidden
/// by the Tenant Portal editor.
/// </summary>
public sealed class SalesMailSettingsService(
    IApplicationSettingsStore settingsStore,
    IOptions<ApplicationSettingsOptions> settingsOptions,
    IConfiguration configuration)
{
    public async Task<SalesMailSettings> GetAsync(
        Guid tenantId,
        string? actor,
        CancellationToken cancellationToken = default)
    {
        var context = new ApplicationSettingsContext(
            settingsOptions.Value.ApplicationKey,
            tenantId,
            Guid.Empty,
            string.IsNullOrWhiteSpace(actor) ? "system:sales-mail" : actor);
        var values = (await settingsStore.LoadAsync(context, cancellationToken))
            .GroupBy(setting => setting.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
        var defaultHost = configuration["SalesNotifications:Mail:Host"] ?? "mailpit";
        var defaultPort = int.TryParse(
            configuration["SalesNotifications:Mail:Port"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredPort)
            ? configuredPort
            : 1025;

        return new SalesMailSettings(
            ReadBoolean(values, SalesMailSettingKeys.Enabled, true),
            ReadString(values, SalesMailSettingKeys.Provider, "smtp"),
            ReadString(values, SalesMailSettingKeys.Host, defaultHost),
            Math.Clamp(ReadInteger(values, SalesMailSettingKeys.Port, defaultPort), 1, 65535),
            ReadString(values, SalesMailSettingKeys.Security, "none"),
            ReadString(values, SalesMailSettingKeys.Authentication, "none"),
            ReadOptionalString(values, SalesMailSettingKeys.Username),
            ReadOptionalString(values, SalesMailSettingKeys.Password),
            ReadString(values, SalesMailSettingKeys.From, "sales-plattform@local.test"),
            ReadString(values, SalesMailSettingKeys.FromName, "SalesPlattform"),
            ReadOptionalString(values, SalesMailSettingKeys.OAuthTenantId),
            ReadOptionalString(values, SalesMailSettingKeys.OAuthClientId),
            ReadOptionalString(values, SalesMailSettingKeys.OAuthClientSecret));
    }

    public async Task<IReadOnlyCollection<string>> GetManagementRecipientsAsync(
        Guid tenantId,
        string? actor,
        CancellationToken cancellationToken = default)
    {
        var context = new ApplicationSettingsContext(
            settingsOptions.Value.ApplicationKey,
            tenantId,
            Guid.Empty,
            string.IsNullOrWhiteSpace(actor) ? "system:sales-mail" : actor);
        var values = await settingsStore.LoadAsync(context, cancellationToken);
        JsonElement? raw = values
            .Where(setting => string.Equals(setting.Key, SalesMailSettingKeys.ManagementRecipients, StringComparison.OrdinalIgnoreCase))
            .Select(setting => (JsonElement?)setting.Value)
            .LastOrDefault();
        if (!raw.HasValue || raw.Value.ValueKind != JsonValueKind.String)
            return [];

        return raw.Value.GetString()?
            .Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeEmail)
            .Where(email => email is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static string ReadString(
        IReadOnlyDictionary<string, JsonElement> values,
        string key,
        string fallback)
        => ReadOptionalString(values, key) ?? fallback;

    private static string? ReadOptionalString(
        IReadOnlyDictionary<string, JsonElement> values,
        string key)
        => values.TryGetValue(key, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;

    private static bool ReadBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string key,
        bool fallback)
        => values.TryGetValue(key, out var value)
            ? value.ValueKind == JsonValueKind.True
                || (value.ValueKind == JsonValueKind.String
                    && bool.TryParse(value.GetString(), out var parsed) && parsed)
            : fallback;

    private static int ReadInteger(
        IReadOnlyDictionary<string, JsonElement> values,
        string key,
        int fallback)
    {
        if (!values.TryGetValue(key, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : fallback;
    }

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            var address = new MailAddress(value.Trim());
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase)
                ? address.Address
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
