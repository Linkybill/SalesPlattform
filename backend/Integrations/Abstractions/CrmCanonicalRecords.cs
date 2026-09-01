using System.Text.Json;

namespace SalesPlattform.Backend.Integrations.Abstractions;

public abstract record CrmCanonicalRecord(
    string ProviderKey,
    string ConnectionKey,
    string EntityType,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt);

public sealed record CrmCanonicalCustomer(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string Name,
    string? Industry,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? Status)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Customer, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalDeal(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string Name,
    string? CustomerExternalId,
    decimal? Amount,
    string? Currency,
    string? PipelineKey,
    string? StageKey,
    string? ProductName,
    decimal? DurationMonths,
    DateTimeOffset? ContractEndAt,
    DateTimeOffset? ClosingAt,
    string? Status,
    string? LossReason,
    DateTimeOffset? LastActivityAt)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Deal, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalLead(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string Name,
    string? CompanyName,
    string? Email,
    string? Phone,
    string? Status,
    string? Source,
    DateTimeOffset? LastContactAt,
    int? TotalCallAttempts)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Lead, ExternalId, Payload, CreatedAt, ModifiedAt);
