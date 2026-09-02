using System.Text.Json;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Integrations.Zoho;

internal sealed record ZohoSynchronizationModuleSnapshot(
    string Module,
    string Status,
    int RecordsRead,
    int RecordsWritten,
    int RecordsFailed,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error,
    IReadOnlyCollection<ZohoSynchronizationErrorSnapshot> Errors);

internal sealed record ZohoSynchronizationErrorSnapshot(
    string? ExternalId,
    string ErrorCode,
    string Message,
    bool Retryable,
    DateTimeOffset OccurredAt);

internal sealed record ZohoSynchronizationWrittenRecordSnapshot(
    string EntityType,
    string ExternalId,
    string PayloadJson,
    DateTimeOffset? ExternalModifiedAt,
    DateTimeOffset SyncedAt);

internal sealed record ZohoSynchronizationSnapshot(
    Guid RunId,
    string Status,
    string Mode,
    IReadOnlyCollection<string> Modules,
    string? CurrentModule,
    int RecordsRead,
    int RecordsWritten,
    int RecordsFailed,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error,
    IReadOnlyCollection<ZohoSynchronizationModuleSnapshot> Items,
    IReadOnlyCollection<ZohoSynchronizationWrittenRecordSnapshot> WrittenRecords);

internal sealed record ZohoSynchronizationWorkItem(
    Guid RunId,
    Guid TenantId,
    string UserSubject);

internal static class ZohoSynchronizationSnapshotMapper
{
    public static ZohoSynchronizationSnapshot Map(
        IntegrationSyncRun run,
        IReadOnlyCollection<ZohoSynchronizationWrittenRecordSnapshot>? writtenRecords = null)
    {
        var modules = JsonSerializer.Deserialize<string[]>(run.RequestedModulesJson) ?? [];
        return new ZohoSynchronizationSnapshot(
            run.Id,
            run.Status,
            run.Mode,
            modules,
            run.CurrentModule,
            run.RecordsRead,
            run.RecordsWritten,
            run.RecordsFailed,
            run.QueuedAt,
            run.StartedAt,
            run.FinishedAt,
            run.Error,
            run.Items
                .OrderBy(item => item.Module)
                .Select(item => new ZohoSynchronizationModuleSnapshot(
                    item.Module,
                    item.Status,
                    item.RecordsRead,
                    item.RecordsWritten,
                    item.RecordsFailed,
                    item.StartedAt,
                    item.FinishedAt,
                    item.Error,
                    item.Errors
                        .OrderByDescending(error => error.OccurredAt)
                        .Take(5)
                        .Select(error => new ZohoSynchronizationErrorSnapshot(
                            error.ExternalId,
                            error.ErrorCode,
                            error.Message,
                            error.Retryable,
                            error.OccurredAt))
                        .ToArray()))
                .ToArray(),
            writtenRecords ?? []);
    }
}
