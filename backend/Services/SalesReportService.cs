using System.Globalization;
using System.Security.Claims;
using IdentityPlatform.Shared.Authorization;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Services;

/// <summary>
/// Read-only fachliche projection for the dashboard webparts. The reports use
/// the tenant database as their source and never query the CRM during a page
/// request.
/// </summary>
public sealed class SalesReportService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    SalesDashboardLayoutService layoutService)
{
    public async Task<SalesDashboardResponse> GetDashboardAsync(
        ClaimsPrincipal user,
        string? timeframe,
        CancellationToken cancellationToken = default)
    {
        EnsureReportAccess(user);
        var selectedTimeframe = NormalizeTimeframe(timeframe);
        var layout = await layoutService.GetForDashboardAsync(user, cancellationToken);

        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var db = session.Context;
        var now = DateTimeOffset.UtcNow;
        var period = await LoadPeriodAsync(db, selectedTimeframe, now, cancellationToken);
        var model = await LoadModelAsync(db, cancellationToken);

        var canSeeManagement = SalesDashboardLayoutService.HasAnyRole(user, "sales-manager", "sales-management");
        var canSeeCleanup = SalesDashboardLayoutService.HasAnyRole(user, "sales-manager", "sales-management", "sales-backoffice");
        return new(
            now,
            selectedTimeframe,
            period.Name,
            layout,
            canSeeManagement ? BuildCockpit(model, period, now) : null,
            canSeeManagement ? BuildTeam(model, period, now) : null,
            canSeeManagement ? BuildMeetings(model, period, now) : null,
            canSeeManagement ? BuildAnalysis(model, period, now) : null,
            canSeeManagement ? BuildCustomers(model, period, now) : null,
            BuildGoals(model, period, now),
            canSeeCleanup ? BuildCleanup(model) : null,
            BuildService(model, period, now),
            BuildCommercial(model, period, now));
    }

    public async Task<SalesDashboardLayoutResponse> GetLayoutAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
        => await layoutService.GetAsync(user, cancellationToken);

    public async Task<SalesDashboardLayoutResponse> SaveLayoutAsync(
        SaveSalesDashboardLayoutRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
        => await layoutService.SaveAsync(request, user, cancellationToken);

    private static void EnsureReportAccess(ClaimsPrincipal user)
    {
        if (!SalesDashboardLayoutService.HasAnyRole(user, "sales-user", "sales-manager", "sales-management", "sales-backoffice"))
            throw new UnauthorizedAccessException("Für die Sales-Reports ist keine passende App-Rolle vorhanden.");
    }

    private static async Task<ReportModel> LoadModelAsync(
        SalesPlattformDbContext db,
        CancellationToken cancellationToken)
    {
        var owners = await db.SalesOwners.AsNoTracking().ToArrayAsync(cancellationToken);
        var customers = await db.SalesCustomers.AsNoTracking().ToArrayAsync(cancellationToken);
        var deals = await db.SalesDeals.AsNoTracking()
            .Include(deal => deal.Customer)
            .Include(deal => deal.Owner)
            .Include(deal => deal.Pipeline)
            .Include(deal => deal.PipelineStage)
            .Include(deal => deal.Product)
                .ThenInclude(product => product!.Category)
            .ToArrayAsync(cancellationToken);
        var contracts = await db.SalesContracts.AsNoTracking().ToArrayAsync(cancellationToken);
        var activities = await db.SalesActivities.AsNoTracking().ToArrayAsync(cancellationToken);
        var appointments = await db.SalesAppointments.AsNoTracking()
            .Include(appointment => appointment.StatusHistory)
            .ToArrayAsync(cancellationToken);
        var serviceCases = await db.SalesServiceCases.AsNoTracking().ToArrayAsync(cancellationToken);
        var offers = await db.SalesOffers.AsNoTracking().ToArrayAsync(cancellationToken);
        var orders = await db.SalesOrders.AsNoTracking().ToArrayAsync(cancellationToken);
        var invoices = await db.SalesInvoices.AsNoTracking().ToArrayAsync(cancellationToken);
        var stageHistory = await db.SalesDealStageHistory.AsNoTracking().ToArrayAsync(cancellationToken);
        var targets = await db.SalesTargets.AsNoTracking().ToArrayAsync(cancellationToken);
        var fiscalYears = await db.SalesFiscalYears.AsNoTracking().ToArrayAsync(cancellationToken);
        var targetPeriods = await db.SalesTargetPeriods.AsNoTracking().ToArrayAsync(cancellationToken);
        var workItems = await db.SalesWorkItems.AsNoTracking()
            .Include(item => item.Owner)
            .ToArrayAsync(cancellationToken);
        var duplicateCandidates = await db.SalesDuplicateCandidates.AsNoTracking()
            .Include(candidate => candidate.CustomerA)
            .Include(candidate => candidate.CustomerB)
            .ToArrayAsync(cancellationToken);
        var qualityFindings = await db.SalesDataQualityFindings.AsNoTracking().ToArrayAsync(cancellationToken);
        var links = await db.IntegrationEntityLinks.AsNoTracking().ToArrayAsync(cancellationToken);
        var categories = await db.SalesProductCategories.AsNoTracking().ToArrayAsync(cancellationToken);
        var pipelines = await db.SalesPipelines.AsNoTracking().ToArrayAsync(cancellationToken);
        var pipelineStages = await db.SalesPipelineStages.AsNoTracking().ToArrayAsync(cancellationToken);

        return new(
            owners,
            customers,
            deals,
            contracts,
            activities,
            appointments,
            serviceCases,
            offers,
            orders,
            invoices,
            stageHistory,
            targets,
            fiscalYears,
            targetPeriods,
            workItems,
            duplicateCandidates,
            qualityFindings,
            links,
            categories,
            pipelines,
            pipelineStages);
    }

    private static async Task<ReportPeriod> LoadPeriodAsync(
        SalesPlattformDbContext db,
        string timeframe,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var fiscalYear = await db.SalesFiscalYears.AsNoTracking()
            .Where(item => item.StartsAt <= today && item.EndsAt >= today && !item.IsClosed)
            .OrderByDescending(item => item.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);
        var start = fiscalYear?.StartsAt ?? new DateOnly(today.Year, 1, 1);
        var end = fiscalYear?.EndsAt ?? new DateOnly(today.Year, 12, 31);

        return timeframe switch
        {
            "month" => new("Monat", StartOfMonth(now), StartOfMonth(now).AddMonths(1), start, end, fiscalYear?.Id),
            "lifetime" => new("Lifetime", null, null, start, end, fiscalYear?.Id),
            _ => new("Geschäftsjahr", start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), start, end, fiscalYear?.Id)
        };
    }

