using System.Security.Claims;
using System.Threading;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations;

/// <summary>
/// Collects provider-neutral CRM API observations for the current request or
/// platform job and persists them as append-only events. Adapter code only
/// reports actual HTTP attempts; provider-specific quota rules are supplied by
/// an <see cref="ICrmApiUsageCostModel"/>.
/// </summary>
public sealed class CrmApiUsageRecorder(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    IEnumerable<ICrmApiUsageCostModel> costModels,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CrmApiUsageRecorder> logger) : ICrmApiUsageRecorder
{
    private readonly object sync = new();
    private readonly List<PendingObservation> observations = [];
    private readonly IReadOnlyDictionary<string, ICrmApiUsageCostModel> registeredCostModels =
        costModels.ToDictionary(model => model.ProviderKey, StringComparer.OrdinalIgnoreCase);
    private readonly AsyncLocal<UsageScope?> currentScope = new();

    public IDisposable BeginScope(
        Guid tenantId,
        Guid? runId = null,
        string? requestedBy = null,
        string? origin = null,
        string? correlationId = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Für die CRM-Verbrauchsmessung ist eine gültige Tenant-ID erforderlich.", nameof(tenantId));

        var previous = currentScope.Value;
        currentScope.Value = new UsageScope(
            tenantId,
            runId,
            requestedBy,
            string.IsNullOrWhiteSpace(origin)
                ? runId is not null ? CrmApiUsageOrigins.Job : CrmApiUsageOrigins.Unknown
                : origin,
            correlationId);
        return new ScopeHandle(() => currentScope.Value = previous);
    }

