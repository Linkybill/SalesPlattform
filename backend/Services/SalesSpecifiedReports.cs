using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

public sealed record SalesReportPanelRow(string Label, string[] MetricKeys);
public sealed record SalesReportPanel(string Key, string Area, string Title, string Kind,
    string Description, string[] Columns, SalesReportPanelRow[] Rows);

public sealed partial class SalesReportService
{
    private static void BuildSpecifiedReports(SalesEvidenceBuilder b, ReportModel model, ReportPeriod period,
        DateTimeOffset now, SalesReportConfiguration configuration)
        => new ReportProjection(b, model, period, now, configuration).Build();

    private sealed partial class ReportProjection(SalesEvidenceBuilder b, ReportModel m, ReportPeriod p,
        DateTimeOffset now, SalesReportConfiguration config)
    {
        private SalesDeal[] Deals { get; } = m.Deals.Where(IsActive).ToArray();
        private SalesDeal[] Won { get; } = m.Deals.Where(d => IsActive(d) && IsStatus(d.Status, "won") && InPeriod(d.ClosingAt ?? d.SourceModifiedAt, p)).ToArray();
        private SalesDeal[] Lost { get; } = m.Deals.Where(d => IsActive(d) && IsStatus(d.Status, "lost") && InPeriod(d.ClosingAt ?? d.SourceModifiedAt, p)).ToArray();
        private SalesAppointment[] Meetings { get; } = m.Appointments.Where(a => a.IsActive && a.SourceDeletedAt is null).ToArray();
        private SalesActivity[] Activities { get; } = m.Activities.Where(a => a.SourceDeletedAt is null && a.OccurredAt <= now).ToArray();
        private string Period => PeriodText(p);
        private string Current => $"Bestand am {now:dd.MM.yyyy} (UTC)";
        private const string Source = "CRM → synchronisierte Daten in der Sales-Datenbank";
        private string Owner(Guid? id) => m.Owners.FirstOrDefault(o => o.Id == id)?.DisplayName ?? "Ohne Besitzer";
        private string Customer(Guid? id) => m.Customers.FirstOrDefault(c => c.Id == id)?.Name ?? "";
        private string? Url(string kind, Guid id) => m.Links.FirstOrDefault(l => l.InternalEntityType == kind && l.InternalEntityId == id)?.ExternalUrl;
        private SalesReportRow R(string kind, Guid id, string name, Guid? customer, Guid? owner, string? status,
            DateTimeOffset? date, decimal? amount = null, string? currency = null, string detail = "")
            => new($"{kind}:{id}", kind, name, Customer(customer), Owner(owner), status, date, amount, currency, detail, Url(kind, id));
        private SalesReportRow D(SalesDeal d) => b.Result.Records.GetValueOrDefault($"deal:{d.Id}")
            ?? R("deal", d.Id, d.Name, d.CustomerId, d.OwnerId, d.Status, d.ClosingAt ?? d.SourceModifiedAt, d.Amount, d.Currency,
                $"Produkt: {d.Product?.Name ?? "Ohne Produkt"}; Pipeline: {d.Pipeline?.Name}; Stufe: {d.PipelineStage?.Name}");
        private SalesReportRow A(SalesAppointment a) => b.Result.Records.GetValueOrDefault($"appointment:{a.Id}")
            ?? R("appointment", a.Id, a.Subject ?? "Termin", null, a.OwnerId, a.Status, a.StartsAt,
                detail: $"Terminart: {a.AppointmentType ?? "Ohne Typ"}; bis {a.EndsAt:dd.MM.yyyy HH:mm} UTC") with { EndDate = a.EndsAt };
        private SalesReportRow C(SalesCustomer c) => R("customer", c.Id, c.Name, c.Id, c.OwnerId, c.Status, c.LastContactAt,
            detail: $"Branche: {c.Industry ?? "Ohne Branche"}; PLZ: {c.PostalCode}; Ort: {c.City}; letzter Telefonkontakt: {c.LastPhoneCallAt:dd.MM.yyyy}");
        private SalesReportRow L(SalesLead l) => R("lead", l.Id, l.Name, l.CustomerId, l.OwnerId, l.Status, l.LastContactAt,
            detail: $"Firma: {l.CompanyName}; letzter Anruf: {l.LastPhoneCallAt:dd.MM.yyyy}; Versuche seit Gespräch: {l.CallsSinceConversation}; insgesamt: {l.TotalCallAttempts}");
        private SalesReportRow Activity(SalesActivity a) => R("activity", a.Id, a.Subject ?? a.ActivityType, null, a.OwnerId,
            a.Result, a.OccurredAt, detail: $"Typ: {a.ActivityType}; Dauer: {a.DurationSeconds} s; Gespräch: {a.CountsAsConversation}");
        private string Count(string key, string title, IEnumerable<SalesReportRow> rows, string formula, string? period = null)
        { b.Count(key, title, rows, period ?? Period, Source, formula); return key; }
        private string Money(string key, string title, IEnumerable<SalesReportRow> rows, string formula, string? period = null)
        { b.Money(key, title, rows, period ?? Period, Source, formula); return key; }
        private string Value(string key, string title, IEnumerable<SalesReportRow> rows, decimal? value, string unit,
            string formula, string? unavailable = null, string? period = null, string? currency = null)
        { b.Add(key, title, rows, value, unit, period ?? Period, Source, formula, unavailable, currency); return key; }
        private string Ratio(string key, string title, IEnumerable<SalesReportRow> rows, decimal numerator, decimal denominator,
            string formula, string? period = null)
            => Value(key, title, rows, denominator > 0 ? Math.Round(100 * numerator / denominator, 1) : null,
                "percent", formula, denominator > 0 ? null : "Kein Nenner im ausgewählten Zeitraum.", period);
        private string AverageMoney(string key, string title, SalesDeal[] deals, string? period = null)
        {
            var sum = Money(key + ":sum", title + " · Summe", deals.Select(D), "Summe gewonnener Dealbeträge.", period);
            var metric = b.Result.Metrics[sum];
            return Value(key, title, deals.Select(D), deals.Length > 0 ? metric.Value / deals.Length : null, "calculated-money",
                "Summe gewonnener Dealbeträge / Anzahl gewonnener Deals.", metric.UnavailableReason ?? (deals.Length == 0 ? "Keine gewonnenen Deals." : null), period, metric.Currency);
        }
        private void Panel(string key, string area, string title, string kind, string[] columns,
            IEnumerable<SalesReportPanelRow> rows, string description = "")
            => b.Result.Panels.Add(new(key, area, title, kind, description, columns, rows.ToArray()));
        private static SalesReportPanelRow Row(string label, params string[] keys) => new(label, keys);
        private bool Match(string? value, string[] names) => names.Any(n => string.Equals(n.Trim(), value?.Trim(), StringComparison.OrdinalIgnoreCase));
        private static DateTimeOffset? Latest(params DateTimeOffset?[] dates) => dates.Where(d => d.HasValue).DefaultIfEmpty(null).Max();

        public void Build()
        {
            Overview();
            PipelineAndTeam();
            Lifetime();
            CustomerAnalysis();
            SourceLists();
            Targets();
        }

        private void Overview()
        {
            var first = Deals.Where(d => IsStatus(d.Status, "won") && d.CustomerId.HasValue && (d.ClosingAt ?? d.SourceModifiedAt).HasValue)
                .GroupBy(d => d.CustomerId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(d => d.ClosingAt ?? d.SourceModifiedAt).ThenBy(d => d.Id).First().Id);
            var newSales = Won.Where(d => d.CustomerId.HasValue && first.GetValueOrDefault(d.CustomerId.Value) == d.Id).ToArray();
            var existing = Won.Where(d => d.CustomerId.HasValue && first.GetValueOrDefault(d.CustomerId.Value) != d.Id).ToArray();
            var unknown = Won.Where(d => !d.CustomerId.HasValue).ToArray();
            var keys = new[] {
                Money("spec:lost", "Verlorener Umsatz", Lost.Select(D), "Verlorene Deals nach Abschlussdatum, ersatzweise Änderungsdatum."),
                Money("spec:new-revenue", "Neukundenumsatz", newSales.Select(D), "Erster gewonnener Deal je Kunde über die gesamte synchronisierte Historie; Abschluss im Zeitraum."),
                Money("spec:existing-revenue", "Bestandskundenumsatz", existing.Select(D), "Weitere gewonnene Deals dieser Kunden mit Abschluss im Zeitraum."),
                Money("spec:unknown-customer-revenue", "Umsatz ohne Kundenzuordnung", unknown.Select(D), "Gewonnene Deals ohne verknüpften Kunden, deshalb nicht Neu/Bestand zugeordnet."),
                AverageMoney("spec:average-deal", "Durchschnittlicher Deal-Wert", Won),
                Count("spec:calls", "Telefonate im Zeitraum", Activities.Where(a => IsCall(a) && InPeriod(a.OccurredAt,p)).Select(Activity), "Anrufzeitpunkt im Zeitraum."),
                Count("spec:new-customers", "Neu angelegte Kunden", m.Customers.Where(c => c.SourceDeletedAt is null && InPeriod(c.SourceCreatedAt,p)).Select(C), "CRM-Erstellungsdatum im Zeitraum; nicht mit erstem Kauf gleichgesetzt.")
            };
            Panel("period-summary", "cockpit", "Abschlüsse, Kunden und Aktivitäten", "metrics", [], keys.Select(k => Row("", k)));
            var statusMetrics=new[]{"attainment","win-rate","coverage"}.Select(k=>b.Result.Metrics[k]).ToArray();
            var thresholds=new[]{(config.AttainmentRed,config.AttainmentGreen),(config.WinRateRed,config.WinRateGreen),(config.CoverageRed,config.CoverageGreen)};
            var statusReady=statusMetrics.All(x=>x.Value.HasValue)&&thresholds.All(x=>x.Item1<=x.Item2);
            var statusKey=Value("spec:status","Cockpit-Status",statusMetrics.SelectMany(x=>x.RecordKeys).Distinct().Select(k=>b.Result.Records[k]),
                statusReady?statusMetrics.Select((x,i)=>x.Value<thresholds[i].Item1?2:x.Value>thresholds[i].Item2?0:1).Max():null,"status",
                $"Schlechteste Bewertung aus Zielerreichung (rot <{config.AttainmentRed} %, grün >{config.AttainmentGreen} %), Win Rate (rot <{config.WinRateRed} %, grün >{config.WinRateGreen} %) und Pipeline-Deckung (rot <{config.CoverageRed}×, grün >{config.CoverageGreen}×). Dazwischen gelb; Schwellen in AppSettings.",
                statusReady?null:"Mindestens eine Kennzahl fehlt oder die Ampelschwellen sind widersprüchlich.");
            Panel("cockpit-status","cockpit","Statusampel","metrics",[],[Row("",statusKey)]);
            var quarterStart = new DateTimeOffset(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1, 0, 0, 0, TimeSpan.Zero);
            var comparisons = new List<SalesReportPanelRow>();
            for (var offset = -1; offset <= 0; offset++)
            {
                var start = quarterStart.AddMonths(offset * 3); var end = start.AddMonths(3);
                var text = $"{start:dd.MM.yyyy}–{end.AddDays(-1):dd.MM.yyyy} (UTC)";
                var closed = Deals.Where(d => d.Status is "won" or "lost" && (d.ClosingAt ?? d.SourceModifiedAt) >= start && (d.ClosingAt ?? d.SourceModifiedAt) < end).ToArray();
                var won = closed.Where(d => d.Status == "won").ToArray();
                var cycle = won.Where(d => d.ClosingAt >= d.SourceCreatedAt && d.SourceCreatedAt.HasValue).ToArray();
                comparisons.Add(Row(offset == -1 ? "Vorquartal" : "Aktuelles Quartal",
                    Ratio($"quarter:{offset}:win", "Win Rate", closed.Select(D), won.Length, closed.Length, "Gewonnene / abgeschlossene Deals × 100.", text),
                    Value($"quarter:{offset}:cycle", "Sales Cycle", cycle.Select(D), cycle.Length > 0 ? (decimal)cycle.Average(d => (d.ClosingAt!.Value - d.SourceCreatedAt!.Value).TotalDays) : null,
                        "days", "Durchschnitt Abschluss minus Erstellung, nur gewonnene Deals mit beiden Datumswerten.", cycle.Length == 0 ? "Keine vollständigen Abschlussdaten." : null, text)));
            }
            Panel("quarter-comparison", "cockpit", "Win Rate und Sales Cycle · Quartalsvergleich", "table", ["Win Rate", "Sales Cycle"], comparisons);
            var activeContracts = Deals.Where(d => d.Status == "won" && d.ContractStartAt <= now && d.ContractEndAt >= now).ToArray();
            var normalized = activeContracts.Where(d => d.DurationMonths > 0 && d.Amount.HasValue).Select(d => D(d) with {
                Key = $"annualized:{d.Id}", Amount = d.Amount * 12 / d.DurationMonths, Detail = $"Gesamtbetrag {d.Amount} / Laufzeit {d.DurationMonths} Monate × 12." }).ToArray();
            var arrKey = Money("spec:arr", "Annualisierter Vertragsumsatz (ARR)", normalized, "Gewonnene Deals mit aktuell laufendem Vertrag: Gesamtbetrag / Laufzeit in Monaten × 12. Keine Addition der gesonderten RecurringAmount-Felder.", Current);
            if (activeContracts.Length == 0 || normalized.Length != activeContracts.Length)
                b.Result.Metrics[arrKey] = b.Result.Metrics[arrKey] with { Value = null, UnavailableReason = activeContracts.Length == 0 ? "Keine Verträge mit belegtem Beginn und Ende." : "Bei aktiven Verträgen fehlen Gesamtbetrag oder Laufzeit." };
            var annual = b.Result.Metrics.GetValueOrDefault("fiscal-won");
            var arr = b.Result.Metrics[arrKey];
            var arrShare = Value("spec:arr-share", "ARR / Geschäftsjahresumsatz", normalized, annual?.Value > 0 && annual.Currency == arr.Currency ? arr.Value / annual.Value * 100 : null,
                "percent", "Annualisierter aktueller Vertragsumsatz / gewonnener Geschäftsjahresumsatz × 100. Unterschiedliche Zeitbezüge explizit; kein Anteil wiederkehrender Abschlüsse.", arr.UnavailableReason ?? (annual?.Value > 0 && annual.Currency == arr.Currency ? null : "Jahresumsatz fehlt oder Währungen sind verschieden."), Current);
            Panel("arr", "cockpit", "Vertragsumsatz", "metrics", [], [Row("",arrKey),Row("",arrShare)]);
            var actions = m.WorkItems.Where(w => IsOpenWorkItem(w.Status) && w.SourceRuleCode is "R-05" or "R-06" or "R-11")
                .OrderByDescending(w => w.PriorityScore).ThenBy(w => w.DueAt).Take(5).ToArray();
            var actionKey = Count("spec:actions", "Handlungsbedarf", actions.Select(w => R("workitem",w.Id,w.Title,null,w.OwnerId,w.Status,w.DueAt,detail:w.Reason ?? "")),
                "Höchstens fünf offene Hinweise aus R-05, R-06 und R-11 nach Priorität.", Current);
            Panel("actions","cockpit","Handlungsbedarf","records",[],[Row("",actionKey)]);
        }
    }
}
