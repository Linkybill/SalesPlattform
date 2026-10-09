using System.Reflection;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations.Zoho;
using SalesPlattform.Backend.Services;

public static class AdditionalReportRegressions
{
    public static async Task Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var day = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var modelType = typeof(SalesReportService).GetNestedType("ReportModel", BindingFlags.NonPublic)!;
        var periodType = typeof(SalesReportService).GetNestedType("ReportPeriod", BindingFlags.NonPublic)!;
        var period = Activator.CreateInstance(periodType, "Monat", day.AddDays(-29), day.AddDays(1),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null)!;
        var build = typeof(SalesReportService).GetMethod("BuildEvidence", BindingFlags.NonPublic | BindingFlags.Static)!;
        SalesReportEvidence Build(Dictionary<string, object> values, SalesReportConfiguration? configuration = null, bool sales = true)
        {
            var constructor = modelType.GetConstructors().Single(c => c.GetParameters().Length > 1);
            var args = constructor.GetParameters().Select(p => values.GetValueOrDefault(p.Name!)
                ?? Array.CreateInstance(p.ParameterType.GetGenericArguments()[0], 0)).ToArray();
            return (SalesReportEvidence)build.Invoke(null, [constructor.Invoke(args), period, now, 30, 90, sales, false, configuration])!;
        }
        SalesCustomer Customer(string name, string? industry) => new() { Id = Guid.NewGuid(), Name = name, Industry = industry };
        SalesDeal Deal(string name, string status = "open") => new() { Id = Guid.NewGuid(), Name = name, Status = status, ClosingAt = day, SourceModifiedAt = day };
        SalesAppointment Appointment(string? type, DateTimeOffset? start = null, string status = "planned", params (string Type, Guid Id)[] targets)
        {
            var id = Guid.NewGuid();
            return new() { Id = id, Subject = "Synthetic meeting", AppointmentType = type, Status = status, StartsAt = start ?? now,
                EndsAt = (start ?? now).AddHours(1), SourceCreatedAt = day.AddDays(-60),
                Relations = targets.Select(t => new SalesAppointmentRelation { Id = Guid.NewGuid(), AppointmentId = id, TargetType = t.Type, TargetId = t.Id }).ToArray() };
        }
        var manufacturing = Customer("Synthetic A", "Manufacturing");
        var second = Customer("Synthetic B", "Manufacturing");
        var services = Customer("Synthetic C", "Services");
        var noIndustry = Customer("Synthetic D", " ");
        var linkedDeal = Deal("Synthetic linked deal"); linkedDeal.CustomerId = manufacturing.Id;
        var lead = new SalesLead { Id = Guid.NewGuid(), Name = "Synthetic lead", CustomerId = services.Id };
        var direct = Appointment("Erstgespräch", targets: [("customer", manufacturing.Id)]);
        var byDeal = Appointment(" erstgespräch ", targets: [("deal", linkedDeal.Id), ("customer", manufacturing.Id), ("customer", second.Id)]);
        var byLead = Appointment("FOLGETERMIN", targets: [("lead", lead.Id)]);
        var ambiguous = Appointment("Erstgespräch", targets: [("customer", manufacturing.Id), ("customer", services.Id)]);
        var missing = Appointment("Erstgespräch");
        var blankIndustry = Appointment("Erstgespräch", targets: [("customer", noIndustry.Id)]);
        var cancelled = Appointment("Folgetermin", status: "cancelled", targets: [("customer", services.Id)]);
        var unknown = Appointment("Unmapped type");
        var untyped = Appointment(null);
        var removed = Appointment("Erstgespräch"); removed.SourceDeletedAt = now;
        var inactive = Appointment("Erstgespräch"); inactive.IsActive = false;
        var nextMonth = Appointment("Erstgespräch", day.AddDays(1));
        var previousMonth = Appointment("Erstgespräch", day.AddDays(-30));
        var values = new Dictionary<string, object> {
            ["Customers"] = new[] { manufacturing, second, services, noIndustry },
            ["Deals"] = new[] { linkedDeal }, ["Leads"] = new[] { lead },
            ["Appointments"] = new[] { direct, byDeal, byLead, ambiguous, missing, blankIndustry, cancelled, unknown, untyped, removed, inactive, nextMonth, previousMonth }
        };
        var evidence = Build(values);
        Check(evidence.Metrics["analysis:first-meetings"].Value == 5, "First meetings use type/start period and exclude inactive/deleted.");
        Check(evidence.Metrics["analysis:follow-up-meetings"].Value == 2, "Follow-ups include cancelled meetings and case-insensitive type.");
        Check(evidence.Metrics["meeting-first-industry:group:Manufacturing"].Value == 2, "Duplicate customer/deal relations and same-industry customers must count each meeting once.");
        Check(evidence.Metrics["meeting-follow-up-industry:group:Services"].Value == 2, "Lead-to-customer relationships must resolve industry.");
        Check(evidence.Metrics["meeting-first-industry:group:Mehrere Branchen"].Value == 1, "Different industries must be explicit, not selected arbitrarily.");
        Check(evidence.Metrics["meeting-first-industry:group:Ohne Branche"].Value == 2, "Unlinked/blank-industry meetings must not disappear.");
        Check(evidence.Metrics["meetings:unclassified"].Value == 2, "Missing and unknown types must remain inspectable.");
        Check(evidence.Records[$"appointment:{byDeal.Id}"].Customer!.Contains(manufacturing.Name), "Meeting evidence includes customer resolved through deal.");
        Check(evidence.Records[$"appointment:{byLead.Id}"].Customer == services.Name, "Meeting evidence includes customer resolved through lead.");
        Check(evidence.Records[$"appointment:{ambiguous.Id}"].Detail.Contains("Mehrere Branchen"), "Detail explains industry ambiguity.");
        var configured = new SalesReportConfiguration(["Unmapped type"], ["Folgetermin"], ["Custom offer stage"], 2);
        Check(Build(values, configured).Metrics["analysis:first-meetings"].Value == 1, "Tenant configuration overrides default first-meeting types.");
        var overlap = configured with { FollowUpMeetingTypes = ["Unmapped type"] };
        var overlapping = Build(values, overlap);
        Check(overlapping.Metrics["analysis:first-meetings"].Value == 0 && overlapping.Metrics["analysis:follow-up-meetings"].Value == 0,
            "Overlapping types must not be double counted.");
        Check(overlapping.Metrics["meetings:unclassified"].RecordKeys.Contains($"appointment:{unknown.Id}"), "Ambiguous type configuration exposes the affected meeting.");
        Check(Build(values, configured with { FirstMeetingTypes = [] }).Metrics["analysis:first-meetings"].Value == 0, "Empty type configuration disables classification.");