    private static SalesCockpitReport BuildCockpit(ReportModel model, ReportPeriod period, DateTimeOffset now)
    {
        var won = model.Deals.Where(deal => IsActive(deal) && IsStatus(deal.Status, "won") && InPeriod(deal.ClosingAt ?? deal.SourceModifiedAt, period)).ToArray();
        var lost = model.Deals.Where(deal => IsActive(deal) && IsStatus(deal.Status, "lost") && InPeriod(deal.ClosingAt ?? deal.SourceModifiedAt, period)).ToArray();
        var open = model.Deals.Where(deal => IsActive(deal) && IsStatus(deal.Status, "open") && !(deal.PipelineStage?.IsTerminal ?? false)).ToArray();
        var annualTarget = AnnualRevenueTarget(model, period);
        var wonRevenue = SumAmount(won);
        var pipeline = SumAmount(open);
        var remainingTarget = Math.Max(annualTarget - wonRevenue, 0m);
        var wonAndLost = won.Length + lost.Length;
        var firstWonByCustomer = won
            .Where(deal => deal.CustomerId.HasValue)
            .GroupBy(deal => deal.CustomerId!.Value)
            .Select(group => group.OrderBy(deal => deal.ClosingAt ?? deal.SourceCreatedAt ?? DateTimeOffset.MaxValue).First())
            .ToArray();
        var newRevenue = SumAmount(firstWonByCustomer);
        var stale = open.Count(deal => (deal.LastActivityAt ?? deal.SourceCreatedAt) is { } activity && activity <= now.AddDays(-30));
        var expiring = model.Contracts.Count(contract => contract.IsActive && contract.EndAt is { } end && end >= now && end <= now.AddDays(90));
        var stages = open
            .GroupBy(deal => deal.PipelineStage?.Name ?? "Ohne Stufe")
            .OrderByDescending(group => SumAmount(group))
            .Select(group => new FunnelStageReport(group.Key, group.Count(), SumAmount(group), null))
            .ToArray();
        var actionPoints = model.WorkItems
            .Where(item => IsOpenWorkItem(item.Status))
            .OrderByDescending(item => item.PriorityScore ?? 0)
            .ThenBy(item => item.DueAt ?? DateTimeOffset.MaxValue)
            .Take(5)
            .Select(item => new ActionPointReport(item.Id, item.Title, item.Reason, item.SourceRuleCode, item.PriorityScore ?? 0, item.DueAt))
            .ToArray();

        return new(
            period.Name,
            "EUR",
            wonRevenue,
            annualTarget,
            Percent(wonRevenue, annualTarget),
            Percent(won.Length, wonAndLost),
            pipeline,
            Percent(pipeline, remainingTarget),
            AverageSalesCycleDays(won),
            SumRecurringAmount(model.Contracts),
            newRevenue,
            Math.Max(wonRevenue - newRevenue, 0m),
            stale,
            expiring,
            stages,
            actionPoints);
    }

