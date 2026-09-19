using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

// A normalized, public-data-only snapshot. A row is transmitted once even if
// several metrics refer to it. Opening details cannot race a subsequent CRM sync.
public sealed record SalesReportEvidence(Dictionary<string, SalesReportMetric> Metrics,
    Dictionary<string, SalesReportRow> Records);
public sealed record SalesReportMetric(string Key, string Label, decimal? Value, string Unit,
    string? Currency, string Period, string Source, string Calculation, string? UnavailableReason,
    string[] RecordKeys);
public sealed record SalesReportRow(string Key, string Kind, string Name, string? Customer, string? Owner,
    string? Status, DateTimeOffset? Date, decimal? Amount, string? Currency, string Detail, string? ExternalUrl);

public sealed class SalesEvidenceBuilder
{
    public SalesReportEvidence Result { get; } = new([], []);
    public void Add(string key, string label, IEnumerable<SalesReportRow> records, decimal? value, string unit,
        string period, string source, string calculation, string? unavailable = null, string? currency = null)
    {
        var rows = records.DistinctBy(r => r.Key).ToArray();
        foreach (var row in rows) Result.Records.TryAdd(row.Key, row);
        Result.Metrics.Add(key, new(key, label, value, unit, currency, period, source, calculation, unavailable,
            rows.Select(r => r.Key).ToArray()));
    }
    public void Count(string key, string label, IEnumerable<SalesReportRow> records, string period, string source, string calculation)
    {
        var rows = records.DistinctBy(r => r.Key).ToArray();
        Add(key, label, rows, rows.Length, "count", period, source, calculation);
    }
    public void Money(string key, string label, IEnumerable<SalesReportRow> records, string period, string source, string calculation)
    {
        var rows = records.DistinctBy(r => r.Key).ToArray();
        var currencies = rows.Select(r => r.Currency?.Trim().ToUpperInvariant()).Distinct().ToArray();
        var error = rows.Any(r => r.Amount is null) ? "Beträge fehlen – keine vollständige Summe."
            : currencies.Contains(null) || currencies.Contains("") ? "Währungsangaben fehlen."
            : currencies.Length > 1 ? "Mehrere Währungen – keine Umrechnung hinterlegt." : null;
        Add(key, label, rows, error is null ? rows.Sum(r => r.Amount ?? 0) : null, "money", period, source, calculation,
            error, currencies.Length == 1 ? currencies[0] : rows.Length == 0 ? "EUR" : null);
    }
}

