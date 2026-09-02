using Npgsql;

namespace SalesPlattform.Backend.Integrations;

internal static class IntegrationErrorFormatter
{
    public static string Describe(Exception exception, int maxLength = 4000)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                var details = new List<string>
                {
                    postgres.MessageText,
                    $"SQLSTATE={postgres.SqlState}"
                };
                Add(details, "Schema", postgres.SchemaName);
                Add(details, "Tabelle", postgres.TableName);
                Add(details, "Spalte", postgres.ColumnName);
                Add(details, "Constraint", postgres.ConstraintName);
                Add(details, "Detail", postgres.Detail);
                Add(details, "Hinweis", postgres.Hint);
                AddUnique(parts, string.Join("; ", details));
            }
            else
            {
                AddUnique(parts, current.Message);
            }
        }

        var message = string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return message.Length <= maxLength ? message : message[..maxLength];
    }

    private static void Add(ICollection<string> values, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values.Add($"{name}={value}");
    }

    private static void AddUnique(ICollection<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
            values.Add(value);
    }
}
