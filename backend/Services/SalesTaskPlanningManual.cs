using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

internal sealed record SalesTaskMatch(Guid WorkItemId, string Title, string TaskType, Guid? MembershipId,
    DateOnly? AssignedOn, DateTimeOffset? CrmCompletedAt)
{
    public string? Assignee { get; init; }
}

public sealed partial class SalesTaskPlanningService
{
    internal async Task<SalesTaskMatch[]> FindTasksAsync(Guid? teamId, Guid? memberId, string? search, int limit,
        bool includeAvailable, bool includeCompleted, ClaimsPrincipal user, CancellationToken ct, bool onlyUnassigned = false)
    {
        SalesTeamUser[] employees = [];
        if (teamId.HasValue || memberId.HasValue || includeAvailable || onlyUnassigned)
            employees = await RequireAdminAsync(user, ct);
        else
            memberId = await directory.CurrentMembershipAsync(user, ct);

        await using var session = await factory.OpenReadOnlyAsync(ct);
        var db = session.Context;
        var assignments = db.SalesTaskAssignments.AsNoTracking();
        if (memberId is Guid member)
            assignments = assignments.Where(a => a.MembershipId == member);
        if (teamId is Guid team)
            assignments = assignments.Where(a => db.SalesTeamUserMembers.Any(m => m.TeamId == team &&
                m.MembershipId == a.MembershipId && db.SalesTeams.Any(t => t.Id == team && t.IsActive)));
        if (!string.IsNullOrWhiteSpace(search))
            assignments = assignments.Where(a => a.Title.ToLower().Contains(search.ToLower()));
        var results = await (from a in assignments
                             join w in db.SalesWorkItems.AsNoTracking() on a.WorkItemId equals w.Id
                             where !onlyUnassigned && ((includeCompleted && a.CrmCompletedAt != null) ||
                                (a.CrmCompletedAt == null && (w.Status == "open" || w.Status == "scheduled" || w.Status == "snoozed")))
                             orderby a.AssignedOn descending, a.Title, a.Id
                             select new SalesTaskMatch(a.WorkItemId, a.Title, a.TaskType, a.MembershipId,
                                 a.AssignedOn, a.CrmCompletedAt)).Take(limit).ToListAsync(ct);
        if ((includeAvailable || onlyUnassigned) && teamId is null && memberId is null && results.Count < limit)
        {
            var available = await CandidatesAsync(db, DateTimeOffset.UtcNow, ct);
            results.AddRange(available.Where(c => !c.Work.RequiresApproval &&
                    (string.IsNullOrWhiteSpace(search) || c.Work.Title.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .Take(limit - results.Count)
                .Select(c => new SalesTaskMatch(c.Work.Id, c.Work.Title, c.TaskType, null, null, null)));
        }
        var names = employees.ToDictionary(e => e.Id, e => e.Email);
        return results.Select(task => task with
        {
            Assignee = task.MembershipId is Guid id ? names.GetValueOrDefault(id) : null
        }).ToArray();
    }

    internal async Task<SalesTaskMatch[]> PreviewAssignmentsAsync(Guid[] workItemIds, Guid recipient,
        ClaimsPrincipal user, CancellationToken ct)
    {
        await RequireRecipientAsync(recipient, user, ct);
        await using var session = await factory.OpenReadOnlyAsync(ct);
        var tasks = await FindAssignableAsync(session.Context, workItemIds, DateTimeOffset.UtcNow, ct);
        return tasks.Select(t => t.Task).ToArray();
    }

    internal async Task<int> AssignAsync(SalesTaskMatch[] expected, Guid recipient, ClaimsPrincipal user, CancellationToken ct)
    {
        await RequireRecipientAsync(recipient, user, ct);
        await using var session = await factory.OpenAsync(ct);
        var db = session.Context;
        await using var transaction = await LockAsync(db, ct);
        var changed = await AssignInDatabaseAsync(db, expected, recipient, DateTimeOffset.UtcNow, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return changed;
    }

    internal static async Task<int> AssignInDatabaseAsync(SalesPlattformDbContext db, SalesTaskMatch[] expected,
        Guid recipient, DateTimeOffset now, CancellationToken ct)
    {
        var today = LocalDay(now);
        var matches = await FindAssignableAsync(db, expected.Select(t => t.WorkItemId).ToArray(), now, ct);
        if (!matches.Select(m => m.Task).SequenceEqual(expected))
            throw new SalesTaskPlanConflictException("Die Aufgaben wurden inzwischen geändert. Bitte erstelle einen neuen Vorschlag.");
        var addedTargets = new HashSet<(string, Guid)>();
        var plan = await db.SalesTaskGoalPlans.Where(p => p.MembershipId == recipient ||
                (p.MembershipId == null && p.DeactivatedAt == null && p.StartsOn <= today && p.EndsOn >= today
                    && p.Audience.Contains(recipient)))
            .OrderBy(p => p.MembershipId != null).Select(p => p.Id).FirstOrDefaultAsync(ct);
        var changed = 0;
        foreach (var match in matches)
        {
            if (match.Assignment is { } assignment)
            {
                if (assignment.MembershipId == recipient) continue;
                assignment.MembershipId = recipient;
            }
            else if (match.Candidate is { } candidate)
            {
                var target = (candidate.TargetType, candidate.TargetId);
                if (!addedTargets.Add(target) || await db.SalesTaskAssignments.AnyAsync(a =>
                    a.AssignedOn == today && a.TargetType == target.Item1 && a.TargetId == target.Item2, ct))
                    throw new SalesTaskPlanConflictException("Für einen der ausgewählten Kontakte besteht heute bereits eine Zuweisung.");
                db.SalesTaskAssignments.Add(new SalesTaskAssignment
                {
                    Id = Guid.NewGuid(), TenantId = db.CurrentTenantId, MembershipId = recipient, PlanId = plan,
                    WorkItemId = candidate.Work.Id, TargetType = candidate.TargetType, TargetId = candidate.TargetId,
                    TaskType = candidate.TaskType, Title = candidate.Work.Title, Industry = candidate.Industry, AssignedOn = today
                });
            }
            changed++;
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new SalesTaskPlanConflictException("Die Aufgaben wurden inzwischen geändert. Bitte erstelle einen neuen Vorschlag."); }
        return changed;
    }

    private async Task RequireRecipientAsync(Guid recipient, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!(await RequireAdminAsync(user, ct)).Any(e => e.Id == recipient))
            throw new KeyNotFoundException("Die Person hat keinen aktiven Sales-Zugriff.");
    }

    private sealed record AssignableTask(SalesTaskMatch Task, SalesTaskAssignment? Assignment, Candidate? Candidate);

    private static async Task<AssignableTask[]> FindAssignableAsync(SalesPlattformDbContext db, Guid[] ids, DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await (from a in db.SalesTaskAssignments
                              join w in db.SalesWorkItems on a.WorkItemId equals w.Id
                              where ids.Contains(a.WorkItemId)
                              select new { Assignment = a, Eligible = !w.RequiresApproval && a.CrmCompletedAt == null &&
                                  (w.Status == "open" || w.Status == "scheduled" || w.Status == "snoozed") })
            .ToDictionaryAsync(a => a.Assignment.WorkItemId, ct);
        var candidates = ids.Any(id => !existing.ContainsKey(id))
            ? (await CandidatesAsync(db, now, ct)).Where(c => ids.Contains(c.Work.Id) && !c.Work.RequiresApproval)
                .ToDictionary(c => c.Work.Id)
            : new Dictionary<Guid, Candidate>();
        return ids.Select(id =>
        {
            if (existing.TryGetValue(id, out var row) && row.Eligible)
            {
                var a = row.Assignment;
                return new AssignableTask(new SalesTaskMatch(id, a.Title, a.TaskType, a.MembershipId, a.AssignedOn, null), a, null);
            }
            if (candidates.TryGetValue(id, out var c))
                return new AssignableTask(new SalesTaskMatch(id, c.Work.Title, c.TaskType, null, null, null), null, c);
            throw new SalesTaskPlanConflictException("Mindestens eine Aufgabe ist nicht mehr offen oder kann nicht zugewiesen werden.");
        }).ToArray();
    }
}
