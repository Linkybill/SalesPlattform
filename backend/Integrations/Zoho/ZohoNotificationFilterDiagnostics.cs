using System.Text.Json;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoNotificationFilters(string Status, string Message, IReadOnlyList<string> Conditions);

// Project only field API names and AND/OR structure. Never return raw provider JSON,
// field values, IDs, labels, unknown properties or unbounded expressions.
internal static class ZohoNotificationFilterDiagnostics
{
    internal static ZohoNotificationFilters Read(JsonElement item)
    {
        static bool Empty(JsonElement value) => value.ValueKind == JsonValueKind.Null
            || (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);
        var summaries = new List<string>();
        if (item.TryGetProperty("fields", out var legacy) && !Empty(legacy))
            return Unknown();
        if (!item.TryGetProperty("notification_condition", out var conditions)) return Unknown();
        if (Empty(conditions)) return new("none", "Keine Feldfilter bei Zoho gesetzt.", []);
        if (conditions.ValueKind != JsonValueKind.Array || conditions.GetArrayLength() > 10) return Unknown();
        var budget = 40;
        foreach (var condition in conditions.EnumerateArray())
        {
            if (condition.ValueKind != JsonValueKind.Object
                || !condition.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || type.GetString() != "field_selection"
                || !condition.TryGetProperty("module", out var module) || ApiName(module) is not { } moduleName
                || !condition.TryGetProperty("field_selection", out var selection)
                || Expression(selection, 0, ref budget) is not { } expression) return Unknown();
            summaries.Add($"{moduleName}: {expression}");
        }
        return new("present", "Feldfilter gesetzt: Benachrichtigungen sind auf bestimmte Feldänderungen beschränkt.", summaries);
    }

    private static ZohoNotificationFilters Unknown()
        => new("unknown", "Feldfilter nicht vollständig prüfbar: Angaben fehlen oder haben ein unbekanntes Format.", []);

    private static string? ApiName(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("api_name", out var name)
            || name.ValueKind != JsonValueKind.String || name.GetString() is not { Length: > 0 and <= 100 } text)
            return null;
        return text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? text : null;
    }

    private static string? Expression(JsonElement node, int depth, ref int budget)
    {
        if (depth > 6 || --budget < 0 || node.ValueKind != JsonValueKind.Object) return null;
        var hasField = node.TryGetProperty("field", out var field) && field.ValueKind != JsonValueKind.Null;
        var hasGroup = node.TryGetProperty("group", out var group) && group.ValueKind != JsonValueKind.Null;
        if (hasField)
            return hasGroup ? null : ApiName(field);
        if (!hasGroup || group.ValueKind != JsonValueKind.Array || group.GetArrayLength() is < 1 or > 10
            || !node.TryGetProperty("group_operator", out var op) || op.ValueKind != JsonValueKind.String
            || op.GetString() is not ("and" or "or")) return null;
        var children = new List<string>();
        foreach (var child in group.EnumerateArray())
        {
            if (Expression(child, depth + 1, ref budget) is not { } expression) return null;
            children.Add(expression);
        }
        return "(" + string.Join(op.GetString() == "and" ? " UND " : " ODER ", children) + ")";
    }
}
