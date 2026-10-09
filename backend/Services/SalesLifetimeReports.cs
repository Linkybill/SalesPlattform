using SalesPlattform.Backend.Data;
namespace SalesPlattform.Backend.Services;
public sealed partial class SalesReportService
{
    private sealed partial class ReportProjection
    {
        private void Lifetime()
        {
            var dates=Deals.Select(d=>d.ClosingAt??d.SourceModifiedAt)
                .Concat(Activities.Select(a=>(DateTimeOffset?)a.OccurredAt))
                .Concat(Meetings.Select(a=>(DateTimeOffset?)a.StartsAt))
                .Concat(m.Customers.Select(c=>c.SourceCreatedAt)).Where(d=>d.HasValue&&d<=now).Select(d=>d!.Value.Year).ToArray();
            var years=dates.Length==0?[]:Enumerable.Range(dates.Min(),now.Year-dates.Min()+1).ToArray();
            var revenue=new List<SalesReportPanelRow>(); var closing=new List<SalesReportPanelRow>();
            var average=new List<SalesReportPanelRow>(); var head=new List<SalesReportPanelRow>();
            var productMix=new List<SalesReportPanelRow>(); var industryMix=new List<SalesReportPanelRow>();
            var calls=new List<SalesReportPanelRow>(); var meetings=new List<SalesReportPanelRow>(); var employees=new List<SalesReportPanelRow>();
            var customers=new List<SalesReportPanelRow>();
            var products=Deals.Where(d=>d.Status=="won").Select(d=>NameOr(d.Product?.Name,"Ohne Produkt")).Distinct().Order().ToArray();
            var industries=Deals.Where(d=>d.Status=="won").Select(d=>NameOr(d.Customer?.Industry,"Ohne Branche")).Distinct().Order().ToArray();
            var ownerIds=m.Owners.Select(o=>o.Id).ToHashSet();
            bool UnknownOwner(Guid? id)=>!id.HasValue||!ownerIds.Contains(id.Value);
            var needsUnassigned=Deals.Any(d=>d.Status=="won"&&UnknownOwner(d.OwnerId))||Activities.Any(a=>UnknownOwner(a.OwnerId))||Meetings.Any(a=>UnknownOwner(a.OwnerId));
            var owners=m.Owners.OrderBy(o=>o.DisplayName).Concat(needsUnassigned?[new SalesOwner{Id=Guid.Empty,DisplayName="Ohne zugeordneten Besitzer"}]:[]).ToArray();
            bool Belongs(Guid? id,SalesOwner owner)=>owner.Id==Guid.Empty?UnknownOwner(id):id==owner.Id;
            foreach(var year in years)
            {
                var from=new DateTimeOffset(year,1,1,0,0,0,TimeSpan.Zero); var to=from.AddYears(1);
                var text=$"Kalenderjahr {year} (UTC)"+(year==now.Year?" · laufendes Jahr":"");
                bool Inside(DateTimeOffset? date)=>date>=from&&date<to&&date<=now;
                var won=Deals.Where(d=>d.Status=="won"&&Inside(d.ClosingAt??d.SourceModifiedAt)).ToArray();
                var lost=Deals.Where(d=>d.Status=="lost"&&Inside(d.ClosingAt??d.SourceModifiedAt)).ToArray();
                var key="life:"+year+":";
                var wonKey=Money(key+"won","Gewonnener Umsatz",won.Select(D),"Gewonnene Deals nach Abschlussdatum, ersatzweise Änderungsdatum.",text);
                var lostKey=Money(key+"lost","Verlorener Umsatz",lost.Select(D),"Verlorene Deals nach Abschlussdatum, ersatzweise Änderungsdatum.",text);
                revenue.Add(Row(year.ToString(),wonKey,lostKey));
                closing.Add(Row(year.ToString(),Count(key+"won-count","Gewonnen",won.Select(D),"Gewonnene Abschlüsse.",text),
                    Count(key+"lost-count","Verloren",lost.Select(D),"Verlorene Abschlüsse.",text)));
                average.Add(Row(year.ToString(),AverageMoney(key+"average","Durchschnittlicher Deal-Wert",won,text)));
                var productiveOwners=won.Where(d=>d.OwnerId.HasValue).Select(d=>d.OwnerId!.Value).Distinct().ToArray();
                var sum=b.Result.Metrics[wonKey];
                head.Add(Row(year.ToString(),Value(key+"per-head","Umsatz je umsatzaktivem Mitarbeiter",won.Select(D),
                    productiveOwners.Length>0&&won.All(d=>d.OwnerId.HasValue)?sum.Value/productiveOwners.Length:null,"calculated-money",
                    "Gewonnener Umsatz / unterschiedliche Besitzer gewonnener Deals dieses Jahres. Keine historische Personalbestandszahl.",
                    sum.UnavailableReason??(productiveOwners.Length==0?"Keine umsatzaktiven Besitzer.":won.Any(d=>!d.OwnerId.HasValue)?"Besitzerzuordnung fehlt.":null),text,sum.Currency)));
                productMix.Add(Row(year.ToString(),products.Select((product,i)=>Money(key+"product:"+i,product,won.Where(d=>NameOr(d.Product?.Name,"Ohne Produkt")==product).Select(D),"Gewonnener Umsatz nach Produkt und Kalenderjahr.",text)).ToArray()));
                industryMix.Add(Row(year.ToString(),industries.Select((industry,i)=>Money(key+"industry:"+i,industry,won.Where(d=>NameOr(d.Customer?.Industry,"Ohne Branche")==industry).Select(D),"Gewonnener Umsatz nach heutiger Kundenbranche und historischem Abschlussjahr; keine historische Branchenänderung rekonstruiert.",text)).ToArray()));
                calls.Add(Row(year.ToString(),owners.Select(o=>Count(key+"calls:"+o.Id,o.DisplayName,Activities.Where(a=>Belongs(a.OwnerId,o)&&IsCall(a)&&Inside(a.OccurredAt)).Select(Activity),"Telefonate nach Aktivitätszeit und Besitzer.",text)).ToArray()));
                meetings.Add(Row(year.ToString(),owners.Select(o=>Count(key+"meetings:"+o.Id,o.DisplayName,Meetings.Where(a=>Belongs(a.OwnerId,o)&&Inside(a.StartsAt)).Select(A),"Termine nach Beginn und Besitzer, alle Status.",text)).ToArray()));
                employees.Add(Row(year.ToString(),owners.Select(o=>Money(key+"employee:"+o.Id,o.DisplayName,won.Where(d=>Belongs(d.OwnerId,o)).Select(D),"Gewonnener Umsatz je Besitzer und Kalenderjahr.",text)).ToArray()));
                customers.Add(CustomerHistoryRow(key,year.ToString(),from,to<now?to:now,text));
            }
            Panel("lifetime-revenue","lifetime","Umsatzentwicklung · gewonnen und verloren","line",["Gewonnen","Verloren"],revenue);
            Panel("lifetime-products","lifetime","Produktmix je Jahr","area",products,productMix);
            Panel("lifetime-industries","lifetime","Branchenmix je Jahr","area",industries,industryMix);
            Panel("lifetime-closing","lifetime","Abschlüsse je Jahr","stacked",["Gewonnen","Verloren"],closing);
            Panel("lifetime-customers","lifetime","Kundenbestand mit Zu- und Abgang","table",["Bestand Periodenstart","Bestand Periodenende","Zugänge","Abgänge","Churn"],customers,"Historische Bestände nur bei belegtem Statusverlauf. Fehlende Historie bleibt sichtbar.");
            Panel("lifetime-calls","lifetime","Telefonaktivität je Jahr und Mitarbeiter","stacked",owners.Select(o=>o.DisplayName).ToArray(),calls);
            Panel("lifetime-meetings","lifetime","Termine je Jahr und Mitarbeiter","stacked",owners.Select(o=>o.DisplayName).ToArray(),meetings);
            Panel("lifetime-average","lifetime","Durchschnittlicher Deal-Wert je Jahr","line",["Deal-Wert"],average);
            Panel("lifetime-employees","lifetime","Mitarbeiterentwicklung · Umsatz je Mitarbeiter","stacked",owners.Select(o=>o.DisplayName).ToArray(),employees);
            Panel("lifetime-head","lifetime","Umsatz je umsatzaktivem Mitarbeiter","line",["Umsatz je Kopf"],head);
            var sold=Deals.Where(d=>d.Status=="won").GroupBy(d=>NameOr(d.Product?.Name,"Ohne Produkt")).OrderByDescending(g=>g.Count());
            Panel("lifetime-sold","lifetime","Verkaufte Produkte gesamt","bar",["Abschlüsse"],sold.Select((g,i)=>Row(g.Key,Count("life-sold:"+i,g.Key,g.Select(D),"Ein gewonnener Deal entspricht einem verkauften Produkt.","Lifetime"))));
        }