        var products = Enumerable.Range(0, 10).Select(i => new SalesProduct { Id = Guid.NewGuid(), Key = "p-" + i, Name = "Product " + i }).ToArray();
        var sold = products.SelectMany((p, index) => Enumerable.Range(0, index == 0 ? 3 : 1).Select(_ => {
            var deal = Deal("Synthetic sale", "won"); deal.Product = p; deal.Amount = index == 9 ? 999999 : 1; return deal;
        })).ToList();
        var missingProduct = Deal("Missing product", "won"); sold.Add(missingProduct);
        var outOfPeriod = Deal("Previous sale", "won"); outOfPeriod.ClosingAt = day.AddMonths(-1); sold.Add(outOfPeriod);
        var deletedSale = Deal("Deleted sale", "won"); deletedSale.SourceDeletedAt = now; sold.Add(deletedSale);
        var inactiveSale = Deal("Inactive sale", "won"); inactiveSale.IsActive = false; sold.Add(inactiveSale);
        var lostSale = Deal("Lost", "lost"); sold.Add(lostSale);
        var fallbackDate = Deal("Modified fallback", "won"); fallbackDate.ClosingAt = null; fallbackDate.Product = products[0]; sold.Add(fallbackDate);
        var productEvidence = Build(new() { ["Deals"] = sold.ToArray() });
        Check(productEvidence.Metrics["analysis:products-count"].Value == 14, "Product count uses won deals, valid fallback dates, missing products and excludes other periods/deleted/lost.");
        Check(productEvidence.Metrics["product-count:group:Product 0"].Value == 4, "Product count counts sales rather than distinct names.");
        Check(productEvidence.Metrics.Keys.First(k => k.StartsWith("product-count:")) == "product-count:group:Product 0", "Product ranking is by count, not amount.");
        Check(productEvidence.Metrics.ContainsKey("product-count:__other"), "Top 8 must retain all other product sales.");
        Check(productEvidence.Metrics.Where(p => p.Key.StartsWith("product-count:")).Sum(p => p.Value.Value) == 14, "Top products plus other must reconcile to total.");
        Check(productEvidence.Metrics.Where(p => p.Key.StartsWith("product-count:")).SelectMany(p => p.Value.RecordKeys).Distinct().Count() == 14,
            "Every sold product record must appear in exactly one group.");

