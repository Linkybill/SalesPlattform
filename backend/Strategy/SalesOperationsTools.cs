using System.ComponentModel;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SalesPlattform.Backend.Services;

namespace SalesPlattform.Backend.Strategy;

internal sealed record SalesPreparedChange(string Title, string[] Details, string? Approval);

[McpServerToolType]
internal sealed class SalesOperationsTools(IHttpContextAccessor accessor, SalesTeamService teams,
    SalesTaskPlanningService planning, TaskGoalProfileService profiles, IDataProtectionProvider dataProtection)
{
    private const string People = "sales_find_people";
    private const string Users = "sales_find_users";
    private const string Tasks = "sales_list_tasks";
    private const string UserTasks = "sales_list_user_tasks";
    private const string PlanOptions = "sales_daily_plan_options";
    private const string Assignment = "sales_prepare_assignment";
    private const string DailyPlan = "sales_prepare_daily_plan";
    private const string PeopleInfo = "Findet Sales-Teams und aktive Sales-Benutzer anhand eines Namens oder einer E-Mail. Liefert IDs. Nur für Mandantenadministratoren.";
    private const string UsersInfo = "Sucht aktive Sales-Benutzer mit firstName, lastName oder beiden Feldern. Liefert Benutzerobjekte mit id, firstName, lastName und email. Suche selbst, sobald ein Name genannt wird.";
    private const string TasksInfo = "Listet offene Sales-Aufgaben, optional mit CRM-erledigten Aufgaben (includeCompleted). Ohne Filter zeigt es die eigenen. Setze onlyUnassigned=true für ausschließlich unverteilte Aufgaben.";
    private const string UserTasksInfo = "Listet Aufgaben eines aktiven Sales-Benutzers. Nutze entweder userId aus sales_find_users oder direkt firstName und lastName. Bei mehreren Benutzern wird die Auswahl zurückgegeben.";
    private const string PlanOptionsInfo = "Liefert heutiges Datum in Europe/Berlin sowie erlaubte Aufgabentypen und Branchen für Tagesprofile.";
    private const string AssignmentInfo = "Bereitet die Zuweisung konkret ausgewählter Aufgaben an einen aktiven Sales-Benutzer vor. Erst eine Bestätigung speichert die Änderung.";
    private const string DailyPlanInfo = "Bereitet ein mandantenweites Tagesprofil für heute vor. Tagesmengen in Stück für alle Aufgabentypen angeben. Erst eine Bestätigung speichert das Profil.";
    private readonly ITimeLimitedDataProtector approvals = dataProtection.CreateProtector("sales-chat-approval-v1").ToTimeLimitedDataProtector();
    private HttpContext Http => accessor.HttpContext is { } http && SalesStrategyEndpoints.HasVerifiedTenant(http)
        ? http : throw new UnauthorizedAccessException();
    private ClaimsPrincipal User => Http.User;

    internal AIFunction[] AsAgentTools(List<SalesPreparedChange> changes) =>
    [
        AIFunctionFactory.Create(FindPeopleAsync, People, PeopleInfo),
        AIFunctionFactory.Create(FindUsersAsync, Users, UsersInfo),
        AIFunctionFactory.Create(ListTasksAsync, Tasks, TasksInfo),
        AIFunctionFactory.Create(ListUserTasksAsync, UserTasks, UserTasksInfo),
        AIFunctionFactory.Create(OptionsAsync, PlanOptions, PlanOptionsInfo),
        AIFunctionFactory.Create(async (Guid[] workItemIds, Guid recipientId, CancellationToken ct) =>
            Capture(changes, await PrepareAssignmentAsync(workItemIds, recipientId, ct)), Assignment, AssignmentInfo),
        AIFunctionFactory.Create(async (string name, DailyTaskGoal[] goals, string[]? industries, CancellationToken ct) =>
            Capture(changes, await PrepareDailyPlanAsync(name, goals, industries, ct)), DailyPlan, DailyPlanInfo)
    ];

    private static SalesPreparedChange Capture(List<SalesPreparedChange> changes, SalesPreparedChange draft)
    {
        lock (changes) changes.Add(draft);
        return draft with { Approval = null };
    }

    [McpServerTool(Name = People, ReadOnly = true), Description(PeopleInfo)]
    public async Task<object> FindPeopleAsync(string search, CancellationToken cancellationToken)
    {
        search = Search(search) ?? throw new McpException("Bitte einen Teamnamen, Namen oder eine E-Mail angeben.");
        var members = await teams.UsersAsync(User, cancellationToken);
        var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matchingTeams = (await teams.ListAsync(cancellationToken))
            .Where(team => team.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(12);
        return new
        {
            teams = matchingTeams.Select(team => new { team.Id, team.Name,
                members = team.Members.Where(m => members.Any(active => active.Id == m.Id)).Take(25) }),
            people = members.Where(m => terms.All(term => m.Email.Contains(term, StringComparison.OrdinalIgnoreCase))).Take(12)
        };
    }

    [McpServerTool(Name = Tasks, ReadOnly = true), Description(TasksInfo)]
    public Task<SalesTaskMatch[]> ListTasksAsync(CancellationToken cancellationToken, Guid? teamId = null,
        Guid? memberId = null, string? search = null, bool includeAvailable = false, int limit = 10, bool includeCompleted = false,
        bool onlyUnassigned = false)
    {
        if (onlyUnassigned && (teamId.HasValue || memberId.HasValue))
            throw new McpException("Unzugewiesene Aufgaben haben keinen Team- oder Benutzerfilter.");
        return planning.FindTasksAsync(teamId, memberId, Search(search), Math.Clamp(limit, 1, 30), includeAvailable,
            includeCompleted, User, cancellationToken, onlyUnassigned);
    }

    [McpServerTool(Name = Users, ReadOnly = true), Description(UsersInfo)]
    public async Task<SalesTeamUser[]> FindUsersAsync(CancellationToken cancellationToken, string? firstName = null,
        string? lastName = null)
    {
        firstName = Search(firstName);
        lastName = Search(lastName);
        if (firstName is null && lastName is null) throw new McpException("Bitte Vorname oder Nachname angeben.");
        return (await teams.FindUsersByNameAsync(User, firstName, lastName, cancellationToken)).Take(15).ToArray();
    }

    [McpServerTool(Name = UserTasks, ReadOnly = true, UseStructuredContent = true), Description(UserTasksInfo)]
    public async Task<SalesUserTaskResult> ListUserTasksAsync(CancellationToken cancellationToken, Guid? userId = null,
        string? firstName = null, string? lastName = null, string? search = null, bool includeCompleted = false, int limit = 10)
    {
        var users = userId is Guid id
            ? (await teams.UsersAsync(User, cancellationToken)).Where(user => user.Id == id).ToArray()
            : await FindUsersAsync(cancellationToken, firstName, lastName);
        if (users.Length != 1) return new(users, []);
        var tasks = await ListTasksAsync(cancellationToken, memberId: users[0].Id, search: search,
            includeCompleted: includeCompleted, limit: limit);
        return new(users, tasks);
    }

    [McpServerTool(Name = PlanOptions, ReadOnly = true), Description(PlanOptionsInfo)]
    public async Task<object> OptionsAsync(CancellationToken cancellationToken)
    {
        await teams.UsersAsync(User, cancellationToken);
        return new { today = SalesTaskPlanningService.LocalDay(DateTimeOffset.UtcNow), taskTypes = TaskGoalProfileService.Types,
            industries = await profiles.IndustriesAsync(cancellationToken) };
    }

    [McpServerTool(Name = Assignment, ReadOnly = true, UseStructuredContent = true), Description(AssignmentInfo)]
    public async Task<SalesPreparedChange> PrepareAssignmentAsync(Guid[] workItemIds, Guid recipientId, CancellationToken cancellationToken)
    {
        if (workItemIds is null || workItemIds.Length is < 1 or > 10 || workItemIds.Contains(Guid.Empty)
            || workItemIds.Distinct().Count() != workItemIds.Length)
            throw new McpException("Bitte eine bis zehn unterschiedliche Aufgaben angeben.");
        var members = await teams.UsersAsync(User, cancellationToken);
        var recipient = members.SingleOrDefault(member => member.Id == recipientId)
            ?? throw new McpException("Die Person hat keinen aktiven Sales-Zugriff.");
        var tasks = await planning.PreviewAssignmentsAsync(workItemIds, recipientId, User, cancellationToken);
        var names = members.ToDictionary(m => m.Id, m => m.Email);
        return Prepare("Aufgaben zuweisen", tasks.Select(task =>
            $"{task.Title} ({(task.MembershipId is Guid member ? names.GetValueOrDefault(member, "bisherige Zuständigkeit") : "unzugewiesen")}) → {recipient.Email}").ToArray(),
            new Change(CurrentTenant(), Subject(), recipientId, tasks, null));
    }

    [McpServerTool(Name = DailyPlan, ReadOnly = true, UseStructuredContent = true), Description(DailyPlanInfo)]
    public async Task<SalesPreparedChange> PrepareDailyPlanAsync(string name, DailyTaskGoal[] goals, string[]? industries,
        CancellationToken cancellationToken)
    {
        await teams.UsersAsync(User, cancellationToken);
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 200) throw new McpException("Bitte einen Profilnamen mit 1 bis 200 Zeichen angeben.");
        if (goals is null || goals.Length != TaskGoalProfileService.Types.Count || goals.Any(g => g is null || g.DailyTarget is < 0 or > 10000)
            || !goals.Select(g => g.TaskType).ToHashSet().SetEquals(TaskGoalProfileService.Types.Select(t => t.Key)))
            throw new McpException("Bitte für jeden festen Aufgabentyp genau eine Tagesmenge zwischen 0 und 10.000 Stück angeben.");
        var normalized = TaskGoalProfileService.NormalizeIndustries(industries);
        if (normalized.Length != 0)
        {
            var known = (await profiles.IndustriesAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (normalized.Any(industry => !known.Contains(industry)))
                throw new McpException("Mindestens eine Branche ist unbekannt. Bitte die verfügbaren Branchen abfragen.");
        }
        var today = SalesTaskPlanningService.LocalDay(DateTimeOffset.UtcNow);
        var plan = new SaveSalesTaskPlan(name, goals, normalized, null, today, today);
        var details = TaskGoalProfileService.Types.Select(type =>
            $"{type.Name}: {goals.Single(g => g.TaskType == type.Key).DailyTarget} pro Person").Prepend($"{name} · {today:dd.MM.yyyy} · gesamter Mandant")
            .Append(normalized.Length == 0 ? "Branchen: alle" : $"Branchen: {string.Join(", ", normalized)}").ToArray();
        return Prepare("Tagesprofil erstellen", details, new Change(CurrentTenant(), Subject(), null, null, plan));
    }

    [McpServerTool(Name = "sales_apply_change", Destructive = true, Idempotent = false),
     Description("Speichert einen konkreten Sales-Vorschlag. Erst nach ausdrücklicher Benutzerbestätigung anwenden und approval unverändert übergeben.")]
    public async Task<object> ApplyAsync(string approval, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(approval) || approval.Length > 32000)
            throw new McpException("Bitte einen gültigen Änderungsvorschlag bestätigen.");
        Change? change;
        try { change = JsonSerializer.Deserialize<Change>(approvals.Unprotect(approval)); }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        { throw new McpException("Der Vorschlag ist ungültig oder abgelaufen. Bitte erneut erstellen."); }
        if (change is null || change.Tenant != CurrentTenant() || change.Subject != Subject())
            throw new UnauthorizedAccessException();
        if (change.Plan is { } plan)
        {
            var saved = await planning.SaveAsync(null, plan, User, cancellationToken);
            return new { message = $"Tagesprofil „{saved.Name}“ wurde gespeichert." };
        }
        if (change.Recipient is not Guid recipient || change.Tasks is null)
            throw new McpException("Der Vorschlag ist unvollständig.");
        var count = await planning.AssignAsync(change.Tasks, recipient, User, cancellationToken);
        return new { message = $"{count} Aufgaben wurden zugewiesen." };
    }

    private SalesPreparedChange Prepare(string title, string[] details, Change change) =>
        new(title, details, approvals.Protect(JsonSerializer.Serialize(change), TimeSpan.FromMinutes(10)));

    private static string? Search(string? search)
    {
        search = search?.Trim();
        if (search?.Length > 120) throw new McpException("Bitte höchstens 120 Zeichen suchen.");
        return string.IsNullOrEmpty(search) ? null : search;
    }

    private Guid CurrentTenant() => SalesStrategyEndpoints.Tenant(Http);
    private string Subject() => User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException();

    private sealed record Change(Guid Tenant, string Subject, Guid? Recipient, SalesTaskMatch[]? Tasks, SaveSalesTaskPlan? Plan);
}

internal sealed record SalesUserTaskResult(SalesTeamUser[] Users, SalesTaskMatch[] Tasks);
