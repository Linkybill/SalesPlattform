namespace SalesPlattform.Backend.Data;

public sealed class SalesSnapshotRun : SalesEntity
{
    public DateOnly SnapshotDate { get; set; }
    public required string SnapshotType { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }

    public ICollection<SalesKpiSnapshot> KpiSnapshots { get; set; } = [];
    public ICollection<SalesPipelineSnapshot> PipelineSnapshots { get; set; } = [];
    public ICollection<SalesActivitySnapshot> ActivitySnapshots { get; set; } = [];
    public ICollection<SalesCustomerStatusSnapshot> CustomerStatusSnapshots { get; set; } = [];
}

public sealed class SalesPipelineSnapshot : SalesEntity
{
    public Guid SnapshotRunId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public Guid PipelineId { get; set; }
    public Guid PipelineStageId { get; set; }
    public Guid? OwnerId { get; set; }
    public long OpenDealCount { get; set; }
    public decimal? OpenAmount { get; set; }
    public decimal? WeightedAmount { get; set; }
    public string? Currency { get; set; }

    public SalesSnapshotRun? SnapshotRun { get; set; }
    public SalesPipeline? Pipeline { get; set; }
    public SalesPipelineStage? PipelineStage { get; set; }
    public SalesOwner? Owner { get; set; }
}

public sealed class SalesKpiSnapshot : SalesEntity
{
    public Guid SnapshotRunId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public required string PeriodType { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public required string MetricKey { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? PipelineId { get; set; }
    public Guid? ProductCategoryId { get; set; }
    public string? Industry { get; set; }
    public string? CountryCode { get; set; }
    public string? PostalRegion { get; set; }
    public decimal? Value { get; set; }
    public long? CountValue { get; set; }
    public decimal? Numerator { get; set; }
    public decimal? Denominator { get; set; }
    public string? Currency { get; set; }
    public string? DetailsJson { get; set; }

    public SalesSnapshotRun? SnapshotRun { get; set; }
    public SalesOwner? Owner { get; set; }
    public SalesPipeline? Pipeline { get; set; }
    public SalesProductCategory? ProductCategory { get; set; }
}

public sealed class SalesActivitySnapshot : SalesEntity
{
    public Guid SnapshotRunId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public Guid? OwnerId { get; set; }
    public string? ActivityType { get; set; }
    public long PlannedCount { get; set; }
    public long CompletedCount { get; set; }
    public long CancelledCount { get; set; }
    public long RescheduledCount { get; set; }
    public long NoShowCount { get; set; }
    public long ReachedCallCount { get; set; }
    public long UnreachedCallCount { get; set; }

    public SalesSnapshotRun? SnapshotRun { get; set; }
    public SalesOwner? Owner { get; set; }
}

public sealed class SalesCustomerStatusSnapshot : SalesEntity
{
    public Guid SnapshotRunId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public required string Status { get; set; }
    public long ActiveCount { get; set; }
    public long AddedCount { get; set; }
    public long LostCount { get; set; }
    public decimal? LifetimeRevenue { get; set; }

    public SalesSnapshotRun? SnapshotRun { get; set; }
}

public sealed class SalesDataQualityFinding : SalesEntity
{
    public required string Code { get; set; }
    public required string Severity { get; set; }
    public required string Status { get; set; }
    public required string EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? FieldName { get; set; }
    public required string Message { get; set; }
    public string? DetailsJson { get; set; }
    public required string Fingerprint { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? LastDetectedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
}

public sealed class SalesDuplicateCandidate : SalesEntity
{
    public Guid CustomerAId { get; set; }
    public Guid CustomerBId { get; set; }
    public decimal Score { get; set; }
    public required string Confidence { get; set; }
    public string? MatchDetailsJson { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }

    public SalesCustomer? CustomerA { get; set; }
    public SalesCustomer? CustomerB { get; set; }
    public ICollection<SalesDuplicateDecision> Decisions { get; set; } = [];
    public ICollection<SalesMergeOperation> MergeOperations { get; set; } = [];
}

public sealed class SalesDuplicateDecision : SalesEntity
{
    public Guid DuplicateCandidateId { get; set; }
    public required string Decision { get; set; }
    public required string DecidedBy { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public Guid? LeadingCustomerId { get; set; }
    public string? FieldSelectionsJson { get; set; }
    public string? Notes { get; set; }

    public SalesDuplicateCandidate? DuplicateCandidate { get; set; }
    public SalesCustomer? LeadingCustomer { get; set; }
}

public sealed class SalesMergeOperation : SalesEntity
{
    public Guid? DuplicateCandidateId { get; set; }
    public Guid SourceCustomerId { get; set; }
    public Guid TargetCustomerId { get; set; }
    public required string Status { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int TransferredDealCount { get; set; }
    public int TransferredActivityCount { get; set; }
    public int TransferredAppointmentCount { get; set; }
    public string? WritebackReference { get; set; }
    public string? Error { get; set; }

    public SalesDuplicateCandidate? DuplicateCandidate { get; set; }
    public SalesCustomer? SourceCustomer { get; set; }
    public SalesCustomer? TargetCustomer { get; set; }
}

public sealed class SalesOwnerChangeRequest : SalesEntity
{
    public required string TargetType { get; set; }
    public Guid TargetId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? OldOwnerId { get; set; }
    public Guid? ProposedOwnerId { get; set; }
    public string? SourceRuleCode { get; set; }
    public required string Reason { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public string? WritebackStatus { get; set; }

    public SalesCustomer? Customer { get; set; }
    public SalesOwner? OldOwner { get; set; }
    public SalesOwner? ProposedOwner { get; set; }
}

public sealed class SalesAuditLog : SalesEntity
{
    public string? ActorSubject { get; set; }
    public string? ActorDisplayName { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? CorrelationId { get; set; }
}