        private SalesReportPanelRow CustomerHistoryRow(string key,string label,DateTimeOffset from,DateTimeOffset to,string text)
        {
            bool Active(string? status)=>Match(status,config.ActiveCustomerStatuses);
            SalesCustomerStatusHistory? State(SalesCustomer c,DateTimeOffset at)=>c.StatusHistory
                .Where(h=>h.ValidFrom<=at&&(!h.ValidTo.HasValue||h.ValidTo>at)).OrderByDescending(h=>h.ValidFrom).FirstOrDefault();
            var eligible=m.Customers.Where(c=>c.SourceCreatedAt<to).ToArray();
            var initial=eligible.Where(c=>c.SourceCreatedAt<from&&Active(State(c,from)?.Status)).ToArray();
            var end=eligible.Where(c=>Active(State(c,to.AddTicks(-1))?.Status)).ToArray();
            var missingStart=eligible.Any(c=>c.SourceCreatedAt<from&&State(c,from) is null);
            var missingEnd=eligible.Any(c=>State(c,to.AddTicks(-1)) is null);
            var unknownCreation=m.Customers.Any(c=>c.SourceCreatedAt is null);
            var added=eligible.Where(c=>c.SourceCreatedAt>=from).ToArray();
            var departed=initial.Where(c=>c.StatusHistory.Any(h=>h.ValidFrom>=from&&h.ValidFrom<to&&Match(h.Status,config.LostCustomerStatuses))).ToArray();
            return Row(label,
                Value(key+"customers-start","Kunden Periodenstart",initial.Select(C),missingStart||unknownCreation?null:initial.Length,"calculated-count","Aktiver Status am Periodenstart aus gültigen Statusintervallen.",missingStart||unknownCreation?"Status- oder Anlagehistorie am Periodenstart unvollständig.":null,text),
                Value(key+"customers-end","Kunden Periodenende",end.Select(C),missingEnd||unknownCreation?null:end.Length,"calculated-count","Aktiver Status am Periodenende aus gültigen Statusintervallen.",missingEnd||unknownCreation?"Status- oder Anlagehistorie am Periodenende unvollständig.":null,text),
                Count(key+"customers-added","Neu angelegte Kunden",added.Select(C),"Kunden mit CRM-Erstellungsdatum innerhalb der Periode.",text),
                Value(key+"customers-lost","Verlorene Kunden",departed.Select(C),missingStart||unknownCreation?null:departed.Length,"calculated-count","Am Periodenstart aktive Kunden mit dokumentiertem Wechsel in einen konfigurierten Verluststatus im Zeitraum.",missingStart||unknownCreation?"Ausgangsbestand ist nicht vollständig belegt.":null,text),
                Value(key+"churn","Churn Rate",initial.Select(C),!missingStart&&!unknownCreation&&initial.Length>0?Percent(departed.Length,initial.Length):null,"percent",
                    "Unterschiedliche verlorene Kunden aus dem Anfangsbestand / aktive Kunden am Periodenstart × 100. Löschung im CRM allein gilt nicht als Kundenverlust.",
                    missingStart||unknownCreation?"Historischer Ausgangsbestand unvollständig.":initial.Length==0?"Kein aktiver Anfangsbestand.":null,text));
        }