public sealed partial class SalesReportService
{
    private static SalesReportEvidence BuildEvidence(ReportModel model, ReportPeriod period, DateTimeOffset now,
        int inactiveDays, int renewalDays, bool salesAccess, bool cleanupAccess)
    {
        var b = new SalesEvidenceBuilder();
        var periodText = PeriodText(period);
        var current = $"Bestand am {now:dd.MM.yyyy} (UTC), unabhängig vom gewählten Zeitraum";
        var owners = model.Owners.ToDictionary(o => o.Id, o => o.DisplayName);
        var customers = model.Customers.ToDictionary(c => c.Id, c => c.Name);
        var links = model.Links.Where(l => !string.IsNullOrWhiteSpace(l.ExternalUrl))
            .GroupBy(l => (l.InternalEntityType, l.InternalEntityId)).ToDictionary(g => g.Key, g => g.First().ExternalUrl);
        string? Owner(Guid? id) => id.HasValue ? owners.GetValueOrDefault(id.Value) : null;
        string? Customer(Guid? id) => id.HasValue ? customers.GetValueOrDefault(id.Value) : null;
        string? Url(string kind, Guid id) => links.GetValueOrDefault((kind, id)) is { } url
            && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? url : null;
        SalesReportRow Row(string kind, Guid id, string name, Guid? customer, Guid? owner, string? status,
            DateTimeOffset? date, decimal? amount = null, string? currency = null, string detail = "")
            => new($"{kind}:{id}", kind, name, Customer(customer), Owner(owner), status, date, amount, currency, detail, Url(kind, id));
        SalesReportRow Deal(SalesDeal d) => Row("deal", d.Id, d.Name, d.CustomerId, d.OwnerId, d.Status,
            d.ClosingAt ?? d.SourceModifiedAt, d.Amount, d.Currency,
            $"Produkt: {d.Product?.Name ?? "ohne Zuordnung"}; Stufe: {d.PipelineStage?.Name ?? "ohne Stufe"}; " +
            $"angelegt: {d.SourceCreatedAt:dd.MM.yyyy}; Abschluss: {d.ClosingAt:dd.MM.yyyy}; " +
            $"letzte Aktivität: {d.LastActivityAt:dd.MM.yyyy}" + (d.ClosingAt is null ? "; Abschlussdatum fehlt: Änderungsdatum als Ersatz" : ""));
        SalesReportRow Appointment(SalesAppointment a) => Row("appointment", a.Id, a.Subject ?? "Termin", null, a.OwnerId,
            AppointmentLabel(AppointmentState(a.Status)), a.StartsAt, detail: $"Angelegt: {a.SourceCreatedAt:dd.MM.yyyy HH:mm} UTC; Typ: {a.AppointmentType ?? "Ohne Typ"}; Verschiebungen: {a.RescheduleCount}");
        SalesReportRow Call(SalesActivity a) => Row("activity", a.Id, a.Subject ?? "Telefonat", null, a.OwnerId,
            a.Result, a.OccurredAt, detail: $"Dauer: {a.DurationSeconds?.ToString() ?? "unbekannt"} s; qualifiziertes Gespräch: {(a.CountsAsConversation == true ? "ja" : "nein")}");
        SalesReportRow Target(SalesTarget t) => Row("target", t.Id, "Umsatzziel · " + (Owner(t.OwnerId) ?? "ohne Besitzer"), null,
            t.OwnerId, t.TargetType, null, t.TargetValue, t.Currency, "Jahresziel in Sales; kein CRM-Umsatz");
        var deals = model.Deals.Where(IsActive).ToArray();
        var won = deals.Where(d => IsStatus(d.Status, "won") && InPeriod(d.ClosingAt ?? d.SourceModifiedAt, period)).ToArray();
        var lost = deals.Where(d => IsStatus(d.Status, "lost") && InPeriod(d.ClosingAt ?? d.SourceModifiedAt, period)).ToArray();
        var open = deals.Where(d => IsStatus(d.Status, "open") && !(d.PipelineStage?.IsTerminal ?? false)).ToArray();
        var appointments = model.Appointments.Where(a => a.IsActive && a.SourceDeletedAt is null).ToArray();
        var selectedAppointments = appointments.Where(a => InPeriod(a.StartsAt, period)).ToArray();
        var calls = model.Activities.Where(a => a.SourceDeletedAt is null && IsCall(a) && InPeriod(a.OccurredAt, period)).ToArray();
        var fiscal = period with { Name = "Geschäftsjahr", From = period.FiscalYearStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            To = period.FiscalYearEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) };
        var fiscalWon = deals.Where(d => IsStatus(d.Status, "won") && InPeriod(d.ClosingAt ?? d.SourceModifiedAt, fiscal)).ToArray();
        var targets = model.Targets.Where(t => period.FiscalYearId.HasValue && t.FiscalYearId == period.FiscalYearId
            && t.TargetPeriodId is null && IsRevenueTarget(t.TargetType)).ToArray();
        const string dealSource = "CRM → synchronisierte Deals in der Sales-Datenbank";
        const string appointmentSource = "CRM → synchronisierte Termine in der Sales-Datenbank";
        const string wonFormula = "Summe der Beträge aktiver, gewonnener Deals. Abschlussdatum im Zeitraum; fehlt es, wird das CRM-Änderungsdatum verwendet. Keine Rechnungs- oder Vertragsbeträge.";

