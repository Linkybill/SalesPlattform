using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

public sealed partial class SalesReportService
{
    private static void BuildAdditionalEvidence(SalesEvidenceBuilder builder, ReportModel model, ReportPeriod period,
        DateTimeOffset now, SalesReportConfiguration configuration, ReportRelationships relationships,
        Func<SalesAppointment, SalesReportRow> appointmentRow, Func<SalesDeal, SalesReportRow> dealRow)
    {
        var periodText = PeriodText(period);
        var current = $"Bestand am {now:dd.MM.yyyy} (UTC), unabhängig vom gewählten Zeitraum";
        const string appointmentsSource = "CRM → synchronisierte Termine und Kunden-/Deal-/Lead-Verknüpfungen";
        const string dealsSource = "CRM → synchronisierte Deals und Produkte";
        var appointments = model.Appointments.Where(a => a.IsActive && a.SourceDeletedAt is null).ToArray();
        var selected = appointments.Where(a => InPeriod(a.StartsAt, period)).ToArray();
        bool Matches(string? value, string[] names) => names.Any(name =>
            string.Equals(value?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        // An overlapping configuration must never count a meeting twice.
        bool First(SalesAppointment a) => Matches(a.AppointmentType, configuration.FirstMeetingTypes)
            && !Matches(a.AppointmentType, configuration.FollowUpMeetingTypes);
        bool FollowUp(SalesAppointment a) => Matches(a.AppointmentType, configuration.FollowUpMeetingTypes)
            && !Matches(a.AppointmentType, configuration.FirstMeetingTypes);

        void Groups(string prefix, string title, IEnumerable<SalesReportRow> rows, Func<SalesReportRow, string> groupKey,
            string reportPeriod, string source, string calculation)
        {
            var groups = rows.DistinctBy(r => r.Key).GroupBy(groupKey, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToArray();
            var limit = prefix == "product-count:" ? 8 : int.MaxValue;
            foreach (var group in groups.Take(limit))
                builder.Count(prefix + "group:" + group.Key, group.Key, group, reportPeriod, source, title + ": " + calculation);
            if (groups.Length > limit)
                builder.Count(prefix + "__other", "Sonstige", groups.Skip(limit).SelectMany(g => g), reportPeriod, source,
                    title + $": übrige {groups.Length - 8} Gruppen nach den Top 8. " + calculation);
        }

        var appointmentIndustries = selected.ToDictionary(a => $"appointment:{a.Id}", relationships.AppointmentIndustry);
        const string industryExplanation = "Jeder Termin genau einmal nach Beginn im Zeitraum, alle Status. Branche des verknüpften Kunden, direkt oder über Deal/Lead. Fehlende Zuordnung: Ohne Branche; mehrere unterschiedliche Branchen: Mehrere Branchen.";
        void MeetingGroups(string prefix, string key, string title, Func<SalesAppointment, bool> predicate, string[] types)
        {
            var rows = selected.Where(predicate).OrderBy(a => a.StartsAt).ThenBy(a => a.Id).Select(appointmentRow).ToArray();
            var calculation = industryExplanation + " Exakte Terminarten aus Tenant-Einstellung: " + string.Join("; ", types) + ".";
            builder.Count(key, title, rows, periodText, appointmentsSource, calculation);
            Groups(prefix, title, rows, row => appointmentIndustries[row.Key], periodText, appointmentsSource, calculation);
        }
        MeetingGroups("meeting-first-industry:", "analysis:first-meetings", "Erstgespräche nach Branche", First, configuration.FirstMeetingTypes);
        MeetingGroups("meeting-follow-up-industry:", "analysis:follow-up-meetings", "Folgetermine nach Branche", FollowUp, configuration.FollowUpMeetingTypes);
        builder.Count("meetings:unclassified", "Termine ohne eindeutige Typzuordnung",
            selected.Where(a => !First(a) && !FollowUp(a)).OrderBy(a => a.StartsAt).ThenBy(a => a.Id).Select(appointmentRow),
            periodText, appointmentsSource,
            "Terminart fehlt, gehört zu keiner konfigurierten Gruppe oder ist in beiden Gruppen eingetragen. Diese Termine werden nicht als Erst-/Folgetermin geraten. Zuordnung in den Tenant-AppSettings ändern.");

        var won = model.Deals.Where(d => IsActive(d) && IsStatus(d.Status, "won")
            && InPeriod(d.ClosingAt ?? d.SourceModifiedAt, period)).ToArray();
        var products = won.ToDictionary(d => $"deal:{d.Id}", d => NameOr(d.Product?.Name, "Ohne Produkt"));
        const string productFormula = "Anzahl aktiver gewonnener Deals nach Abschlussdatum, ersatzweise CRM-Änderungsdatum. Ein Deal entspricht einem Produkt. Keine Umsatzsumme, keine Menge von Angebotspositionen.";
        builder.Count("analysis:products-count", "Verkaufte Produkte", won.Select(dealRow), periodText, dealsSource, productFormula);
        Groups("product-count:", "Top-Produkte nach Anzahl", won.Select(dealRow), row => products[row.Key], periodText, dealsSource, productFormula);

        var offerDeals = model.Deals.Where(d => IsActive(d) && IsStatus(d.Status, "open") && !(d.PipelineStage?.IsTerminal ?? false)
            && Matches(d.PipelineStage?.Name, configuration.OfferStageNames)).ToArray();
        var dealIndustries = offerDeals.ToDictionary(d => $"deal:{d.Id}", d => relationships.CustomerIndustry(d.CustomerId));
        var offerDealFormula = "Aktive offene Deals außerhalb terminaler Stufen, deren aktuelle Stufe exakt einer konfigurierten Angebotsstufe entspricht: "
            + string.Join("; ", configuration.OfferStageNames) + ". Aktueller Bestand, kein Abschlussdatumfilter. Separate Angebotsbelege werden nicht addiert.";
        builder.Count("analysis:offer-deals", "Offene Angebots-Deals", offerDeals.Select(dealRow), current, dealsSource, offerDealFormula);
        Groups("offer-deal-industry:", "Offene Angebots-Deals nach Branche", offerDeals.Select(dealRow), row => dealIndustries[row.Key],
            current, dealsSource, offerDealFormula);

        var owners = model.Owners.ToDictionary(o => o.Id, o => o.DisplayName);
        var offerLinks = model.Links.Where(l => l.InternalEntityType == "offer").GroupBy(l => l.InternalEntityId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.ExternalUrl).FirstOrDefault(url =>
                Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"));
        SalesReportRow Offer(SalesOffer o) => new($"offer:{o.Id}", "offer", o.Name,
            relationships.CustomerName(relationships.OfferCustomer(o)), o.OwnerId.HasValue ? owners.GetValueOrDefault(o.OwnerId.Value) : null,
            o.Status, o.IssuedAt ?? o.SourceCreatedAt, o.Amount, o.Currency,
            $"Gültig bis: {o.ValidUntil:dd.MM.yyyy}; Branche: {relationships.CustomerIndustry(relationships.OfferCustomer(o))}", offerLinks.GetValueOrDefault(o.Id));
        var offers = model.Offers.Where(o => o.IsActive && o.SourceDeletedAt is null && !IsOfferClosed(o.Status)
            && InPeriod(o.IssuedAt ?? o.SourceCreatedAt, period)).ToArray();
        var offerIndustries = offers.ToDictionary(o => $"offer:{o.Id}", o => relationships.CustomerIndustry(relationships.OfferCustomer(o)));
        const string offerFormula = "Nicht abgeschlossene aktive CRM-Angebotsbelege nach Ausstellungsdatum, ersatzweise CRM-Erstellungsdatum. Branche über Kunde, ersatzweise verknüpften Deal. Ein Beleg einmal; keine Vermischung mit Deals in Angebotsstufen.";
        builder.Count("analysis:offer-documents", "Offene Angebotsbelege", offers.Select(Offer), periodText, "CRM → Angebotsbelege", offerFormula);
        Groups("offer-document-industry:", "Offene Angebotsbelege nach Branche", offers.Select(Offer), row => offerIndustries[row.Key],
            periodText, "CRM → Angebotsbelege", offerFormula);

        var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var end = day.AddDays(configuration.PreparationDays);
        builder.Count("meetings:preparation", $"Terminvorbereitung · nächste {configuration.PreparationDays} Tage",
            appointments.Where(a => a.StartsAt >= day && a.StartsAt < end).OrderBy(a => a.StartsAt).ThenBy(a => a.Id).Select(appointmentRow),
            $"{day:dd.MM.yyyy}–{end.AddDays(-1):dd.MM.yyyy} (UTC), unabhängig vom Reportzeitraum", appointmentsSource,
            $"Nächste {configuration.PreparationDays} Kalendertage einschließlich heute (Tenant-Einstellung). Beginn ab Tagesanfang UTC, Ende exklusiv. Alle Status einschließlich abgesagter/verschobener Termine; chronologisch sortiert. Keine CRM-Änderung.");
        var week = StartOfWeek(now);
        foreach (var (state, label) in new[] { ("cancelled", "Abgesagte Termine"), ("rescheduled", "Verschobene Termine"), ("no-show", "Nicht stattgefundene Termine"), ("completed", "Durchgeführte Termine") })
        {
            bool Predicate(SalesAppointment a) => AppointmentState(a.Status) == state || state == "rescheduled" && a.RescheduleCount > 0;
            builder.Count("meetings:status-" + state, label, selected.Where(Predicate).OrderBy(a => a.StartsAt).ThenBy(a => a.Id).Select(appointmentRow),
                periodText, appointmentsSource, "Aktive Termine mit Beginn im Zeitraum nach aktuellem Status; bei Verschiebungen zusätzlich protokollierte Verschiebeanzahl.");
            builder.Count("meetings:week-" + state, label + " dieser Woche",
                appointments.Where(a => a.StartsAt >= week && a.StartsAt < week.AddDays(7) && Predicate(a)).OrderBy(a => a.StartsAt).ThenBy(a => a.Id).Select(appointmentRow),
                $"{week:dd.MM.yyyy}–{week.AddDays(6):dd.MM.yyyy} (UTC)", appointmentsSource, "Terminbeginn in der aktuellen Kalenderwoche Montag bis Sonntag. Aktueller Status; Verschiebungen zusätzlich aus protokollierter Verschiebeanzahl.");
        }
    }

    private static string NameOr(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed class ReportRelationships(ReportModel model)
    {
        private readonly Dictionary<Guid, SalesCustomer> customers = model.Customers
            .Where(c => c.IsActive && c.SourceDeletedAt is null).ToDictionary(c => c.Id);
        private readonly Dictionary<Guid, SalesDeal> deals = model.Deals.Where(IsActive).ToDictionary(d => d.Id);
        private readonly Dictionary<Guid, SalesLead> leads = model.Leads
            .Where(l => l.IsActive && l.SourceDeletedAt is null).ToDictionary(l => l.Id);

        public Guid? OfferCustomer(SalesOffer offer) => offer.CustomerId
            ?? (offer.DealId.HasValue ? deals.GetValueOrDefault(offer.DealId.Value)?.CustomerId : null);
        public string? CustomerName(Guid? id) => id.HasValue ? customers.GetValueOrDefault(id.Value)?.Name : null;
        public string CustomerIndustry(Guid? id) => NameOr(id.HasValue ? customers.GetValueOrDefault(id.Value)?.Industry : null, "Ohne Branche");

        public Guid? RelationCustomer(SalesAppointmentRelation relation) => relation.TargetType switch
        {
            "customer" => relation.TargetId,
            "deal" => deals.GetValueOrDefault(relation.TargetId)?.CustomerId,
            "lead" => leads.GetValueOrDefault(relation.TargetId)?.CustomerId,
            _ => null
        };

        public string AppointmentIndustry(SalesAppointment appointment)
        {
            var industries = appointment.Relations.Select(RelationCustomer).Where(id => id.HasValue).Select(CustomerIndustry)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return industries.Length switch { 0 => "Ohne Branche", 1 => industries[0], _ => "Mehrere Branchen" };
        }

        public string? AppointmentNames(SalesAppointment appointment)
        {
            var names = appointment.Relations.Select(r => CustomerName(RelationCustomer(r))
                ?? (r.TargetType == "lead" ? leads.GetValueOrDefault(r.TargetId)?.CompanyName
                    ?? leads.GetValueOrDefault(r.TargetId)?.Name : r.TargetType == "deal" ? deals.GetValueOrDefault(r.TargetId)?.Name : null))
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToArray();
            return names.Length == 0 ? null : string.Join("; ", names);
        }
    }
}
