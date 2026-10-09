using SalesPlattform.Backend.Data;
namespace SalesPlattform.Backend.Services;
public sealed partial class SalesReportService
{
    private sealed partial class ReportProjection
    {
        private void PipelineAndTeam()
        {
            var activeDeals = Deals.ToDictionary(d => d.Id);
            foreach (var pipeline in m.Pipelines.OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
            {
                var stages = m.PipelineStages.Where(s => s.PipelineId == pipeline.Id && s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToArray();
                var histories = m.StageHistory.Where(h => h.PipelineId == pipeline.Id && h.EnteredAt <= now && activeDeals.ContainsKey(h.DealId)).ToArray();
                bool At(SalesDealStageHistory h, SalesPipelineStage s) => h.PipelineStageId == s.Id
                    || h.PipelineStageId is null && (h.StageKeySnapshot == s.Key || h.StageKeySnapshot == s.Name);
                var rows = new List<SalesReportPanelRow>();
                for (var i = 0; i < stages.Length; i++)
                {
                    var stage = stages[i]; var prefix = $"process:{pipeline.Id}:{stage.Id}:";
                    var open = activeDeals.Values.Where(d => d.PipelineId == pipeline.Id && d.PipelineStageId == stage.Id && d.Status == "open" && !stage.IsTerminal).ToArray();
                    var entrants = histories.Where(h => At(h,stage) && InPeriod(h.EnteredAt,p)).GroupBy(h => h.DealId).Select(g => g.OrderBy(h => h.EnteredAt).First()).ToArray();
                    var converted = i + 1 < stages.Length ? entrants.Where(h => histories.Any(next => next.DealId == h.DealId && At(next, stages[i + 1]) && next.EnteredAt >= h.EnteredAt)).ToArray() : [];
                    var weightedRows = open.Select(d => D(d) with { Key = "weighted:" + d.Id, Amount = stage.Probability is >= 0 and <= 1 ? (d.Amount ?? 0) * stage.Probability : null }).ToArray();
                    var weightedKey = Money(prefix+"weighted","Gewichtete Pipeline",weightedRows,"Offener Dealbetrag × Stufenwahrscheinlichkeit (0 bis 1).",Current);
                    if (open.Length > 0 && stage.Probability is not (>= 0 and <= 1))
                        b.Result.Metrics[weightedKey] = b.Result.Metrics[weightedKey] with { Value=null, UnavailableReason="Keine gültige Stufenwahrscheinlichkeit hinterlegt." };
                    var durations = histories.Where(h => At(h,stage) && InPeriod(h.EnteredAt,p) && h.ExitedAt >= h.EnteredAt).ToArray();
                    var dwellRows = durations.Select(h => R("stage-history",h.Id,activeDeals[h.DealId].Name,activeDeals[h.DealId].CustomerId,
                        activeDeals[h.DealId].OwnerId,stage.Name,h.EnteredAt,detail:$"Austritt: {h.ExitedAt:dd.MM.yyyy HH:mm} UTC; Pipeline: {pipeline.Name}"));
                    var conversionKey = Value(prefix+"conversion","Conversion zur nächsten Stufe",entrants.Select(h => D(activeDeals[h.DealId])),
                        i + 1 < stages.Length && entrants.Length > 0 ? Percent(converted.Length,entrants.Length) : null,"percent",
                        $"Kohorte: {entrants.Length} unterschiedliche Deals mit Eintritt in diese Stufe im Zeitraum; davon {converted.Length} später bis heute in der unmittelbar nächsten konfigurierten Stufe. Wiederholte Eintritte zählen einmal.",
                        i + 1 == stages.Length ? "Letzte Stufe – keine Folgestufe." : entrants.Length == 0 ? "Keine Stufeneintritte dokumentiert." : null);
                    rows.Add(Row(stage.Name,
                        Money(prefix+"open","Offene Pipeline",open.Select(D),"Aktueller offener Bestand dieser Pipeline/Stufe.",Current),
                        weightedKey, Count(prefix+"entered","Erreichte Stufe",entrants.Select(h=>D(activeDeals[h.DealId])),"Unterschiedliche Deals mit Stufeneintritt im Zeitraum."),
                        conversionKey, Value(prefix+"dwell","Verweildauer",dwellRows,durations.Length>0?(decimal)durations.Average(h=>(h.ExitedAt!.Value-h.EnteredAt).TotalDays):null,
                            "days","Mittelwert Austritt minus Eintritt; abgeschlossene Aufenthalte, Eintritt im Zeitraum.",durations.Length==0?"Keine abgeschlossenen Stufenaufenthalte.":null)));
                }
                Panel("process:"+pipeline.Id,"analysis","Funnel und Verweildauer · "+pipeline.Name,"table",
                    ["Offene Pipeline","Gewichtete Pipeline","Eintritte","Conversion","Verweildauer"],rows);
                Panel("process-dwell:"+pipeline.Id,"analysis","Verweildauer je Stufe · "+pipeline.Name,"bar",["Tage"],
                    rows.Select(r=>Row(r.Label,r.MetricKeys[4])));
                Panel("process-weighted:"+pipeline.Id,"analysis","Gewichtete Pipeline · "+pipeline.Name,"bar",["Gewichteter Betrag"],
                    rows.Select(r=>Row(r.Label,r.MetricKeys[1])));
                var wins=Won.Where(d=>d.PipelineId==pipeline.Id).ToArray();
                var touches=wins.Select(d=>new { Deal=d, Activities=Activities.Where(a=>a.Relations.Any(r=>r.TargetType=="deal"&&r.TargetId==d.Id)
                    && a.OccurredAt >= d.SourceCreatedAt && a.OccurredAt <= (d.ClosingAt??d.SourceModifiedAt)).ToArray() }).ToArray();
                var touchKey=Value("touchpoints:"+pipeline.Id,"Touchpoints bis Abschluss",touches.SelectMany(t=>t.Activities.Select(Activity)),
                    wins.Length>0 && wins.All(d=>d.SourceCreatedAt.HasValue)?(decimal)touches.Average(t=>t.Activities.Length):null,"factor",
                    "Durchschnitt direkt mit dem Deal verknüpfter Aktivitäten zwischen Anlage und Abschluss je gewonnenem Deal. Keine Verteilung aller Kundenaktivitäten auf mehrere Deals.",
                    wins.Length==0?"Keine gewonnenen Deals.":wins.Any(d=>!d.SourceCreatedAt.HasValue)?"Erstellungsdatum fehlt.":null);
                Panel("touchpoints:"+pipeline.Id,"team","Touchpoints · "+pipeline.Name,"metrics",[],[Row("",touchKey)]);
            }

            var teamRows=new List<SalesReportPanelRow>();
            var callRows=new List<SalesReportPanelRow>();
            var meetingRows=new List<SalesReportPanelRow>();
            var types=Meetings.Where(a=>InPeriod(a.StartsAt,p)).Select(a=>NameOr(a.AppointmentType,"Ohne Typ")).Distinct().Order().ToArray();
            foreach(var owner in m.Owners.Where(o=>o.IsActive).OrderBy(o=>o.DisplayName))
            {
                var prefix=$"spec-owner:{owner.Id}:";
                var calls=Activities.Where(a=>a.OwnerId==owner.Id&&IsCall(a)&&InPeriod(a.OccurredAt,p)).ToArray();
                var callKeys=new[] {
                    Count(prefix+"reached","Erreicht",calls.Where(a=>a.CountsAsConversation==true).Select(Activity),"Als qualifiziertes Gespräch importierte Anrufe."),
                    Count(prefix+"unreached","Nicht erreicht",calls.Where(a=>a.CountsAsConversation==false).Select(Activity),"Als Versuch importierte Anrufe."),
                    Count(prefix+"unknown","Ungeklärt",calls.Where(a=>a.CountsAsConversation is null).Select(Activity),"Anrufe ohne belastbare Gesprächsklassifikation.")
                };
                callRows.Add(Row(owner.DisplayName,callKeys));
                var ownWon=Won.Where(d=>d.OwnerId==owner.Id).ToArray(); var ownLost=Lost.Where(d=>d.OwnerId==owner.Id).ToArray();
                var quotes=m.Offers.Where(o=>o.IsActive&&o.SourceDeletedAt is null&&o.OwnerId==owner.Id&&InPeriod(o.IssuedAt??o.SourceCreatedAt,p)).ToArray();
                var offerKey=Count(prefix+"offers","Angebotsbelege",quotes.Select(o=>R("offer",o.Id,o.Name,o.CustomerId,o.OwnerId,o.Status,o.IssuedAt??o.SourceCreatedAt,o.Amount,o.Currency)),"Angebotsbelege nach Ausstellungs-, ersatzweise Erstellungsdatum; keine Deal-Stufenzählung.");
                var responded=m.Leads.Where(l=>l.SourceDeletedAt is null&&l.OwnerId==owner.Id&&InPeriod(l.SourceCreatedAt,p)&&l.FirstActivityAt>=l.SourceCreatedAt&&l.FirstActivityAt<=now).ToArray();
                var calendar=m.Calendars.FirstOrDefault(c=>c.IsActive&&c.IsDefault);
                var responseKey=Value(prefix+"response","Lead-Response-Zeit",responded.Select(L),
                    calendar is not null&&responded.Length>0?(decimal)responded.Average(l=>WorklistService.CalculateWorkingHours(l.SourceCreatedAt!.Value,l.FirstActivityAt!.Value,calendar)):null,
                    "hours","Durchschnitt erste importierte Kontaktaktivität minus Lead-Erstellung innerhalb des hinterlegten Arbeitskalenders inklusive Pausen/Feiertagen. Kohorte nach Lead-Erstellung.",
                    calendar is null?"Kein Arbeitskalender hinterlegt.":responded.Length==0?"Keine vollständig dokumentierte Erstreaktion.":null);
                teamRows.Add(Row(owner.DisplayName,offerKey,
                    Ratio(prefix+"win-rate","Win Rate",ownWon.Concat(ownLost).Select(D),ownWon.Length,ownWon.Length+ownLost.Length,"Gewonnene / abgeschlossene Deals × 100."),responseKey));
                meetingRows.Add(Row(owner.DisplayName,types.Select((t,index)=>Count(prefix+"type:"+index,t,Meetings.Where(a=>a.OwnerId==owner.Id&&InPeriod(a.StartsAt,p)&&NameOr(a.AppointmentType,"Ohne Typ")==t).Select(A),"Termine nach Beginn und importierter Terminart.")).ToArray()));
            }
            Panel("team-calls","team","Anrufe je Mitarbeiter und Ergebnis","stacked",["Erreicht","Nicht erreicht","Ungeklärt"],callRows);
            Panel("team-meeting-types","team","Termine je Mitarbeiter und Typ","stacked",types,meetingRows);
            Panel("team-process","team","Angebote, Abschlüsse und Reaktionszeit","table",["Angebote","Win Rate","Response (Arbeitsstunden)"],teamRows);
            var completed=Meetings.Where(a=>InPeriod(a.StartsAt,p)&&AppointmentState(a.Status)=="completed").ToArray();
            var offers=m.Offers.Where(o=>o.IsActive&&o.SourceDeletedAt is null&&InPeriod(o.IssuedAt??o.SourceCreatedAt,p)).ToArray();
            var rate=Ratio("spec:meeting-offer","Termin-zu-Angebot-Quote",completed.Select(A).Concat(offers.Select(o=>R("offer",o.Id,o.Name,o.CustomerId,o.OwnerId,o.Status,o.IssuedAt??o.SourceCreatedAt))),
                offers.Length,completed.Length,"Angebotsbelege im Zeitraum / durchgeführte Termine im Zeitraum × 100. Aktivitätsverhältnis, keine individuelle Konversionskohorte; kann über 100 % liegen.");
            Panel("meeting-offer","meetings","Vom Termin zum Angebot","metrics",[],[Row("",rate)]);
        }

        private void Targets()
        {
            var rows=new List<SalesReportPanelRow>();
            var yearStart=p.FiscalYearStart.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc);
            var timeShare=TimeShare(p with {From=yearStart,To=p.FiscalYearEnd.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)},now);
            var timeKey=Value("spec:year-time","Zeitanteil Geschäftsjahr",[],timeShare,"percent",
                "Vergangene Tage / Gesamttage des Geschäftsjahres × 100, auf 0 bis 100 begrenzt.",period:$"{p.FiscalYearStart:dd.MM.yyyy}–{p.FiscalYearEnd:dd.MM.yyyy}");
            var progress=m.Owners.Where(o=>o.IsActive).OrderByDescending(o=>b.Result.Metrics.GetValueOrDefault($"owner:{o.Id}:attainment")?.Value)
                .Select(o=>Row(o.DisplayName,$"owner:{o.Id}:attainment",timeKey)).ToArray();
            Panel("goal-progress","team","Zielerreichung im Jahresverlauf","progress",["Zielerreichung","Zeitanteil"],progress,
                "Sortiert nach Zielerreichung. Die gestrichelte Marke zeigt den verstrichenen Anteil des Geschäftsjahres.");
            foreach(var owner in m.Owners.Where(o=>o.IsActive))
            {
                var annual=m.Targets.Where(t=>t.OwnerId==owner.Id&&t.FiscalYearId==p.FiscalYearId&&t.TargetPeriodId is null&&IsRevenueTarget(t.TargetType)).ToArray();
                var periods=m.TargetPeriods.Where(t=>t.FiscalYearId==p.FiscalYearId).OrderBy(t=>t.StartsAt).ToArray();
                foreach(var (type,months) in new[]{("month",1),("quarter",3)})
                    if(!periods.Any(t=>t.PeriodType==type))
                        periods=periods.Concat(Enumerable.Range(0,12/months).Select(i=>{
                            var generated=new SalesTargetPeriod {
                                Id=Guid.Empty, FiscalYearId=p.FiscalYearId??Guid.Empty, PeriodType=type,
                                StartsAt=DateOnly.FromDateTime(yearStart.AddMonths(i*months)),
                                EndsAt=DateOnly.FromDateTime(yearStart.AddMonths((i+1)*months).AddDays(-1)) };
                            if(type=="month")
                            {
                                var quarter=periods.FirstOrDefault(t=>t.PeriodType=="quarter"&&t.StartsAt<=generated.StartsAt&&t.EndsAt>=generated.EndsAt);
                                if(quarter is not null)
                                {
                                    var third=Math.Round(quarter.DistributionWeight/3,6);
                                    generated.DistributionWeight=generated.StartsAt.AddMonths(1)>quarter.EndsAt?quarter.DistributionWeight-2*third:third;
                                }
                            }
                            else generated.DistributionWeight=periods.Where(t=>t.PeriodType=="month"&&t.StartsAt>=generated.StartsAt&&t.EndsAt<=generated.EndsAt).Sum(t=>t.DistributionWeight);
                            return generated;
                        }).Where(t=>t.StartsAt<=p.FiscalYearEnd)).OrderBy(t=>t.StartsAt).ToArray();
                foreach(var period in periods)
                {
                    var specific=m.Targets.Where(t=>period.Id!=Guid.Empty&&t.OwnerId==owner.Id&&t.TargetPeriodId==period.Id&&IsRevenueTarget(t.TargetType)).ToArray();
                    var peers=periods.Where(x=>x.PeriodType==period.PeriodType).ToArray();
                    var weightSum=peers.Sum(x=>x.DistributionWeight);
                    var validWeights=peers.All(x=>x.DistributionWeight>=0)&&(weightSum==0 || weightSum==1 || weightSum==100);
                    var weight=weightSum>0 ? period.DistributionWeight/weightSum : 1m/peers.Length;
                    var goalRows=(specific.Length>0?specific:annual).Select(t=>R("target",t.Id,"Ziel · "+owner.DisplayName,null,owner.Id,t.TargetType,null,t.TargetValue,t.Currency)).ToArray();
                    var key=$"target-period:{owner.Id}:{period.PeriodType}:{period.StartsAt:yyyy-MM-dd}";
                    var sum=Money(key+":sum","Zielbasis",goalRows,"Explizites Periodenziel oder Jahresziel.");
                    var source=b.Result.Metrics[sum];
                    var priorWeight=weightSum>0?peers.Where(x=>x.StartsAt<period.StartsAt).Sum(x=>x.DistributionWeight)/weightSum
                        :(decimal)peers.Count(x=>x.StartsAt<period.StartsAt)/peers.Length;
                    decimal? target=specific.Length>0?source.Value:source.Value.HasValue
                        ?Math.Round(source.Value.Value*(priorWeight+weight),2)-Math.Round(source.Value.Value*priorWeight,2):null;
                    var goal=Value(key+":target","Periodenziel",goalRows,goalRows.Length>0&&(specific.Length>0||validWeights)?target:null,"calculated-money",
                        "Explizites Periodenziel; sonst Jahresziel × Periodengewicht, ohne Gewicht zeitanteilige Verteilung.",goalRows.Length==0?"Jahresziel fehlt.":specific.Length==0&&!validWeights?"Periodengewichte müssen insgesamt 100 % (oder 1) ergeben.":source.UnavailableReason,
                        $"{period.StartsAt:dd.MM.yyyy}–{period.EndsAt:dd.MM.yyyy}",source.Currency);
                    var actual=Deals.Where(d=>d.Status=="won"&&(d.ClosingAt??d.SourceModifiedAt)>=period.StartsAt.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)
                        &&(d.ClosingAt??d.SourceModifiedAt)<period.EndsAt.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)&&d.OwnerId==owner.Id).ToArray();
                    rows.Add(Row(owner.DisplayName+" · "+period.StartsAt.ToString("MM.yyyy")+" · "+period.PeriodType,goal,
                        Money(key+":actual","Erreicht",actual.Select(D),"Gewonnene Deals im Zielzeitraum.",$"{period.StartsAt:dd.MM.yyyy}–{period.EndsAt:dd.MM.yyyy}")));
                }
            }
            Panel("target-periods","goals","Monats- und Quartalsziele","table",["Ziel","Erreicht"],rows);
            var activityRows=new List<SalesReportPanelRow>();
            foreach(var target in m.Targets.Where(t=>!IsRevenueTarget(t.TargetType)&&t.FiscalYearId==p.FiscalYearId))
            {
                var period=m.TargetPeriods.FirstOrDefault(t=>t.Id==target.TargetPeriodId);
                var from=(period?.StartsAt??p.FiscalYearStart).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc);
                var to=(period?.EndsAt??p.FiscalYearEnd).AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc);
                var type=target.TargetType.Trim().ToLowerInvariant();
                IEnumerable<SalesReportRow>? source=type switch {
                    "conversations" or "calls" => Activities.Where(a=>a.OwnerId==target.OwnerId&&IsCall(a)&&a.CountsAsConversation==true&&a.OccurredAt>=from&&a.OccurredAt<to).Select(Activity),
                    "appointments" or "meetings" => Meetings.Where(a=>a.OwnerId==target.OwnerId&&a.SourceCreatedAt>=from&&a.SourceCreatedAt<to&&(string.IsNullOrWhiteSpace(target.AppointmentType)||string.Equals(a.AppointmentType,target.AppointmentType,StringComparison.OrdinalIgnoreCase))).Select(A),
                    "deals" => Deals.Where(d=>d.OwnerId==target.OwnerId&&d.SourceCreatedAt>=from&&d.SourceCreatedAt<to).Select(D),
                    "offers" => m.Offers.Where(o=>o.IsActive&&o.SourceDeletedAt is null&&o.OwnerId==target.OwnerId&&(o.IssuedAt??o.SourceCreatedAt)>=from&&(o.IssuedAt??o.SourceCreatedAt)<to).Select(o=>R("offer",o.Id,o.Name,o.CustomerId,o.OwnerId,o.Status,o.IssuedAt??o.SourceCreatedAt)),
                    _ => null };
                var actualRows=source?.ToArray()??[];
                activityRows.Add(Row(Owner(target.OwnerId)+" · "+target.TargetType,
                    Value("activity-target:"+target.Id,"Aktivitätsziel",[R("target",target.Id,target.TargetType,null,target.OwnerId,null,null)],target.TargetValue,"calculated-count","Gespeichertes Aktivitätsziel.",period:$"{from:dd.MM.yyyy}–{to.AddDays(-1):dd.MM.yyyy}"),
                    Value("activity-actual:"+target.Id,"Erreicht",actualRows,source is null?null:actualRows.Length,"calculated-count","Anrufe: nur qualifizierte Gespräche; Termine/Deals: neu angelegt; Angebote: Ausstellungsdatum im Zielzeitraum.",
                        source is null?"Zielart nicht zugeordnet.":null, $"{from:dd.MM.yyyy}–{to.AddDays(-1):dd.MM.yyyy}")));
            }
            Panel("activity-targets","goals","Aktivitätsziele","table",["Ziel","Erreicht"],activityRows,"Gespeicherte Ziele für erreichte Gespräche, neue Termine, Angebote und Deals.");
        }
    }
}