        var offerStage = new SalesPipelineStage { Id = Guid.NewGuid(), Key = "offer", Name = " Angebot ", StageType = "open" };
        linkedDeal.PipelineStage = offerStage; linkedDeal.ClosingAt = day.AddYears(-1);
        var terminal = Deal("Terminal"); terminal.PipelineStage = new() { Id = Guid.NewGuid(), Key = "terminal", Name = "Angebot", StageType = "won", IsTerminal = true };
        var otherStage = Deal("Other stage");
        var closedDeal = Deal("Closed offer deal", "won"); closedDeal.PipelineStage = offerStage;
        var deletedOfferDeal = Deal("Deleted offer deal"); deletedOfferDeal.PipelineStage = offerStage; deletedOfferDeal.SourceDeletedAt = now;
        SalesOffer Offer(string status = "sent", DateTimeOffset? issued = null) => new() { Id = Guid.NewGuid(), Name = "Synthetic offer document",
            Status = status, IssuedAt = issued ?? day, CustomerId = manufacturing.Id, Amount = 10 };
        var document = Offer(); document.CustomerId = null; document.DealId = linkedDeal.Id;
        var closedDocument = Offer("accepted");
        var previousDocument = Offer(issued: day.AddMonths(-1));
        var deletedDocument = Offer(); deletedDocument.SourceDeletedAt = now;
        var noCustomerDocument = Offer(); noCustomerDocument.CustomerId = null;
        var fallbackDocument = Offer(); fallbackDocument.IssuedAt = null; fallbackDocument.SourceCreatedAt = day;
        var offerEvidence = Build(new() {
            ["Customers"] = new[] { manufacturing }, ["Deals"] = new[] { linkedDeal, terminal, otherStage, closedDeal, deletedOfferDeal },
            ["Offers"] = new[] { document, closedDocument, previousDocument, deletedDocument, noCustomerDocument, fallbackDocument }
        });
        Check(offerEvidence.Metrics["analysis:offer-deals"].Value == 1, "Offer-stage deals use current open nonterminal stock, independent of close date.");
        Check(offerEvidence.Metrics["analysis:offer-documents"].Value == 3, "Offer documents use issued/source-created period and exclude closed/deleted.");
        Check(offerEvidence.Metrics["offer-document-industry:group:Manufacturing"].Value == 2, "Offer industry resolves through deal if customer missing.");
        Check(offerEvidence.Metrics["offer-document-industry:group:Ohne Branche"].Value == 1, "Unlinked offer documents remain visible.");
        Check(offerEvidence.Records[$"offer:{document.Id}"].Customer == manufacturing.Name, "Offer drilldown resolves fallback customer consistently.");
        Check(offerEvidence.Metrics["analysis:offer-deals"].RecordKeys.All(k => k.StartsWith("deal:"))
            && offerEvidence.Metrics["analysis:offer-documents"].RecordKeys.All(k => k.StartsWith("offer:")), "Do not mix quotes and pipeline deals.");
        Check(Build(new() { ["Deals"] = new[] { linkedDeal } }, configured).Metrics["analysis:offer-deals"].Value == 0,
            "Offer stage names honor tenant configuration.");

