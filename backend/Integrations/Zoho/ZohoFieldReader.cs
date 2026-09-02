using System.Globalization;
using System.Text.Json;

namespace SalesPlattform.Backend.Integrations.Zoho;

internal static class ZohoFieldReader
{
    public static string? String(JsonElement record, params string[] fieldNames)
    {
        var value = Find(record, fieldNames);
        if (value is null)
            return null;

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object => StringFromObject(value.Value),
            _ => null
        };
    }

    public static string? LookupId(JsonElement record, params string[] fieldNames)
    {
        var value = Find(record, fieldNames);
        return value is { ValueKind: JsonValueKind.Object }
            && value.Value.TryGetProperty("id", out var id)
            ? id.GetString()
            : String(record, fieldNames);
    }

    public static decimal? Decimal(JsonElement record, params string[] fieldNames)
    {
        var value = Find(record, fieldNames);
        if (value is null)
            return null;
        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetDecimal(out var number))
            return number;
        return System.Decimal.TryParse(
            String(record, fieldNames),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    public static int? Int32(JsonElement record, params string[] fieldNames)
        => int.TryParse(String(record, fieldNames), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static bool Bool(JsonElement record, params string[] fieldNames)
    {
        var value = Find(record, fieldNames);
        if (value is null) return false;
        if (value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.Value.GetBoolean();
        var text = String(record, fieldNames);
        return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "active", StringComparison.OrdinalIgnoreCase)
            || text == "1";
    }

    public static DateTimeOffset? DateTimeOffset(JsonElement record, params string[] fieldNames)
    {
        var text = String(record, fieldNames);
        return System.DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }

    public static DateTimeOffset? Date(JsonElement record, params string[] fieldNames)
    {
        var text = String(record, fieldNames);
        return System.DateTime.TryParseExact(
            text,
            ["yyyy-MM-dd", "dd.MM.yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var value)
            ? new System.DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : DateTimeOffset(record, fieldNames);
    }

    private static JsonElement? Find(JsonElement record, IReadOnlyCollection<string> fieldNames)
    {
        if (record.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var fieldName in fieldNames)
        {
            if (record.TryGetProperty(fieldName, out var value))
                return value;
        }

        return null;
    }

    private static string? StringFromObject(JsonElement value)
    {
        if (value.TryGetProperty("name", out var name)) return name.GetString();
        if (value.TryGetProperty("display_value", out var display)) return display.GetString();
        return null;
    }
}
