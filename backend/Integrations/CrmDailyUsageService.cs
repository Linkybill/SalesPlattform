using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Integrations;

public sealed record CrmDailyUsageDay(DateOnly Date, long Requests, long SuccessfulRequests,
    long FailedRequests, long EstimatedUnits, bool IsPartial);
public sealed record CrmDailyUsageSeries(string ProviderKey, string ConnectionKey, string UsageUnit,
    IReadOnlyList<CrmDailyUsageDay> Days);
public sealed record CrmDailyUsageReport(DateTimeOffset FromUtc, DateTimeOffset ToUtc, string TimeZone,
    IReadOnlyList<CrmDailyUsageSeries> Series);
public sealed record CrmDailyUsageAggregate(DateOnly Day, string ProviderKey, string ConnectionKey,
    string UsageUnit, long Requests, long SuccessfulRequests, long FailedRequests, long EstimatedUnits);

/// <summary>Bounded calendar-day aggregates; raw HTTP events never leave the database.</summary>
public sealed class CrmDailyUsageService(PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory)
{
    public async Task<CrmDailyUsageReport> GetAsync(int days, CancellationToken cancellationToken)
    {
        var toUtc = DateTimeOffset.UtcNow;
        var fromUtc = WindowStart(days, toUtc);
        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var rows = await AggregateQuery(session.Context.IntegrationApiUsageEvents.AsNoTracking(), fromUtc, toUtc)
            .ToArrayAsync(cancellationToken);
        return BuildReport(rows, fromUtc, toUtc);
    }

    internal static DateTimeOffset WindowStart(int days, DateTimeOffset now)
    {
        if (days is < 1 or > 90) throw new ArgumentException("Der Tageszeitraum muss zwischen 1 und 90 Tagen liegen.");
        return new DateTimeOffset(now.UtcDateTime.Date.AddDays(1 - days), TimeSpan.Zero);
    }

    internal static IQueryable<CrmDailyUsageAggregate> AggregateQuery(IQueryable<IntegrationApiUsageEvent> events,
        DateTimeOffset fromUtc, DateTimeOffset toUtc)
        => events.Where(x => x.OccurredAt >= fromUtc && x.OccurredAt < toUtc)
            .GroupBy(x => new { Day = DateOnly.FromDateTime(x.OccurredAt.UtcDateTime), x.ProviderKey, x.ConnectionKey, x.UsageUnit })
            .Select(g => new CrmDailyUsageAggregate(g.Key.Day, g.Key.ProviderKey, g.Key.ConnectionKey,
                g.Key.UsageUnit, g.LongCount(), g.Sum(x => x.Succeeded ? 1L : 0L),
                g.Sum(x => x.Succeeded ? 0L : 1L), g.Sum(x => x.EstimatedUnits)));

    internal static CrmDailyUsageReport BuildReport(IEnumerable<CrmDailyUsageAggregate> rows,
        DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var first = DateOnly.FromDateTime(fromUtc.UtcDateTime);
        var today = DateOnly.FromDateTime(toUtc.UtcDateTime);
        var calendar = Enumerable.Range(0, today.DayNumber - first.DayNumber + 1).Select(first.AddDays).ToArray();
        var series = rows.GroupBy(x => new { x.ProviderKey, x.ConnectionKey, x.UsageUnit })
            .OrderBy(g => g.Key.ProviderKey).ThenBy(g => g.Key.ConnectionKey).ThenBy(g => g.Key.UsageUnit)
            .Select(g =>
            {
                var byDate = g.ToDictionary(x => x.Day);
                return new CrmDailyUsageSeries(g.Key.ProviderKey, g.Key.ConnectionKey, g.Key.UsageUnit,
                    calendar.Select(day =>
                    {
                        byDate.TryGetValue(day, out var row);
                        return new CrmDailyUsageDay(day, row?.Requests ?? 0, row?.SuccessfulRequests ?? 0,
                            row?.FailedRequests ?? 0, row?.EstimatedUnits ?? 0, day == today);
                    }).ToArray());
            }).ToArray();
        return new(fromUtc, toUtc, "UTC", series);
    }
}
