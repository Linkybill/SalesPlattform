namespace SalesPlattform.Backend.Data;

public sealed class IntegrationConnection : SalesEntity
{
    public required string ProviderKey { get; set; }
    public required string ConnectionKey { get; set; }
    public required string DisplayName { get; set; }
    public string? ExternalOrganizationId { get; set; }
    public required string ApiDomain { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset? LastTokenRefreshAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public bool IsActive { get; set; }
}

public sealed class IntegrationOAuthState : SalesEntity
{
    public required string ProviderKey { get; set; }
    public required string StateHash { get; set; }
    public required string UserSubject { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class IntegrationEntityLink : SalesEntity
{
    public required string ProviderKey { get; set; }
    public string ConnectionKey { get; set; } = "default";
    public required string EntityType { get; set; }
    public required string ExternalId { get; set; }
    public required string InternalEntityType { get; set; }
    public Guid InternalEntityId { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }
}

public sealed class IntegrationRawRecord : SalesEntity
{
    public required string ProviderKey { get; set; }
    public string ConnectionKey { get; set; } = "default";
    public required string EntityType { get; set; }
    public required string ExternalId { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset? ExternalModifiedAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? SourceDeletedAt { get; set; }
    public Guid? SyncRunId { get; set; }
    public DateTimeOffset SyncedAt { get; set; }

    public IntegrationSyncRun? SyncRun { get; set; }
}

public sealed class IntegrationSyncRun : SalesEntity
{
    public string ProviderKey { get; set; } = string.Empty;
    public string ConnectionKey { get; set; } = "default";
    public string Mode { get; set; } = "full";
    public string Status { get; set; } = "queued";
    public string RequestedModulesJson { get; set; } = "[]";
    public string? RequestedBy { get; set; }
    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? CurrentModule { get; set; }
    public int RecordsRead { get; set; }
    public int RecordsWritten { get; set; }
    public int RecordsFailed { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? WorkerId { get; set; }
    public string? Error { get; set; }
    public string? CorrelationId { get; set; }

    public ICollection<IntegrationRawRecord> RawRecords { get; set; } = [];
    public ICollection<IntegrationSyncRunItem> Items { get; set; } = [];
    public ICollection<IntegrationSyncError> Errors { get; set; } = [];
}

public sealed class IntegrationSyncRunItem : SalesEntity
{
    public Guid SyncRunId { get; set; }
    public required string Module { get; set; }
    public required string Status { get; set; }
    public string? Cursor { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int RecordsRead { get; set; }
    public int RecordsWritten { get; set; }
    public int RecordsFailed { get; set; }
    public string? Error { get; set; }

    public IntegrationSyncRun? SyncRun { get; set; }
    public ICollection<IntegrationSyncError> Errors { get; set; } = [];
}

public sealed class IntegrationSyncError : SalesEntity
{
    public Guid SyncRunId { get; set; }
    public Guid? SyncRunItemId { get; set; }
    public required string Module { get; set; }
    public string? ExternalId { get; set; }
    public required string ErrorCode { get; set; }
    public required string Message { get; set; }
    public bool Retryable { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? DetailsJson { get; set; }

    public IntegrationSyncRun? SyncRun { get; set; }
    public IntegrationSyncRunItem? SyncRunItem { get; set; }
}

public sealed class IntegrationSyncCursor : SalesEntity
{
    public required string ProviderKey { get; set; }
    public string ConnectionKey { get; set; } = "default";
    public required string EntityType { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
    public string? LastExternalId { get; set; }
    public Guid? LastSuccessfulRunId { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public IntegrationSyncRun? LastSuccessfulRun { get; set; }
}

public sealed class IntegrationFieldMapping : SalesEntity
{
    public required string ProviderKey { get; set; }
    public required string ConnectionKey { get; set; }
    public required string SourceEntityType { get; set; }
    public required string SourceField { get; set; }
    public required string TargetEntityType { get; set; }
    public required string TargetField { get; set; }
    public string? TransformationKey { get; set; }
    public bool IsRequired { get; set; }
    public string? ConfigurationJson { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class IntegrationPipelineMapping : SalesEntity
{
    public required string ProviderKey { get; set; }
    public required string ConnectionKey { get; set; }
    public required string ExternalPipelineId { get; set; }
    public Guid InternalPipelineId { get; set; }
    public string? SourceNameSnapshot { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LastSeenAt { get; set; }

    public SalesPipeline? InternalPipeline { get; set; }
}

public sealed class IntegrationStageMapping : SalesEntity
{
    public required string ProviderKey { get; set; }
    public required string ConnectionKey { get; set; }
    public required string ExternalPipelineId { get; set; }
    public required string ExternalStageId { get; set; }
    public Guid InternalPipelineId { get; set; }
    public Guid InternalStageId { get; set; }
    public string? SourceNameSnapshot { get; set; }
    public decimal? SourceProbability { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LastSeenAt { get; set; }

    public SalesPipeline? InternalPipeline { get; set; }
    public SalesPipelineStage? InternalStage { get; set; }
}

public sealed class IntegrationWritebackOperation : SalesEntity
{
    public required string ProviderKey { get; set; }
    public required string ConnectionKey { get; set; }
    public required string EntityType { get; set; }
    public Guid InternalEntityId { get; set; }
    public string? ExternalId { get; set; }
    public required string OperationType { get; set; }
    public required string Status { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}

public sealed class IntegrationWebhookEvent : SalesEntity
{
    public required string ProviderKey { get; set; }
    public required string ConnectionKey { get; set; }
    public required string EventType { get; set; }
    public string? ExternalEventId { get; set; }
    public required string PayloadJson { get; set; }
    public required string Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
}
