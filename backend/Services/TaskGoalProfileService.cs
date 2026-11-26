using System.Security.Claims;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Authorization;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

public sealed record TaskGoalType(string Key, string Name);
public sealed record TaskGoalItemDto(string TaskType, int? SharePercent, int? MinutesPerTask, int DailyTarget, int? Priority);
public sealed record SaveTaskGoalProfileRequest(string Name, int? DailyCapacityMinutes, TaskGoalItemDto[] Items, Guid? Revision, string[]? Industries = null);
public sealed record TaskGoalProfileDto(Guid Id, string Name, int DailyCapacityMinutes, Guid Revision, TaskGoalItemDto[] Items, string[] Industries);
public sealed class TaskGoalProfileConflictException : Exception;

public sealed class TaskGoalProfileService(PlatformTenantDbContextFactory<SalesPlattformDbContext> factory,
    TenantAdminAccessService access)
{
    public static IReadOnlyList<TaskGoalType> Types { get; } = Array.AsReadOnly<TaskGoalType>([
        new("cold-call", "Kaltakquise-Anruf"),
        new("lead-reactivation-call", "Lead-Reaktivierungsanruf"),
        new("renewal-conversation", "Verlängerungsgespräch"),
        new("follow-up", "Wiedervorlage bearbeiten")]);

    public async Task<TaskGoalProfileDto[]> ListAsync(CancellationToken ct)
    {
        await using var session = await factory.OpenReadOnlyAsync(ct);
        var profiles = await session.Context.TaskGoalProfiles.AsNoTracking().Include(p => p.Items)
            .OrderBy(p => p.Name).ThenBy(p => p.Id).ToArrayAsync(ct);
        return profiles.Select(Describe).ToArray();
    }

    public async Task<string[]> IndustriesAsync(CancellationToken ct)
    {
        await using var session = await factory.OpenReadOnlyAsync(ct);
        var leads = session.Context.SalesLeads.Where(x => x.IsActive && x.SourceDeletedAt == null).Select(x => x.Industry);
        var customers = session.Context.SalesCustomers.Where(x => x.IsActive && x.SourceDeletedAt == null).Select(x => x.Industry);
        var industries = await leads.Union(customers).ToArrayAsync(ct);
        return industries.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string[] NormalizeIndustries(string[]? values)
    {
        if (values is null) return [];
        if (values.Length > 100 || values.Any(v => v is null || v.Trim().Length is < 1 or > 300))
            throw new ArgumentException("Bitte gültige Branchen auswählen.");
        return values.Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<TaskGoalProfileDto> SaveAsync(Guid? id, SaveTaskGoalProfileRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await access.IsCurrentTenantAdminAsync(user, ct)) throw new UnauthorizedAccessException();
        await using var session = await factory.OpenAsync(ct);
        return await SaveToDatabaseAsync(session.Context, id, request, ct);
    }

    public static void Validate(SaveTaskGoalProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            throw new ArgumentException("Bitte einen Profilnamen mit 1 bis 200 Zeichen eingeben.");
        if (request.DailyCapacityMinutes is < 1 or > 1440)
            throw new ArgumentException("Die Tageskapazität muss zwischen 1 und 1440 Minuten liegen.");
        if (request.Items is null || request.Items.Length != Types.Count || request.Items.Any(i => i is null)
            || !request.Items.Select(i => i.TaskType).ToHashSet().SetEquals(Types.Select(t => t.Key)))
            throw new ArgumentException("Jeder feste Aufgabentyp muss genau einmal enthalten sein.");
        if (request.Items.Any(i => i.DailyTarget is < 0 or > 10000))
            throw new ArgumentException("Das Tagesziel muss zwischen 0 und 10000 liegen.");
        if (request.Items.Any(i => i.SharePercent is < 0 or > 100 || i.MinutesPerTask is < 1 or > 1440 || i.Priority is < 1 or > 4))
            throw new ArgumentException("Die optionalen Planungswerte liegen außerhalb des gültigen Bereichs.");
        if (request.Items.All(i => i.DailyTarget == 0))
            throw new ArgumentException("Bitte mindestens ein Tagesziel größer als null festlegen.");
    }

    public static async Task<TaskGoalProfileDto> SaveToDatabaseAsync(SalesPlattformDbContext db, Guid? id,
        SaveTaskGoalProfileRequest request, CancellationToken ct)
    {
        Validate(request);
        TaskGoalProfile profile;
        if (id is { } profileId)
        {
            profile = await db.TaskGoalProfiles.Include(p => p.Items).SingleOrDefaultAsync(p => p.Id == profileId, ct)
                ?? throw new KeyNotFoundException();
            if (request.Revision is null || request.Revision != profile.Revision) throw new TaskGoalProfileConflictException();
        }
        else
        {
            if (request.Revision is not null) throw new ArgumentException("Ein neues Profil hat noch keinen Bearbeitungsstand.");
            profile = new TaskGoalProfile { Id = Guid.NewGuid(), TenantId = db.CurrentTenantId, Name = request.Name.Trim() };
            db.TaskGoalProfiles.Add(profile);
        }
        profile.Name = request.Name.Trim();
        if (request.Industries is not null) profile.Industries = NormalizeIndustries(request.Industries);
        if (request.DailyCapacityMinutes is int capacity) profile.DailyCapacityMinutes = capacity;
        profile.Revision = Guid.NewGuid();
        foreach (var value in request.Items)
        {
            var item = profile.Items.SingleOrDefault(i => i.TaskType == value.TaskType);
            if (item is null)
            {
                item = new TaskGoalProfileItem { Id = Guid.NewGuid(), TenantId = db.CurrentTenantId, ProfileId = profile.Id, TaskType = value.TaskType };
                profile.Items.Add(item);
                db.TaskGoalProfileItems.Add(item);
            }
            if (value.SharePercent is int share) item.SharePercent = share;
            if (value.MinutesPerTask is int minutes) item.MinutesPerTask = minutes;
            item.DailyTarget = value.DailyTarget;
            if (value.Priority is int priority) item.Priority = priority;
        }
        await db.SaveChangesAsync(ct);
        return Describe(profile);
    }

    private static TaskGoalProfileDto Describe(TaskGoalProfile p) => new(p.Id, p.Name, p.DailyCapacityMinutes, p.Revision,
        Types.Select(t => p.Items.Single(i => i.TaskType == t.Key))
            .Select(i => new TaskGoalItemDto(i.TaskType, i.SharePercent, i.MinutesPerTask, i.DailyTarget, i.Priority)).ToArray(),
        p.Industries);
}
