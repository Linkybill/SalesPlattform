using System.Reflection;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
static MethodInfo Method(string name) => typeof(CrmDailyUsageService).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
DateTimeOffset Start(int days, DateTimeOffset now) => (DateTimeOffset)Method("WindowStart").Invoke(null, [days, now])!;
IQueryable<CrmDailyUsageAggregate> Query(IQueryable<IntegrationApiUsageEvent> rows, DateTimeOffset from, DateTimeOffset to)
    => (IQueryable<CrmDailyUsageAggregate>)Method("AggregateQuery").Invoke(null, [rows, from, to])!;
CrmDailyUsageReport Report(IEnumerable<CrmDailyUsageAggregate> rows, DateTimeOffset from, DateTimeOffset to)
    => (CrmDailyUsageReport)Method("BuildReport").Invoke(null, [rows, from, to])!;
var end = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
var start = Start(7, end);
Check(start == new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero), "Seven calendar days must include today.");
Check(Start(1, end).Hour == 0 && Start(1, end).Day == 19, "One day must start at UTC midnight.");
Check(Start(7, end.ToOffset(TimeSpan.FromHours(5.5))) == start, "Browser/offset must not move UTC bucket boundaries.");
foreach (var length in new[] { 0, -1, 91, int.MaxValue })
{
    try { Start(length, end); throw new Exception("Unbounded date range accepted."); }
    catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException) { checks++; }
}
IntegrationApiUsageEvent Event(DateTimeOffset at, long units = 1, bool success = true, string provider = "zoho", string connection = "default", string unit = "credits")
    => new() { Id = Guid.NewGuid(), ProviderKey = provider, ConnectionKey = connection, HttpMethod = "GET",
        Endpoint = "/crm/v8/Tasks", Operation = "read", Category = "records", UsageUnit = unit,
        OccurredAt = at, EstimatedUnits = units, Succeeded = success };
var events = new[] {
    Event(start.AddTicks(-1), 900), // outside window
    Event(start, 2), Event(start.AddHours(1), 3, false),
    Event(start.AddDays(1).AddTicks(-1), 4),
    Event(start.AddDays(1), 5),
    Event(start.AddDays(2).AddMinutes(30).ToOffset(TimeSpan.FromHours(-5)), 6),
    Event(end.AddTicks(-1), 7), Event(end, 900), Event(end.AddHours(1), 900),
    Event(start, 99, provider: "another"), Event(start, 88, connection: "secondary"),
    Event(start, 77, unit: "requests")
};
var buckets = Query(events.AsQueryable(), start, end).ToArray();
var report = Report(buckets, start, end);
Check(report.Series.Count == 4, "Providers, connections and units must remain separate.");
var series = report.Series.Single(x => x.ProviderKey == "zoho" && x.ConnectionKey == "default" && x.UsageUnit == "credits");
Check(series.Days.Count == 7, "Missing dates must be filled, not omitted.");
Check(series.Days[0] is { Requests: 3, SuccessfulRequests: 2, FailedRequests: 1, EstimatedUnits: 9 }, "Daily counts and unit sum wrong.");
Check(series.Days[1].EstimatedUnits == 5 && series.Days[2].EstimatedUnits == 6, "Midnight/offset event assigned to wrong day.");
Check(series.Days[3] is { Requests: 0, EstimatedUnits: 0 }, "No-event days must show recorded zero.");
Check(series.Days.Sum(x => x.EstimatedUnits) == 27, "Window bounds or series isolation wrong.");
Check(series.Days.Count(x => x.IsPartial) == 1 && series.Days[^1].IsPartial, "Only today must be marked partial.");
Check(series.Days.All(x => x.Requests == x.SuccessfulRequests + x.FailedRequests), "Status totals inconsistent.");
Check(Report([], start, end).Series.Count == 0, "Empty source must not invent provider usage.");
var yearEnd = new DateTimeOffset(2027, 1, 1, 1, 0, 0, TimeSpan.Zero);
Check(Start(7, yearEnd).Year == 2026, "Year boundary broken.");
var ninetyStart = Start(90, end);
Check(Report(Query(new[] { Event(end.AddMinutes(-1)) }.AsQueryable(), ninetyStart, end), ninetyStart, end).Series[0].Days.Count == 90,
    "Ninety-day calendar broken.");

// Compile the real production expression with Npgsql and Shared tenant filters, without a DB connection.
var options = new DbContextOptionsBuilder<SalesPlattformDbContext>().UseNpgsql("Host=unused.invalid;Database=synthetic;Username=test").Options;
using var db = new SalesPlattformDbContext(options);
var tenantA = Guid.NewGuid();
((IPlatformTenantDbContext)db).SetPlatformTenant(tenantA, true);
var sql = Query(db.IntegrationApiUsageEvents.AsNoTracking(), start, end).ToQueryString();
Check(sql.Contains("GROUP BY") && sql.Contains("sum(") && sql.Contains("count("), "Aggregation must execute server-side.");
Check(sql.Contains("UTC") || sql.Contains("AT TIME ZONE 'UTC'"), "SQL must explicitly group UTC dates.");
Check(sql.Contains(tenantA.ToString()), "Shared tenant query filter missing.");
((IPlatformTenantDbContext)db).SetPlatformTenant(Guid.NewGuid(), true);
Check(!Query(db.IntegrationApiUsageEvents, start, end).ToQueryString().Contains(tenantA.ToString()), "Tenant switch retained previous filter value.");
Check(!sql.Contains("requested_by", StringComparison.OrdinalIgnoreCase) && !sql.Contains("endpoint", StringComparison.OrdinalIgnoreCase), "Daily aggregate must not load raw call details.");
Console.WriteLine($"Daily usage: {checks} checks passed (synthetic data and real PostgreSQL SQL translation; no database/CRM calls).");