        private void CustomerAnalysis()
        {
            var customers=m.Customers.Where(c=>c.IsActive&&c.SourceDeletedAt is null).ToArray();
            var categories=m.Categories.Where(c=>c.IsActive).OrderBy(c=>c.Name).ToArray();
            var matrix=new List<SalesReportPanelRow>();
            var won=Deals.Where(d=>d.Status=="won").ToArray();
            foreach(var customer in customers)
                matrix.Add(Row(customer.Name,categories.Select(category=>Count($"matrix:{customer.Id}:{category.Id}",category.Name,
                    won.Where(d=>d.CustomerId==customer.Id&&d.Product?.CategoryId==category.Id).Select(D),
                    "Gewonnene Deals dieser Kategorie und dieses aktiven Kunden über die gesamte Historie. 0 kennzeichnet eine mögliche Verkaufslücke.","Lifetime")).ToArray()));
            Panel("cross-selling-matrix","analysis","Cross-Selling · Kunde × Produktkategorie","matrix",categories.Select(c=>c.Name).ToArray(),matrix);
            var cross=Value("spec:cross-selling","Cross-Selling-Quote",customers.Select(C),customers.Length>0?
                (decimal)customers.Average(c=>won.Where(d=>d.CustomerId==c.Id&&d.Product?.CategoryId is not null).Select(d=>d.Product!.CategoryId).Distinct().Count()):null,
                "factor","Mittlere Anzahl unterschiedlicher verkaufter Produktkategorien je aktivem Kunden einschließlich Kunden mit 0 Kategorien.",
                customers.Length==0?"Keine aktiven Kunden.":null,"Lifetime");
            Panel("cross-selling-rate","analysis","Produktbreite im Kundenstamm","metrics",[],[Row("",cross)]);
            var historyFrom=p.From??m.Customers.Where(c=>c.SourceCreatedAt.HasValue).Select(c=>c.SourceCreatedAt!.Value).DefaultIfEmpty(now).Min();
            Panel("churn","analysis","Kundenentwicklung und Churn","table",["Anfangsbestand","Endbestand","Neu","Verloren","Churn"],
                [CustomerHistoryRow("period:",p.Name,historyFrom,p.To<now?p.To.Value:now,Period)]);
            var orderedLoss=Lost.GroupBy(d=>NameOr(d.LossReason,"Ohne Angabe")).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key).ToArray();
            var lossRows=new List<SalesReportPanelRow>(); var total=0;
            for(var i=0;i<orderedLoss.Length;i++)
            {
                var group=orderedLoss[i];total+=group.Count();
                lossRows.Add(Row(group.Key,Count("loss-detail:"+i,group.Key,group.Select(D),"Anzahl verlorener Deals je Verlustgrund."),
                    Ratio("loss-cumulative:"+i,"Kumuliert",orderedLoss.Take(i+1).SelectMany(g=>g).Select(D),total,Lost.Length,"Kumulierte Zahl verlorener Deals / alle verlorenen Deals × 100.")));
            }
            Panel("loss-pareto","analysis","Verlustgründe mit kumuliertem Anteil","pareto",["Anzahl","Kumuliert %"],lossRows);