    public void Record(CrmApiUsageObservation observation)
    {
        var scope = currentScope.Value ?? ResolveHttpScope();
        if (scope is null)
        {
            logger.LogWarning(
                "CRM-API-Verbrauch konnte nicht tenantbezogen erfasst werden: {Provider} {Endpoint}",
                observation.ProviderKey,
                observation.Endpoint);
            return;
        }

        var endpoint = string.IsNullOrWhiteSpace(observation.Endpoint)
            ? "/"
            : observation.Endpoint;
        var cost = observation.EstimatedUnits is not null
            ? new CrmApiUsageCost(
                observation.EstimatedUnits.Value,
                string.IsNullOrWhiteSpace(observation.UsageUnit) ? "units" : observation.UsageUnit)
            : (registeredCostModels.TryGetValue(observation.ProviderKey, out var costModel)
                ? costModel.Estimate(new CrmApiUsageRequest(
                    observation.HttpMethod,
                    endpoint,
                    observation.StatusCode,
                    observation.RecordsAffected))
                : new CrmApiUsageCost(1, "requests"));

        var occurredAt = (observation.OccurredAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var pending = new PendingObservation(
            scope.TenantId,
            scope.RunId,
            scope.Origin,
            scope.RequestedBy,
            scope.CorrelationId,
            new CrmApiUsageObservation(
                observation.ProviderKey,
                string.IsNullOrWhiteSpace(observation.ConnectionKey) ? "default" : observation.ConnectionKey,
                observation.HttpMethod,
                endpoint,
                string.IsNullOrWhiteSpace(observation.Operation)
                    ? $"{observation.HttpMethod} {endpoint}"
                    : observation.Operation,
                string.IsNullOrWhiteSpace(observation.Category)
                    ? CrmApiUsageCategories.Other
                    : observation.Category,
                observation.StatusCode,
                observation.Succeeded,
                observation.Retryable,
                EstimatedUnits: Math.Max(0, cost.Units),
                UsageUnit: string.IsNullOrWhiteSpace(cost.UnitName) ? "units" : cost.UnitName,
                ProviderUnitsRemaining: observation.ProviderUnitsRemaining,
                ProviderUnitsLimit: observation.ProviderUnitsLimit,
                RecordsAffected: observation.RecordsAffected,
                DurationMilliseconds: Math.Max(0, observation.DurationMilliseconds),
                OccurredAt: occurredAt));

        lock (sync)
        {
            observations.Add(pending);
        }
    }

    public CrmApiUsagePendingSummary GetPendingSummary()
    {
        PendingObservation[] pending;
        lock (sync)
        {
            pending = observations.ToArray();
        }

        var unitsByName = pending
            .GroupBy(item => item.Observation.UsageUnit ?? "units", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Observation.EstimatedUnits ?? 0), StringComparer.OrdinalIgnoreCase);
        return new CrmApiUsagePendingSummary(
            pending.LongLength,
            pending.Sum(item => item.Observation.EstimatedUnits ?? 0),
            unitsByName,
            pending.LongCount(item => !item.Observation.Succeeded),
            pending.LongCount(item => item.Observation.Retryable),
            pending.Length == 0
                ? null
                : pending.Max(item => item.Observation.OccurredAt),
            pending
                .GroupBy(item => item.Observation.Category, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.LongCount(), StringComparer.OrdinalIgnoreCase));
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        PendingObservation[] pending;
        lock (sync)
        {
            if (observations.Count == 0)
                return;
            pending = observations.ToArray();
            observations.Clear();
        }

        try
        {
            await using var session = await dbFactory.OpenAsync(cancellationToken);
            session.Context.IntegrationApiUsageEvents.AddRange(pending.Select(item => new IntegrationApiUsageEvent
            {
                Id = Guid.NewGuid(),
                TenantId = item.TenantId,
                RunId = item.RunId,
                Origin = item.Origin,
                RequestedBy = item.RequestedBy,
                CorrelationId = item.CorrelationId,
                ProviderKey = item.Observation.ProviderKey,
                ConnectionKey = item.Observation.ConnectionKey,
                HttpMethod = item.Observation.HttpMethod,
                Endpoint = item.Observation.Endpoint,
                Operation = item.Observation.Operation,
                Category = item.Observation.Category,
                StatusCode = item.Observation.StatusCode,
                Succeeded = item.Observation.Succeeded,
                Retryable = item.Observation.Retryable,
                EstimatedUnits = item.Observation.EstimatedUnits ?? 0,
                UsageUnit = item.Observation.UsageUnit ?? "units",
                ProviderUnitsRemaining = item.Observation.ProviderUnitsRemaining,
                ProviderUnitsLimit = item.Observation.ProviderUnitsLimit,
                RecordsAffected = item.Observation.RecordsAffected,
                DurationMilliseconds = item.Observation.DurationMilliseconds,
                OccurredAt = item.Observation.OccurredAt ?? DateTimeOffset.UtcNow
            }));
            await session.Context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            lock (sync)
            {
                observations.InsertRange(0, pending);
            }
            throw;
        }
    }

    private UsageScope? ResolveHttpScope()
    {
        var user = httpContextAccessor.HttpContext?.User;
        return Guid.TryParse(user?.FindFirstValue("tenant_id"), out var tenantId)
            && tenantId != Guid.Empty
            ? new UsageScope(
                tenantId,
                null,
                user?.FindFirstValue("sub"),
                CrmApiUsageOrigins.UserInterface,
                httpContextAccessor.HttpContext?.TraceIdentifier)
            : null;
    }

    private sealed record UsageScope(
        Guid TenantId,
        Guid? RunId,
        string? RequestedBy,
        string Origin,
        string? CorrelationId);

    private sealed record PendingObservation(
        Guid TenantId,
        Guid? RunId,
        string Origin,
        string? RequestedBy,
        string? CorrelationId,
        CrmApiUsageObservation Observation);

    private sealed class ScopeHandle(Action dispose) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                dispose();
        }
    }
}