    private static SalesTeamReport BuildTeam(ReportModel model, ReportPeriod period, DateTimeOffset now)
    {
        var owners = model.Owners.Where(owner => owner.IsActive).OrderBy(owner => owner.DisplayName).ToArray();
        var rows = owners.Select(owner =>
        {
            var target = TargetForOwner(model, owner.Id, period);
            var won = model.Deals.Where(deal => deal.OwnerId == owner.Id && IsActive(deal) && IsStatus(deal.Status, "won") && InPeriod(deal.ClosingAt ?? deal.SourceModifiedAt, period)).ToArray();
            var open = model.Deals.Where(deal => deal.OwnerId == owner.Id && IsActive(deal) && IsStatus(deal.Status, "open") && !(deal.PipelineStage?.IsTerminal ?? false)).ToArray();
            var calls = model.Activities.Where(activity => activity.OwnerId == owner.Id && IsCall(activity) && InPeriod(activity.OccurredAt, period)).ToArray();
            var appointments = model.Appointments.Where(appointment => appointment.OwnerId == owner.Id && InPeriod(appointment.StartsAt, period)).ToArray();
            var attainment = Percent(SumAmount(won), target);
            var pace = attainment - TimeShare(period, now);
            return new TeamMemberReport(
                owner.Id,
                owner.DisplayName,
                SumAmount(won),
                target,
                attainment,
                pace,
                open.Length,
                SumAmount(open),
                appointments.Length,
                calls.Length,
                calls.Count(call => call.CountsAsConversation == true),
                appointments.GroupBy(appointment => string.IsNullOrWhiteSpace(appointment.AppointmentType) ? "Ohne Typ" : appointment.AppointmentType!)
                    .OrderBy(group => group.Key)
                    .Select(group => new BreakdownReport(group.Key, group.Count(), null))
                    .ToArray());
        }).OrderByDescending(row => row.AttainmentPercent).ToArray();

        var appointmentTypes = model.Appointments
            .Where(appointment => InPeriod(appointment.StartsAt, period))
            .GroupBy(appointment => string.IsNullOrWhiteSpace(appointment.AppointmentType) ? "Ohne Typ" : appointment.AppointmentType!)
            .OrderByDescending(group => group.Count())
            .Select(group => new BreakdownReport(group.Key, group.Count(), null))
            .ToArray();
        return new(period.Name, TimeShare(period, now), rows, appointmentTypes);
    }

    private static SalesMeetingReport BuildMeetings(ReportModel model, ReportPeriod period, DateTimeOffset now)
    {
        var weekStart = StartOfWeek(now);
        var weekEnd = weekStart.AddDays(7);
        var weekAppointments = model.Appointments.Where(appointment => appointment.StartsAt >= weekStart && appointment.StartsAt < weekEnd).ToArray();
        var inPeriod = model.Appointments.Where(appointment => InPeriod(appointment.StartsAt, period)).ToArray();
        var completed = inPeriod.Count(appointment => AppointmentState(appointment.Status) == "completed");
        var cancelled = inPeriod.Count(appointment => AppointmentState(appointment.Status) == "cancelled");
        var rescheduled = inPeriod.Count(appointment => appointment.RescheduleCount > 0 || AppointmentState(appointment.Status) == "rescheduled");
        var noShow = inPeriod.Count(appointment => AppointmentState(appointment.Status) == "no-show");
        var planned = inPeriod.Length;
        var created = inPeriod.Count(appointment => appointment.SourceCreatedAt is { } createdAt && InPeriod(createdAt, period));
        return new(
            period.Name,
            created,
            weekAppointments.Length,
            planned,
            completed,
            cancelled,
            rescheduled,
            noShow,
            Percent(completed, planned),
            Percent(noShow, planned),
            Percent(rescheduled, planned),
            inPeriod
                .GroupBy(appointment => string.IsNullOrWhiteSpace(appointment.AppointmentType) ? "Ohne Typ" : appointment.AppointmentType!)
                .OrderByDescending(group => group.Count())
                .Select(group => new BreakdownReport(group.Key, group.Count(), null))
                .ToArray(),
            inPeriod
                .GroupBy(appointment => AppointmentState(appointment.Status))
                .OrderByDescending(group => group.Count())
                .Select(group => new BreakdownReport(group.Key, group.Count(), null))
                .ToArray());
    }

