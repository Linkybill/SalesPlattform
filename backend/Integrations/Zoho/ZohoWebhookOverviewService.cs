using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoHookSubscriptionView(string Module, string Status,
    DateTimeOffset? ExpiresAt, DateTimeOffset? LastCheckedAt, DateTimeOffset? LastRenewedAt,
    string? Error);
public sealed record ZohoHookEventView(Guid Id, string Module, string Operation, string Status,
    int AttemptCount, DateTimeOffset ReceivedAt, DateTimeOffset? ProcessedAt, string? Error);
public sealed record ZohoWebhookOverview(DateTimeOffset ObservedAt, string? CallbackBaseUrl,
    string CallbackUrlSource, string? CallbackUrlError,
    bool SchemaCached, IReadOnlyList<ZohoHookSubscriptionView> Subscriptions,
    IReadOnlyDictionary<string, int> Counts, int Total, int Page, int PageSize,
    IReadOnlyList<ZohoHookEventView> Events);

/// <summary>Read-only operational view. Never return stored payloads, token hashes or raw errors.</summary>
public sealed class ZohoWebhookOverviewService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ZohoSchemaCacheService schemaCache,
    ZohoWebhookSettingsService webhookSettings)
{
    internal static void ValidateQuery(string? module, string? status, int page, int pageSize)
    {
        if (page < 1 || page > 10000 || pageSize < 1 || pageSize > 100
            // Permit historical modules too, but keep the query bounded and literal.
            || (module is { Length: > 0 } && (module.Length > 100
                || module.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
            || (status is { Length: > 0 } && status is not ("queued" or "processing" or "processed" or "failed")))
            throw new ArgumentException("Ungültiger Hook-Filter oder Seitenbereich (1–100 Einträge pro Seite).");
    }

    public async Task<ZohoWebhookOverview> GetAsync(string? module, string? status, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        ValidateQuery(module, status, page, pageSize);
        var now = DateTimeOffset.UtcNow;
        var schema = await schemaCache.GetCachedAsync(cancellationToken);
        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var db = session.Context;
        // Explicit projection: even the query does not load subscription tokens or callback query strings.
        var subscriptions = await db.IntegrationSubscriptions.AsNoTracking()
            .Where(x => x.ProviderKey == "zoho" && x.ConnectionKey == "default")
            .Select(x => new ZohoHookSubscriptionView(x.Module, x.Status, x.ExpiresAt,
                x.LastCheckedAt, x.LastRenewedAt, x.Error))
            .ToArrayAsync(cancellationToken);
        var modules = ZohoCrmHookUpdateService.RelevantModules.Concat(subscriptions.Select(x => x.Module))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var subscriptionViews = modules.Select(name =>
        {
            var entry = subscriptions.FirstOrDefault(x => x.Module.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                return entry with { Status = SubscriptionStatus(entry.Status, entry.ExpiresAt, now), Error = SafeError(entry.Error) };
            return new ZohoHookSubscriptionView(name, schema is null ? "schema-missing"
                : schema.AvailableModules.Contains(name, StringComparer.OrdinalIgnoreCase) ? "missing" : "unavailable",
                null, null, null, null);
        }).ToArray();

        var query = db.IntegrationWebhookEvents.AsNoTracking()
            .Where(x => x.ProviderKey == "zoho" && x.ConnectionKey == "default");
        if (!string.IsNullOrEmpty(module)) query = query.Where(x => x.EventType.StartsWith(module + "."));
        var counts = await query.GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);
        if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(cancellationToken);
        // No PayloadJson selected, including for legacy events that still contain verification tokens.
        var rows = await query.OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.EventType, x.Status, x.AttemptCount, x.ReceivedAt, x.ProcessedAt, x.Error })
            .ToArrayAsync(cancellationToken);
        var events = rows.Select(x =>
        {
            var parts = x.EventType.Split('.', 2);
            return new ZohoHookEventView(x.Id, parts[0], parts.Length > 1 ? parts[1] : "unknown",
                x.Status, x.AttemptCount, x.ReceivedAt, x.ProcessedAt, SafeError(x.Error));
        }).ToArray();
        var callback = await webhookSettings.ResolveCurrentAsync(cancellationToken);
        return new(now, callback.BaseUri?.AbsoluteUri, callback.Source, callback.Error,
            schema is not null, subscriptionViews, counts, total, page, pageSize, events);
    }

    internal static string SubscriptionStatus(string status, DateTimeOffset? expiresAt, DateTimeOffset now)
        => status != "active" ? status : expiresAt is null ? "unknown-expiry"
            : expiresAt <= now ? "expired" : "active";

    // Allowlisted diagnostics only. Exception messages can contain CRM records, URLs or tokens.
    internal static string? SafeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        if (message.Contains("OAUTH_SCOPE_MISMATCH", StringComparison.OrdinalIgnoreCase))
            return "OAUTH_SCOPE_MISMATCH: Zoho-Berechtigung fehlt; Verbindung mit den benötigten Scopes erneuern.";
        if (message.Contains("INVALID_DATA", StringComparison.OrdinalIgnoreCase))
            return "INVALID_DATA: Zoho hat übermittelte Daten abgelehnt.";
        if (message.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase))
            return "HTTP 429: Zoho-Anfragelimit erreicht.";
        if (message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase))
            return "HTTP 401: Zoho-Authentifizierung prüfen.";
        return "Hook-Verarbeitung fehlgeschlagen. Details über Ereignis-ID im zugehörigen Joblauf prüfen.";
    }
}
