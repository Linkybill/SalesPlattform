using System.Text;
using System.Text.Json;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed partial class ZohoCrmAdapter
{
    internal async Task MoveNotificationCallbackAsync(string channelId, string module, string notifyUrl,
        IReadOnlyList<string> events, DateTimeOffset expiresAt, CancellationToken token)
    {
        var body = BuildNotificationHandoff(channelId, module, notifyUrl, events, expiresAt);
        using var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Patch, "/crm/v8/actions/watch", token, content: content);
        using var document = await ParseDocumentAsync(response, token);
        _ = ParseRegistrationConfirmation(document.RootElement, channelId, module, DateTimeOffset.UtcNow);
        // The caller must GET and compare the exact existing channel/token/events before committing locally.
    }

    internal static JsonElement BuildNotificationHandoff(string channelId, string module, string notifyUrl,
        IReadOnlyList<string> events, DateTimeOffset expiresAt)
    {
        if (channelId.Length is < 1 or > 100 || !channelId.All(char.IsAsciiDigit)
            || module.Length is < 1 or > 100 || module.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')
            || events.Count == 0 || events.Count > 4 || events.Distinct(StringComparer.Ordinal).Count() != events.Count
            || events.Any(value => !new[] { $"{module}.create", $"{module}.edit", $"{module}.delete", $"{module}.all" }.Contains(value))
            || !Uri.TryCreate(notifyUrl, UriKind.Absolute, out var callback) || callback.UserInfo.Length != 0
            || callback.Fragment.Length != 0 || callback.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("Invalid retained callback handoff identity.");
        // PATCH keeps omitted fields, particularly the verification token and notification filters.
        // Supply the confirmed existing expiry explicitly so no default one-hour expiry is introduced.
        return JsonSerializer.SerializeToElement(new { watch = new[] { new
        {
            channel_id = channelId, notify_url = notifyUrl, events,
            channel_expiry = expiresAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture)
        } } });
    }
}
