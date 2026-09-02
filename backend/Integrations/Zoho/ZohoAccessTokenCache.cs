using System.Collections.Concurrent;

namespace SalesPlattform.Backend.Integrations.Zoho;

/// <summary>
/// Keeps the short-lived Zoho access token in process memory. Refresh tokens
/// remain in the encrypted tenant-app secret store; access tokens never need
/// to be persisted in the tenant database.
/// </summary>
public sealed class ZohoAccessTokenCache
{
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Failure> failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> refreshLocks = new(StringComparer.Ordinal);

    public bool TryGet(string key, out ZohoAccessToken token)
    {
        if (entries.TryGetValue(key, out var entry)
            && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            token = entry.Token;
            return true;
        }

        entries.TryRemove(key, out _);
        token = null!;
        return false;
    }

    public async Task<ZohoAccessToken> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<(ZohoAccessToken Token, DateTimeOffset ExpiresAt)>> factory,
        CancellationToken cancellationToken = default)
    {
        if (TryGetFailure(key, out var failureMessage))
            throw new InvalidOperationException(failureMessage);

        if (TryGet(key, out var cached))
            return cached;

        var refreshLock = refreshLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFailure(key, out failureMessage))
                throw new InvalidOperationException(failureMessage);

            if (TryGet(key, out cached))
                return cached;

            var created = await factory(cancellationToken);
            entries[key] = new Entry(created.Token, created.ExpiresAt);
            return created.Token;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void Invalidate(string key)
    {
        entries.TryRemove(key, out _);
        failures.TryRemove(key, out _);
    }

    public void SetFailure(
        string key,
        string message,
        TimeSpan duration)
        => failures[key] = new Failure(message, DateTimeOffset.UtcNow.Add(duration));

    private bool TryGetFailure(string key, out string message)
    {
        if (failures.TryGetValue(key, out var failure)
            && failure.BlockedUntil > DateTimeOffset.UtcNow)
        {
            message = failure.Message;
            return true;
        }

        failures.TryRemove(key, out _);
        message = string.Empty;
        return false;
    }

    private sealed record Entry(ZohoAccessToken Token, DateTimeOffset ExpiresAt);
    private sealed record Failure(string Message, DateTimeOffset BlockedUntil);
}
