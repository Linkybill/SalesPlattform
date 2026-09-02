namespace SalesPlattform.Backend.Integrations;

public static class CallQualification
{
    public const int DefaultConversationThresholdSeconds = 20;
    public const int MinimumConversationThresholdSeconds = 1;
    public const int MaximumConversationThresholdSeconds = 3600;

    public static int NormalizeThreshold(int thresholdSeconds)
        => Math.Clamp(
            thresholdSeconds,
            MinimumConversationThresholdSeconds,
            MaximumConversationThresholdSeconds);

    public static bool IsConversation(
        int? durationSeconds,
        string? connectionStatus,
        string? result,
        int thresholdSeconds)
    {
        var threshold = NormalizeThreshold(thresholdSeconds);
        return durationSeconds.HasValue
            && durationSeconds.Value >= threshold
            && !IsNonConversation(connectionStatus, result);
    }

    public static string? ConversationClass(
        bool isCall,
        int? durationSeconds,
        string? connectionStatus,
        string? result,
        int thresholdSeconds)
    {
        if (!isCall || !durationSeconds.HasValue)
            return null;

        return IsConversation(durationSeconds, connectionStatus, result, thresholdSeconds)
            ? "conversation"
            : "attempt";
    }

    public static bool IsNonConversation(string? connectionStatus, string? result)
    {
        var value = $"{connectionStatus} {result}"
            .ToLowerInvariant()
            .Replace('_', ' ')
            .Replace('-', ' ');
        return value.Contains("mailbox", StringComparison.Ordinal)
            || value.Contains("voicemail", StringComparison.Ordinal)
            || value.Contains("ansage", StringComparison.Ordinal)
            || value.Contains("no answer", StringComparison.Ordinal)
            || value.Contains("not reached", StringComparison.Ordinal)
            || value.Contains("nicht erreicht", StringComparison.Ordinal)
            || value.Contains("not connected", StringComparison.Ordinal)
            || value.Contains("keine verbindung", StringComparison.Ordinal)
            || value.Contains("wrong person", StringComparison.Ordinal)
            || value.Contains("wrong contact", StringComparison.Ordinal)
            || value.Contains("wrong number", StringComparison.Ordinal)
            || value.Contains("falsche person", StringComparison.Ordinal)
            || value.Contains("falscher kontakt", StringComparison.Ordinal)
            || value.Contains("falsche kontakt", StringComparison.Ordinal)
            || value.Contains("falscher ansprechpartner", StringComparison.Ordinal)
            || value.Contains("falsche ansprechpartner", StringComparison.Ordinal)
            || value.Contains("falsch verbunden", StringComparison.Ordinal)
            || value.Contains("falsche nummer", StringComparison.Ordinal)
            || value.Contains("nicht der richtige", StringComparison.Ordinal)
            || value.Contains("nicht die richtige", StringComparison.Ordinal)
            || value.Contains("nicht zuständig", StringComparison.Ordinal)
            || value.Contains("nicht zustaendig", StringComparison.Ordinal)
            || value.Contains("kein ansprechpartner", StringComparison.Ordinal);
    }
}
