using System.Text.Json;

namespace SalesPlattform.Backend.Integrations.Abstractions;

public static class CrmProviders
{
    public const string Zoho = "zoho";
}

public static class CrmEntityTypes
{
    public const string Customer = "customer";
    public const string Contact = "contact";
    public const string Lead = "lead";
    public const string Deal = "deal";
    public const string Product = "product";
    public const string Activity = "activity";
    public const string Appointment = "appointment";
}

public sealed record CrmConnectionTestResult(
    string Provider,
    bool Connected,
    string? ApiDomain,
    IReadOnlyCollection<string> AvailableModules,
    string? Error = null);

public sealed record CrmFieldMetadata(
    string ApiName,
    string? Label,
    string? DataType);

public sealed record CrmExternalRecord(
    string Provider,
    string Module,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? ModifiedAt);

public interface ICrmAdapter
{
    string ProviderKey { get; }

    Task<CrmConnectionTestResult> TestConnectionAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetModulesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CrmFieldMetadata>> GetFieldsAsync(
        string module,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CrmExternalRecord>> GetRecordsAsync(
        string module,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default);
}
