using IdentityPlatform.Shared.Jobs;
using SalesPlattform.Backend.Services;

namespace SalesPlattform.Backend.Integrations.Abstractions;

/// <summary>
/// The canonical business-change boundary of the SalesPlattform. A provider
/// adapter maps its own payload into <see cref="CrmSynchronizationChange"/>
/// and then hands the change to the same business pipeline used by crawls,
/// hooks and future CRM providers.
/// </summary>
public sealed record CrmBusinessChangeRequest(
    string ProviderKey,
    string ConnectionKey,
    string Mode,
    IReadOnlyCollection<CrmSynchronizationChange> Changes,
    string? RequestedBy = null);

public sealed record CrmBusinessChangeResult(
    WorklistEvaluationResult Evaluation,
    CrmTaskMirrorResult TaskMirror,
    SalesSnapshotResult Snapshot,
    SalesNotificationDeliveryResult Notifications);

/// <summary>
/// Provider-neutral application service for the business consequences of CRM
/// changes. It deliberately knows nothing about Zoho payloads or webhook
/// registration. Any CRM adapter can consume this service after mapping its
/// records to the canonical SalesPlattform model.
/// </summary>
public interface ICrmBusinessChangeProcessor
{
    Task<CrmBusinessChangeResult> ProcessAsync(
        PlatformJobExecutionContext context,
        CrmBusinessChangeRequest request,
        CancellationToken cancellationToken = default);
}
