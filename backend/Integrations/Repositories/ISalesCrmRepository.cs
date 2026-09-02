using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Repositories;

public interface ISalesCrmRepository
{
    Task<bool> HasActiveSyncRunAsync(
        string providerKey,
        string connectionKey,
        CancellationToken cancellationToken);

    void AddSyncRun(IntegrationSyncRun run);

    Task<IntegrationSyncRun?> GetSyncRunAsync(
        Guid runId,
        bool includeItems,
        bool asNoTracking,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<IntegrationSyncRun>> GetActiveSyncRunsAsync(
        string providerKey,
        string connectionKey,
        bool includeItems,
        bool asNoTracking,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<IntegrationSyncRun>> GetRecentSyncRunsAsync(
        string providerKey,
        string connectionKey,
        int limit,
        bool includeItems,
        CancellationToken cancellationToken);

    Task<IntegrationSyncRunItem> GetOrCreateSyncRunItemAsync(
        IntegrationSyncRun run,
        string module,
        CancellationToken cancellationToken);

    Task UpsertAsync(
        CrmCanonicalRecord record,
        Guid syncRunId,
        int callConversationThresholdSeconds,
        CancellationToken cancellationToken);

    Task<IntegrationSyncCursor> GetOrCreateCursorAsync(
        string providerKey,
        string connectionKey,
        string entityType,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetExternalIdsAsync(
        string providerKey,
        string connectionKey,
        string entityType,
        CancellationToken cancellationToken);

    Task<bool> MarkDeletedAsync(
        CrmDeletedRecord record,
        Guid syncRunId,
        CancellationToken cancellationToken);

    Task BackfillLeadActivityMarkersAsync(
        CancellationToken cancellationToken);

    Task RecalculateLeadCallCountersAsync(
        IReadOnlyCollection<CrmSynchronizationChange> changes,
        int callConversationThresholdSeconds,
        CancellationToken cancellationToken);

    void AddSyncError(
        Guid syncRunId,
        Guid syncRunItemId,
        string module,
        string? externalId,
        Exception exception);

    Task ClearSyncErrorsAsync(
        Guid syncRunId,
        CancellationToken cancellationToken);

    void DetachRecordChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ISalesCrmRepositoryFactory
{
    ISalesCrmRepository Create(SalesPlattformDbContext db);
}
