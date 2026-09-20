using System.Globalization;
using System.Text.Json;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed partial class ZohoCrmAdapter
{
    internal static ZohoNotificationRegistration ParseRegistrationConfirmation(
        JsonElement root, string channelId, string module, DateTimeOffset now)
    {
        var results = GetArray(root, "watch").ToArray();
        if (results.Length == 1 && IsWatchSuccess(results[0])
            && results[0].TryGetProperty("details", out var details))
        {
            var events = GetArray(details, "events").ToArray();
            if (events.Length == 1 && GetString(events[0], "channel_id") == channelId
                && GetString(events[0], "resource_name") == module
                && DateTimeOffset.TryParse(GetString(events[0], "channel_expiry"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var expiry) && expiry > now && expiry <= now.AddDays(7))
                return new(channelId, expiry.ToUniversalTime());
        }
        throw new InvalidOperationException("Zoho hat den angeforderten Channel, das Modul und den gültigen Ablauf nicht eindeutig bestätigt.");
    }

    internal static void ValidateDisableConfirmation(JsonElement root, string channelId)
    {
        var results = GetArray(root, "watch").ToArray();
        if (results.Length == 0 || results.Any(result => !IsWatchSuccess(result)
            || !result.TryGetProperty("details", out var details) || GetString(details, "channel_id") != channelId))
            throw new InvalidOperationException("Zoho hat die Deaktivierung des alten Channels nicht eindeutig bestätigt.");
    }

    private static bool IsWatchSuccess(JsonElement result)
        => result.ValueKind == JsonValueKind.Object && GetString(result, "code") == "SUCCESS"
            && GetString(result, "status") == "success";
}
