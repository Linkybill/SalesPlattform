using System.Security.Claims;
using System.Text.Json;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SalesPlattform.Backend.Authorization;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Services;

public sealed record DailyTaskGoal(string TaskType, int DailyTarget);
public sealed record SalesTaskGoalConfiguration(DailyTaskGoal[] Goals, string[] Industries);
public sealed record SaveSalesTaskPlan(string Name, DailyTaskGoal[]? Goals, string[]? Industries,
    Guid? ProfileId, DateOnly? StartsOn, DateOnly? EndsOn, Guid? Revision = null);
public sealed record SalesTaskPlanDto(Guid Id, Guid? MembershipId, string Name, DateOnly? StartsOn, DateOnly? EndsOn,
    SalesTaskGoalConfiguration Configuration, DateTimeOffset? DeactivatedAt, Guid Revision);
public sealed class SalesTaskPlanConflictException : Exception
{
    public SalesTaskPlanConflictException(string message) : base(message) { }
}
public sealed record AssignedSalesTask(Guid Id, string TaskType, string Title, string? Industry, DateOnly AssignedOn,
    string? CrmUrl, string? CrmTaskUrl, string? CrmStatus, DateTimeOffset? CrmCompletedAt);

public sealed class SalesTaskPlanningService(PlatformTenantDbContextFactory<SalesPlattformDbContext> factory,
    SalesTeamDirectory directory, TenantAdminAccessService authorization)
{
    private static readonly TimeZoneInfo PlanningTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly HashSet<string> ExcludedLeadStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "blocked", "disqualified", "not interested", "unqualified", "junk lead", "do not contact",
        "gesperrt", "disqualifiziert", "nicht qualifiziert", "kein interesse", "nicht interessiert",
        "nicht erreichbar", "nicht kontaktieren"
    };

    private static DateOnly LocalDay(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, PlanningTimeZone).DateTime);

    private async Task<SalesTeamUser[]> RequireAdminAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await authorization.IsCurrentTenantAdminAsync(user, ct)) throw new UnauthorizedAccessException();
        return await directory.GetAsync(user, ct);
    }

    public async Task<object> ListAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var employees = await RequireAdminAsync(user, ct);
        await using var session = await factory.OpenReadOnlyAsync(ct);
        var plans = await session.Context.SalesTaskGoalPlans.AsNoTracking().OrderByDescending(p => p.StartsOn).ToArrayAsync(ct);
        var currentIds = employees.Select(e => e.Id).ToHashSet();
        return new { employees, plans = plans.Where(p => p.MembershipId is null || currentIds.Contains(p.MembershipId.Value)).Select(Describe) };
    }

    public async Task<SalesTaskPlanDto> SaveAsync(Guid? memberId, SaveSalesTaskPlan input, ClaimsPrincipal user, CancellationToken ct, Guid? periodId = null)
    {
        var employees = await RequireAdminAsync(user, ct);
        if (memberId is { } member && !employees.Any(e => e.Id == member)) throw new KeyNotFoundException();
        await using var session = await factory.OpenAsync(ct);
        var db = session.Context;
        await using var transaction = await LockAsync(db, ct);
        var result = await SaveToDatabaseAsync(db, memberId, periodId, input, employees, LocalDay(DateTimeOffset.UtcNow), ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return result;
    }

    public static async Task<SalesTaskPlanDto> SaveToDatabaseAsync(SalesPlattformDbContext db, Guid? memberId, Guid? periodId,
        SaveSalesTaskPlan input, IReadOnlyCollection<SalesTeamUser> employees, DateOnly today, CancellationToken ct)
    {
        if (memberId is not null && (input.StartsOn is not null || input.EndsOn is not null || periodId is not null))
            throw new ArgumentException("Persönliche Ziele haben keinen Zeitraum.");
        var period = periodId is Guid id ? await FindPeriodAsync(db, id, ct) : null;
        if (period is not null)
        {
            RequireEditablePeriod(period, today);
            if (period.Revision != input.Revision)
                throw new SalesTaskPlanConflictException("Das Zeitraumziel wurde inzwischen geändert. Bitte lade die Übersicht neu.");
        }
        if (memberId is null)
        {
            var invalidStart = period?.StartsOn is DateOnly previousStart && previousStart < today
                ? input.StartsOn != previousStart : input.StartsOn < today;
            if (input.StartsOn is null || input.EndsOn is null || input.EndsOn < today || input.EndsOn < input.StartsOn
                || invalidStart)
                throw new ArgumentException("Bitte einen gültigen Zeitraum wählen. Ein vergangener Starttermin bleibt erhalten.");
            if (await db.SalesTaskGoalPlans.AnyAsync(p => (periodId == null || p.Id != periodId) && p.MembershipId == null && p.DeactivatedAt == null
                && p.StartsOn <= input.EndsOn && p.EndsOn >= input.StartsOn, ct))
                throw new ArgumentException("Für diesen Zeitraum gibt es bereits ein Zeitraumziel.");
        }

        var name = input.Name?.Trim();
        var goals = input.Goals;
        var industries = input.Industries;
        if (input.ProfileId is Guid profileId)
        {
            var profile = await db.TaskGoalProfiles.AsNoTracking().Include(p => p.Items).SingleOrDefaultAsync(p => p.Id == profileId, ct)
                ?? throw new KeyNotFoundException();
            name = string.IsNullOrWhiteSpace(name) ? profile.Name : name;
            goals = profile.Items.Select(i => new DailyTaskGoal(i.TaskType, i.DailyTarget)).ToArray();
            industries = profile.Industries;
        }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new ArgumentException("Bitte einen Namen mit 1 bis 200 Zeichen eingeben.");
        if (goals is null || goals.Length != TaskGoalProfileService.Types.Count || goals.Any(g => g is null)
            || !goals.Select(g => g.TaskType).ToHashSet().SetEquals(TaskGoalProfileService.Types.Select(t => t.Key))
            || goals.Any(g => g.DailyTarget is < 0 or > 10000))
            throw new ArgumentException("Bitte für jeden Aufgabentyp eine gültige Tagesmenge eingeben.");

        var configuration = new SalesTaskGoalConfiguration(goals, TaskGoalProfileService.NormalizeIndustries(industries));
        var plan = period;
        if (memberId is not null)
            plan = await db.SalesTaskGoalPlans.SingleOrDefaultAsync(p => p.MembershipId == memberId, ct);
        if (plan is null)
        {
            plan = new SalesTaskGoalPlan { Id = Guid.NewGuid(), TenantId = db.CurrentTenantId, MembershipId = memberId,
                Name = name, Configuration = "",
                Audience = memberId is null ? employees.Select(e => e.Id).Distinct().ToArray() : [] };
            db.SalesTaskGoalPlans.Add(plan);
        }
        plan.Name = name;
        plan.StartsOn = input.StartsOn;
        plan.EndsOn = input.EndsOn;
        plan.Configuration = JsonSerializer.Serialize(configuration);
        plan.Revision = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return Describe(plan);
    }

    public async Task<SalesTaskPlanDto> DeactivatePeriodAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await authorization.IsCurrentTenantAdminAsync(user, ct)) throw new UnauthorizedAccessException();
        await using var session = await factory.OpenAsync(ct);
        var db = session.Context;
        await using var transaction = await LockAsync(db, ct);
        var result = await DeactivatePeriodInDatabaseAsync(db, id, DateTimeOffset.UtcNow, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return result;
    }

    public static async Task<SalesTaskPlanDto> DeactivatePeriodInDatabaseAsync(SalesPlattformDbContext db, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var period = await FindPeriodAsync(db, id, ct);
        if (period.DeactivatedAt is null)
        {
            RequireEditablePeriod(period, LocalDay(now));
            period.DeactivatedAt = now;
            period.Revision = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }
        return Describe(period);
    }

    private static async Task<SalesTaskGoalPlan> FindPeriodAsync(SalesPlattformDbContext db, Guid id, CancellationToken ct)
        => await db.SalesTaskGoalPlans.SingleOrDefaultAsync(p => p.Id == id && p.MembershipId == null, ct)
            ?? throw new KeyNotFoundException();

    private static void RequireEditablePeriod(SalesTaskGoalPlan period, DateOnly today)
    {
        if (period.DeactivatedAt is not null || period.EndsOn < today)
            throw new SalesTaskPlanConflictException("Dieses Zeitraumziel ist bereits abgelaufen oder deaktiviert.");
    }

    public async Task<AssignedSalesTask[]> MineAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var member = await directory.CurrentMembershipAsync(user, ct);
        await using var session = await factory.OpenReadOnlyAsync(ct);
        var db = session.Context;
        var todayStart = TimeZoneInfo.ConvertTimeToUtc(
            LocalDay(DateTimeOffset.UtcNow).ToDateTime(TimeOnly.MinValue), PlanningTimeZone);
        var completedSince = new DateTimeOffset(todayStart);
        var assignments = await (from a in db.SalesTaskAssignments.AsNoTracking()
                      join w in db.SalesWorkItems.AsNoTracking() on a.WorkItemId equals w.Id
                      where a.MembershipId == member && (a.CrmCompletedAt >= completedSince
                          || (a.CrmCompletedAt == null && (w.Status == "open" || w.Status == "scheduled" || w.Status == "snoozed")))
                      orderby a.AssignedOn descending, a.TaskType, a.Title
                      select a).ToArrayAsync(ct);
        var targetIds = assignments.Select(a => a.TargetId).Distinct().ToArray();
        var links = await db.IntegrationEntityLinks.AsNoTracking()
            .Where(l => targetIds.Contains(l.InternalEntityId) && l.ExternalUrl != null && l.SourceDeletedAt == null)
            .OrderByDescending(l => l.LastSeenAt).ToArrayAsync(ct);
        var urls = links.Where(l => Uri.TryCreate(l.ExternalUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .GroupBy(l => (l.InternalEntityType, l.InternalEntityId)).ToDictionary(l => l.Key, l => l.First().ExternalUrl);
        var workIds = assignments.Select(a => a.WorkItemId).ToArray();
        var taskLinks = await (from link in db.IntegrationEntityLinks.AsNoTracking()
                               join activity in db.SalesActivities.AsNoTracking() on link.InternalEntityId equals activity.Id
                               where link.WorkItemId.HasValue && workIds.Contains(link.WorkItemId.Value)
                                   && link.EntityType == CrmEntityTypes.Activity && activity.ActivityType == "task"
                                   && link.SourceDeletedAt == null && activity.SourceDeletedAt == null
                               orderby link.LastSeenAt descending
                               select new { link.WorkItemId, link.ExternalUrl, activity.Result }).ToArrayAsync(ct);
        var crmTasks = taskLinks.GroupBy(link => link.WorkItemId!.Value).ToDictionary(group => group.Key, group => group.First());
        return assignments.Select(a => new AssignedSalesTask(a.Id, a.TaskType, a.Title, a.Industry, a.AssignedOn,
            urls.GetValueOrDefault((a.TargetType, a.TargetId)),
            crmTasks.TryGetValue(a.WorkItemId, out var crmTask) && Uri.TryCreate(crmTask.ExternalUrl, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps ? crmTask.ExternalUrl : null,
            crmTask?.Result, a.CrmCompletedAt)).ToArray();
    }

    public async Task<int> DistributeAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var session = await factory.OpenAsync(ct);
        var db = session.Context;
        await using var transaction = await LockAsync(db, ct);
        var result = await DistributeToDatabaseAsync(db, now, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return result;
    }

    internal static async Task<IDbContextTransaction?> LockAsync(SalesPlattformDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var key = $"sales-task-planning:{db.CurrentTenantId:D}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private static SalesTaskPlanDto Describe(SalesTaskGoalPlan p) => new(p.Id, p.MembershipId, p.Name, p.StartsOn, p.EndsOn,
        JsonSerializer.Deserialize<SalesTaskGoalConfiguration>(p.Configuration)!, p.DeactivatedAt, p.Revision);

    private sealed record Recipient(Guid Member, Guid Plan, SalesTaskGoalConfiguration Configuration);
    private sealed record Candidate(SalesWorkItem Work, string TargetType, Guid TargetId, string TaskType, string? Industry);

    private static bool MatchesIndustry(string[] industries, string? industry) =>
        industries.Length == 0 || industries.Contains(industry?.Trim(), StringComparer.OrdinalIgnoreCase);

    public static async Task<int> DistributeToDatabaseAsync(SalesPlattformDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var today = LocalDay(now);
        var plans = await db.SalesTaskGoalPlans.AsNoTracking().Where(p => p.MembershipId != null
            || (p.DeactivatedAt == null && p.StartsOn <= today && p.EndsOn >= today)).ToArrayAsync(ct);
        var period = plans.SingleOrDefault(p => p.MembershipId == null);
        var recipients = period is null
            ? plans.Select(p => new Recipient(p.MembershipId!.Value, p.Id, Describe(p).Configuration)).ToArray()
            : period.Audience.Select(member => new Recipient(member, period.Id, Describe(period).Configuration)).ToArray();
        if (recipients.Length == 0) return 0;

        var previous = await db.SalesTaskAssignments.AsNoTracking().Where(a => a.AssignedOn == today).ToArrayAsync(ct);
        var usedTargets = previous.Select(a => (a.TargetType, a.TargetId)).ToHashSet();
        var candidates = await CandidatesAsync(db, now, ct);
        var previousByRecipient = previous.ToLookup(a => (a.MembershipId, a.TaskType));
        var added = 0;
        foreach (var type in TaskGoalProfileService.Types)
        {
            var queue = candidates.Where(c => c.TaskType == type.Key).ToList();
            var quotas = recipients.OrderBy(r => r.Configuration.Industries.Length == 0)
                .ThenBy(r => r.Configuration.Industries.Length).ThenBy(r => r.Member).Select(r =>
                (Recipient: r, Remaining: r.Configuration.Goals.Single(g => g.TaskType == type.Key).DailyTarget
                    - previousByRecipient[(r.Member, type.Key)].Count(a => MatchesIndustry(r.Configuration.Industries, a.Industry))))
                .Where(q => q.Remaining > 0).ToList();
            while (queue.Count > 0 && quotas.Count > 0)
            {
                for (var i = 0; i < quotas.Count;)
                {
                    var (recipient, remaining) = quotas[i];
                    var industries = recipient.Configuration.Industries;
                    var match = queue.FindIndex(c => !usedTargets.Contains((c.TargetType, c.TargetId))
                        && MatchesIndustry(industries, c.Industry));
                    if (match < 0)
                    {
                        quotas.RemoveAt(i);
                        continue;
                    }
                    var candidate = queue[match];
                    queue.RemoveAt(match);
                    usedTargets.Add((candidate.TargetType, candidate.TargetId));
                    db.SalesTaskAssignments.Add(new SalesTaskAssignment
                    {
                        Id = Guid.NewGuid(), TenantId = db.CurrentTenantId, MembershipId = recipient.Member, PlanId = recipient.Plan,
                        WorkItemId = candidate.Work.Id, TargetType = candidate.TargetType, TargetId = candidate.TargetId,
                        TaskType = candidate.TaskType, Title = candidate.Work.Title, Industry = candidate.Industry, AssignedOn = today
                    });
                    added++;
                    if (remaining == 1) quotas.RemoveAt(i);
                    else quotas[i++] = (recipient, remaining - 1);
                }
            }
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    private static async Task<Candidate[]> CandidatesAsync(SalesPlattformDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var items = await db.SalesWorkItems.AsNoTracking().Include(w => w.Relations)
            .Where(w => w.Status == "open" || w.Status == "scheduled" || w.Status == "snoozed")
            .Where(w => !(from link in db.IntegrationEntityLinks
                         join activity in db.SalesActivities on link.InternalEntityId equals activity.Id
                         where link.WorkItemId == w.Id && link.EntityType == CrmEntityTypes.Activity && link.SourceDeletedAt == null
                             && activity.ActivityType == "task" && activity.SourceDeletedAt == null && activity.Result != null
                             && CrmTaskStatus.CompletedValues.Contains(activity.Result.Trim().ToLower())
                         select link.Id).Any())
            .OrderByDescending(w => w.PriorityScore).ThenBy(w => w.DueAt).ThenBy(w => w.Id).ToArrayAsync(ct);
        var reserved = await (from a in db.SalesTaskAssignments.AsNoTracking()
                              join w in db.SalesWorkItems.AsNoTracking() on a.WorkItemId equals w.Id
                              where w.Status == "open" || w.Status == "scheduled" || w.Status == "snoozed"
                              select new { a.TargetType, a.TargetId }).Distinct().ToArrayAsync(ct);
        var reservedTargets = reserved.Select(r => (r.TargetType, r.TargetId)).ToHashSet();
        var related = items.Select(w => (Work: w, Target: w.Relations.FirstOrDefault(r => r.RelationRole == "primary")))
            .Where(x => x.Target is not null).Select(x => (x.Work, Target: x.Target!)).ToArray();
        var pendingSteps = related.Where(x => x.Work.PreviousWorkItemId != null || x.Work.WorkItemType == "call-not-reachable")
            .Select(x => (x.Target.TargetType, x.Target.TargetId)).ToHashSet();
        var leadIds = related.Where(x => x.Target.TargetType == "lead").Select(x => x.Target.TargetId).Distinct().ToArray();
        var contractIds = related.Where(x => x.Target.TargetType == "contract").Select(x => x.Target.TargetId).Distinct().ToArray();
        var otherIds = related.Where(x => x.Target.TargetType is not ("lead" or "contract" or "customer"))
            .Select(x => x.Target.TargetId).Distinct().ToArray();
        var leads = await db.SalesLeads.AsNoTracking().Include(l => l.Customer).Where(l => leadIds.Contains(l.Id)
            && l.IsActive && l.SourceDeletedAt == null).ToDictionaryAsync(l => l.Id, ct);
        var contracts = await db.SalesContracts.AsNoTracking().Include(c => c.Customer).Where(c => contractIds.Contains(c.Id)
            && c.IsActive && c.SourceDeletedAt == null && c.EndAt >= now).ToDictionaryAsync(c => c.Id, ct);
        var customers = await db.SalesCustomers.AsNoTracking().Where(c => c.IsActive && c.SourceDeletedAt == null)
            .ToDictionaryAsync(c => c.Id, c => c.Industry, ct);
        var otherTargets = await db.SalesOffers.Where(o => otherIds.Contains(o.Id) && o.IsActive && o.SourceDeletedAt == null)
            .Select(o => new { Type = "offer", o.Id, o.CustomerId })
            .Concat(db.SalesDeals.Where(o => otherIds.Contains(o.Id) && o.IsActive && o.SourceDeletedAt == null)
                .Select(o => new { Type = "deal", o.Id, o.CustomerId }))
            .Concat(db.SalesServiceCases.Where(o => otherIds.Contains(o.Id) && o.IsActive && o.SourceDeletedAt == null)
                .Select(o => new { Type = "service-case", o.Id, o.CustomerId }))
            .Concat(db.SalesOrders.Where(o => otherIds.Contains(o.Id) && o.IsActive && o.SourceDeletedAt == null)
                .Select(o => new { Type = "order", o.Id, o.CustomerId }))
            .Concat(db.SalesInvoices.Where(o => otherIds.Contains(o.Id) && o.IsActive && o.SourceDeletedAt == null)
                .Select(o => new { Type = "invoice", o.Id, o.CustomerId }))
            .ToDictionaryAsync(o => (o.Type, o.Id), o => o.CustomerId, ct);
        var results = new List<Candidate>();
        foreach (var (work, target) in related)
        {
            if (reservedTargets.Contains((target.TargetType, target.TargetId))) continue;
            if (work.AvailableFrom > now || work.SnoozedUntil > now) continue;
            var lead = target.TargetType == "lead" ? leads.GetValueOrDefault(target.TargetId) : null;
            if (target.TargetType == "lead" && (lead is null || !WorklistService.IsOpenStatus(lead.Status)
                || ExcludedLeadStatuses.Contains(lead.Status.Trim().Replace('_', ' ')))) continue;
            var contract = target.TargetType == "contract" ? contracts.GetValueOrDefault(target.TargetId) : null;
            if (target.TargetType == "contract" && contract is null) continue;
            if (target.TargetType == "customer" && !customers.ContainsKey(target.TargetId)) continue;
            if (target.TargetType is "offer" or "deal" or "service-case" or "order" or "invoice"
                && !otherTargets.ContainsKey((target.TargetType, target.TargetId))) continue;
            var hasOtherStep = work.PreviousWorkItemId is null && pendingSteps.Contains((target.TargetType, target.TargetId));
            var type = work switch
            {
                { SourceRuleCode: "R-06" } when contract is not null && !hasOtherStep => "renewal-conversation",
                { WorkItemType: "lead-reactivation" } when lead?.LastContactAt is not null && !string.IsNullOrWhiteSpace(lead.Phone) && !hasOtherStep => "lead-reactivation-call",
                { WorkItemType: "lead-first-response" or "call-follow-up" or "call-long-runner" }
                    when lead is not null && lead.LastContactAt is null && !string.IsNullOrWhiteSpace(lead.Phone) && !hasOtherStep => "cold-call",
                { PreviousWorkItemId: not null } when !work.RequiresApproval => "follow-up",
                { WorkItemType: "offer-follow-up" or "call-follow-up" or "call-long-runner" }
                    when !work.RequiresApproval && !hasOtherStep => "follow-up",
                _ => null
            };
            if (type is null || (type != "renewal-conversation" && work.DueAt > now && work.PreviousWorkItemId is null)) continue;
            string? relatedIndustry = otherTargets.TryGetValue((target.TargetType, target.TargetId), out var targetCustomer) && targetCustomer is Guid customerId
                ? customers.GetValueOrDefault(customerId) : null;
            var industry = target.TargetType switch
            {
                "lead" => string.IsNullOrWhiteSpace(lead?.Industry) ? lead?.Customer?.Industry : lead.Industry,
                "contract" => contract?.Customer?.Industry,
                "customer" => customers.GetValueOrDefault(target.TargetId),
                _ => relatedIndustry
            };
            results.Add(new Candidate(work, target.TargetType, target.TargetId, type, industry));
        }
        return results.ToArray();
    }
}
