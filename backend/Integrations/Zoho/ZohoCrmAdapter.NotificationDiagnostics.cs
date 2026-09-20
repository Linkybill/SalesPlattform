using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesPlattform.Backend.Integrations.Zoho;

// Internal provider projection: never serialize this directly to a browser or log it.
internal sealed record ZohoNotificationSnapshot(string ChannelId, string Module,
    string? NotifyUrl, DateTimeOffset? ExpiresAt, IReadOnlyList<string> Events,
    [property: JsonIgnore] string? TokenHash, ZohoNotificationFilters Filters, bool? NotifyOnRelatedAction)
{
    public override string ToString() => "Zoho notification snapshot (private)";
}

public sealed partial class ZohoCrmAdapter
{
    internal async Task<IReadOnlyList<ZohoNotificationSnapshot>> ReadNotificationsAsync(
        string channelId, string module, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(channelId) || !channelId.All(char.IsAsciiDigit))
            throw new InvalidOperationException("Invalid stored notification channel.");
        var entries = new List<ZohoNotificationSnapshot>();
        // Usually one entry; bound pagination and never report a partial list as complete.
        for (var page = 1; page <= 5; page++)
        {
            using var response = await SendAsync(HttpMethod.Get,
                $"/crm/v8/actions/watch?channel_id={Uri.EscapeDataString(channelId)}&module={Uri.EscapeDataString(module)}&page={page}&per_page=200",
                cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return entries;
            using var document = await ParseDocumentAsync(response, cancellationToken);
            entries.AddRange(ParseNotificationSnapshots(document.RootElement));
            if (!document.RootElement.TryGetProperty("info", out var info)
                || !info.TryGetProperty("more_records", out var more) || more.ValueKind == JsonValueKind.False)
                return entries;
            if (more.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("Invalid notification pagination.");
        }
        throw new InvalidOperationException("Notification pagination limit reached.");
    }

    internal static IReadOnlyList<ZohoNotificationSnapshot> ParseNotificationSnapshots(JsonElement root)
    {
        if (!root.TryGetProperty("watch", out var watch) || watch.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Invalid notification response.");
        return watch.EnumerateArray().Select(item => new ZohoNotificationSnapshot(
            GetString(item, "channel_id") ?? string.Empty,
            GetString(item, "resource_name") ?? string.Empty,
            GetString(item, "notify_url"),
            DateTimeOffset.TryParse(GetString(item, "channel_expiry"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var expiry) ? expiry.ToUniversalTime() : null,
            GetArray(item, "events").Select(value => value.GetString() ?? string.Empty).ToArray(),
            ReadNotificationTokenHash(item), ZohoNotificationFilterDiagnostics.Read(item),
            item.TryGetProperty("notify_on_related_action", out var related)
                && related.ValueKind is JsonValueKind.True or JsonValueKind.False ? related.GetBoolean() : null)).ToArray();
    }

    private static string? ReadNotificationTokenHash(JsonElement item)
        => item.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String
            && token.GetString() is { Length: > 0 and <= 50 } value
                ? ZohoCrmHookUpdateService.HashToken(value) : null;
}
