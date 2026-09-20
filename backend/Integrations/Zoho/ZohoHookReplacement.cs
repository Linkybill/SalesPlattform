namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoHookReplacementResult(bool Installed, string Stage, string? Warning = null);

// The remote API and tenant DB cannot share a transaction. Never delete either
// channel on an uncertain DB commit. Only a confirmed save permits old-channel cleanup.
internal static class ZohoHookReplacement
{
    internal static async Task<ZohoHookReplacementResult> ExecuteAsync(
        string? oldChannelId,
        Func<CancellationToken, Task<ZohoNotificationRegistration>> register,
        Func<ZohoNotificationRegistration, CancellationToken, Task> persist,
        Func<string, CancellationToken, Task> disable,
        CancellationToken cancellationToken)
    {
        var stage = "register";
        var installed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var registration = await register(cancellationToken);
            if (registration.ChannelId == oldChannelId || registration.ExpiresAt is null)
                throw new InvalidOperationException("Invalid replacement confirmation.");
            stage = "persist";
            cancellationToken.ThrowIfCancellationRequested();
            await persist(registration, cancellationToken);
            installed = true;
            stage = "cleanup";
            if (!string.IsNullOrWhiteSpace(oldChannelId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await disable(oldChannelId, cancellationToken);
            }
            return new(true, "complete");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            var message = stage switch
            {
                "register" => "Neuregistrierung nicht bestätigt; bisherige lokale Zuordnung bleibt unverändert. Ein neuer Channel kann bei unklarer Providerantwort bereits existieren.",
                "persist" => "Speichern der neuen Zuordnung nicht bestätigt. Kein Channel wurde deaktiviert; beide können bei Zoho noch aktiv sein. Lokalen und Providerstand vor einem weiteren Versuch prüfen.",
                _ => "Neue Registrierung ist lokal gespeichert; alter Channel konnte nicht sicher deaktiviert werden und kann bis zum Ablauf weitere Callbacks senden. Bereinigung prüfen."
            };
            // Only safe, fixed error classes. Provider error bodies may contain tokens.
            var hint = exception is OperationCanceledException ? " Zeitlimit beim Teilschritt."
                : exception.Message.Contains("OAUTH_SCOPE_MISMATCH", StringComparison.OrdinalIgnoreCase) ? " Zoho-Berechtigung fehlt."
                : exception.Message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase) ? " Zoho-Authentifizierung prüfen."
                : exception.Message.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase) ? " Zoho-Anfragelimit erreicht."
                : "";
            return new(installed, stage, message + hint);
        }
    }
}
