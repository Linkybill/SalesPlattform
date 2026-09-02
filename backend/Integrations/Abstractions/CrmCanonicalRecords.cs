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

public sealed record CrmCanonicalOwner(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string DisplayName,
    string? Email,
    bool IsActive)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Owner, ExternalId, Payload, CreatedAt, ModifiedAt);

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
    string? Status,
    string? LegalName,
    string? TaxNumber,
    string? WebsiteDomain,
    string? RegionCode,
    string? AddressLine1,
    string? HouseNumber,
    string? OwnerExternalId)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Customer, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalContact(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string Name,
    string? FirstName,
    string? LastName,
    string? CustomerExternalId,
    string? Email,
    string? Phone,
    string? MobilePhone,
    string? JobTitle,
    bool IsPrimary)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Contact, ExternalId, Payload, CreatedAt, ModifiedAt);

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
    DateTimeOffset? LastPhoneCallAt,
    int? CallsSinceConversation,
    int? TotalCallAttempts,
    string? CustomerExternalId,
    string? ContactExternalId,
    string? OwnerExternalId)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Lead, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalProductCategory(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string Key,
    string Name)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.ProductCategory, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalProduct(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string Key,
    string Name,
    string? Description,
    string? CategoryExternalId,
    string? CategoryName,
    bool IsActive)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Product, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalPipeline(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string Key,
    string Name,
    string? Description,
    int SortOrder)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Pipeline, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalPipelineStage(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string PipelineExternalId,
    string Key,
    string Name,
    string StageType,
    int SortOrder,
    decimal? Probability,
    bool IsTerminal)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.PipelineStage, ExternalId, Payload, CreatedAt, ModifiedAt);

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
    DateTimeOffset? ContractStartAt,
    DateTimeOffset? ContractEndAt,
    DateTimeOffset? ClosingAt,
    string? Status,
    string? LossReason,
    DateTimeOffset? LastActivityAt,
    string? OwnerExternalId,
    string? PipelineExternalId,
    string? StageExternalId,
    string? ProductExternalId)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Deal, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalDealStageHistory(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string DealExternalId,
    string? PipelineExternalId,
    string? StageExternalId,
    string StageKeySnapshot,
    DateTimeOffset EnteredAt,
    DateTimeOffset? ExitedAt)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.DealStageHistory, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalContract(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string CustomerExternalId,
    string DealExternalId,
    string? ProductExternalId,
    string? OwnerExternalId,
    string? ContractNumber,
    string Status,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    decimal? DurationMonths,
    decimal? RecurringAmount,
    string? Currency)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Contract, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalActivity(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string ActivityType,
    string? Subject,
    DateTimeOffset OccurredAt,
    int? DurationSeconds,
    string? Direction,
    string? ConnectionStatus,
    string? ConversationClass,
    bool? CountsAsConversation,
    string? Result,
    string? OwnerExternalId,
    IReadOnlyCollection<CrmRecordRelation> Relations)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Activity, ExternalId, Payload, CreatedAt, ModifiedAt);

public sealed record CrmCanonicalAppointment(
    string ProviderKey,
    string ConnectionKey,
    string ExternalId,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    string? Subject,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string? AppointmentType,
    string? OwnerExternalId,
    DateTimeOffset? OriginalStartsAt,
    int RescheduleCount,
    IReadOnlyCollection<CrmRecordRelation> Relations)
    : CrmCanonicalRecord(ProviderKey, ConnectionKey, CrmEntityTypes.Appointment, ExternalId, Payload, CreatedAt, ModifiedAt);
