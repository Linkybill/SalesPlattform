using System.Text.Json;

namespace SalesPlattform.Backend.Integrations.Abstractions;

public sealed record CrmSynchronizationRequest(
    Guid RunId,
    Guid TenantId,
    string Mode,
    string RequestedBy,
    IReadOnlyCollection<string>? RequestedModules = null);

public sealed record CrmSynchronizationProgress(
    string? Step,
    string? Message,
    long RecordsRead,
    long RecordsWritten,
    long RecordsFailed,
    JsonElement? Details = null);

public sealed record CrmSynchronizationResult(
    string ProviderKey,
    string Mode,
    long RecordsRead,
    long RecordsWritten,
    long RecordsFailed,
    string? Message,
    JsonElement? Details = null)
{
    public bool HasWarnings => RecordsFailed > 0;
}

public interface ICrmSynchronizationProgressSink
{
    Task ReportAsync(
        CrmSynchronizationProgress progress,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// High-level provider adapter. Platform jobs and schedules call only the
/// provider-neutral orchestrator; provider-specific paging, modules, mapping
/// and OAuth remain behind this boundary.
/// </summary>
public interface ICrmSynchronizationAdapter
{
    string ProviderKey { get; }

    Task<CrmSynchronizationResult> SynchronizeAsync(
        CrmSynchronizationRequest request,
        ICrmSynchronizationProgressSink progress,
        CancellationToken cancellationToken = default);
}
