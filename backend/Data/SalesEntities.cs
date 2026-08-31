using IdentityPlatform.Shared.Database;

namespace SalesPlattform.Backend.Data;

public sealed class SalesCustomer : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Industry { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? OwnerExternalId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public ICollection<SalesDeal> Deals { get; set; } = [];
}

public sealed class SalesContact : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
}

public sealed class SalesLead : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? CompanyName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Status { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset? LastContactAt { get; set; }
    public int CallAttempts { get; set; }
    public string? OwnerExternalId { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
}

public sealed class SalesProduct : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Category { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
}

public sealed class SalesPipeline : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SalesPipelineStage : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public Guid PipelineId { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public decimal? Probability { get; set; }
}

public sealed class SalesDeal : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }
    public required string Name { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? PipelineKey { get; set; }
    public string? StageKey { get; set; }
    public string? ProductName { get; set; }
    public decimal? DurationMonths { get; set; }
    public DateTimeOffset? ContractEndAt { get; set; }
    public DateTimeOffset? ClosingAt { get; set; }
    public string? Status { get; set; }
    public string? LossReason { get; set; }
    public string? OwnerExternalId { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public SalesCustomer? Customer { get; set; }
}

public sealed class SalesDealStageHistory : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public Guid DealId { get; set; }
    public required string StageKey { get; set; }
    public DateTimeOffset EnteredAt { get; set; }
    public DateTimeOffset? ExitedAt { get; set; }
}

public sealed class SalesActivity : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public required string ActivityType { get; set; }
    public string? Subject { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Direction { get; set; }
    public string? Result { get; set; }
    public string? OwnerExternalId { get; set; }
    public string? RelatedEntityType { get; set; }
    public string? RelatedExternalId { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
}

public sealed class SalesAppointment : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public string? Subject { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public required string Status { get; set; }
    public string? AppointmentType { get; set; }
    public string? OwnerExternalId { get; set; }
    public string? RelatedEntityType { get; set; }
    public string? RelatedExternalId { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
}
