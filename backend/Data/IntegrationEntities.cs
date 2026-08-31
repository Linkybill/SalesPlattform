using IdentityPlatform.Shared.Database;

namespace SalesPlattform.Backend.Data;

public sealed class IntegrationConnection : PlatformTenantEntity
{
    public Guid Id { get; set; }
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

public sealed class IntegrationOAuthState : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public required string ProviderKey { get; set; }
    public required string StateHash { get; set; }
    public required string UserSubject { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class IntegrationEntityLink : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public required string ProviderKey { get; set; }
    public required string EntityType { get; set; }
    public required string ExternalId { get; set; }
    public required string InternalEntityType { get; set; }
    public Guid InternalEntityId { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class IntegrationRawRecord : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public required string ProviderKey { get; set; }
    public required string EntityType { get; set; }
    public required string ExternalId { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset? ExternalModifiedAt { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}

public sealed class IntegrationSyncRun : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public required string ProviderKey { get; set; }
    public required string Mode { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int RecordsRead { get; set; }
    public int RecordsWritten { get; set; }
    public int RecordsFailed { get; set; }
    public string? Error { get; set; }
}

public sealed class IntegrationSyncCursor : PlatformTenantEntity
{
    public Guid Id { get; set; }
    public required string ProviderKey { get; set; }
    public required string EntityType { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
    public string? LastExternalId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
