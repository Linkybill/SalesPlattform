using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Integrations.Zoho;

internal static class ZohoHookHandoffChannelFlow
{
    internal static async Task<string?> ExecuteAsync(IntegrationHookHandoffChannel intent, IntegrationSubscription subscription,
        Func<CancellationToken, Task<IReadOnlyList<ZohoNotificationSnapshot>>> read,
        Func<IReadOnlyList<string>, DateTimeOffset, CancellationToken, Task> patch,
        Func<CancellationToken, Task> save, CancellationToken token)
    {
        if (subscription.ChannelId != intent.ChannelId || subscription.Module != intent.Module
            || subscription.Status != "active" || !SameHash(subscription.VerificationTokenHash, intent.VerificationTokenHash)
            || !SameEvents(subscription.EventsJson, intent.EventsJson)
            || (subscription.NotifyUrl != intent.PreviousNotifyUrl && subscription.NotifyUrl != intent.TargetNotifyUrl))
            return "channel-conflict";
        try
        {
            var before = RequireExact(await read(token), intent);
            if (before is null) return "channel-conflict";
            if (before.ExpiresAt is not { } expires || expires <= DateTimeOffset.UtcNow || expires > DateTimeOffset.UtcNow.AddDays(7))
                return "channel-expired";
            if (before.NotifyUrl != intent.TargetNotifyUrl)
            {
                if (before.NotifyUrl != intent.PreviousNotifyUrl) return "channel-conflict";
                // A durable intent and attempted marker always precede the external mutation.
                intent.PatchAttemptedAt ??= DateTimeOffset.UtcNow;
                intent.ErrorCode = "verification-pending";
                await save(token);
                await patch(before.Events, expires, token);
            }
            var after = RequireExact(await read(token), intent);
            if (after is null || after.NotifyUrl != intent.TargetNotifyUrl || after.NotifyOnRelatedAction != before.NotifyOnRelatedAction
                || after.ExpiresAt != before.ExpiresAt) return "verification-pending";
            subscription.NotifyUrl = intent.TargetNotifyUrl;
            subscription.ExpiresAt = after.ExpiresAt;
            subscription.LastCheckedAt = DateTimeOffset.UtcNow;
            intent.VerifiedAt = subscription.LastCheckedAt;
            intent.ErrorCode = null;
            await save(token);
            return null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // Keep the original channel and token, including after a successful write with a lost reply.
            // The next explicit/regular job reads the same channel before considering any PATCH.
            return exception.Message.Contains("OAUTH_SCOPE_MISMATCH", StringComparison.OrdinalIgnoreCase)
                ? "scope-missing" : "provider-unavailable";
        }
    }

    private static ZohoNotificationSnapshot? RequireExact(IReadOnlyList<ZohoNotificationSnapshot> entries,
        IntegrationHookHandoffChannel intent)
    {
        if (entries.Count != 1) return null;
        var value = entries[0];
        return value.ChannelId == intent.ChannelId && value.Module == intent.Module
            && SameHash(value.TokenHash, intent.VerificationTokenHash)
            && SameEvents(JsonSerializer.Serialize(value.Events), intent.EventsJson)
            && value.Filters.Status == "none" ? value : null;
    }

    internal static bool SameHash(string? left, string? right)
        => left is { Length: 64 } && right is { Length: 64 }
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    internal static bool SameEvents(string left, string right)
    {
        try
        {
            var a = JsonSerializer.Deserialize<string[]>(left); var b = JsonSerializer.Deserialize<string[]>(right);
            return a is { Length: > 0 and <= 4 } && b is { Length: > 0 and <= 4 }
                && a.Distinct(StringComparer.Ordinal).Count() == a.Length && b.Distinct(StringComparer.Ordinal).Count() == b.Length
                && a.Order(StringComparer.Ordinal).SequenceEqual(b.Order(StringComparer.Ordinal));
        }
        catch (JsonException) { return false; }
    }
}