        // Goals remain team-readable; percentages always compare fiscal-year
        // revenue with fiscal-year targets, never monthly/lifetime revenue.
        void OwnerMetrics(SalesOwner owner)
        {
            var prefix = $"owner:{owner.Id}:";
            var ownerWon = won.Where(d => d.OwnerId == owner.Id).ToArray();
            var achieved = fiscalWon.Where(d => d.OwnerId == owner.Id).ToArray();
            var goals = targets.Where(t => t.OwnerId == owner.Id).ToArray();
            b.Money(prefix + "target", "Jahresziel", goals.Select(Target), PeriodText(fiscal), "Sales-Zielplanung", "Summe der Jahres-Umsatzziele dieses Mitarbeiters.");
            if (goals.Length == 0) b.Result.Metrics[prefix + "target"] = b.Result.Metrics[prefix + "target"] with { Value = null, UnavailableReason = "Jahresziel nicht hinterlegt." };
            b.Money(prefix + "achieved", "Umsatz im Geschäftsjahr", achieved.Select(Deal), PeriodText(fiscal), dealSource, wonFormula);
            var goal = b.Result.Metrics[prefix + "target"];
            var revenue = b.Result.Metrics[prefix + "achieved"];
            var error = RatioError(goal, revenue);
            var value = error is null ? Percent(revenue.Value!.Value, goal.Value!.Value) : (decimal?)null;
            b.Add(prefix + "attainment", "Zielerreichung", goals.Select(Target).Concat(achieved.Select(Deal)), value, "percent",
                PeriodText(fiscal), "CRM-Deals und Sales-Jahresziele", "Gewonnener Umsatz im Geschäftsjahr / Jahresziel × 100.", error);
            b.Add(prefix + "pace", "Pace", goals.Select(Target).Concat(achieved.Select(Deal)), value - TimeShare(fiscal, now), "points",
                PeriodText(fiscal), "CRM-Deals und Sales-Jahresziele", $"Zielerreichung minus verstrichener Geschäftsjahresanteil ({TimeShare(fiscal, now):0.0} %).", error);
            if (!salesAccess) return;
            b.Money(prefix + "won", "Gewonnener Umsatz", ownerWon.Select(Deal), periodText, dealSource, wonFormula);
            b.Money(prefix + "pipeline", "Offene Pipeline", open.Where(d => d.OwnerId == owner.Id).Select(Deal), current, dealSource, "Summe offener Deals außerhalb terminaler Stufen.");
            b.Count(prefix + "appointments", "Termine", selectedAppointments.Where(a => a.OwnerId == owner.Id).Select(Appointment), periodText, appointmentSource, "Terminbeginn im Zeitraum; alle Terminstatus.");
            b.Count(prefix + "calls", "Telefonate", calls.Where(a => a.OwnerId == owner.Id).Select(Call), periodText, "CRM → synchronisierte Anrufaktivitäten", "Telefonate mit Aktivitätsdatum im Zeitraum, einschließlich erfolgloser Versuche.");
            b.Count(prefix + "conversations", "Erreichte Gespräche", calls.Where(a => a.OwnerId == owner.Id && a.CountsAsConversation == true).Select(Call), periodText,
                "CRM → qualifizierte Anrufaktivitäten", "Nur als Gespräch qualifizierte Anrufe. Verbindungsstatus und die konfigurierte Mindestdauer werden beim Import berücksichtigt.");
        }
        foreach (var owner in model.Owners.Where(o => o.IsActive)) OwnerMetrics(owner);