public sealed class CrmApiUsageService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory)
{
    public async Task<CrmApiUsageReport> GetAsync(
        int hours = 24,
        CancellationToken cancellationToken = default)
    {
        hours = Math.Clamp(hours, 1, 168);
        var toUtc = DateTimeOffset.UtcNow;
        var fromUtc = toUtc.Subtract(TimeSpan.FromHours(hours));
        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var events = await session.Context.IntegrationApiUsageEvents
            .AsNoTracking()
            .Where(item => item.OccurredAt >= fromUtc && item.OccurredAt <= toUtc)
            .Select(item => new UsageEventProjection(
                item.Id,
                item.RunId,
                item.Origin,
                item.RequestedBy,
                item.CorrelationId,
                item.ProviderKey,
                item.ConnectionKey,
                item.HttpMethod,
                item.Endpoint,
                item.Operation,
                item.Category,
                item.StatusCode,
                item.Succeeded,
                item.Retryable,
                item.EstimatedUnits,
                item.UsageUnit,
                item.ProviderUnitsRemaining,
                item.ProviderUnitsLimit,
                item.DurationMilliseconds,
                item.OccurredAt))
            .ToArrayAsync(cancellationToken);

        var runIds = events
            .Where(item => item.RunId is not null)
            .Select(item => item.RunId!.Value)
            .Distinct()
            .ToArray();
        var runs = runIds.Length == 0
            ? new Dictionary<Guid, RunProjection>()
            : await session.Context.IntegrationSyncRuns
                .AsNoTracking()
                .Where(item => runIds.Contains(item.Id))
                .Select(item => new RunProjection(
                    item.Id,
                    item.Mode,
                    item.Status,
                    item.CurrentModule))
                .ToDictionaryAsync(item => item.Id, cancellationToken);

        var providers = events
            .GroupBy(item => new { item.ProviderKey, item.ConnectionKey })
            .Select(group => new CrmApiUsageProviderReport(
                group.Key.ProviderKey,
                group.Key.ConnectionKey,
                group.LongCount(),
                group.LongCount(item => item.Succeeded),
                group.LongCount(item => !item.Succeeded),
                group.LongCount(item => item.Retryable),
                group.Sum(item => item.EstimatedUnits),
                group
                    .GroupBy(item => item.UsageUnit, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(unitGroup => unitGroup.Key, unitGroup => unitGroup.Sum(item => item.EstimatedUnits), StringComparer.OrdinalIgnoreCase),
                group
                    .Where(item => item.ProviderUnitsRemaining is not null)
                    .OrderByDescending(item => item.OccurredAt)
                    .Select(item => item.ProviderUnitsRemaining)
                    .FirstOrDefault(),
                group
                    .Where(item => item.ProviderUnitsLimit is not null)
                    .OrderByDescending(item => item.OccurredAt)
                    .Select(item => item.ProviderUnitsLimit)
                    .FirstOrDefault(),
                group
                    .Where(item => item.ProviderUnitsRemaining is not null)
                    .OrderByDescending(item => item.OccurredAt)
                    .Select(item => item.UsageUnit)
                    .FirstOrDefault(),
                group
                    .Where(item => item.ProviderUnitsRemaining is not null)
                    .OrderByDescending(item => item.OccurredAt)
                    .Select(item => (DateTimeOffset?)item.OccurredAt)
                    .FirstOrDefault(),
                group
                    .GroupBy(item => new { item.Category, item.Operation, item.HttpMethod, item.Endpoint, item.UsageUnit })
                    .Select(breakdown => new CrmApiUsageBreakdown(
                        breakdown.Key.Category,
                        breakdown.Key.Operation,
                        breakdown.Key.HttpMethod,
                        breakdown.Key.Endpoint,
                        breakdown.Key.UsageUnit,
                        breakdown.LongCount(),
                        breakdown.LongCount(item => item.Succeeded),
                        breakdown.LongCount(item => !item.Succeeded),
                        breakdown.Sum(item => item.EstimatedUnits)))
                    .OrderByDescending(item => item.EstimatedUnits)
                    .ThenBy(item => item.Endpoint)
                    .Take(100)
                    .ToArray()))
            .OrderByDescending(item => item.EstimatedUnits)
            .ToArray();

        var scopes = events
            .GroupBy(item => new
            {
                item.RunId,
                item.Origin,
                item.RequestedBy,
                item.CorrelationId
            })
            .Select(group =>
            {
                var run = group.Key.RunId is Guid runId && runs.TryGetValue(runId, out var runProjection)
                    ? runProjection
                    : null;
                var jobName = ResolveJobName(run?.Mode, group);
                return new CrmApiUsageScopeSummary(
                    group.Key.RunId,
                    jobName,
                    group.Key.Origin,
                    group.Key.RequestedBy,
                    group.Key.CorrelationId,
                    run?.Mode,
                    run?.Status,
                    run?.CurrentModule,
                    group.Min(item => item.OccurredAt),
                    group.Max(item => item.OccurredAt),
                    group.LongCount(),
                    group.LongCount(item => item.Succeeded),
                    group.LongCount(item => !item.Succeeded),
                    group.LongCount(item => item.Retryable),
                    group
                        .GroupBy(item => item.UsageUnit, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(unitGroup => unitGroup.Key, unitGroup => unitGroup.Sum(item => item.EstimatedUnits), StringComparer.OrdinalIgnoreCase));
            })
            .OrderByDescending(item => item.LastObservedAt)
            .ToArray();

        return new CrmApiUsageReport(
            fromUtc,
            toUtc,
            events.LongLength,
            events.LongCount(item => item.Succeeded),
            events.LongCount(item => !item.Succeeded),
            events.LongCount(item => item.Retryable),
            events
                .GroupBy(item => item.UsageUnit, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.EstimatedUnits), StringComparer.OrdinalIgnoreCase),
            providers,
            scopes);
    }

    private static string? ResolveJobName(
        string? runMode,
        IEnumerable<UsageEventProjection> events)
        => runMode?.ToLowerInvariant() switch
        {
            "full" => "CRM-Vollimport",
            "incremental" => "CRM-Änderungen synchronisieren",
            _ when events.Any(item => string.Equals(
                item.Category,
                CrmApiUsageCategories.Schema,
                StringComparison.OrdinalIgnoreCase)) => "Zoho-Schema cachen",
            _ => null
        };

    public async Task<CrmApiUsageCallPage> GetCallsAsync(
        int hours = 24,
        Guid? runId = null,
        string? origin = null,
        string? requestedBy = null,
        string? correlationId = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        hours = Math.Clamp(hours, 1, 168);
        offset = Math.Clamp(offset, 0, 1_000_000);
        limit = Math.Clamp(limit, 1, 250);
        var toUtc = DateTimeOffset.UtcNow;
        var fromUtc = toUtc.Subtract(TimeSpan.FromHours(hours));

        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var query = session.Context.IntegrationApiUsageEvents
            .AsNoTracking()
            .Where(item => item.OccurredAt >= fromUtc && item.OccurredAt <= toUtc);

        query = runId is not null
            ? query.Where(item => item.RunId == runId)
            : query.Where(item => item.RunId == null);
        if (!string.IsNullOrWhiteSpace(origin))
            query = query.Where(item => item.Origin == origin);
        if (!string.IsNullOrWhiteSpace(requestedBy))
            query = query.Where(item => item.RequestedBy == requestedBy);
        if (!string.IsNullOrWhiteSpace(correlationId))
            query = query.Where(item => item.CorrelationId == correlationId);

        var total = await query.LongCountAsync(cancellationToken);
        var calls = await query
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Skip(offset)
            .Take(limit)
            .Select(item => new CrmApiUsageCall(
                item.Id,
                item.RunId,
                item.Origin,
                item.RequestedBy,
                item.CorrelationId,
                item.ProviderKey,
                item.ConnectionKey,
                item.HttpMethod,
                item.Endpoint,
                item.Operation,
                item.Category,
                item.StatusCode,
                item.Succeeded,
                item.Retryable,
                item.EstimatedUnits,
                item.UsageUnit,
                item.ProviderUnitsRemaining,
                item.OccurredAt,
                item.DurationMilliseconds))
            .ToArrayAsync(cancellationToken);

        return new CrmApiUsageCallPage(total, offset, limit, calls);
    }

    private sealed record UsageEventProjection(
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
        int? ProviderUnitsLimit,
        long DurationMilliseconds,
        DateTimeOffset OccurredAt);

    private sealed record RunProjection(
        Guid Id,
        string Mode,
        string Status,
        string? CurrentModule);
}
