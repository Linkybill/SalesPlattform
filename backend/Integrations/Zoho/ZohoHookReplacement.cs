namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoHookReplacementResult(bool Installed, string Stage, string? Warning = null);

// Deletion is a separate job phase completed before any registration starts.
// Never delete a newly registered channel on an uncertain DB commit.
internal static class ZohoHookReplacement
{
    internal static async Task<ZohoHookReplacementResult> ExecuteAsync(
        string? oldChannelId,
        Func<CancellationToken, Task<ZohoNotificationRegistration>> register,
        Func<ZohoNotificationRegistration, CancellationToken, Task> persist,
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
            return new(true, "complete");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            var message = stage switch
            {
                "register" => "Neuregistrierung nicht bestätigt; ein zuvor deaktivierter Channel bleibt deaktiviert. Ein neuer Channel kann bei unklarer Providerantwort bereits existieren.",
                "persist" => "Speichern der neuen Zuordnung nicht bestätigt. Der neue Channel wird nicht gelöscht; ein zuvor deaktivierter Channel bleibt deaktiviert. Lokalen und Providerstand vor einem weiteren Versuch prüfen.",
                _ => "Hook-Aktualisierung nicht bestätigt; lokalen und Providerstand prüfen."
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
