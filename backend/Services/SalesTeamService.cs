using System.Security.Claims;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Authorization;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

public sealed record SaveSalesTeamRequest(string Name, Guid[] MemberIds, Guid? Revision);
public sealed record SalesTeamDto(Guid Id, string Name, Guid Revision, SalesTeamUser[] Members);
public sealed class SalesTeamConflictException : Exception;

public sealed class SalesTeamService(PlatformTenantDbContextFactory<SalesPlattformDbContext> factory,
    SalesTeamDirectory directory, TenantAdminAccessService access)
{
    public async Task<SalesTeamDto[]> ListAsync(CancellationToken ct)
    {
        await using var session = await factory.OpenReadOnlyAsync(ct);
        var teams = await session.Context.SalesTeams.AsNoTracking().Where(t => t.IsActive)
            .Include(t => t.UserMembers).OrderBy(t => t.Name).ThenBy(t => t.Id).ToArrayAsync(ct);
        return teams.Select(Describe).ToArray();
    }

    public async Task<SalesTeamUser[]> UsersAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await access.IsCurrentTenantAdminAsync(user, ct)) throw new UnauthorizedAccessException();
        return await directory.GetAsync(user, ct);
    }

    public async Task<SalesTeamDto> SaveAsync(Guid? id, SaveSalesTeamRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var users = await UsersAsync(user, ct);
        await using var session = await factory.OpenAsync(ct);
        return await SaveToDatabaseAsync(session.Context, id, request, users, ct);
    }

    public static void Validate(SaveSalesTeamRequest request, IReadOnlyCollection<SalesTeamUser> users)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            throw new ArgumentException("Bitte einen Teamnamen mit 1 bis 200 Zeichen eingeben.");
        if (request.MemberIds is null || request.MemberIds.Length is < 1 or > 1000
            || request.MemberIds.Distinct().Count() != request.MemberIds.Length)
            throw new ArgumentException("Bitte mindestens eine Person auswählen; jede Person darf nur einmal im Team vorkommen.");
        var allowed = users.Select(u => u.Id).ToHashSet();
        if (request.MemberIds.Any(id => id == Guid.Empty || !allowed.Contains(id)))
            throw new ArgumentException("Mindestens eine Person hat keinen aktiven Sales-Zugriff mehr. Bitte die Benutzerauswahl neu laden.");
    }

    public static async Task<SalesTeamDto> SaveToDatabaseAsync(SalesPlattformDbContext db, Guid? id,
        SaveSalesTeamRequest request, IReadOnlyCollection<SalesTeamUser> users, CancellationToken ct)
    {
        Validate(request, users);
        SalesTeam team;
        if (id is { } teamId)
        {
            team = await db.SalesTeams.Include(t => t.UserMembers).SingleOrDefaultAsync(t => t.Id == teamId && t.IsActive, ct)
                ?? throw new KeyNotFoundException();
            if (request.Revision is null || request.Revision != team.Revision) throw new SalesTeamConflictException();
        }
        else
        {
            if (request.Revision is not null) throw new ArgumentException("Ein neues Team hat noch keinen Bearbeitungsstand.");
            var newId = Guid.NewGuid();
            team = new SalesTeam { Id = newId, TenantId = db.CurrentTenantId, Key = $"team-{newId:N}", Name = request.Name.Trim() };
            db.SalesTeams.Add(team);
        }
        team.Name = request.Name.Trim();
        team.Revision = Guid.NewGuid();
        var selected = request.MemberIds.ToHashSet();
        foreach (var removed in team.UserMembers.Where(m => !selected.Contains(m.MembershipId)).ToArray())
        {
            db.SalesTeamUserMembers.Remove(removed);
            team.UserMembers.Remove(removed);
        }
        var byId = users.ToDictionary(u => u.Id);
        foreach (var membershipId in selected)
        {
            var member = team.UserMembers.SingleOrDefault(m => m.MembershipId == membershipId);
            if (member is null)
            {
                member = new SalesTeamUserMember { Id = Guid.NewGuid(), TenantId = db.CurrentTenantId, TeamId = team.Id, MembershipId = membershipId, Email = byId[membershipId].Email };
                team.UserMembers.Add(member);
                db.SalesTeamUserMembers.Add(member);
            }
            member.Email = byId[membershipId].Email;
        }
        await db.SaveChangesAsync(ct);
        return Describe(team);
    }

    private static SalesTeamDto Describe(SalesTeam t) => new(t.Id, t.Name, t.Revision,
        t.UserMembers.OrderBy(m => m.Email).Select(m => new SalesTeamUser(m.MembershipId, m.Email)).ToArray());
}
