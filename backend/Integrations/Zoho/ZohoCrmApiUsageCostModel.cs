using System.Globalization;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Zoho;

/// <summary>
/// Zoho CRM's documented credit costs for the endpoints used by this app.
/// Unknown endpoints deliberately fall back to Zoho's documented default of
/// one credit so a future Zoho feature remains visible until its exact cost is
/// added here.
/// </summary>
public sealed class ZohoCrmApiUsageCostModel : ICrmApiUsageCostModel
{
    public string ProviderKey => CrmProviders.Zoho;

    public CrmApiUsageCost Estimate(CrmApiUsageRequest request)
    {
        var endpoint = request.Endpoint.ToLowerInvariant();
        if (endpoint.Contains("/actions/watch", StringComparison.Ordinal))
            return new CrmApiUsageCost(1, "credits");
        if (endpoint.EndsWith("/deleted", StringComparison.Ordinal))
            return new CrmApiUsageCost(2, "credits");
        if (endpoint.Contains("/send_mail", StringComparison.Ordinal))
            return new CrmApiUsageCost(20, "credits");
        if (endpoint.Contains("/convert", StringComparison.Ordinal))
            return new CrmApiUsageCost(5, "credits");
        if (request.RecordsAffected is > 0
            && request.HttpMethod is "POST" or "PUT" or "PATCH")
        {
            return new CrmApiUsageCost(
                Math.Max(1, (request.RecordsAffected.Value + 9L) / 10L),
                "credits");
        }

        return new CrmApiUsageCost(1, "credits");
    }

    public static string NormalizeEndpoint(string path)
    {
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        var rawPath = queryIndex >= 0 ? path[..queryIndex] : path;
        var segments = rawPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => IsExternalId(segment) ? "{id}" : segment)
            .ToArray();
        var normalized = "/" + string.Join('/', segments);
        if (queryIndex < 0)
            return normalized;

        var query = path[(queryIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Equals("module", StringComparison.OrdinalIgnoreCase))
            .Select(pair => $"module={pair[1]}")
            .ToArray();
        return query.Length == 0 ? normalized : $"{normalized}?{string.Join('&', query)}";
    }

    public static string Classify(string httpMethod, string path)
    {
        var endpoint = path.ToLowerInvariant();
        if (endpoint.Contains("/actions/watch", StringComparison.Ordinal))
            return CrmApiUsageCategories.Subscriptions;
        if (endpoint.Contains("/settings/modules", StringComparison.Ordinal)
            || endpoint.Contains("/settings/fields", StringComparison.Ordinal))
            return CrmApiUsageCategories.Schema;
        if (endpoint.EndsWith("/deleted", StringComparison.Ordinal))
            return CrmApiUsageCategories.Deletes;
        if (endpoint.EndsWith("/org", StringComparison.Ordinal))
            return CrmApiUsageCategories.Organization;
        if (endpoint.Contains("/emails", StringComparison.Ordinal)
            || endpoint.Contains("stage_history", StringComparison.Ordinal)
            || endpoint.Contains("related_lists", StringComparison.Ordinal))
            return CrmApiUsageCategories.RelatedRecords;
        if (httpMethod is "POST" or "PUT" or "PATCH" or "DELETE")
            return CrmApiUsageCategories.Writes;
        if (endpoint.Contains("/settings/", StringComparison.Ordinal))
            return CrmApiUsageCategories.Schema;
        if (endpoint.StartsWith("/crm/", StringComparison.Ordinal))
            return CrmApiUsageCategories.Records;
        return CrmApiUsageCategories.Other;
    }

    private static bool IsExternalId(string segment)
        => Guid.TryParse(segment, out _)
            || (segment.Length >= 12
                && segment.All(char.IsDigit)
                && long.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _));
}