    private static SalesAnalysisReport BuildAnalysis(ReportModel model, ReportPeriod period, DateTimeOffset now)
    {
        var won = model.Deals.Where(deal => IsActive(deal) && IsStatus(deal.Status, "won") && InPeriod(deal.ClosingAt ?? deal.SourceModifiedAt, period)).ToArray();
        var lost = model.Deals.Where(deal => IsActive(deal) && IsStatus(deal.Status, "lost") && InPeriod(deal.ClosingAt ?? deal.SourceModifiedAt, period)).ToArray();
        var products = won.GroupBy(deal => deal.Product?.Name ?? "Ohne Produkt").OrderByDescending(group => SumAmount(group)).Take(8).Select(group => new BreakdownReport(group.Key, group.Count(), SumAmount(group))).ToArray();
        var industries = won.GroupBy(deal => deal.Customer?.Industry ?? "Ohne Branche").OrderByDescending(group => SumAmount(group)).Take(8).Select(group => new BreakdownReport(group.Key, group.Count(), SumAmount(group))).ToArray();
        var regions = won.GroupBy(deal => PostalRegion(deal.Customer?.PostalCode)).OrderByDescending(group => SumAmount(group)).Take(20).Select(group => new BreakdownReport(group.Key, group.Count(), SumAmount(group))).ToArray();
        var lossReasons = lost.GroupBy(deal => string.IsNullOrWhiteSpace(deal.LossReason) ? "Ohne Angabe" : deal.LossReason!).OrderByDescending(group => group.Count()).Select(group => new BreakdownReport(group.Key, group.Count(), SumAmount(group))).ToArray();
        var dwell = model.StageHistory
            .Where(history => InPeriod(history.EnteredAt, period))
            .GroupBy(history => history.StageKeySnapshot)
            .OrderByDescending(group => group.Count())
            .Select(group => new StageDwellReport(group.Key, group.Count(), Math.Round(group.Average(history => Math.Max(0, ((history.ExitedAt ?? now) - history.EnteredAt).TotalDays)), 1)))
            .ToArray();
        var wonByCustomer = won.Where(deal => deal.CustomerId.HasValue).GroupBy(deal => deal.CustomerId!.Value).ToDictionary(group => group.Key, group => group);
        var crossSelling = model.Customers.Where(customer => customer.IsActive && wonByCustomer.ContainsKey(customer.Id)).Select(customer =>
        {
            var categories = wonByCustomer[customer.Id].Select(deal => deal.Product?.Category?.Name ?? "Ohne Kategorie").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
            return new CrossSellingReport(customer.Id, customer.Name, categories, categories.Length);
        }).OrderByDescending(item => item.CategoryCount).ThenBy(item => item.CustomerName).Take(50).ToArray();
        return new(period.Name, products, industries, regions, lossReasons, dwell, crossSelling);
    }

