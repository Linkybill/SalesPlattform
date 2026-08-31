using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoCrmAdapter(
    IHttpClientFactory httpClientFactory,
    ZohoTokenService tokenService) : ICrmAdapter
{
    public string ProviderKey => CrmProviders.Zoho;

    public async Task<CrmConnectionTestResult> TestConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var modules = await GetModulesAsync(cancellationToken);
        var token = await tokenService.GetAccessTokenAsync(cancellationToken);
        return new CrmConnectionTestResult(ProviderKey, true, token.ApiDomain, modules);
    }

    public async Task<IReadOnlyCollection<string>> GetModulesAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "/crm/v8/settings/modules",
            cancellationToken);
        using var document = await ParseDocumentAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("modules", out var modules)
            || modules.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return modules.EnumerateArray()
            .Select(module => module.TryGetProperty("api_name", out var apiName)
                ? apiName.GetString()
                : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<CrmFieldMetadata>> GetFieldsAsync(
        string module,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/crm/v8/settings/fields?module={Uri.EscapeDataString(module)}",
            cancellationToken);
        using var document = await ParseDocumentAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("fields", out var fields)
            || fields.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return fields.EnumerateArray()
            .Select(field => new CrmFieldMetadata(
                GetString(field, "api_name") ?? string.Empty,
                GetString(field, "field_label") ?? GetString(field, "display_label"),
                GetString(field, "data_type")))
            .Where(field => !string.IsNullOrWhiteSpace(field.ApiName))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<CrmExternalRecord>> GetRecordsAsync(
        string module,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default)
    {
        if (fields.Count == 0)
            throw new ArgumentException("Mindestens ein Zoho-Feld ist erforderlich.", nameof(fields));

        var records = new List<CrmExternalRecord>();
        var page = 1;
        const int pageSize = 200;
        while (true)
        {
            var query = $"/crm/v8/{Uri.EscapeDataString(module)}"
                + $"?fields={Uri.EscapeDataString(string.Join(',', fields.Take(50)))}"
                + $"&page={page}&per_page={pageSize}";
            using var response = await SendAsync(HttpMethod.Get, query, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                break;

            using var document = await ParseDocumentAsync(response, cancellationToken);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
                break;

            var pageRecords = data.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out _))
                .Select(item => new CrmExternalRecord(
                    ProviderKey,
                    module,
                    item.GetProperty("id").GetString()!,
                    item.Clone(),
                    ZohoFieldReader.DateTimeOffset(item, "Modified_Time", "ModifiedTime")))
                .ToArray();
            records.AddRange(pageRecords);
            if (pageRecords.Length < pageSize)
                break;
            page++;
        }

        return records;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        var token = await tokenService.GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(
            method,
            $"{token.ApiDomain.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", token.Value);
        var response = await httpClientFactory.CreateClient().SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NoContent)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException(
                $"Zoho CRM antwortete auf {path} mit HTTP {statusCode}: {body}");
        }

        return response;
    }

    private static async Task<JsonDocument> ParseDocumentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            ? property.GetString()
            : null;
}
