using System.Text.Json;

namespace SalesPlattform.Backend.Integrations.Abstractions;

public static class CrmProviders
{
    public const string Zoho = "zoho";
}

public static class CrmSyncModes
{
    public const string Full = "full";
    public const string Incremental = "incremental";
}

public static class CrmEntityTypes
{
    public const string Owner = "owner";
    public const string Customer = "customer";
    public const string Contact = "contact";
    public const string Lead = "lead";
    public const string ProductCategory = "product-category";
    public const string Deal = "deal";
    public const string Product = "product";
    public const string Pipeline = "pipeline";
    public const string PipelineStage = "pipeline-stage";
    public const string DealStageHistory = "deal-stage-history";
    public const string Contract = "contract";
    public const string Activity = "activity";
    public const string Appointment = "appointment";
    public const string ServiceCase = "service-case";
    public const string Offer = "offer";
    public const string Order = "order";
    public const string Invoice = "invoice";
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
    DateTimeOffset? ModifiedAt,
    IReadOnlyCollection<CrmRecordRelation>? Relations = null,
    string ConnectionKey = "default",
    string? ExternalUrl = null);

public sealed record CrmDeletedRecord(
    string Provider,
    string Module,
    string EntityType,
    string ExternalId,
    DateTimeOffset DeletedAt,
    string ConnectionKey = "default");

public sealed record CrmRecordRelation(
    string EntityType,
    string ExternalId,
    string? Role = null);

public sealed record CrmTaskWriteRequest(
    string Subject,
    DateTimeOffset? DueAt,
    string? Description,
    string? OwnerExternalId,
    string? TargetEntityType,
    string? TargetExternalId);

public sealed record CrmTaskWriteResult(
    string Provider,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    string? ExternalUrl = null);

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
        DateTimeOffset? modifiedSince = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CrmDeletedRecord>> GetDeletedRecordsAsync(
        string module,
        DateTimeOffset? deletedSince = null,
        CancellationToken cancellationToken = default);

    Task<CrmTaskWriteResult> CreateTaskAsync(
        CrmTaskWriteRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CrmExternalRecord>> GetRelatedRecordsAsync(
        string parentModule,
        string parentExternalId,
        string relatedList,
        IReadOnlyCollection<string> fields,
        DateTimeOffset? modifiedSince = null,
        CancellationToken cancellationToken = default);
}