        if (salesAccess)
        {
            b.Money("won", "Gewonnener Umsatz", won.Select(Deal), periodText, dealSource, wonFormula);
            b.Count("won-count", "Gewonnene Abschlüsse", won.Select(Deal), periodText, dealSource, "Anzahl gewonnener Deals nach Abschlussdatum (ersatzweise Änderungsdatum).");
            b.Money("annual-target", "Jahresziel", targets.Select(Target), PeriodText(fiscal), "Sales-Zielplanung", "Summe aller Jahres-Umsatzziele des Geschäftsjahres, keine Monats-/Quartalsziele doppelt zählen.");
            if (targets.Length == 0) b.Result.Metrics["annual-target"] = b.Result.Metrics["annual-target"] with { Value = null, UnavailableReason = "Jahresziel nicht hinterlegt." };
            b.Money("fiscal-won", "Umsatz im Geschäftsjahr", fiscalWon.Select(Deal), PeriodText(fiscal), dealSource, wonFormula);
            var annual = b.Result.Metrics["annual-target"]; var achieved = b.Result.Metrics["fiscal-won"];
            var ratioError = RatioError(annual, achieved);
            b.Add("attainment", "Zielerreichung Geschäftsjahr", targets.Select(Target).Concat(fiscalWon.Select(Deal)),
                ratioError is null ? Percent(achieved.Value!.Value, annual.Value!.Value) : null, "percent", PeriodText(fiscal),
                "CRM-Deals und Sales-Jahresziele", "Gewonnener Umsatz im Geschäftsjahr / Jahresziel × 100. Unabhängig vom Report-Zeitraum.", ratioError);
            b.Add("win-rate", "Abschlussquote (Win Rate)", won.Concat(lost).Select(Deal), won.Length + lost.Length == 0 ? null : Percent(won.Length, won.Length + lost.Length),
                "percent", periodText, dealSource, $"Gewonnene / (gewonnene + verlorene) Deals × 100. Zähler: {won.Length}; Nenner: {won.Length + lost.Length}. Offene Deals zählen nicht mit.",
                won.Length + lost.Length == 0 ? "Keine abgeschlossenen Deals im Zeitraum." : null);
            b.Money("pipeline", "Offene Pipeline", open.Select(Deal), current, dealSource, "Summe aktiver offener Deals außerhalb terminaler Stufen; kein Zeitraumfilter.");
            var pipeline = b.Result.Metrics["pipeline"];
            var remaining = annual.Value - achieved.Value;
            var coverageError = ratioError ?? pipeline.UnavailableReason ?? (pipeline.RecordKeys.Length > 0 && pipeline.Currency != annual.Currency ? "Pipeline und Ziel haben unterschiedliche Währungen." : null)
                ?? (remaining <= 0 ? "Jahresziel bereits erreicht – kein verbleibendes Ziel." : null);
            b.Add("coverage", "Pipeline-Deckung", open.Select(Deal).Concat(targets.Select(Target)).Concat(fiscalWon.Select(Deal)),
                coverageError is null ? Math.Round(pipeline.Value!.Value / remaining!.Value, 2) : null, "factor", current + "; Zielbezug: " + PeriodText(fiscal),
                "CRM-Deals und Sales-Jahresziele", "Offene Pipeline / (Jahresziel − gewonnener Umsatz im Geschäftsjahr). Ergebnis als Vielfaches, nicht als Umsatz.", coverageError);
            var cycles = won.Where(d => d.SourceCreatedAt.HasValue && d.ClosingAt >= d.SourceCreatedAt).ToArray();
            b.Add("cycle", "Durchschnittliche Deal-Dauer", cycles.Select(Deal), cycles.Length == 0 ? null : (decimal?)AverageSalesCycleDays(cycles), "days", periodText,
                dealSource, $"Mittelwert Abschlussdatum − Erstellungsdatum des Deals, nicht des Leads. {won.Length - cycles.Length} gewonnene Deals ohne gültiges Datumspaar ausgeschlossen.",
                cycles.Length == 0 ? "Keine gewonnenen Deals mit gültigem Erstellungs- und Abschlussdatum." : null);
            var contracts = model.Contracts.Where(c => c.IsActive && c.SourceDeletedAt is null && (!c.StartAt.HasValue || c.StartAt <= now) && (!c.EndAt.HasValue || c.EndAt >= now)).ToArray();
            SalesReportRow Contract(SalesContract c) => Row("contract", c.Id, c.ContractNumber ?? "Vertrag", c.CustomerId, c.OwnerId, c.Status, c.EndAt,
                c.RecurringAmount, c.Currency, $"Beginn: {c.StartAt:dd.MM.yyyy}; Ende: {c.EndAt:dd.MM.yyyy}; Laufzeit: {c.DurationMonths} Monate; Betragsperiode nicht verlässlich hinterlegt");
            b.Money("recurring", "Wiederkehrende Vertragsbeträge", contracts.Select(Contract), current, "CRM → synchronisierte Verträge",
                "Summe des gespeicherten RecurringAmount aktuell aktiver Verträge. Kein belastbarer ARR: Eine verbindliche Monats-/Jahresbasis fehlt im Quellmodell. Nicht mit gewonnenem Deal-Umsatz gleichsetzen.");
            b.Count("stale", "Hängende Deals", open.Where(d => (d.LastActivityAt ?? d.SourceCreatedAt) <= now.AddDays(-inactiveDays)).Select(Deal), current,
                dealSource, $"Offene Deals ohne Aktivität seit mindestens {inactiveDays} Tagen (Tenant-Einstellung). Ohne Aktivitätsdatum zählt das Erstellungsdatum; ohne beide Daten keine Einstufung.");
            b.Count("expiring", $"Verträge in {renewalDays} Tagen fällig", contracts.Where(c => c.EndAt >= now && c.EndAt <= now.AddDays(renewalDays)).Select(Contract),
                current, "CRM → synchronisierte Verträge", $"Aktive Verträge mit Ende zwischen jetzt und jetzt + {renewalDays} Tagen (Tenant-Einstellung).");

            void DealGroups(string prefix, IEnumerable<SalesDeal> source, Func<SalesDeal, string> key, string title, string explanation, int? top = null)
            {
                var groups = source.GroupBy(key).OrderByDescending(g => g.Sum(SafeAmount)).ThenBy(g => g.Key).ToArray();
                foreach (var group in groups.Take(top ?? int.MaxValue))
                    b.Money(prefix + "group:" + group.Key, group.Key, group.Select(Deal), periodText, dealSource, title + ": " + explanation);
                if (top.HasValue && groups.Length > top)
                    b.Money(prefix + "__other", "Sonstige", groups.Skip(top.Value).SelectMany(g => g).Select(Deal), periodText, dealSource,
                        title + $": alle übrigen {groups.Length - top} Gruppen nach den Top {top}. " + explanation);
            }
            DealGroups("product:", won, d => d.Product?.Name ?? "Ohne Produkt", "Umsatz nach Produkt", wonFormula, 8);
            DealGroups("industry:", won, d => d.Customer?.Industry ?? "Ohne Branche", "Umsatz nach Branche", wonFormula, 8);
            DealGroups("region:", won, d => PostalRegion(d.Customer?.PostalCode), "Umsatz nach PLZ-Bereich", wonFormula);
            foreach (var customer in model.Customers.Where(c => c.IsActive && c.SourceDeletedAt is null))
            {
                b.Money($"customer:{customer.Id}:revenue", "Gewonnener Umsatz · " + customer.Name,
                    deals.Where(d => d.CustomerId == customer.Id && IsStatus(d.Status, "won")).Select(Deal), "Lifetime: alle synchronisierten Zeiträume", dealSource,
                    "Summe aller aktiven gewonnenen Deals dieses Kunden, unabhängig vom ausgewählten Report-Zeitraum. Kein Rechnungsumsatz.");
                b.Count($"customer:{customer.Id}:open", "Offene Deals · " + customer.Name, open.Where(d => d.CustomerId == customer.Id).Select(Deal), current, dealSource,
                    "Aktive offene Deals dieses Kunden außerhalb terminaler Stufen.");
            }
            foreach (var group in lost.GroupBy(d => string.IsNullOrWhiteSpace(d.LossReason) ? "Ohne Angabe" : d.LossReason))
                b.Count("loss:" + group.Key, group.Key!, group.Select(Deal), periodText, dealSource, "Anzahl verlorener Deals mit diesem Verlustgrund; Abschlussdatum im Zeitraum, ersatzweise Änderungsdatum.");
            foreach (var group in open.GroupBy(d => (d.Pipeline?.Name ?? "Ohne Pipeline") + " · " + (d.PipelineStage?.Name ?? "Ohne Stufe")))
                b.Money("funnel:" + group.Key, group.Key, group.Select(Deal), current, dealSource, "Summe offener Deals dieser Pipeline und Stufe; keine historische Conversion-Rate.");
            foreach (var group in model.StageHistory.Where(h => InPeriod(h.EnteredAt, period)).GroupBy(h => h.StageKeySnapshot))
            {
                var rows = group.Select(h => Row("stage-history", h.Id, deals.FirstOrDefault(d => d.Id == h.DealId)?.Name ?? "Deal-Historie", null, null,
                    h.StageKeySnapshot, h.EnteredAt, detail: $"Eintritt: {h.EnteredAt:dd.MM.yyyy HH:mm}; Austritt: {h.ExitedAt:dd.MM.yyyy HH:mm}; offene Aufenthalte bis Auswertungszeitpunkt")).ToArray();
                b.Add("dwell:" + group.Key, group.Key, rows, Math.Round((decimal)group.Average(h => Math.Max(0, ((h.ExitedAt ?? now) - h.EnteredAt).TotalDays)), 1),
                    "days", periodText, "CRM → synchronisierte Stufenhistorie", "Durchschnittliche Aufenthaltsdauer der Stufeneintritte im Zeitraum; noch offene Aufenthalte bis jetzt.");
            }
            foreach (var group in won.Where(d => d.CustomerId.HasValue).GroupBy(d => d.CustomerId!.Value))
                b.Add("cross:" + group.Key, Customer(group.Key) ?? "Kunde", group.Select(Deal), group.Select(d => d.Product?.Category?.Name ?? "Ohne Kategorie").Distinct().Count(),
                    "count", periodText, dealSource, "Anzahl unterschiedlicher Produktkategorien der gewonnenen Deals dieses Kunden; keine automatische Verkaufsempfehlung.");

            var week = StartOfWeek(now);
            var newAppointments = appointments.Where(a => InPeriod(a.SourceCreatedAt, period)).ToArray();
            b.Count("meetings:new", "Neu angelegte Termine", newAppointments.Select(Appointment), periodText, appointmentSource, "CRM-Erstellungsdatum im Zeitraum, unabhängig davon, wann der Termin stattfindet.");
            b.Count("meetings:week", "Termine dieser Woche", appointments.Where(a => a.StartsAt >= week && a.StartsAt < week.AddDays(7)).Select(Appointment),
                $"{week:dd.MM.yyyy}–{week.AddDays(6):dd.MM.yyyy} (UTC)", appointmentSource, "Terminbeginn in der aktuellen Kalenderwoche, Montag bis Sonntag; alle Status.");
            b.Count("meetings:planned", "Termine im Zeitraum", selectedAppointments.Select(Appointment), periodText, appointmentSource, "Alle aktiven Termine mit Beginn im gewählten Zeitraum; Nenner für die Terminquoten.");
            b.Count("meetings:missed", "Nicht stattgefundene / verschobene Termine", selectedAppointments.Where(a => a.RescheduleCount > 0 || AppointmentState(a.Status) is "no-show" or "cancelled" or "rescheduled").Select(Appointment), periodText,
                appointmentSource, "Termine mit Status abgesagt, nicht erschienen oder verschoben, oder mit mindestens einer protokollierten Verschiebung; Terminbeginn im Zeitraum.");
            void MeetingRate(string key, string label, Func<SalesAppointment, bool> predicate)
            {
                var count = selectedAppointments.Count(predicate);
                b.Add(key, label, selectedAppointments.Select(Appointment), selectedAppointments.Length == 0 ? null : Percent(count, selectedAppointments.Length),
                    "percent", periodText, appointmentSource, $"{count} passende / {selectedAppointments.Length} Termine mit Beginn im Zeitraum × 100. Tabelle zeigt den vollständigen Nenner und den Status.",
                    selectedAppointments.Length == 0 ? "Keine Termine im Zeitraum." : null);
            }
            MeetingRate("meetings:completion", "Durchführungsquote", a => AppointmentState(a.Status) == "completed");
            MeetingRate("meetings:no-show", "No-Show-Quote", a => AppointmentState(a.Status) == "no-show");
            MeetingRate("meetings:reschedule", "Verschiebequote", a => a.RescheduleCount > 0 || AppointmentState(a.Status) == "rescheduled");
            foreach (var group in selectedAppointments.GroupBy(a => AppointmentState(a.Status)))
                b.Count("meeting-status:" + group.Key, AppointmentLabel(group.Key), group.Select(Appointment), periodText, appointmentSource, "Anzahl Termine mit diesem aktuellen Status und Beginn im Zeitraum.");
            foreach (var group in selectedAppointments.GroupBy(a => a.AppointmentType ?? "Ohne Typ"))
                b.Count("meeting-type:" + group.Key, group.Key, group.Select(Appointment), periodText, appointmentSource, "Anzahl Termine dieser Art mit Beginn im Zeitraum.");
        }

