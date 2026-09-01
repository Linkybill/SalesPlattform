using System.Text.Json;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoSyncJobStartResult(
    Guid RunId,
    string Status);

public sealed record ZohoSyncModuleSnapshot(
    string Module,
    string Status,
    int RecordsRead,
    int RecordsWritten,
    int RecordsFailed,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error);

public sealed record ZohoSyncJobSnapshot(
    Guid RunId,
    string Status,
    IReadOnlyCollection<string> Modules,
    string? CurrentModule,
    int RecordsRead,
    int RecordsWritten,
    int RecordsFailed,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error,
    IReadOnlyCollection<ZohoSyncModuleSnapshot> Items);

internal sealed record ZohoSyncJobWorkItem(
    Guid RunId,
    Guid TenantId,
    string UserSubject);

internal static class ZohoSyncJobSnapshotMapper
{
    public static ZohoSyncJobSnapshot Map(IntegrationSyncRun run)
    {
        var modules = JsonSerializer.Deserialize<string[]>(run.RequestedModulesJson) ?? [];
        return new ZohoSyncJobSnapshot(
            run.Id,
            run.Status,
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
                .Select(item => new ZohoSyncModuleSnapshot(
                    item.Module,
                    item.Status,
                    item.RecordsRead,
                    item.RecordsWritten,
                    item.RecordsFailed,
                    item.StartedAt,
                    item.FinishedAt,
                    item.Error))
                .ToArray());
    }
}
