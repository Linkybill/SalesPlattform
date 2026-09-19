using System.Reflection;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Services;

// Real report projection, synthetic canonical entities, no database or CRM calls.
var assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}
var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
var owner = new SalesOwner { Id = Guid.NewGuid(), DisplayName = "Test Owner" };
var fiscalId = Guid.NewGuid();
var monthStart = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
var modelType = typeof(SalesReportService).GetNestedType("ReportModel", BindingFlags.NonPublic)!;
var periodType = typeof(SalesReportService).GetNestedType("ReportPeriod", BindingFlags.NonPublic)!;
var projection = typeof(SalesReportService).GetMethod("BuildEvidence", BindingFlags.Static | BindingFlags.NonPublic)!;
var period = Activator.CreateInstance(periodType, "Monat", monthStart, monthStart.AddMonths(1), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), fiscalId)!;
SalesReportEvidence Build(Dictionary<string, object>? values = null, bool sales = true, bool cleanup = true)
{
    values ??= [];
    values.TryAdd("Owners", new[] { owner });
    var ctor = modelType.GetConstructors().Single(c => c.GetParameters().Length > 1);
    var args = ctor.GetParameters().Select(p => values.GetValueOrDefault(p.Name!) ?? Array.CreateInstance(p.ParameterType.GetGenericArguments()[0], 0)).ToArray();
    return (SalesReportEvidence)projection.Invoke(null, [ctor.Invoke(args), period, now, 10, 14, sales, cleanup])!;
}
SalesDeal Deal(decimal amount, string status = "won", DateTimeOffset? closing = null) => new()
{
    Id = Guid.NewGuid(), Name = "Synthetic deal", OwnerId = owner.Id, Amount = amount, Currency = "EUR", Status = status,
    ClosingAt = closing ?? now, SourceCreatedAt = now.AddDays(-30), SourceModifiedAt = now
};
SalesReportRow Row(string key, decimal? amount, string? currency = "EUR") => new(key, "deal", key, null, null, "won", now, amount, currency, "", null);

var builder = new SalesEvidenceBuilder();
builder.Money("sum", "Sum", [Row("a", 10), Row("a", 10), Row("b", 20)], "month", "CRM", "sum");
Check(builder.Result.Metrics["sum"].Value == 30, "Money must sum unique rows, not duplicates.");
Check(builder.Result.Records.Count == 2, "Normalize repeated rows.");
builder.Money("mixed", "Mixed", [Row("c", 10), Row("d", 10, "USD")], "month", "CRM", "sum");
Check(builder.Result.Metrics["mixed"].Value is null, "No invented currency conversion.");
builder.Money("missing", "Missing", [Row("e", null)], "month", "CRM", "sum");
Check(builder.Result.Metrics["missing"].Value is null, "Unknown amount is not zero.");
builder.Money("currency", "Currency", [Row("f", 20, null)], "month", "CRM", "sum");
Check(builder.Result.Metrics["currency"].Value is null, "Unknown currency is not EUR.");

var empty = Build();
foreach (var key in new[] { "annual-target", "attainment", "coverage", "win-rate", $"owner:{owner.Id}:target", $"owner:{owner.Id}:pace" })
    Check(empty.Metrics[key].Value is null && empty.Metrics[key].UnavailableReason is not null, key + " must explain unavailable data.");
Check(empty.Metrics["won"].Value == 0, "No won deals legitimately means zero revenue.");

var september = Deal(100);
var january = Deal(200, closing: now.AddMonths(-8));
var lost = Deal(50, "lost");
var open = Deal(600, "open");
open.LastActivityAt = now.AddDays(-11);
var recent = Deal(40, "open");
recent.LastActivityAt = now.AddDays(-5);
var deleted = Deal(9999); deleted.SourceDeletedAt = now;
var goal = new SalesTarget { Id = Guid.NewGuid(), FiscalYearId = fiscalId, OwnerId = owner.Id, TargetType = "revenue", TargetValue = 1000, Currency = "EUR" };
var data = Build(new() { ["Deals"] = new[] { september, january, lost, open, recent, deleted }, ["Targets"] = new[] { goal } });
Check(data.Metrics["won"].Value == 100, "Selected period revenue excludes other months/deleted deals.");
Check(data.Metrics["fiscal-won"].Value == 300, "Target comparison uses whole fiscal year.");
Check(data.Metrics["attainment"].Value == 30, "Do not compare September revenue to annual target.");
Check(data.Metrics["win-rate"].Value == 50 && data.Metrics["win-rate"].RecordKeys.Length == 2, "Win-rate details must include won and lost denominator.");
Check(data.Metrics["stale"].RecordKeys.SequenceEqual(new[] { $"deal:{open.Id}" }), "Stale threshold must use supplied tenant configuration (10 days).");
Check(data.Metrics["coverage"].Value == Math.Round(640m / 700m, 2), "Coverage uses remaining fiscal-year target, expressed as multiple.");
Check(!data.Records.ContainsKey($"deal:{deleted.Id}"), "Deleted deals must not leak into evidence.");
Check(data.Metrics["cycle"].Value == 30, "Deal cycle requires actual creation and closing timestamps.");
foreach (var metric in data.Metrics.Values)
{
    Check(metric.RecordKeys.All(data.Records.ContainsKey), "Every evidence reference must resolve: " + metric.Key);
    if (metric.Unit == "money" && metric.Value.HasValue)
        Check(metric.Value == metric.RecordKeys.Sum(key => data.Records[key].Amount ?? 0), "Visible money must equal exact detail rows: " + metric.Key);
    if (metric.Unit == "count")
        Check(metric.Value == metric.RecordKeys.Length, "Visible count must equal unique detail rows: " + metric.Key);
}
goal.Currency = "USD";
Check(Build(new() { ["Deals"] = new[] { september }, ["Targets"] = new[] { goal } }).Metrics["attainment"].Value is null, "Different target/revenue currencies cannot be divided.");

