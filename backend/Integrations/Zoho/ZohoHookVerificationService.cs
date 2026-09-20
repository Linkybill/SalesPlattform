using System.Security.Claims;
using System.Security.Cryptography;
using IdentityPlatform.Shared.Database;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoHookVerification(DateTimeOffset CheckedAt, string Module,
    string Status, string Message, string? ExpectedUrl = null, string? RegisteredUrl = null,
    DateTimeOffset? ExpiresAt = null, IReadOnlyList<string>? Events = null,
    ZohoNotificationFilters? Filters = null, ZohoHookCheckResult? TokenCheck = null,
    ZohoHookCheckResult? LocalChannelCheck = null, DateTimeOffset? LocalExpiresAt = null,
    bool? NotifyOnRelatedAction = null);

public sealed record ZohoHookCheckResult(string Status, string Message);

public sealed class ZohoHookVerificationService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ZohoWebhookSettingsService settings,
    ZohoCrmAdapter crm,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ZohoHookVerificationService> logger)
{
    public async Task<ZohoHookVerification> CheckAsync(string module, CancellationToken cancellationToken)
    {
        if (!ZohoCrmHookUpdateService.RelevantModules.Contains(module, StringComparer.Ordinal))
            throw new ArgumentException("Unbekanntes Zoho-Modul.");
        var now = DateTimeOffset.UtcNow;
        if (!Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id"), out var tenantId)
            || tenantId == Guid.Empty)
            throw new InvalidOperationException("Kein gültiger Mandantenkontext.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var callback = await settings.ResolveCurrentAsync(timeout.Token);
            if (callback.BaseUri is null)
                return new(now, module, "configuration-error", "Keine gültige Webhook-URL konfiguriert.");
            var expected = ZohoCrmHookUpdateService.BuildTenantWebhookUrl(callback.BaseUri, tenantId);
            string[] channels;
            await using (var session = await dbFactory.OpenReadOnlyAsync(timeout.Token))
            {
                channels = await session.Context.IntegrationSubscriptions.AsNoTracking()
                    .Where(x => x.ProviderKey == "zoho" && x.ConnectionKey == "default" && x.Module == module)
                    .Select(x => x.ChannelId).Take(2).ToArrayAsync(timeout.Token);
            }
            if (channels.Length == 0)
                return new(now, module, "not-registered", "Lokal kein Channel vorhanden. Zuerst CRM-Hooks erneuern starten.", expected);
            if (channels.Length != 1)
                return new(now, module, "ambiguous", "Mehrere lokale Channels gefunden; keine eindeutige Prüfung möglich.", expected);
            var remote = await crm.ReadNotificationsAsync(channels[0], module, timeout.Token);
            // Re-read after the provider call: a concurrent renewal must not verify
            // an old channel against stale local state. Never return this entity.
            await using var currentSession = await dbFactory.OpenReadOnlyAsync(timeout.Token);
            var current = await currentSession.Context.IntegrationSubscriptions.AsNoTracking()
                .Where(x => x.ProviderKey == "zoho" && x.ConnectionKey == "default" && x.Module == module)
                .Select(x => new IntegrationSubscription
                {
                    ProviderKey = "zoho", ConnectionKey = "default", Module = module,
                    EventsJson = "[]", NotifyUrl = "",
                    ChannelId = x.ChannelId, Status = x.Status, ExpiresAt = x.ExpiresAt,
                    VerificationTokenHash = x.VerificationTokenHash
                }).Take(2).ToArrayAsync(timeout.Token);
            return Evaluate(module, channels[0], expected, remote, DateTimeOffset.UtcNow,
                current.Length == 1 ? current[0] : null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(now, module, "timeout", "Zoho-Prüfung nach 30 Sekunden abgebrochen. Später erneut prüfen.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Provider errors (including OAuth) may contain credentials. Never log the exception/body.
            logger.LogWarning("Zoho registration verification failed ({ExceptionType}).", exception.GetType().Name);
            return SafeFailure(module, exception, now);
        }
    }

    internal static ZohoHookVerification SafeFailure(string module, Exception exception, DateTimeOffset now)
    {
        var message = exception.Message;
        if (message.Contains("OAUTH_SCOPE_MISMATCH", StringComparison.OrdinalIgnoreCase))
            return new(now, module, "scope-missing", "ZohoCRM.notifications.READ fehlt. Nach dem Sales-Update unter CRM-Integration erneut Zoho verbinden und die Berechtigungen bestätigen.");
        if (message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase))
            return new(now, module, "authentication-error", "Zoho-Authentifizierung fehlgeschlagen. Verbindung prüfen bzw. erneut Zoho verbinden.");
        if (message.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase))
            return new(now, module, "rate-limited", "Zoho-Anfragelimit erreicht. Später erneut prüfen.");
        return new(now, module, "check-failed", "Registrierung konnte nicht bei Zoho geprüft werden. Verbindung und technische Logs prüfen; keine erfolgreiche Prüfung.");
    }

    internal static ZohoHookVerification Evaluate(string module, string channelId, string expected,
        IReadOnlyList<ZohoNotificationSnapshot> entries, DateTimeOffset now, IntegrationSubscription? local)
    {
        var matches = entries.Where(x => x.ChannelId == channelId && x.Module == module).ToArray();
        if (matches.Length == 0)
            return new(now, module, "not-found", "Der lokale Channel wurde bei Zoho für dieses Modul nicht gefunden.", expected);
        if (matches.Length != 1)
            return new(now, module, "ambiguous", "Zoho liefert mehrere passende Registrierungen; keine eindeutige Prüfung möglich.", expected);
        var entry = matches[0];
        var localCheck = CheckLocalChannel(local, channelId, now);
        var tokenCheck = local is null || local.ChannelId != channelId
            ? new ZohoHookCheckResult("not-checked", "Token-Abgleich nicht möglich: lokaler Channel fehlt oder wurde während der Prüfung geändert.")
            : CheckToken(local.VerificationTokenHash, entry.TokenHash);
        var allowed = new[] { $"{module}.create", $"{module}.edit", $"{module}.delete", $"{module}.all" };
        var events = entry.Events.Where(x => allowed.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
        var (status, message) = !Uri.TryCreate(entry.NotifyUrl, UriKind.Absolute, out var uri)
            || uri.AbsoluteUri != new Uri(expected).AbsoluteUri
            ? ("url-mismatch", "Die bei Zoho gespeicherte Callback-Adresse weicht von der aktuellen Mandantenkonfiguration ab. Registrierung gezielt prüfen/korrigieren.")
            : entry.ExpiresAt is null ? ("expiry-unknown", "Zoho liefert keine gültige Ablaufzeit.")
            : entry.ExpiresAt <= now ? ("expired", "Die Registrierung bei Zoho ist abgelaufen. Mit 'Hooks aktualisieren' einen manuellen Neuaufbau starten.")
            : !(events.Contains($"{module}.all", StringComparer.OrdinalIgnoreCase)
                || (module != "Users" && allowed.Take(3).All(x => events.Contains(x, StringComparer.OrdinalIgnoreCase))))
                ? ("events-mismatch", "Die erwarteten Ereignistypen sind bei Zoho nicht vollständig registriert. Registrierung gezielt prüfen/korrigieren.")
            : localCheck.Status != "ready" ? ("local-not-ready", localCheck.Message)
            : tokenCheck.Status != "match" ? ("token-not-verified", tokenCheck.Message)
            : entry.Filters.Status != "none" ? ("filters-" + entry.Filters.Status, entry.Filters.Message)
            : ("verified", "Registrierung direkt bei Zoho bestätigt: URL, Ablauf und Ereignisse stimmen; keine Feldfilter, Token stimmt überein und lokaler Channel ist aktiv und gültig. Kein Nachweis einer erfolgreichen Callback-Zustellung.");
        return new(now, module, status, message, expected, SafeCallbackUrl(entry.NotifyUrl), entry.ExpiresAt, events,
            entry.Filters, tokenCheck, localCheck, local?.ExpiresAt, entry.NotifyOnRelatedAction);
    }

    private static ZohoHookCheckResult CheckLocalChannel(IntegrationSubscription? local, string channelId, DateTimeOffset now)
        => local is null ? new("missing", "Lokaler Channel fehlt oder ist nicht eindeutig.")
            : local.ChannelId != channelId ? new("changed", "Lokaler Channel wurde während der Prüfung geändert. Bitte erneut prüfen.")
            : local.Status != "active" ? new("inactive", "Lokaler Channel ist nicht aktiv; der Empfänger würde den Callback ablehnen.")
            : local.ExpiresAt is null ? new("expiry-unknown", "Lokale Ablaufzeit fehlt; der Empfänger würde den Callback ablehnen.")
            : local.ExpiresAt <= now ? new("expired", "Lokaler Channel ist abgelaufen; der Empfänger würde den Callback ablehnen.")
            : new("ready", "Lokaler Channel stimmt überein, ist aktiv und gültig.");

    private static ZohoHookCheckResult CheckToken(string? localHash, string? remoteHash)
    {
        if (string.IsNullOrEmpty(remoteHash)) return new("remote-missing", "Verification-Token bei Zoho fehlt oder ist ungültig.");
        if (string.IsNullOrEmpty(localHash)) return new("local-missing", "Lokaler Verification-Token-Hash fehlt.");
        try
        {
            var localBytes = Convert.FromHexString(localHash);
            var remoteBytes = Convert.FromHexString(remoteHash);
            if (localBytes.Length != 32 || remoteBytes.Length != 32)
                return new("invalid", "Verification-Token-Abgleich nicht möglich: ungültiger Hash.");
            return CryptographicOperations.FixedTimeEquals(localBytes, remoteBytes)
                ? new("match", "Verification-Token stimmt überein.")
                : new("mismatch", "Verification-Token weicht ab; der Empfänger würde den Callback ablehnen.");
        }
        catch (FormatException) { return new("invalid", "Verification-Token-Abgleich nicht möglich: ungültiger Hash."); }
    }

    internal static string? SafeCallbackUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return null;
        // Do not expose userinfo, fragments, arbitrary paths or secret query parameters.
        var path = uri.AbsolutePath.EndsWith("/api/integrations/zoho/webhook", StringComparison.Ordinal)
            ? "/api/integrations/zoho/webhook" : "/[abweichender Pfad ausgeblendet]";
        var query = QueryHelpers.ParseQuery(uri.Query);
        var tenant = query.TryGetValue("tenant_id", out var ids) && ids.Count == 1 && Guid.TryParse(ids[0], out var id)
            ? $"?tenant_id={id:D}" : string.Empty;
        var origin = new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri.GetLeftPart(UriPartial.Authority);
        return origin
            + path + tenant + (query.Keys.Any(x => x != "tenant_id") || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0
                ? " [weitere Angaben ausgeblendet]" : string.Empty);
    }
}
