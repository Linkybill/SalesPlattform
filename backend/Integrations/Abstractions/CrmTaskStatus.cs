namespace SalesPlattform.Backend.Integrations.Abstractions;

public static class CrmTaskStatus
{
    internal static readonly string[] CompletedValues = ["completed", "abgeschlossen", "erledigt"];

    public static bool IsCompleted(string? status)
        => status is not null && CompletedValues.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);
}