    private static SalesCustomerReport BuildCustomers(ReportModel model, ReportPeriod period, DateTimeOffset now)
    {
        var customerLinks = model.Links
            .Where(link => link.InternalEntityType == "customer" && !string.IsNullOrWhiteSpace(link.ExternalUrl))
            .GroupBy(link => link.InternalEntityId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.ExternalUrl).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)));
        var dealsByCustomer = model.Deals.Where(IsActive).Where(deal => deal.CustomerId.HasValue).GroupBy(deal => deal.CustomerId!.Value).ToDictionary(group => group.Key, group => group.ToArray());
        var rows = model.Customers.Where(customer => customer.IsActive).Select(customer => new CustomerMapPoint(
            customer.Id,
            customer.Name,
            customer.Owner?.DisplayName ?? model.Owners.FirstOrDefault(owner => owner.Id == customer.OwnerId)?.DisplayName,
            customer.CountryCode,
            customer.PostalCode,
            customer.RegionCode,
            customer.Latitude,
            customer.Longitude,
            customer.LifetimeRevenue ?? 0m,
            customer.LastContactAt,
            dealsByCustomer.TryGetValue(customer.Id, out var deals) ? deals.Count(deal => IsStatus(deal.Status, "open")) : 0,
            customer.NeedsReview || !customer.Latitude.HasValue || !customer.Longitude.HasValue,
            customerLinks.TryGetValue(customer.Id, out var url) ? url : null)).OrderByDescending(customer => customer.LifetimeRevenue).ToArray();
        var regions = rows.GroupBy(row => PostalRegion(row.PostalCode)).OrderByDescending(group => group.Sum(row => row.LifetimeRevenue)).Select(group => new BreakdownReport(group.Key, group.Count(), group.Sum(row => row.LifetimeRevenue))).Take(20).ToArray();
        return new(period.Name, rows, rows.Count(row => row.NeedsReview), regions);
    }

    private static SalesGoalsReport BuildGoals(ReportModel model, ReportPeriod period, DateTimeOffset now)
    {
        var rows = model.Owners.Where(owner => owner.IsActive).Select(owner =>
        {
            var target = TargetForOwner(model, owner.Id, period);
            var won = model.Deals.Where(deal => deal.OwnerId == owner.Id && IsActive(deal) && IsStatus(deal.Status, "won") && InPeriod(deal.ClosingAt ?? deal.SourceModifiedAt, period)).Sum(SafeAmount);
            var attainment = Percent(won, target);
            var pace = attainment - TimeShare(period, now);
            return new GoalPaceReport(owner.Id, owner.DisplayName, target, won, attainment, TimeShare(period, now), pace, PaceStatus(pace));
        }).OrderByDescending(row => row.AttainmentPercent).ToArray();
        return new(period.Name, TimeShare(period, now), rows);
    }

    private static SalesCleanupReport BuildCleanup(ReportModel model)
    {
        var candidates = model.DuplicateCandidates
            .Where(candidate => !string.Equals(candidate.Status, "resolved", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Score)
            .Take(100)
            .Select(candidate => new DuplicateCandidateReport(
                candidate.Id,
                candidate.CustomerA?.Name ?? "Unbekannter Kunde",
                candidate.CustomerB?.Name ?? "Unbekannter Kunde",
                candidate.Score,
                candidate.Confidence,
                candidate.Status,
                candidate.MatchDetailsJson))
            .ToArray();
        var findings = model.QualityFindings
            .Where(finding => !string.Equals(finding.Status, "resolved", StringComparison.OrdinalIgnoreCase))
            .GroupBy(finding => finding.Severity)
            .OrderByDescending(group => group.Key)
            .Select(group => new BreakdownReport(group.Key, group.Count(), null))
            .ToArray();
        return new(candidates, findings, candidates.Length + model.QualityFindings.Count(finding => !string.Equals(finding.Status, "resolved", StringComparison.OrdinalIgnoreCase)));
    }

    private static SalesServiceReport BuildService(ReportModel model, ReportPeriod period, DateTimeOffset now)
    {
        var serviceCaseLinks = model.Links
            .Where(link => link.InternalEntityType == CrmEntityTypes.ServiceCase
                && !string.IsNullOrWhiteSpace(link.ExternalUrl))
            .GroupBy(link => link.InternalEntityId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.ExternalUrl).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)));
        var cases = model.ServiceCases
            .Where(item => item.IsActive && InPeriod(item.OpenedAt ?? item.SourceCreatedAt, period))
            .ToArray();
        var open = cases.Where(item => !IsClosedServiceCase(item.Status)).ToArray();
        var overdue = open.Where(item => item.DueAt is { } dueAt && dueAt < now).ToArray();
        var urgent = open
            .Where(item => item.Priority.Contains("critical", StringComparison.OrdinalIgnoreCase)
                || item.Priority.Contains("high", StringComparison.OrdinalIgnoreCase)
                || item.Priority.Contains("hoch", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.DueAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(item => item.OpenedAt ?? DateTimeOffset.MinValue)
            .Take(8)
            .Select(item => new ServiceCaseSummary(
                item.Id,
                item.Subject,
                item.Status,
                item.Priority,
                item.OpenedAt,
                item.DueAt,
                item.CustomerId.HasValue
                    ? model.Customers.FirstOrDefault(customer => customer.Id == item.CustomerId)?.Name
                    : null,
                serviceCaseLinks.TryGetValue(item.Id, out var externalUrl) ? externalUrl : null))
            .ToArray();
        return new(
            period.Name,
            cases.Length,
            open.Length,
            overdue.Length,
            urgent.Length,
            cases.GroupBy(item => string.IsNullOrWhiteSpace(item.Status) ? "Ohne Status" : item.Status)
                .OrderByDescending(group => group.Count())
                .Select(group => new BreakdownReport(group.Key, group.Count(), null))
                .ToArray(),
            cases.GroupBy(item => string.IsNullOrWhiteSpace(item.Priority) ? "Ohne Priorität" : item.Priority)
                .OrderByDescending(group => group.Count())
                .Select(group => new BreakdownReport(group.Key, group.Count(), null))
                .ToArray(),
            urgent);
    }

    private static SalesCommercialReport BuildCommercial(ReportModel model, ReportPeriod period, DateTimeOffset now)
    {
        var offers = model.Offers.Where(item => item.IsActive && InPeriod(item.IssuedAt ?? item.SourceCreatedAt, period)).ToArray();
        var orders = model.Orders.Where(item => item.IsActive && InPeriod(item.OrderedAt ?? item.SourceCreatedAt, period)).ToArray();
        var invoices = model.Invoices.Where(item => item.IsActive && InPeriod(item.IssuedAt ?? item.SourceCreatedAt, period)).ToArray();
        var openOffers = offers.Where(item => !IsOfferClosed(item.Status)).ToArray();
        var openOrders = orders.Where(item => !IsOrderClosed(item.Status)).ToArray();
        var openInvoices = invoices.Where(item => !IsInvoiceClosed(item.Status) && (item.OpenAmount ?? item.Amount ?? 0m) > 0m).ToArray();
        var overdueOffers = openOffers.Count(item => item.ValidUntil is { } validUntil && validUntil < now);
        var overdueOrders = openOrders.Count(item => item.PromisedAt is { } promisedAt && promisedAt < now);
        var overdueInvoices = openInvoices.Count(item => item.DueAt is { } dueAt && dueAt < now);
        var breakdown = offers.GroupBy(item => string.IsNullOrWhiteSpace(item.Status) ? "Ohne Status" : item.Status)
            .OrderByDescending(group => group.Count())
            .Select(group => new BreakdownReport($"Angebot · {group.Key}", group.Count(), SumAmount(group.Select(item => item.Amount ?? 0m))))
            .Concat(orders.GroupBy(item => string.IsNullOrWhiteSpace(item.Status) ? "Ohne Status" : item.Status)
                .OrderByDescending(group => group.Count())
                .Select(group => new BreakdownReport($"Auftrag · {group.Key}", group.Count(), SumAmount(group.Select(item => item.Amount ?? 0m)))))
            .Concat(invoices.GroupBy(item => string.IsNullOrWhiteSpace(item.Status) ? "Ohne Status" : item.Status)
                .OrderByDescending(group => group.Count())
                .Select(group => new BreakdownReport($"Rechnung · {group.Key}", group.Count(), SumAmount(group.Select(item => item.OpenAmount ?? item.Amount ?? 0m)))))
            .Take(20)
            .ToArray();
        return new(
            period.Name,
            offers.Length,
            openOffers.Length,
            SumAmount(offers.Select(item => item.Amount ?? 0m)),
            overdueOffers,
            orders.Length,
            openOrders.Length,
            SumAmount(orders.Select(item => item.Amount ?? 0m)),
            overdueOrders,
            invoices.Length,
            openInvoices.Length,
            SumAmount(openInvoices.Select(item => item.OpenAmount ?? item.Amount ?? 0m)),
            overdueInvoices,
            breakdown);
    }

    private static decimal AnnualRevenueTarget(ReportModel model, ReportPeriod period)
        => model.Targets.Where(target => target.FiscalYearId == period.FiscalYearId && IsRevenueTarget(target.TargetType) && target.TargetPeriodId is null).Sum(target => target.TargetValue);

    private static decimal TargetForOwner(ReportModel model, Guid ownerId, ReportPeriod period)
    {
        var annual = model.Targets.Where(target => target.OwnerId == ownerId && target.FiscalYearId == period.FiscalYearId && IsRevenueTarget(target.TargetType) && target.TargetPeriodId is null).Sum(target => target.TargetValue);
        if (annual > 0 || period.Name == "Geschäftsjahr") return annual;
        return model.Targets.Where(target => target.OwnerId == ownerId && target.FiscalYearId == period.FiscalYearId && IsRevenueTarget(target.TargetType)).Sum(target => target.TargetValue);
    }

    private static bool IsRevenueTarget(string targetType)
        => targetType.Contains("revenue", StringComparison.OrdinalIgnoreCase)
            || targetType.Contains("umsatz", StringComparison.OrdinalIgnoreCase)
            || targetType.Contains("amount", StringComparison.OrdinalIgnoreCase);

    private static decimal SumAmount(IEnumerable<SalesDeal> deals) => deals.Sum(SafeAmount);
    private static decimal SafeAmount(SalesDeal deal) => deal.Amount ?? 0m;
    private static decimal SumAmount(IEnumerable<decimal> amounts) => amounts.Sum();
    private static decimal SumRecurringAmount(IEnumerable<SalesContract> contracts)
        => contracts.Where(contract => contract.IsActive).Sum(contract => contract.RecurringAmount ?? 0m);

    private static double? AverageSalesCycleDays(IEnumerable<SalesDeal> deals)
    {
        var values = deals.Where(deal => deal.SourceCreatedAt.HasValue && deal.ClosingAt.HasValue).Select(deal => (deal.ClosingAt!.Value - deal.SourceCreatedAt!.Value).TotalDays).Where(value => value >= 0).ToArray();
        return values.Length == 0 ? null : Math.Round(values.Average(), 1);
    }

    private static decimal Percent(decimal numerator, decimal denominator)
        => denominator <= 0 ? 0m : Math.Round(numerator / denominator * 100m, 1);
    private static decimal Percent(int numerator, int denominator) => Percent((decimal)numerator, denominator);
    private static bool IsActive(SalesDeal deal) => deal.IsActive && deal.SourceDeletedAt is null;
    private static bool IsOpenWorkItem(string status) => status is "open" or "scheduled" or "snoozed";
    private static bool IsCall(SalesActivity activity) => string.Equals(activity.ActivityType, "call", StringComparison.OrdinalIgnoreCase);
    private static bool IsStatus(string? value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    private static bool IsClosedServiceCase(string? status)
        => IsAnyStatus(status, "closed", "resolved", "completed", "erledigt", "gelöst", "cancelled", "abgeschlossen");
    private static bool IsOfferClosed(string? status)
        => IsAnyStatus(status, "accepted", "approved", "rejected", "declined", "cancelled", "expired", "angenommen", "abgelehnt");
    private static bool IsOrderClosed(string? status)
        => IsAnyStatus(status, "delivered", "completed", "cancelled", "closed", "geliefert", "abgeschlossen");
    private static bool IsInvoiceClosed(string? status)
        => IsAnyStatus(status, "paid", "settled", "cancelled", "bezahlt", "beglichen");
    private static bool IsAnyStatus(string? value, params string[] expected)
        => expected.Any(item => value?.Contains(item, StringComparison.OrdinalIgnoreCase) == true);

    private static bool InPeriod(DateTimeOffset? value, ReportPeriod period)
        => value.HasValue && (!period.From.HasValue || value.Value >= period.From.Value) && (!period.To.HasValue || value.Value < period.To.Value);

    private static DateTimeOffset StartOfMonth(DateTimeOffset now)
        => new(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        var difference = ((int)now.DayOfWeek + 6) % 7;
        return new DateTimeOffset(now.Date.AddDays(-difference), TimeSpan.Zero);
    }

    private static decimal TimeShare(ReportPeriod period, DateTimeOffset now)
    {
        if (period.From is null || period.To is null) return 100m;
        var total = (period.To.Value - period.From.Value).TotalDays;
        var elapsed = Math.Clamp((now - period.From.Value).TotalDays, 0, total);
        return total <= 0 ? 100m : Math.Round((decimal)(elapsed / total * 100), 1);
    }

    private static string PaceStatus(decimal pace)
        => pace >= 5 ? "Vor Plan" : pace >= -5 ? "Im Plan" : pace >= -15 ? "Rückstand" : "Kritisch";

    private static string AppointmentState(string? status)
    {
        var value = (status ?? "planned").Trim().ToLowerInvariant();
        if (value.Contains("cancel") || value.Contains("absag")) return "cancelled";
        if (value.Contains("resched") || value.Contains("verschob")) return "rescheduled";
        if (value.Contains("no-show") || value.Contains("noshow") || value.Contains("nicht erschienen")) return "no-show";
        if (value.Contains("complete") || value.Contains("held") || value.Contains("stattgef")) return "completed";
        return "planned";
    }

    private static string PostalRegion(string? postalCode)
        => string.IsNullOrWhiteSpace(postalCode) ? "Ohne PLZ" : new string(postalCode.Where(char.IsDigit).Take(2).ToArray()) is { Length: > 0 } digits ? digits : "Ohne PLZ";

    private static string NormalizeTimeframe(string? timeframe)
        => timeframe?.Trim().ToLowerInvariant() switch { "month" => "month", "lifetime" => "lifetime", _ => "year" };

    private sealed record ReportPeriod(string Name, DateTimeOffset? From, DateTimeOffset? To, DateOnly FiscalYearStart, DateOnly FiscalYearEnd, Guid? FiscalYearId);

    private sealed record ReportModel(
        IReadOnlyCollection<SalesOwner> Owners,
        IReadOnlyCollection<SalesCustomer> Customers,
        IReadOnlyCollection<SalesDeal> Deals,
        IReadOnlyCollection<SalesContract> Contracts,
        IReadOnlyCollection<SalesActivity> Activities,
        IReadOnlyCollection<SalesAppointment> Appointments,
        IReadOnlyCollection<SalesServiceCase> ServiceCases,
        IReadOnlyCollection<SalesOffer> Offers,
        IReadOnlyCollection<SalesOrder> Orders,
        IReadOnlyCollection<SalesInvoice> Invoices,
        IReadOnlyCollection<SalesDealStageHistory> StageHistory,
        IReadOnlyCollection<SalesTarget> Targets,
        IReadOnlyCollection<SalesFiscalYear> FiscalYears,
        IReadOnlyCollection<SalesTargetPeriod> TargetPeriods,
        IReadOnlyCollection<SalesWorkItem> WorkItems,
        IReadOnlyCollection<SalesDuplicateCandidate> DuplicateCandidates,
        IReadOnlyCollection<SalesDataQualityFinding> QualityFindings,
        IReadOnlyCollection<IntegrationEntityLink> Links,
        IReadOnlyCollection<SalesProductCategory> Categories,
        IReadOnlyCollection<SalesPipeline> Pipelines,
        IReadOnlyCollection<SalesPipelineStage> PipelineStages);
}

public sealed record SalesDashboardResponse(
    DateTimeOffset GeneratedAt,
    string Timeframe,
    string PeriodName,
    SalesDashboardLayoutResponse Layout,
    SalesCockpitReport? Cockpit,
    SalesTeamReport? Team,
    SalesMeetingReport? Meetings,
    SalesAnalysisReport? Analysis,
    SalesCustomerReport? Customers,
    SalesGoalsReport Goals,
    SalesCleanupReport? Cleanup,
    SalesServiceReport Service,
    SalesCommercialReport Commercial);

public sealed record SalesCockpitReport(
    string PeriodName,
    string Currency,
    decimal WonRevenue,
    decimal AnnualTarget,
    decimal TargetAttainmentPercent,
    decimal WinRatePercent,
    decimal PipelineAmount,
    decimal PipelineCoveragePercent,
    double? AverageSalesCycleDays,
    decimal Arr,
    decimal NewRevenue,
    decimal ExistingRevenue,
    int StaleDealCount,
    int ExpiringContractCount,
    IReadOnlyCollection<FunnelStageReport> Funnel,
    IReadOnlyCollection<ActionPointReport> ActionPoints);

public sealed record FunnelStageReport(string Name, int DealCount, decimal Amount, decimal? ConversionPercent);
public sealed record ActionPointReport(Guid Id, string Title, string? Reason, string? RuleCode, decimal PriorityScore, DateTimeOffset? DueAt);
public sealed record SalesTeamReport(string PeriodName, decimal TimeSharePercent, IReadOnlyCollection<TeamMemberReport> Members, IReadOnlyCollection<BreakdownReport> AppointmentTypes);
public sealed record TeamMemberReport(Guid OwnerId, string Name, decimal WonRevenue, decimal Target, decimal AttainmentPercent, decimal Pace, int OpenDealCount, decimal PipelineAmount, int AppointmentCount, int CallCount, int ConversationCount, IReadOnlyCollection<BreakdownReport> AppointmentTypes);
public sealed record SalesMeetingReport(string PeriodName, int NewAppointments, int CurrentWeekAppointments, int PlannedAppointments, int CompletedAppointments, int CancelledAppointments, int RescheduledAppointments, int NoShowAppointments, decimal CompletionRatePercent, decimal NoShowRatePercent, decimal RescheduleRatePercent, IReadOnlyCollection<BreakdownReport> ByType, IReadOnlyCollection<BreakdownReport> ByStatus);
public sealed record BreakdownReport(string Label, int Count, decimal? Amount);
public sealed record SalesAnalysisReport(string PeriodName, IReadOnlyCollection<BreakdownReport> ByProduct, IReadOnlyCollection<BreakdownReport> ByIndustry, IReadOnlyCollection<BreakdownReport> ByRegion, IReadOnlyCollection<BreakdownReport> LossReasons, IReadOnlyCollection<StageDwellReport> StageDwell, IReadOnlyCollection<CrossSellingReport> CrossSelling);
public sealed record StageDwellReport(string Stage, int DealCount, double AverageDays);
public sealed record CrossSellingReport(Guid CustomerId, string CustomerName, IReadOnlyCollection<string> Categories, int CategoryCount);
public sealed record SalesCustomerReport(string PeriodName, IReadOnlyCollection<CustomerMapPoint> Customers, int UnmappedCount, IReadOnlyCollection<BreakdownReport> Regions);
public sealed record CustomerMapPoint(Guid Id, string Name, string? OwnerName, string? CountryCode, string? PostalCode, string? RegionCode, decimal? Latitude, decimal? Longitude, decimal LifetimeRevenue, DateTimeOffset? LastContactAt, int OpenDealCount, bool NeedsReview, string? ExternalUrl);
public sealed record SalesGoalsReport(string PeriodName, decimal TimeSharePercent, IReadOnlyCollection<GoalPaceReport> Members);
public sealed record GoalPaceReport(Guid OwnerId, string Name, decimal Target, decimal Achieved, decimal AttainmentPercent, decimal TimeSharePercent, decimal Pace, string Status);
public sealed record SalesCleanupReport(IReadOnlyCollection<DuplicateCandidateReport> Duplicates, IReadOnlyCollection<BreakdownReport> QualityFindings, int OpenFindingCount);
public sealed record DuplicateCandidateReport(Guid Id, string CustomerA, string CustomerB, decimal Score, string Confidence, string Status, string? MatchDetailsJson);
public sealed record SalesServiceReport(string PeriodName, int TotalCases, int OpenCases, int OverdueCases, int UrgentCases, IReadOnlyCollection<BreakdownReport> ByStatus, IReadOnlyCollection<BreakdownReport> ByPriority, IReadOnlyCollection<ServiceCaseSummary> UrgentItems);
public sealed record ServiceCaseSummary(Guid Id, string Subject, string Status, string Priority, DateTimeOffset? OpenedAt, DateTimeOffset? DueAt, string? CustomerName, string? ExternalUrl);
public sealed record SalesCommercialReport(string PeriodName, int OfferCount, int OpenOfferCount, decimal OfferAmount, int OverdueOfferCount, int OrderCount, int OpenOrderCount, decimal OrderAmount, int OverdueOrderCount, int InvoiceCount, int OpenInvoiceCount, decimal OpenInvoiceAmount, int OverdueInvoiceCount, IReadOnlyCollection<BreakdownReport> StatusBreakdown);