        var atStart = Appointment(null, day);
        var atLastInstant = Appointment(null, day.AddDays(5).AddTicks(-1), "cancelled");
        var atEnd = Appointment(null, day.AddDays(5));
        var before = Appointment(null, day.AddTicks(-1));
        var later = Appointment(null, day.AddDays(1), "rescheduled");
        var futureDeleted = Appointment(null, day.AddDays(2)); futureDeleted.SourceDeletedAt = now;
        var prep = Build(new() { ["Appointments"] = new[] { atEnd, atLastInstant, futureDeleted, later, before, atStart } });
        Check(prep.Metrics["meetings:preparation"].RecordKeys.SequenceEqual(new[] { $"appointment:{atStart.Id}", $"appointment:{later.Id}", $"appointment:{atLastInstant.Id}" }),
            "Preparation includes today and following four days in chronological order, including cancellations, independent of selected month.");
        Check(prep.Metrics["meetings:preparation"].Period.Contains("04.10.2026"), "Preparation period visibly crosses month boundary.");
        Check(prep.Metrics["meetings:status-rescheduled"].Value == 0 && prep.Metrics["meetings:week-rescheduled"].Value == 1,
            "Weekly reschedules are independent of selected month and remain separate.");
        Check(Build(new() { ["Appointments"] = new[] { atStart, later, atLastInstant } }, configured).Metrics["meetings:preparation"].Value == 2,
            "Preparation horizon must use tenant setting.");
        var shifted = Appointment(null, day); shifted.RescheduleCount = 1;
        Check(Build(new() { ["Appointments"] = new[] { shifted } }).Metrics["meetings:status-rescheduled"].Value == 1,
            "Historically shifted meetings remain in the rescheduled list even when status is planned.");

        foreach (var report in new[] { evidence, productEvidence, offerEvidence, prep })
        {
            foreach (var metric in report.Metrics.Values.Where(m => m.Key.StartsWith("analysis:") || m.Key.StartsWith("product-count:")
                || m.Key.StartsWith("meeting-first-industry:") || m.Key.StartsWith("meeting-follow-up-industry:") || m.Key.StartsWith("offer-")
                || m.Key.StartsWith("meetings:status-") || m.Key.StartsWith("meetings:week-") || m.Key == "meetings:preparation"))
                Check(metric.Value == metric.RecordKeys.Distinct().Count() && metric.RecordKeys.All(report.Records.ContainsKey),
                    "Visible counts must equal exact unique drilldown rows: " + metric.Key);
        }
        var restricted = Build(values, sales: false);
        Check(!restricted.Metrics.Keys.Any(k => k.StartsWith("analysis:") || k.StartsWith("offer-deal-industry:") || k == "meetings:preparation"),
            "New evidence must respect sales report access.");

