using SalesPlattform.Backend.Data;
namespace SalesPlattform.Backend.Services;
public sealed partial class SalesReportService
{
    private sealed partial class ReportProjection
    {
        private void SourceLists()
        {
            foreach(var (prefix,selector) in new (string,Func<SalesDeal,string>)[] {
                ("full-product:",d=>NameOr(d.Product?.Name,"Ohne Produkt")),
                ("full-industry:",d=>NameOr(d.Customer?.Industry,"Ohne Branche")) })
            {
                foreach(var group in Won.GroupBy(selector).OrderByDescending(g=>g.Sum(SafeAmount)).ThenBy(g=>g.Key))
                    Money(prefix+"group:"+group.Key,group.Key,group.Select(D),"Gewonnene Dealbeträge, alle Gruppen ohne Top-8-Zusammenfassung.");
            }
            var distinct=Won.GroupBy(d=>NameOr(d.Product?.Name,"Ohne Produkt")).OrderBy(g=>g.Key).Select((g,i)=>Row(g.Key,
                Value("product-distinct:"+i,g.Key,g.Select(D),g.Any(d=>!string.IsNullOrWhiteSpace(d.Product?.Name))?1:0,"calculated-count",
                    "Individuelle Zählung des Produktnamens innerhalb der Produktgruppe: ein befüllter Name zählt einmal, fehlende Namen nicht. Quelle: gewonnene Deals im Zeitraum.")));
            Panel("product-distinct","analysis","Top Produkte · individuelle Produktzählung","bar",["Unterschiedliche Produktnamen"],distinct,
                "Entspricht der im Screenshot beschrifteten Zählart. Die Anzahl verkaufter Produkte wird separat als Anzahl gewonnener Deals gezeigt.");
            var rolling=p with {Name="Letzte 30 Tage",From=now.AddDays(-30),To=now};
            var recent=Deals.Where(d=>d.Status=="won"&&InPeriod(d.ClosingAt??d.SourceModifiedAt,rolling)).GroupBy(d=>NameOr(d.Product?.Name,"Ohne Produkt")).OrderByDescending(g=>g.Count()).ToArray();
            Panel("products-30days","analysis","Top-Produkte · letzte 30 Tage","bar",["Verkaufte Produkte"],recent.Select((g,i)=>Row(g.Key,
                Count("products-30days:"+i,g.Key,g.Select(D),"Gewonnene Deals in den letzten 30 Tagen; ein Deal = ein Produkt.",PeriodText(rolling)))));

            var relations=new ReportRelationships(m);
            var lastMeeting=Meetings.Where(a=>a.StartsAt<=now&&AppointmentState(a.Status) is not ("cancelled" or "no-show" or "rescheduled"))
                .SelectMany(a=>a.Relations.Select(r=>(Customer:relations.RelationCustomer(r),Meeting:a)))
                .Where(x=>x.Customer.HasValue).GroupBy(x=>x.Customer!.Value).ToDictionary(g=>g.Key,g=>g.Max(x=>x.Meeting.StartsAt));
            var cutoff=now.AddMonths(-config.DormantMonths);
            var customers=m.Customers.Where(c=>c.IsActive&&c.SourceDeletedAt is null).ToArray();
            bool Disinterested(SalesCustomer c)=>Match(c.Status,config.DisinterestStatuses)
                || Deals.Any(d=>d.CustomerId==c.Id&&d.Status=="lost"&&(Match(d.LossReason,config.DisinterestStatuses)||Match(d.PipelineStage?.Name,config.DisinterestStatuses)));
            DateTimeOffset? Last(SalesCustomer c)=>Latest(c.LastContactAt<=now?c.LastContactAt:null,lastMeeting.TryGetValue(c.Id,out var at)?at:null);
            SalesReportRow ContactRow(SalesCustomer c)=>C(c) with {Key="dormant-customer:"+c.Id,Date=Last(c),
                Detail=C(c).Detail+$"; letzter Termin: {(lastMeeting.TryGetValue(c.Id,out var at)?at.ToString("dd.MM.yyyy HH:mm")+" UTC":"nicht dokumentiert")}; letzter Kundenkontakt: {c.LastContactAt:dd.MM.yyyy HH:mm} UTC"};
            var prospects=customers.Where(c=>!Disinterested(c)&&!Deals.Any(d=>d.CustomerId==c.Id&&d.Status=="won")&&Last(c)<cutoff).OrderBy(Last).ToArray();
            var uninterested=customers.Where(c=>Disinterested(c)&&Last(c)<cutoff).OrderBy(Last).ToArray();
            var never=customers.Where(c=>Last(c) is null).OrderBy(c=>c.Name);
            foreach(var (key,title,rows) in new (string,string,IEnumerable<SalesReportRow>)[] {
                ("dormant:prospects",$"Interessenten · Termin/Kontakt älter als {config.DormantMonths} Monate",prospects.Select(ContactRow)),
                ("dormant:disinterested",$"Ohne Interesse · Termin/Kontakt älter als {config.DormantMonths} Monate",uninterested.Select(ContactRow)),
                ("dormant:never","Kunden ohne dokumentierten Termin oder Kontakt",never.Select(ContactRow)) })
            {
                Count(key,title,rows,$"Separater Kontaktreport mit {config.DormantMonths} Kalendermonaten. Letztes vergangenes, nicht abgesagtes/ausgefallenes/verschobenes Meeting oder echter Kundenkontakt. Interessenten ohne gewonnenen Deal; ohne Interesse über konfigurierte Status-/Verlustwerte. Keine Änderung von LastContactAt oder R-07.",Current);
                Panel(key,"dormant",title,"records",[],[Row("",key)]);
            }
            var standalone=m.Leads.Where(l=>l.IsActive&&l.SourceDeletedAt is null&&!l.CustomerId.HasValue&&(l.LastContactAt is null||l.LastContactAt<cutoff)).OrderBy(l=>l.LastContactAt);
            var leadKey=Count("dormant:leads","Leads ohne aktuellen Kontakt",standalone.Select(L),$"Eigenständige CRM-Leads ohne Kundenverknüpfung, letzter Kontakt fehlt oder ist älter als {config.DormantMonths} Kalendermonate.",Current);
            Panel("dormant:leads","dormant","Weitere Leads ohne aktuellen Kontakt","records",[],[Row("",leadKey)]);

            var customerById=customers.ToDictionary(c=>c.Id);
            var leads=m.Leads.Where(l=>l.IsActive&&l.SourceDeletedAt is null).ToDictionary(l=>l.Id);
            var calls=Activities.Where(IsCall).SelectMany(a=>a.Relations.Select(r=>(Activity:a,Relation:r)))
                .Select(x=> {
                    var id=x.Relation.TargetType switch {
                        "customer" => (Guid?)x.Relation.TargetId,
                        "deal" => Deals.FirstOrDefault(d=>d.Id==x.Relation.TargetId)?.CustomerId,
                        "lead" => leads.GetValueOrDefault(x.Relation.TargetId)?.CustomerId,
                        _ => null };
                    return (x.Activity,Type:id.HasValue?"customer":x.Relation.TargetType,Id:id??x.Relation.TargetId);
                }).Where(x=>x.Type=="customer"&&customerById.ContainsKey(x.Id)||x.Type=="lead"&&leads.ContainsKey(x.Id))
                .GroupBy(x=>(x.Type,x.Id));
            var attempts=new List<(int Count,SalesReportRow Row)>();
            foreach(var group in calls)
            {
                var all=group.Select(x=>x.Activity).DistinctBy(a=>a.Id).OrderBy(a=>a.OccurredAt).ToArray();
                var lastConversation=all.Where(a=>a.CountsAsConversation==true).Select(a=>(DateTimeOffset?)a.OccurredAt).Max();
                var missed=all.Where(a=>a.CountsAsConversation==false&&(!lastConversation.HasValue||a.OccurredAt>lastConversation)).ToArray();
                if(missed.Length==0)continue;
                var row=group.Key.Type=="customer"?C(customerById[group.Key.Id]):L(leads[group.Key.Id]);
                attempts.Add((missed.Length,row with {Key="attempts:"+row.Key,Date=missed.Max(a=>a.OccurredAt),
                    Detail=row.Detail+$"; erfolglose Versuche seit letztem Gespräch: {missed.Length}; letzter Versuch: {missed.Max(a=>a.OccurredAt):dd.MM.yyyy HH:mm} UTC; von: {Owner(missed.Last().OwnerId)}"}));
            }
            foreach(var (key,title,filter) in new (string,string,Func<int,bool>)[] {
                ("calls:under-five",$"Erfolglose Anrufversuche · unter {config.CallEmailAttempts}",n=>n>0&&n<config.CallEmailAttempts),
                ("calls:fifth",$"Versuch {config.CallEmailAttempts} · Zwischen-E-Mail",n=>n==config.CallEmailAttempts),
                ("calls:long",$"Langläufer · {config.CallLongMin} bis {config.CallLongMax} Versuche",n=>n>=config.CallLongMin&&n<=config.CallLongMax),
                ("calls:unreachable",$"Mehr als {config.CallUnreachableAfter} erfolglose Versuche",n=>n>config.CallUnreachableAfter) })
            {
                Count(key,title,attempts.Where(x=>filter(x.Count)).OrderBy(x=>x.Row.Date).Select(x=>x.Row),
                    "Importierte erfolglose Anrufe je Kunde/Lead seit letztem qualifizierten Gespräch, unabhängig von Fälligkeit und offenen Arbeitslistenvorgängen. Jede Anruf-ID einmal je Ziel.",Current);
                Panel(key,"followups",title,"records",[],[Row("",key)]);
            }
            foreach(var (key,title) in new[] {("meetings:week-cancelled","Abgesagte Termine · aktuelle Woche"),("meetings:week-rescheduled","Verschobene Termine · aktuelle Woche"),("meetings:week","Meetings · aktuelle Kalenderwoche")})
                Panel("list:"+key,"meetings",title,"records",[],[Row("",key)]);
            Panel("list:preparation","followups",$"Terminvorbereitung · nächste {config.PreparationDays} Tage","records",[],[Row("","meetings:preparation")]);
            var expiring=m.Contracts.Where(c=>c.IsActive&&c.SourceDeletedAt is null&&c.EndAt>=now&&c.EndAt<=now.AddDays(config.RenewalDays)).OrderBy(c=>c.EndAt).ToArray();
            var contractKey=Count("list:renewals","Auslaufende Verträge",expiring.Select(c=>R("contract",c.Id,c.ContractNumber??"Vertrag",c.CustomerId,c.OwnerId,c.Status,c.EndAt,c.RecurringAmount,c.Currency)),
                $"Aktive Verträge mit Ende innerhalb der nächsten {config.RenewalDays} Tage.",Current);
            Panel("list:renewals","renewals","Auslaufende Verträge","records",[],[Row("",contractKey)]);
            var missingProducts=Count("data:products","Gewonnene Deals ohne Produktzuordnung",Won.Where(d=>d.Product is null).Select(D),"Ausgewertete gewonnene Deals mit fehlender kanonischer Produktzuordnung.");
            var missingTypes=Count("data:meeting-types","Termine ohne Terminart",Meetings.Where(a=>InPeriod(a.StartsAt,p)&&string.IsNullOrWhiteSpace(a.AppointmentType)).Select(A),"Termine im Zeitraum, bei denen keine Terminart importiert wurde.");
            var missingCategories=Count("data:categories","Gewonnene Deals ohne Produktkategorie",Won.Where(d=>d.Product?.CategoryId is null).Select(D),"Unvollständige Zuordnung kann die dokumentierte Cross-Selling-Breite unterschätzen.");
            Panel("report-quality","analysis","Datenbasis der Diagramme prüfen","metrics",[],[Row("",missingProducts),Row("",missingTypes),Row("",missingCategories),Row("","meetings:unclassified")],
                "Fehlende Produkt-/Terminzuordnungen können die Verteilungen verändern. Details zeigen die betroffenen CRM-Datensätze.");
        }
    }
}