var futureAppointment = new SalesAppointment { Id = Guid.NewGuid(), Subject = "Created now, takes place later", Status = "planned", StartsAt = now.AddMonths(2), EndsAt = now.AddMonths(2).AddHours(1), SourceCreatedAt = now, OwnerId = owner.Id };
var missed = new SalesAppointment { Id = Guid.NewGuid(), Subject = "No show", Status = "no-show", StartsAt = now, EndsAt = now.AddHours(1), SourceCreatedAt = now.AddMonths(-1), OwnerId = owner.Id };
var removed = new SalesAppointment { Id = Guid.NewGuid(), Status = "planned", StartsAt = now, SourceCreatedAt = now, SourceDeletedAt = now };
var meetings = Build(new() { ["Appointments"] = new[] { futureAppointment, missed, removed } });
Check(meetings.Metrics["meetings:new"].RecordKeys.SequenceEqual(new[] { $"appointment:{futureAppointment.Id}" }), "Newly created meetings must not be constrained by start date.");
Check(meetings.Metrics["meetings:planned"].Value == 1, "Selected meetings are based on start date, exclude deleted.");
Check(meetings.Metrics["meetings:no-show"].Value == 100, "No-show rate uses selected meetings as denominator.");
Check(meetings.Metrics["meetings:missed"].Value == 1, "Missing meetings list includes no-shows.");
Check(meetings.Metrics["meetings:week"].Value == 1, "Current week excludes future/deleted appointments.");
missed.Status = "Nicht stattgefunden";
Check(Build(new() { ["Appointments"] = new[] { missed } }).Metrics["meetings:completion"].Value == 0, "Nicht stattgefunden must not match stattgefunden and count as completed.");

var invoice = new SalesInvoice { Id = Guid.NewGuid(), Name = "Synthetic unpaid invoice", Status = "unpaid", Amount = 100, OpenAmount = 100, Currency = "EUR", IssuedAt = now, DueAt = now.AddDays(-1) };
var commercial = Build(new() { ["Invoices"] = new[] { invoice } });
Check(commercial.Metrics["commercial:outstanding"].Value == 100, "Unpaid must not match paid and hide outstanding debt.");
Check(commercial.Metrics["commercial:invoices-overdue"].Value == 1, "Unpaid overdue invoice must stay overdue.");

var cases = Enumerable.Range(0, 12).Select(i => new SalesServiceCase { Id = Guid.NewGuid(), Subject = $"Case {i}", Status = "open", Priority = "high", OpenedAt = now, DueAt = now.AddDays(-1) }).ToArray();
var service = Build(new() { ["ServiceCases"] = cases });
Check(service.Metrics["service:urgent"].Value == 12, "Urgent count cannot be capped to first 8 display rows.");
Check(service.Metrics["service:urgent"].RecordKeys.Length == 12, "All urgent records must be accessible.");

var products = Enumerable.Range(1, 10).Select(i => { var d = Deal(i); d.Product = new SalesProduct { Id = Guid.NewGuid(), Key = $"product-{i}", Name = $"Product {i}" }; return d; }).ToArray();
var productEvidence = Build(new() { ["Deals"] = products });
Check(productEvidence.Metrics["product:__other"].Value == 3, "Top 8 must expose the remaining two as Sonstige.");
Check(productEvidence.Metrics.Where(p => p.Key.StartsWith("product:")).Sum(p => p.Value.Value) == 55, "Product breakdown cannot silently lose revenue.");

var contract = new SalesContract { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = "active", StartAt = now.AddMonths(-1), EndAt = now.AddDays(13), RecurringAmount = 20, Currency = "EUR" };
var contracts = Build(new() { ["Contracts"] = new[] { contract } });
Check(contracts.Metrics["expiring"].Value == 1 && contracts.Metrics["expiring"].Label.Contains("14"), "Renewal horizon is supplied configuration.");
Check(contracts.Metrics["recurring"].Calculation.Contains("Kein belastbarer ARR"), "Unnormalised recurring amount must never be presented as reliable ARR.");
contract.EndAt = now.AddDays(15);
Check(Build(new() { ["Contracts"] = new[] { contract } }).Metrics["expiring"].Value == 0, "Contracts beyond configured horizon do not count.");

var restricted = Build(new() { ["Deals"] = new[] { september }, ["Appointments"] = new[] { missed } }, sales: false, cleanup: false);
Check(!restricted.Metrics.ContainsKey("won") && !restricted.Metrics.ContainsKey("meetings:week") && !restricted.Metrics.ContainsKey("quality-total"), "Evidence must honor the same report permissions.");
Check(restricted.Metrics.Keys.All(k => !k.StartsWith("customer:") && !k.StartsWith("product:")), "Restricted report details cannot be exposed under alternate keys.");
Console.WriteLine($"Report evidence: {assertions} assertions passed; no database or CRM writes.");