            string Region(SalesCustomer c)=>NameOr(c.CountryCode,"Ohne Land")+" · "+NameOr(c.RegionCode,"Ohne Region");
            string Postal(SalesCustomer c)=>NameOr(c.CountryCode,"Ohne Land")+" · "+PostalRegion(c.PostalCode);
            foreach(var dimension in new[]{ "postal","country","region" })
            {
                Func<SalesCustomer,string> group=dimension switch {"postal"=>Postal,"region"=>Region,_=>c=>NameOr(c.CountryCode,"Ohne Land")};
                var rows=new List<SalesReportPanelRow>(); var i=0;
                foreach(var g in customers.GroupBy(group).OrderByDescending(g=>Won.Where(d=>d.CustomerId.HasValue&&g.Any(c=>c.Id==d.CustomerId)).Sum(SafeAmount)).ThenBy(g=>g.Key))
                {
                    var ids=g.Select(c=>c.Id).ToHashSet();
                    var current=Won.Where(d=>d.CustomerId.HasValue&&ids.Contains(d.CustomerId.Value)).ToArray();
                    var prior=Deals.Where(d=>d.Status=="won"&&d.CustomerId.HasValue&&ids.Contains(d.CustomerId.Value)
                        && p.From.HasValue&&p.To.HasValue&&(d.ClosingAt??d.SourceModifiedAt)>=p.From.Value.AddYears(-1)&&(d.ClosingAt??d.SourceModifiedAt)<p.To.Value.AddYears(-1)).ToArray();
                    var prefix="geo:"+dimension+":"+(i++)+":";
                    var before=Money(prefix+"prior","Vorjahresumsatz",prior.Select(D),"Gleicher Zeitraum ein Jahr zuvor.",p.From.HasValue&&p.To.HasValue?$"{p.From.Value.AddYears(-1):dd.MM.yyyy}–{p.To.Value.AddYears(-1).AddDays(-1):dd.MM.yyyy}":"Kein Vorjahreszeitraum bei Lifetime");
                    if(!p.From.HasValue)b.Result.Metrics[before]=b.Result.Metrics[before] with {Value=null,UnavailableReason="Für Lifetime keinen Vergleichszeitraum vorgeben."};
                    rows.Add(Row(g.Key,Count(prefix+"calculated-count","Kunden",g.Select(C),"Aktive Kunden dieser Region.",Current),
                        Money(prefix+"revenue","Umsatz",current.Select(D),"Gewonnene Deals nach Zeitraum und aktueller Kundenregion."),before));
                }
                Panel("geo:"+dimension,"customers",dimension switch {"postal"=>"PLZ-Bereiche · Umsatz und Kundenanzahl","region"=>"Umsatz je Region · Vorjahresvergleich",_=>"Umsatz je Land · Vorjahresvergleich"},"table",
                    ["Kunden","Umsatz","Vorjahr"],rows,"Land und PLZ werden gemeinsam ausgewertet; gleiche Postleitzahlen verschiedener Länder bleiben getrennt.");
            }
            string? LeadZone(SalesLead lead)
            {
                var location=config.Locations.GetValueOrDefault("lead:"+lead.Id);
                if(!string.IsNullOrWhiteSpace(location?.CountryCode)&&!string.IsNullOrWhiteSpace(location.PostalCode))
                    return location.CountryCode.Trim().ToUpperInvariant()+" · "+PostalRegion(location.PostalCode);
                var customer=m.Customers.FirstOrDefault(c=>c.Id==lead.CustomerId);
                return customer is not null&&!string.IsNullOrWhiteSpace(customer.CountryCode)&&!string.IsNullOrWhiteSpace(customer.PostalCode)?Postal(customer):null;
            }
            var missing=m.Leads.Where(l=>l.SourceDeletedAt is null&&l.IsActive&&LeadZone(l) is null).ToArray();
            var noLocation=Count("geo:lead-coverage","Leads ohne verknüpften Standort",missing.Select(L),"Ohne kanonische Adresse oder zugeordneten Kunden lässt sich kein PLZ-Gebiet bestimmen.",Current);
            Panel("geo:coverage","customers","Weiße Flecken · Datenabdeckung","metrics",[],[Row("",noLocation)],
                "Standorte stammen aus verknüpften Kunden oder den ergänzenden Report-Standorten. Unverortbare Leads werden ausgewiesen.");
            var zones=m.Leads.Where(l=>l.IsActive&&l.SourceDeletedAt is null&&LeadZone(l) is not null)
                .Select(l=>new{Lead=l,Zone=LeadZone(l)!}).GroupBy(x=>x.Zone).Where(g=>!customers.Any(c=>Postal(c)==g.Key));
            Panel("geo:white-spots","customers","PLZ-Bereiche mit Leads ohne aktiven Kunden","bar",["Leads"],
                zones.Select((g,i)=>Row(g.Key,Count("geo:white:"+i,g.Key,g.Select(x=>L(x.Lead)),"Leads mit bekanntem Kundenstandort in einem Gebiet ohne aktiven Kunden.",Current))));
            bool Valid(decimal? lat,decimal? lon)=>lat is >= -90 and <= 90&&lon is >= -180 and <= 180;
            double Distance(SalesReportLocation location,SalesCustomer customer)
            {
                double Radians(decimal v)=>(double)v*Math.PI/180;
                var latitude=Radians(customer.Latitude!.Value-location.Latitude!.Value);
                var longitude=Radians(customer.Longitude!.Value-location.Longitude!.Value);
                var a=Math.Pow(Math.Sin(latitude/2),2)+Math.Cos(Radians(location.Latitude.Value))*Math.Cos(Radians(customer.Latitude.Value))*Math.Pow(Math.Sin(longitude/2),2);
                return 6371.0088*2*Math.Asin(Math.Sqrt(Math.Clamp(a,0,1)));
            }
            var distanceRows=m.Owners.Where(o=>o.IsActive).Select(o=>{
                var location=config.Locations.GetValueOrDefault("owner:"+o.Id);
                var assigned=customers.Where(c=>c.OwnerId==o.Id).ToArray();
                var located=assigned.Where(c=>Valid(c.Latitude,c.Longitude)).ToArray();
                var ready=location is not null&&Valid(location.Latitude,location.Longitude)&&assigned.Length>0&&located.Length==assigned.Length;
                return Row(o.DisplayName,Value("geo:distance:"+o.Id,"Entfernung zum Kunden",assigned.Select(C),
                    ready?(decimal)located.Average(c=>Distance(location!,c)):null,"km",
                    "Mittlere Luftlinienentfernung (Haversine, Erdradius 6371,0088 km) zwischen konfiguriertem Betreuerstandort und CRM-Kundenkoordinaten. Keine Fahrstrecke.",
                    ready?null:"Betreuerstandort, vollständige Kundenkoordinaten oder zugeordnete Kunden fehlen. Report-Standorte können in den AppSettings ergänzt werden.",Current));
            });
            Panel("geo:distances","customers","Entfernung Betreuer–Kunden","table",["Durchschnittliche Entfernung"],distanceRows);
        }
    }
}