        var cases = model.ServiceCases.Where(c => c.IsActive && c.SourceDeletedAt is null && InPeriod(c.OpenedAt ?? c.SourceCreatedAt, period)).ToArray();
        SalesReportRow Case(SalesServiceCase c) => Row("service-case", c.Id, c.Subject, c.CustomerId, c.OwnerId, c.Status, c.OpenedAt ?? c.SourceCreatedAt,
            detail: $"Priorität: {c.Priority}; Fälligkeit: {c.DueAt:dd.MM.yyyy}");
        var openCases = cases.Where(c => !IsClosedServiceCase(c.Status)).ToArray();
        const string serviceSource = "CRM → synchronisierte Servicefälle";
        b.Count("service:total", "Servicefälle", cases.Select(Case), periodText, serviceSource, "Aktive Fälle nach Eröffnungsdatum, ersatzweise CRM-Erstellungsdatum.");
        b.Count("service:open", "Offene Servicefälle", openCases.Select(Case), periodText, serviceSource, "Nicht abgeschlossene Fälle des ausgewählten Eröffnungszeitraums.");
        b.Count("service:overdue", "Überfällige Servicefälle", openCases.Where(c => c.DueAt < now).Select(Case), periodText, serviceSource, "Offene Fälle mit Fälligkeit vor dem Auswertungszeitpunkt.");
        b.Count("service:urgent", "Dringende Servicefälle", openCases.Where(c => IsAnyStatus(c.Priority, "critical", "high", "hoch")).Select(Case), periodText, serviceSource, "Alle offenen Fälle mit hoher/kritischer Priorität; keine Begrenzung auf die ersten acht.");
        foreach (var group in cases.GroupBy(c => c.Status)) b.Count("service-status:" + group.Key, group.Key, group.Select(Case), periodText, serviceSource, "Fälle des Eröffnungszeitraums nach Status.");
        foreach (var group in cases.GroupBy(c => c.Priority)) b.Count("service-priority:" + group.Key, group.Key, group.Select(Case), periodText, serviceSource, "Fälle des Eröffnungszeitraums nach Priorität.");