        var mapper = new ZohoCrmRecordMapper();
        var mapped = (CrmCanonicalAppointment)mapper.Map(new("zoho", "Events", "synthetic",
            JsonSerializer.SerializeToElement(new { Event_Title = "Synthetic", Appointment_Type = "Folgetermin", Start_DateTime = now, End_DateTime = now.AddHours(1) }), null));
        Check(mapped.AppointmentType == "Folgetermin" && mapper.GetPreferredFields("Events").Contains("Appointment_Type"),
            "Request and map alternate meeting type field when available.");

        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid();
        var store = new ReportSettingsStore();
        store.Values[tenantA] = [
            new(SalesApplicationSettingsService.FirstMeetingTypesKey, "tenantApp", tenantA, null, JsonSerializer.SerializeToElement("Custom; custom; "), now),
            new(SalesApplicationSettingsService.FollowUpMeetingTypesKey, "tenantApp", tenantA, null, JsonSerializer.SerializeToElement(""), now),
            new(SalesApplicationSettingsService.PreparationDaysKey, "tenantApp", tenantA, null, JsonSerializer.SerializeToElement(2), now)];
        var settings = new SalesApplicationSettingsService(store, Options.Create(new ApplicationSettingsOptions { ApplicationKey = "sales-plattform" }));
        var tenantConfiguration = await settings.GetReportConfigurationAsync(tenantA, "synthetic", default);
        Check(tenantConfiguration.FirstMeetingTypes.SequenceEqual(new[] { "Custom" }) && tenantConfiguration.FollowUpMeetingTypes.Length == 0
            && tenantConfiguration.PreparationDays == 2, "Tenant settings must parse/deduplicate names and honor explicit empty list.");
        Check((await settings.GetReportConfigurationAsync(tenantB, "synthetic", default)).PreparationDays == 5,
            "Another tenant uses its own defaults.");
        Check(store.Contexts.All(c => c.ApplicationKey == "sales-plattform") && store.Contexts.Select(c => c.TenantId).Distinct().Count() == 2,
            "Report settings use tenant/app context.");

        // Exercise the actual loader so missing Include(Relations) cannot pass via hand-wired navigation properties.
        var options = new DbContextOptionsBuilder<SalesPlattformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using (var db = new SalesPlattformDbContext(options))
        {
            ((IPlatformTenantDbContext)db).SetPlatformTenant(tenantA, true);
            db.SalesCustomers.Add(manufacturing);
            var storedAppointment = Appointment("Erstgespräch", targets: [("customer", manufacturing.Id)]);
            db.SalesAppointments.Add(storedAppointment);
            db.SalesLeads.Add(lead);
            await db.SaveChangesAsync();
        }
        using (var db = new SalesPlattformDbContext(options))
        {
            ((IPlatformTenantDbContext)db).SetPlatformTenant(tenantB, true);
            db.SalesCustomers.Add(Customer("Other tenant", "Other industry"));
            db.SalesAppointments.Add(Appointment("Erstgespräch"));
            await db.SaveChangesAsync();
        }
        using (var db = new SalesPlattformDbContext(options))
        {
            ((IPlatformTenantDbContext)db).SetPlatformTenant(tenantA, true);
            var loader = typeof(SalesReportService).GetMethod("LoadModelAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            var task = (Task)loader.Invoke(null, [db, CancellationToken.None])!; await task;
            var model = task.GetType().GetProperty("Result")!.GetValue(task)!;
            var loaded = (SalesReportEvidence)build.Invoke(null, [model, period, now, 30, 90, true, false, SalesReportConfiguration.Default])!;
            Check(loaded.Metrics["analysis:first-meetings"].Value == 1 && loaded.Metrics["meeting-first-industry:group:Manufacturing"].Value == 1,
                "Actual tenant loader must load relationships and exclude foreign appointments/customers.");
            Check(!loaded.Records.Values.Any(r => r.Customer == "Other tenant"), "Foreign tenant must never appear in detail rows.");
            Check(db.ChangeTracker.Entries().Count() == 0, "Report loader is read-only and does not track or mutate CRM entities.");
        }
        Console.WriteLine($"Additional reports: {checks} checks passed; synthetic sources only.");
    }

    private sealed class ReportSettingsStore : IApplicationSettingsStore
    {
        public Dictionary<Guid, ApplicationSettingValueRecord[]> Values { get; } = [];
        public List<ApplicationSettingsContext> Contexts { get; } = [];
        public Task<IReadOnlyCollection<ApplicationSettingValueRecord>> LoadAsync(ApplicationSettingsContext context, CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return Task.FromResult<IReadOnlyCollection<ApplicationSettingValueRecord>>(Values.GetValueOrDefault(context.TenantId) ?? []);
        }
        public Task SetAsync(ApplicationSettingsContext context, string key, string scope, JsonElement value, string? updatedBy, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Reports must not write settings.");
        public Task DeleteAsync(ApplicationSettingsContext context, string key, string scope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Reports must not delete settings.");
    }
}
