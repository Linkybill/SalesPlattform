namespace SalesPlattform.Backend.Integrations.Abstractions;

public static class CrmApiUsageCategories
{
    public const string Authentication = "authentication";
    public const string Schema = "schema";
    public const string Records = "records";
    public const string Deletes = "deletes";
    public const string RelatedRecords = "related-records";
    public const string Writes = "writes";
    public const string Organization = "organization";
    public const string Subscriptions = "subscriptions";
    public const string Other = "other";
}

public static class CrmApiUsageOrigins
{
    public const string Job = "job";
    public const string UserInterface = "user-interface";
    public const string System = "system";
    public const string Unknown = "unknown";
}

/// <summary>
/// Provider-neutral observation of one actual CRM HTTP attempt. The adapter
/// supplies provider-specific values such as the remaining credit header and
/// may leave the estimated cost empty when its provider has no metering model.
/// </summary>
public sealed record CrmApiUsageObservation(
    string ProviderKey,
    string ConnectionKey,
    string HttpMethod,
    string Endpoint,
    string Operation,
    string Category,
    int? StatusCode,
    bool Succeeded,
    bool Retryable,
    long? EstimatedUnits = null,
    string? UsageUnit = null,
    int? ProviderUnitsRemaining = null,
    int? ProviderUnitsLimit = null,
    int? RecordsAffected = null,
    long DurationMilliseconds = 0,
    DateTimeOffset? OccurredAt = null);

public sealed record CrmApiUsageRequest(
    string HttpMethod,
    string Endpoint,
    int? StatusCode,
    int? RecordsAffected);

public sealed record CrmApiUsageCost(
    long Units,
    string UnitName);

/// <summary>
/// A CRM provider can register its own cost model. This keeps Zoho credits,
/// Salesforce requests, HubSpot windows, and future provider quotas out of the
/// provider-neutral synchronization code.
/// </summary>
public interface ICrmApiUsageCostModel
{
    string ProviderKey { get; }

    CrmApiUsageCost Estimate(CrmApiUsageRequest request);
}

public interface ICrmApiUsageRecorder
{
    IDisposable BeginScope(
        Guid tenantId,
        Guid? runId = null,
        string? requestedBy = null,
        string? origin = null,
        string? correlationId = null);

    void Record(CrmApiUsageObservation observation);

    CrmApiUsagePendingSummary GetPendingSummary();

    Task FlushAsync(CancellationToken cancellationToken = default);
}

public sealed record CrmApiUsagePendingSummary(
    long Requests,
    long EstimatedUnits,
    IReadOnlyDictionary<string, long> UnitsByName,
    long FailedRequests,
    long RetryableRequests,
    DateTimeOffset? LastObservedAt,
    IReadOnlyDictionary<string, long> RequestsByCategory);

public sealed record CrmApiUsageReport(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    long Requests,
    long SuccessfulRequests,
    long FailedRequests,
    long RetryableRequests,
    IReadOnlyDictionary<string, long> UnitsByName,
    IReadOnlyCollection<CrmApiUsageProviderReport> Providers,
    IReadOnlyCollection<CrmApiUsageScopeSummary> Scopes);

public sealed record CrmApiUsageScopeSummary(
    Guid? RunId,
    string? JobName,
    string Origin,
    string? RequestedBy,
    string? CorrelationId,
    string? RunMode,
    string? RunStatus,
    string? CurrentModule,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    long Requests,
    long SuccessfulRequests,
    long FailedRequests,
    long RetryableRequests,
    IReadOnlyDictionary<string, long> UnitsByName);

public sealed record CrmApiUsageCallPage(
    long Total,
    int Offset,
    int Limit,
    IReadOnlyCollection<CrmApiUsageCall> Calls);

public sealed record CrmApiUsageCall(
    Guid Id,
    Guid? RunId,
    string Origin,
    string? RequestedBy,
    string? CorrelationId,
    string ProviderKey,
    string ConnectionKey,
    string HttpMethod,
    string Endpoint,
    string Operation,
    string Category,
    int? StatusCode,
    bool Succeeded,
    bool Retryable,
    long EstimatedUnits,
    string UsageUnit,
    int? ProviderUnitsRemaining,
    DateTimeOffset OccurredAt,
    long DurationMilliseconds);

public sealed record CrmApiUsageProviderReport(
    string ProviderKey,
    string ConnectionKey,
    long Requests,
    long SuccessfulRequests,
    long FailedRequests,
    long RetryableRequests,
    long EstimatedUnits,
    IReadOnlyDictionary<string, long> UnitsByName,
    int? LatestProviderUnitsRemaining,
    int? LatestProviderUnitsLimit,
    string? LatestProviderUnitName,
    DateTimeOffset? LatestProviderObservationAt,
    IReadOnlyCollection<CrmApiUsageBreakdown> Breakdown);

public sealed record CrmApiUsageBreakdown(
    string Category,
    string Operation,
    string HttpMethod,
    string Endpoint,
    string UsageUnit,
    long Requests,
    long SuccessfulRequests,
    long FailedRequests,
    long EstimatedUnits);
