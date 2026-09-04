using System.ComponentModel.DataAnnotations.Schema;
using IdentityPlatform.Shared.Database;

namespace SalesPlattform.Backend.Data;

public abstract class SalesEntity : PlatformTenantEntity
{
    public Guid Id { get; set; }
}

public sealed class SalesOwner : SalesEntity
{
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }

    public ICollection<SalesTeamMember> TeamMemberships { get; set; } = [];
    public ICollection<SalesCustomer> Customers { get; set; } = [];
    public ICollection<SalesLead> Leads { get; set; } = [];
    public ICollection<SalesDeal> Deals { get; set; } = [];
    public ICollection<SalesContract> Contracts { get; set; } = [];
    public ICollection<SalesActivity> Activities { get; set; } = [];
    public ICollection<SalesAppointment> Appointments { get; set; } = [];
    public ICollection<SalesWorkItem> WorkItems { get; set; } = [];
    public ICollection<SalesTarget> Targets { get; set; } = [];
}

public sealed class SalesTeam : SalesEntity
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<SalesTeamMember> Members { get; set; } = [];
}

public sealed class SalesTeamMember : SalesEntity
{
    public Guid TeamId { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public bool IsPrimary { get; set; }

    public SalesTeam? Team { get; set; }
    public SalesOwner? Owner { get; set; }
}

public sealed class SalesCustomer : SalesEntity
{
    public required string Name { get; set; }
    public string? LegalName { get; set; }
    public string? TaxNumber { get; set; }
    public string? WebsiteDomain { get; set; }
    public string? Industry { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? RegionCode { get; set; }
    public string? CountryCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? HouseNumber { get; set; }
    public Guid? OwnerId { get; set; }
    /// <summary>
    /// Platform-side history anchor for the current CRM owner. It is updated
    /// when the synchronized owner changes and is used by the owner-change
    /// rule; the CRM remains the source of the owner itself.
    /// </summary>
    public DateTimeOffset? OwnerAssignedAt { get; set; }
    public string Status { get; set; } = "unknown";
    public DateTimeOffset? LastContactAt { get; set; }
    public DateTimeOffset? LastPhoneCallAt { get; set; }
    public decimal? LifetimeRevenue { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NeedsReview { get; set; }
    public string? GeocodingStatus { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }

    public SalesOwner? Owner { get; set; }
    public ICollection<SalesLead> Leads { get; set; } = [];
    public ICollection<SalesDeal> Deals { get; set; } = [];
    public ICollection<SalesContract> Contracts { get; set; } = [];
    public ICollection<SalesCustomerStatusHistory> StatusHistory { get; set; } = [];

    [NotMapped]
    [Obsolete("Country wird durch CountryCode ersetzt.")]
    public string? Country
    {
        get => CountryCode;
        set => CountryCode = value;
    }

    [NotMapped]
    [Obsolete("OwnerExternalId wird durch OwnerId und IntegrationEntityLink ersetzt.")]
    public string? OwnerExternalId { get; set; }
}

public sealed class SalesCustomerStatusHistory : SalesEntity
{
    public Guid CustomerId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }

    public SalesCustomer? Customer { get; set; }
}

public sealed class SalesLead : SalesEntity
{
    public Guid? CustomerId { get; set; }
    public Guid? OwnerId { get; set; }
    public required string Name { get; set; }
    public string? CompanyName { get; set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public string? Phone { get; set; }
    public string? NormalizedPhone { get; set; }
    public string Status { get; set; } = "new";
    public string? Source { get; set; }
    public DateTimeOffset? LastContactAt { get; set; }
    public DateTimeOffset? LastPhoneCallAt { get; set; }
    public DateTimeOffset? ResponseDueAt { get; set; }
    public int CallsSinceConversation { get; set; }
    public int TotalCallAttempts { get; set; }
    public DateTimeOffset? FirstActivityAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NeedsReview { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }

    public SalesCustomer? Customer { get; set; }
    public SalesOwner? Owner { get; set; }

    [NotMapped]
    [Obsolete("OwnerExternalId wird durch OwnerId und IntegrationEntityLink ersetzt.")]
    public string? OwnerExternalId { get; set; }

    [NotMapped]
    [Obsolete("CallAttempts wird durch TotalCallAttempts ersetzt.")]
    public int CallAttempts
    {
        get => TotalCallAttempts;
        set => TotalCallAttempts = value;
    }
}

public sealed class SalesProductCategory : SalesEntity
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public ICollection<SalesProduct> Products { get; set; } = [];
}

public sealed class SalesProduct : SalesEntity
{
    public Guid? CategoryId { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }

    public SalesProductCategory? Category { get; set; }
    public ICollection<SalesDeal> Deals { get; set; } = [];
    public ICollection<SalesContract> Contracts { get; set; } = [];
}

public sealed class SalesPipeline : SalesEntity
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }

    public ICollection<SalesPipelineStage> Stages { get; set; } = [];
    public ICollection<SalesDeal> Deals { get; set; } = [];
    public ICollection<SalesDealStageHistory> StageHistory { get; set; } = [];
}

public sealed class SalesPipelineStage : SalesEntity
{
    public Guid PipelineId { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string StageType { get; set; }
    public int SortOrder { get; set; }
    public decimal? Probability { get; set; }
    public bool IsTerminal { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? SourceModifiedAt { get; set; }

    public SalesPipeline? Pipeline { get; set; }
    public ICollection<SalesDeal> Deals { get; set; } = [];
    public ICollection<SalesDealStageHistory> StageHistory { get; set; } = [];
}

public sealed class SalesDeal : SalesEntity
{
    public Guid? CustomerId { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? PipelineId { get; set; }
    public Guid? PipelineStageId { get; set; }
    public Guid? ProductId { get; set; }
    public required string Name { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string Status { get; set; } = "open";
    public string? LossReason { get; set; }
    public decimal? DurationMonths { get; set; }
    public DateTimeOffset? ContractStartAt { get; set; }
    public DateTimeOffset? ContractEndAt { get; set; }
    public DateTimeOffset? ClosingAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NeedsReview { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }

    public SalesCustomer? Customer { get; set; }
    public SalesOwner? Owner { get; set; }
    public SalesPipeline? Pipeline { get; set; }
    public SalesPipelineStage? PipelineStage { get; set; }
    public SalesProduct? Product { get; set; }
    public ICollection<SalesContract> Contracts { get; set; } = [];
    public ICollection<SalesDealStageHistory> StageHistory { get; set; } = [];

    [NotMapped]
    [Obsolete("PipelineKey wird durch PipelineId und Integration-Pipeline-Mapping ersetzt.")]
    public string? PipelineKey { get; set; }

    [NotMapped]
    [Obsolete("StageKey wird durch PipelineStageId und Integration-Stage-Mapping ersetzt.")]
    public string? StageKey { get; set; }

    [NotMapped]
    [Obsolete("ProductName wird durch ProductId und IntegrationEntityLink ersetzt.")]
    public string? ProductName { get; set; }

    [NotMapped]
    [Obsolete("OwnerExternalId wird durch OwnerId und IntegrationEntityLink ersetzt.")]
    public string? OwnerExternalId { get; set; }
}

public sealed class SalesContract : SalesEntity
{
    public Guid CustomerId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? OwnerId { get; set; }
    public string? ContractNumber { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public decimal? DurationMonths { get; set; }
    public decimal? RecurringAmount { get; set; }
    public string? Currency { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }

    public SalesCustomer? Customer { get; set; }
    public SalesDeal? Deal { get; set; }
    public SalesProduct? Product { get; set; }
    public SalesOwner? Owner { get; set; }
}

public sealed class SalesDealStageHistory : SalesEntity
{
    public Guid DealId { get; set; }
    public Guid? PipelineId { get; set; }
    public Guid? PipelineStageId { get; set; }
    public required string StageKeySnapshot { get; set; }
    public DateTimeOffset EnteredAt { get; set; }
    public DateTimeOffset? ExitedAt { get; set; }
    public DateTimeOffset? SourceObservedAt { get; set; }
    public string? SourceEventKey { get; set; }

    public SalesDeal? Deal { get; set; }
    public SalesPipeline? Pipeline { get; set; }
    public SalesPipelineStage? PipelineStage { get; set; }

    [NotMapped]
    [Obsolete("StageKey wird durch StageKeySnapshot ersetzt.")]
    public string StageKey
    {
        get => StageKeySnapshot;
        set => StageKeySnapshot = value;
    }
}

public sealed class SalesActivity : SalesEntity
{
    public required string ActivityType { get; set; }
    public string? Subject { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Direction { get; set; }
    public string? ConnectionStatus { get; set; }
    public string? ConversationClass { get; set; }
    public bool? CountsAsConversation { get; set; }
    public string? Result { get; set; }
    public Guid? OwnerId { get; set; }
    public bool IsCorrected { get; set; }
    public string? CorrectionNote { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }

    public SalesOwner? Owner { get; set; }
    public ICollection<SalesActivityRelation> Relations { get; set; } = [];

    [NotMapped]
    [Obsolete("OwnerExternalId wird durch OwnerId und IntegrationEntityLink ersetzt.")]
    public string? OwnerExternalId { get; set; }

    [NotMapped]
    [Obsolete("RelatedEntityType wird durch SalesActivityRelation ersetzt.")]
    public string? RelatedEntityType { get; set; }

    [NotMapped]
    [Obsolete("RelatedExternalId wird durch SalesActivityRelation ersetzt.")]
    public string? RelatedExternalId { get; set; }
}

public sealed class SalesActivityRelation : SalesEntity
{
    public Guid ActivityId { get; set; }
    public required string TargetType { get; set; }
    public Guid TargetId { get; set; }
    public string? RelationRole { get; set; }

    public SalesActivity? Activity { get; set; }
}

public sealed class SalesAppointment : SalesEntity
{
    public string? Subject { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public required string Status { get; set; }
    public string? AppointmentType { get; set; }
    public Guid? OwnerId { get; set; }
    public DateTimeOffset? OriginalStartsAt { get; set; }
    public int RescheduleCount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }

    public SalesOwner? Owner { get; set; }
    public ICollection<SalesAppointmentRelation> Relations { get; set; } = [];
    public ICollection<SalesAppointmentStatusHistory> StatusHistory { get; set; } = [];

    [NotMapped]
    [Obsolete("OwnerExternalId wird durch OwnerId und IntegrationEntityLink ersetzt.")]
    public string? OwnerExternalId { get; set; }

    [NotMapped]
    [Obsolete("RelatedEntityType wird durch SalesAppointmentRelation ersetzt.")]
    public string? RelatedEntityType { get; set; }

    [NotMapped]
    [Obsolete("RelatedExternalId wird durch SalesAppointmentRelation ersetzt.")]
    public string? RelatedExternalId { get; set; }
}

public sealed class SalesAppointmentRelation : SalesEntity
{
    public Guid AppointmentId { get; set; }
    public required string TargetType { get; set; }
    public Guid TargetId { get; set; }
    public string? RelationRole { get; set; }

    public SalesAppointment? Appointment { get; set; }
}

public sealed class SalesAppointmentStatusHistory : SalesEntity
{
    public Guid AppointmentId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public DateTimeOffset? OriginalStartsAt { get; set; }
    public string? Source { get; set; }
    public string? Notes { get; set; }

    public SalesAppointment? Appointment { get; set; }
}

public sealed class SalesServiceCase : SalesEntity
{
    public Guid? CustomerId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? OwnerId { get; set; }
    public required string Subject { get; set; }
    public string? Description { get; set; }
    public required string Status { get; set; }
    public required string Priority { get; set; }
    public string? Origin { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public SalesCustomer? Customer { get; set; }
    public SalesDeal? Deal { get; set; }
    public SalesOwner? Owner { get; set; }
}

public sealed class SalesOffer : SalesEntity
{
    public Guid? CustomerId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? OwnerId { get; set; }
    public required string Name { get; set; }
    public string? OfferNumber { get; set; }
    public required string Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public SalesCustomer? Customer { get; set; }
    public SalesDeal? Deal { get; set; }
    public SalesOwner? Owner { get; set; }
    public ICollection<SalesOrder> Orders { get; set; } = [];
}

public sealed class SalesOrder : SalesEntity
{
    public Guid? CustomerId { get; set; }
    public Guid? OfferId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? OwnerId { get; set; }
    public required string Name { get; set; }
    public string? OrderNumber { get; set; }
    public required string Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? OrderedAt { get; set; }
    public DateTimeOffset? PromisedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public SalesCustomer? Customer { get; set; }
    public SalesOffer? Offer { get; set; }
    public SalesDeal? Deal { get; set; }
    public SalesOwner? Owner { get; set; }
    public ICollection<SalesInvoice> Invoices { get; set; } = [];
}

public sealed class SalesInvoice : SalesEntity
{
    public Guid? CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? OwnerId { get; set; }
    public required string Name { get; set; }
    public string? InvoiceNumber { get; set; }
    public required string Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? OpenAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? SourceModifiedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public SalesCustomer? Customer { get; set; }
    public SalesOrder? Order { get; set; }
    public SalesDeal? Deal { get; set; }
    public SalesOwner? Owner { get; set; }
}
