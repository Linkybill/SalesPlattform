using System.Reflection;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Services;

public static class SpecifiedReportRegressions
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var parseAreas=typeof(SalesApplicationSettingsService).GetMethod("ReadPostalAreas",BindingFlags.NonPublic|BindingFlags.Static)!;
        object? ParseAreas(string text)=>parseAreas.Invoke(null,[new Dictionary<string,System.Text.Json.JsonElement>{["sales.reports.postalAreas"]=System.Text.Json.JsonSerializer.SerializeToElement(text)}]);
        Check(ParseAreas("{invalid") is null&&ParseAreas("{}") is null,"Invalid polygon configuration must not break report requests.");
        Check(ParseAreas("{\"type\":\"FeatureCollection\",\"features\":[]}") is null,"Empty boundaries keep area mode unavailable.");
        Check(ParseAreas("{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{\"countryCode\":\"DE\",\"postalPrefix\":\"10\"},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[13.3,52.4],[13.6,52.4],[13.6,52.6],[13.3,52.6],[13.3,52.4]]]}}]}") is System.Text.Json.JsonElement,"Configured GeoJSON boundaries can reach the map.");

        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var fiscalId = Guid.NewGuid();
        var owner = new SalesOwner { Id = Guid.NewGuid(), DisplayName = "Synthetic owner" };
        var modelType = typeof(SalesReportService).GetNestedType("ReportModel", BindingFlags.NonPublic)!;
        var periodType = typeof(SalesReportService).GetNestedType("ReportPeriod", BindingFlags.NonPublic)!;
        var period = Activator.CreateInstance(periodType, "Monat", from, from.AddMonths(1), new DateOnly(2026,1,1), new DateOnly(2026,12,31), fiscalId)!;
        SalesReportEvidence Build(Dictionary<string, object> values, SalesReportConfiguration? config = null, bool sales = true)
        {
            values.TryAdd("Owners", new[] { owner });
            var ctor = modelType.GetConstructors().Single(c => c.GetParameters().Length > 1);
            var args = ctor.GetParameters().Select(p => values.GetValueOrDefault(p.Name!) ?? Array.CreateInstance(p.ParameterType.GetGenericArguments()[0], 0)).ToArray();
            return (SalesReportEvidence)typeof(SalesReportService).GetMethod("BuildEvidence", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [ctor.Invoke(args), period, now, 30, 90, sales, false, config ?? SalesReportConfiguration.Default])!;
        }
        SalesCustomer Customer(string name) => new() { Id=Guid.NewGuid(),Name=name,OwnerId=owner.Id,Status="active",SourceCreatedAt=from.AddYears(-2),Industry="Industry",CountryCode="DE",PostalCode="10115" };
        SalesDeal Deal(decimal amount, DateTimeOffset close, SalesCustomer? customer = null, string status = "won") =>
            new() { Id=Guid.NewGuid(),Name="Synthetic deal",Amount=amount,Currency="EUR",ClosingAt=close,SourceModifiedAt=close,SourceCreatedAt=close.AddDays(-30),
                OwnerId=owner.Id,CustomerId=customer?.Id,Customer=customer,Status=status };
        var a=Customer("A"); var b=Customer("B");
        var category=new SalesProductCategory { Id=Guid.NewGuid(),Key="category",Name="Category" };
        var product=new SalesProduct { Id=Guid.NewGuid(),Key="product",Name="Product",CategoryId=category.Id,Category=category };
        var first=Deal(100,from,a); first.Product=product;
        var second=Deal(200,from.AddDays(1),a); second.Product=product;
        var prior=Deal(50,from.AddYears(-1),b);
        var repeat=Deal(300,from,b);
        var unknown=Deal(400,from);
        var values=new Dictionary<string,object> { ["Customers"]=new[]{a,b},["Deals"]=new[]{first,second,prior,repeat,unknown},["Categories"]=new[]{category} };
        var report=Build(values);
        Check(report.Metrics["spec:new-revenue"].Value==100,"First historical win identifies new customer revenue.");
        Check(report.Metrics["spec:existing-revenue"].Value==500,"Repeat customer revenue includes known earlier-year wins.");
        Check(report.Metrics["spec:unknown-customer-revenue"].Value==400,"Unlinked deals stay explicit.");
        Check(report.Metrics["life:2025:won"].Value==50&&report.Metrics["life:2026:won"].Value==1000,"Lifetime must retain separate annual values.");
        Check(report.Metrics["spec:cross-selling"].Value==0.5m,"Cross-selling includes accounts without any category.");
        Check(report.Metrics["period:customers-start"].Value is null,"Missing history must not invent initial stock.");
        Check(report.Metrics["spec:arr"].Value is null,"No dated contracts must not fabricate ARR.");

        first.ContractStartAt=from;first.ContractEndAt=from.AddYears(2);first.DurationMonths=24;
        Check(Build(values).Metrics["spec:arr"].Value==50,"Annualize two-year contract amount, do not use raw contract sum.");
        first.DurationMonths=null;
        Check(Build(values).Metrics["spec:arr"].Value is null,"Incomplete contract duration invalidates ARR.");
        first.DurationMonths=24;
        second.Currency="USD";
        Check(Build(values).Metrics["life:2026:won"].Value is null,"Annual mixed currencies cannot be summed.");
        Check(Build(values).Metrics["life:2026:average"].Value is null,"Derived mean preserves mixed-currency unavailability.");
        second.Currency="EUR";

        foreach(var customer in new[]{a,b})
            customer.StatusHistory.Add(new(){Id=Guid.NewGuid(),CustomerId=customer.Id,Status="active",ValidFrom=customer.SourceCreatedAt!.Value});
        a.StatusHistory.First().ValidTo=from.AddDays(3);
        a.StatusHistory.Add(new(){Id=Guid.NewGuid(),CustomerId=a.Id,Status="lost",ValidFrom=from.AddDays(3)});
        report=Build(values);
        Check(report.Metrics["period:customers-start"].Value==2&&report.Metrics["period:customers-end"].Value==1,"Stock uses status intervals at both boundaries.");
        Check(report.Metrics["period:customers-lost"].Value==1&&report.Metrics["period:churn"].Value==50,"Churn denominator is the initial active cohort.");

        var old=Customer("Old prospect"); old.LastContactAt=now.AddMonths(-5).AddTicks(-1);
        var edge=Customer("Exactly five months"); edge.LastContactAt=now.AddMonths(-5);
        var never=Customer("Never contacted");
        var noInterest=Customer("No interest");noInterest.Status="Keine Interesse";noInterest.LastContactAt=now.AddMonths(-6);
        var recent=Customer("Recent meeting");recent.LastContactAt=now.AddMonths(-6);
        SalesAppointment Appointment(SalesCustomer customer,DateTimeOffset date)=>new(){Id=Guid.NewGuid(),Subject="Synthetic",StartsAt=date,EndsAt=date.AddHours(1),Status="planned",
            Relations=[new(){Id=Guid.NewGuid(),TargetType="customer",TargetId=customer.Id}]};
        unknown.OwnerId=null;
        Check(Build(values).Metrics["life:2026:employee:"+Guid.Empty].Value==400,"Lifetime owner distribution retains unassigned revenue.");
        unknown.OwnerId=owner.Id;
        var originalContact=old.LastContactAt;
        var contactValues=new Dictionary<string,object>{["Customers"]=new[]{old,edge,never,noInterest,recent},["Appointments"]=new[]{Appointment(old,now.AddDays(2)),Appointment(recent,now.AddDays(-1))}};
        report=Build(contactValues);
        Check(report.Metrics["dormant:prospects"].RecordKeys.SequenceEqual(new[]{"dormant-customer:"+old.Id}),"Five calendar months is strict; future meeting cannot reset last actual contact.");
        Check(report.Metrics["dormant:never"].Value==1,"Never-contacted customers have a separate list.");
        Check(report.Metrics["dormant:disinterested"].Value==1,"No-interest customers are reported separately.");
        Check(old.LastContactAt==originalContact,"Reporting must never set customer contact.");
        Check(report.Records.Values.Any(r=>r.Kind=="appointment"&&r.EndDate.HasValue),"Meeting drilldown retains its ending timestamp.");

        SalesActivity Call(DateTimeOffset date,bool reached)=>new(){Id=Guid.NewGuid(),ActivityType="call",OccurredAt=date,CountsAsConversation=reached,OwnerId=owner.Id,
            Relations=[new(){Id=Guid.NewGuid(),TargetType="customer",TargetId=old.Id},new(){Id=Guid.NewGuid(),TargetType="customer",TargetId=old.Id}]};
        var calls=new[]{Call(now.AddDays(-8),false),Call(now.AddDays(-7),true),Call(now.AddDays(-2),false),Call(now.AddDays(-1),false)};
        contactValues["Activities"]=calls;
        report=Build(contactValues);
        Check(report.Metrics["calls:under-five"].Value==1&&report.Metrics["calls:fifth"].Value==0,"Call relation duplicates must not inflate attempts.");
        Check(report.Records["attempts:customer:"+old.Id].Detail.Contains("Versuche seit letztem Gespräch: 2"),"Successful conversation resets failed-call cohort.");

        var pipeline=new SalesPipeline{Id=Guid.NewGuid(),Key="p",Name="Pipeline"};
        var stage1=new SalesPipelineStage{Id=Guid.NewGuid(),PipelineId=pipeline.Id,Key="one",Name="One",StageType="open",SortOrder=1,Probability=.5m};
        var stage2=new SalesPipelineStage{Id=Guid.NewGuid(),PipelineId=pipeline.Id,Key="two",Name="Two",StageType="open",SortOrder=2};
        var open=Deal(600,from,a,"open");open.PipelineId=pipeline.Id;open.PipelineStageId=stage1.Id;
        SalesDealStageHistory History(SalesPipelineStage stage,DateTimeOffset at,DateTimeOffset? exit=null)=>new(){Id=Guid.NewGuid(),DealId=open.Id,PipelineId=pipeline.Id,PipelineStageId=stage.Id,StageKeySnapshot=stage.Key,EnteredAt=at,ExitedAt=exit};
        var histories=new[]{History(stage1,from,from.AddDays(2)),History(stage2,from.AddDays(2)),History(stage1,from.AddDays(3),from.AddDays(7))};
        var processValues=new Dictionary<string,object>{["Deals"]=new[]{open},["Pipelines"]=new[]{pipeline},["PipelineStages"]=new[]{stage1,stage2},["StageHistory"]=histories};
        report=Build(processValues);
        var prefix=$"process:{pipeline.Id}:{stage1.Id}:";
        Check(report.Metrics[prefix+"weighted"].Value==300,"Weighted pipeline multiplies stage probability.");
        Check(report.Metrics[prefix+"entered"].Value==1&&report.Metrics[prefix+"conversion"].Value==100,"Re-entry deduplication in stage conversion.");
        Check(report.Metrics[prefix+"dwell"].Value==3,"Dwell averages completed stays.");
        stage1.Probability=null;
        Check(Build(processValues).Metrics[prefix+"weighted"].Value is null,"Missing probability is unknown, not zero.");

        var target=new SalesTarget{Id=Guid.NewGuid(),FiscalYearId=fiscalId,OwnerId=owner.Id,TargetType="revenue",TargetValue=12000,Currency="EUR"};
        var goalValues=new Dictionary<string,object>{["Targets"]=new[]{target}};
        report=Build(goalValues);
        Check(report.Panels.Single(p=>p.Key=="target-periods").Rows.Length==16,"Fallback includes twelve months and four quarters.");
        Check(report.Metrics[$"target-period:{owner.Id}:month:2026-01-01:target"].Value==1000,"Even monthly distribution.");
        Check(report.Metrics[$"target-period:{owner.Id}:quarter:2026-01-01:target"].Value==3000,"Even quarterly distribution.");
        target.TargetValue=100;
        Check(Build(goalValues).Metrics.Where(k=>k.Key.StartsWith($"target-period:{owner.Id}:month:")&&k.Key.EndsWith(":target")).Sum(k=>k.Value.Value)==100,"Cent rounding across twelve periods must preserve total.");
        target.TargetValue=12000;
        var quarters=Enumerable.Range(0,4).Select(i=>new SalesTargetPeriod{Id=Guid.NewGuid(),FiscalYearId=fiscalId,PeriodType="quarter",
            StartsAt=new DateOnly(2026,1,1).AddMonths(i*3),EndsAt=new DateOnly(2026,1,1).AddMonths((i+1)*3).AddDays(-1),DistributionWeight=new[]{20m,25m,20m,35m}[i]}).ToArray();
        goalValues["TargetPeriods"]=quarters;
        Check(Build(goalValues).Metrics[$"target-period:{owner.Id}:quarter:2026-01-01:target"].Value==2400,"Seasonal quarterly target uses configured weight.");
        Check(Build(goalValues).Metrics[$"target-period:{owner.Id}:month:2026-01-01:target"].Value==800,"Generated months inherit seasonal quarter weights.");
        Check(Build(goalValues).Metrics.Where(k=>k.Key.StartsWith($"target-period:{owner.Id}:month:")&&k.Key.EndsWith(":target")).Sum(k=>k.Value.Value)==12000,"Monthly weighted targets retain the exact annual total.");
        quarters[0].DistributionWeight=19;
        Check(Build(goalValues).Metrics[$"target-period:{owner.Id}:quarter:2026-01-01:target"].Value is null,"Invalid 99-percent distribution must not silently normalize.");

        a.Latitude=0;a.Longitude=1;b.Latitude=0;b.Longitude=1;
        var locationConfig=SalesReportConfiguration.Default with { Locations=new Dictionary<string,SalesReportLocation>{["owner:"+owner.Id]=new(0,0,null,null)} };
        Check(Math.Abs(Build(values,locationConfig).Metrics["geo:distance:"+owner.Id].Value!.Value-111.195m)<.01m,"Owner/customer distance uses real coordinates.");
        b.Latitude=null;
        Check(Build(values,locationConfig).Metrics["geo:distance:"+owner.Id].Value is null,"Partial geocoverage must not pose as full average.");
        var lead=new SalesLead{Id=Guid.NewGuid(),Name="Unlinked regional lead"};
        var leadConfig=locationConfig with{Locations=new Dictionary<string,SalesReportLocation>{["lead:"+lead.Id]=new(null,null,"DE","99999")}};
        values["Leads"]=new[]{lead};
        Check(Build(values,leadConfig).Metrics["geo:white:0"].Value==1,"Configured standalone lead locations enable white-spot reports.");
        Check(Build(values).Metrics["geo:lead-coverage"].Value==1,"Unlocated leads remain visible.");
        var calendar=new SalesWorkCalendar{Id=Guid.NewGuid(),Key="work",Name="Work",TimeZone="UTC",IsDefault=true,
            WorkingHours=[new(){DayOfWeek=5,IsWorkingDay=true,StartAt=TimeSpan.FromHours(9),EndAt=TimeSpan.FromHours(17),BreakStartAt=TimeSpan.FromHours(12),BreakEndAt=TimeSpan.FromHours(13)}]};
        lead.OwnerId=owner.Id;lead.SourceCreatedAt=now.AddHours(-3);lead.FirstActivityAt=now.AddHours(3);
        // FirstActivityAt in the future cannot be counted. Then use a completed workday.
        values["Calendars"]=new[]{calendar};
        Check(Build(values).Metrics["spec-owner:"+owner.Id+":response"].Value is null,"Future first contact is excluded.");
        lead.SourceCreatedAt=now.AddDays(-7).AddHours(-3);lead.FirstActivityAt=now.AddDays(-7).AddHours(3);
        Check(Build(values).Metrics["spec-owner:"+owner.Id+":response"].Value==5,"Working-hours response excludes lunch break.");
        calendar.Holidays.Add(new(){Date=DateOnly.FromDateTime(lead.SourceCreatedAt.Value.UtcDateTime),Name="Holiday"});
        Check(Build(values).Metrics["spec-owner:"+owner.Id+":response"].Value==0,"Holidays excluded from response time.");
        report=Build(values);
        foreach(var panel in report.Panels)
            Check(panel.Rows.SelectMany(r=>r.MetricKeys).All(report.Metrics.ContainsKey),"Every rendered panel references existing evidence: "+panel.Key);
        Check(report.Panels.Select(p=>p.Key).Distinct().Count()==report.Panels.Count,"Panel keys must be unique.");
        Check(Build(values,sales:false).Panels.Count==0,"Restricted users cannot access supplementary reports.");
        Console.WriteLine($"Specified reports: {checks} checks passed; synthetic canonical records only.");
    }
}