        var offers = model.Offers.Where(o => o.IsActive && o.SourceDeletedAt is null && InPeriod(o.IssuedAt ?? o.SourceCreatedAt, period))
            .Select(o => Row("offer", o.Id, o.Name, o.CustomerId, o.OwnerId, o.Status, o.IssuedAt ?? o.SourceCreatedAt, o.Amount, o.Currency, $"Gültig bis: {o.ValidUntil:dd.MM.yyyy}")).ToArray();
        var orders = model.Orders.Where(o => o.IsActive && o.SourceDeletedAt is null && InPeriod(o.OrderedAt ?? o.SourceCreatedAt, period))
            .Select(o => Row("order", o.Id, o.Name, o.CustomerId, o.OwnerId, o.Status, o.OrderedAt ?? o.SourceCreatedAt, o.Amount, o.Currency, $"Liefertermin: {o.PromisedAt:dd.MM.yyyy}")).ToArray();
        var invoices = model.Invoices.Where(i => i.IsActive && i.SourceDeletedAt is null && InPeriod(i.IssuedAt ?? i.SourceCreatedAt, period))
            .Select(i => Row("invoice", i.Id, i.Name, i.CustomerId, i.OwnerId, i.Status, i.IssuedAt ?? i.SourceCreatedAt, i.OpenAmount ?? i.Amount, i.Currency,
                $"Fällig: {i.DueAt:dd.MM.yyyy}; Betragsspalte: offener Betrag, ersatzweise Rechnungsbetrag")).ToArray();
        foreach (var (key, label, rows) in new[] { ("offers", "Angebote", offers), ("orders", "Aufträge", orders), ("invoices", "Rechnungen", invoices) })
        {
            b.Count("commercial:" + key, label, rows, periodText, "CRM → " + label, "Anzahl nach Ausstellungs-/Bestelldatum, ersatzweise CRM-Erstellungsdatum; alle Status.");
            foreach (var group in rows.GroupBy(r => r.Status ?? "Ohne Status"))
                b.Count("commercial-status:" + key + ":" + group.Key, label + " · " + group.Key, group, periodText, "CRM → " + label, "Anzahl dieser Belege mit diesem Status, nicht deren Geldbetrag.");
        }
        b.Money("commercial:outstanding", "Offener Rechnungsbetrag", invoices.Where(i => !IsInvoiceClosed(i.Status) && (i.Amount > 0 || i.Amount is null)), periodText,
            "CRM → Rechnungen", "Summe offener Beträge nicht bezahlter Rechnungen des Ausstellungszeitraums; fehlt OpenAmount, gilt der Rechnungsbetrag.");
        b.Count("commercial:offers-open", "Offene Angebote", offers.Where(o => !IsOfferClosed(o.Status)), periodText, "CRM → Angebote", "Nicht abgeschlossene Angebote des Ausstellungszeitraums.");
        b.Count("commercial:orders-open", "Offene Aufträge", orders.Where(o => !IsOrderClosed(o.Status)), periodText, "CRM → Aufträge", "Nicht abgeschlossene Aufträge des Bestellzeitraums.");
        b.Count("commercial:invoices-open", "Offene Rechnungen", invoices.Where(i => !IsInvoiceClosed(i.Status) && i.Amount > 0), periodText, "CRM → Rechnungen", "Nicht abgeschlossene Rechnungen des Ausstellungszeitraums mit positivem offenem Betrag.");
        b.Money("commercial:offers-amount", "Angebotsvolumen", offers, periodText, "CRM → Angebote", "Summe aller Angebotsbeträge des Ausstellungszeitraums; alle Status.");
        b.Money("commercial:orders-amount", "Auftragsvolumen", orders, periodText, "CRM → Aufträge", "Summe aller Auftragsbeträge des Bestellzeitraums; alle Status.");
        var overdueOffers = model.Offers.Where(o => !IsOfferClosed(o.Status) && o.ValidUntil < now).Select(o => $"offer:{o.Id}").ToHashSet();
        var overdueOrders = model.Orders.Where(o => !IsOrderClosed(o.Status) && o.PromisedAt < now).Select(o => $"order:{o.Id}").ToHashSet();
        var overdueInvoices = model.Invoices.Where(i => !IsInvoiceClosed(i.Status) && i.DueAt < now && (i.OpenAmount ?? i.Amount) > 0).Select(i => $"invoice:{i.Id}").ToHashSet();
        b.Count("commercial:offers-overdue", "Überfällige Angebote", offers.Where(o => overdueOffers.Contains(o.Key)), periodText, "CRM → Angebote", "Nicht abgeschlossene Angebote des Ausstellungszeitraums, deren Gültigkeit abgelaufen ist.");
        b.Count("commercial:orders-overdue", "Überfällige Aufträge", orders.Where(o => overdueOrders.Contains(o.Key)), periodText, "CRM → Aufträge", "Nicht abgeschlossene Aufträge des Bestellzeitraums mit überschrittenem Liefertermin.");
        b.Count("commercial:invoices-overdue", "Überfällige Rechnungen", invoices.Where(i => overdueInvoices.Contains(i.Key)), periodText, "CRM → Rechnungen", "Nicht abgeschlossene Rechnungen des Ausstellungszeitraums mit positivem offenen Betrag und überschrittener Fälligkeit.");
        if (cleanupAccess)
        {
            SalesReportRow Quality(SalesDataQualityFinding f) => Row("quality", f.Id, f.Message, null, null, f.Status, f.DetectedAt, detail: $"{f.Code}; {f.Severity}; {f.EntityType}; Feld: {f.FieldName}");
            var findings = model.QualityFindings.Where(f => !IsStatus(f.Status, "resolved")).ToArray();
            b.Count("quality-total", "Offene Datenprüfungen", findings.Select(Quality), current, "Sales-Datenqualitätsprüfung", "Alle noch nicht aufgelösten Prüfungen.");
            foreach (var group in findings.GroupBy(f => f.Severity))
                b.Count("quality:" + group.Key, group.Key, group.Select(Quality), current, "Sales-Datenqualitätsprüfung", "Anzahl noch nicht aufgelöster Prüfungen dieser Schwere.");
        }
        return b.Result;
    }

    private static string? RatioError(SalesReportMetric target, SalesReportMetric revenue)
        => target.RecordKeys.Length == 0 || target.Value <= 0 ? "Jahresziel nicht hinterlegt."
            : target.UnavailableReason ?? revenue.UnavailableReason
            ?? (revenue.RecordKeys.Length > 0 && target.Currency != revenue.Currency ? "Ziel und Umsatz haben unterschiedliche Währungen." : null);
    private static string AppointmentLabel(string status) => status switch
    {
        "completed" => "Durchgeführt", "cancelled" => "Abgesagt", "rescheduled" => "Verschoben",
        "no-show" => "Nicht erschienen / nicht stattgefunden", "planned" => "Geplant", _ => status
    };
    private static string PeriodText(ReportPeriod period) => period.From.HasValue && period.To.HasValue
        ? $"{period.Name}: {period.From:dd.MM.yyyy}–{period.To.Value.AddDays(-1):dd.MM.yyyy} (UTC)"
        : "Lifetime: alle synchronisierten Zeiträume";
}
