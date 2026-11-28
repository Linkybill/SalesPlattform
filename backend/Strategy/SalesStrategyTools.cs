using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using IdentityPlatform.Shared.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Services;

namespace SalesPlattform.Backend.Strategy;

public sealed record SalesStrategySignal(Guid Id, string Title, string Type, string Reason, DateTimeOffset? DueAt,
    string Priority, bool AssignedToMe, string? CrmUrl, DateTimeOffset? LastDocumentedContact = null,
    decimal? RecurringAmount = null, string? Currency = null);

public sealed record SalesStrategyResult(DateTimeOffset AsOf, DateTimeOffset? WorklistRefreshedAt, SalesStrategySignal[] Items);

[McpServerToolType]
public sealed class SalesStrategyTools(
    IHttpContextAccessor accessor, IAuthorizationService authorization, IServiceScopeFactory scopes)
{
    private const string Calls = "sales_call_priorities";
    private const string Renewals = "sales_renewal_risks";
    private const string Evidence = "sales_explain_priority";
    private const string CallsDescription = "Liefert priorisierte Anrufkandidaten aus der sichtbaren Sales-Arbeitsliste. Eigene offene Zuweisungen stehen zuerst.";
    private const string RenewalsDescription = "Liefert sichtbare Vertragsverlängerungen mit Fälligkeit, dokumentiertem Kontakt und wiederkehrendem Betrag, soweit vorhanden.";
    private const string EvidenceDescription = "Sucht Belege in sichtbaren Sales-Vorgängen nach Vorgangs-ID oder Kunden- bzw. Leadnamen.";

    internal AIFunction[] AsAgentTools()
        => [AIFunctionFactory.Create((CancellationToken ct, int limit = 3) =>
                GetCallsAsync(Math.Clamp(limit, 1, 5), ct), Calls, CallsDescription),
            AIFunctionFactory.Create((CancellationToken ct, int limit = 3) =>
                GetRenewalsAsync(Math.Clamp(limit, 1, 5), ct), Renewals, RenewalsDescription),
            AIFunctionFactory.Create(GetEvidenceAsync, Evidence, EvidenceDescription)];

    [McpServerTool(Name = Calls, ReadOnly = true), Description(CallsDescription)]
    public Task<SalesStrategyResult> GetCallsAsync(int limit = 10, CancellationToken cancellationToken = default)
        => ReadAsync(Math.Clamp(limit, 1, 20), renewalsOnly: false, id: null, search: null, cancellationToken);

    [McpServerTool(Name = Renewals, ReadOnly = true), Description(RenewalsDescription)]
    public Task<SalesStrategyResult> GetRenewalsAsync(int limit = 10, CancellationToken cancellationToken = default)
        => ReadAsync(Math.Clamp(limit, 1, 20), renewalsOnly: true, id: null, search: null, cancellationToken);

    [McpServerTool(Name = Evidence, ReadOnly = true), Description(EvidenceDescription)]
    public Task<SalesStrategyResult> GetEvidenceAsync(Guid? id = null, string? search = null, CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        if ((id is null && string.IsNullOrEmpty(search)) || search?.Length > 120)
            throw new McpException("Bitte eine Vorgangs-ID oder einen Namen mit bis zu 120 Zeichen angeben.");
        return ReadAsync(id is null ? 5 : 1, renewalsOnly: false, id, search, cancellationToken);
    }

    private async Task<SalesStrategyResult> ReadAsync(int limit, bool renewalsOnly, Guid? id, string? search, CancellationToken token)
    {
        var http = accessor.HttpContext;
        if (http is null || !SalesStrategyEndpoints.HasVerifiedTenant(http)
            || !(await authorization.AuthorizeAsync(http.User, resource: null, "sales-access")).Succeeded)
            throw new UnauthorizedAccessException();

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var mine = await services.GetRequiredService<SalesTaskPlanningService>().MineAsync(http.User, token);
        var assigned = mine.Where(item => item.CrmCompletedAt is null).ToDictionary(item => item.WorkItemId);
        var worklist = await services.GetRequiredService<WorklistService>().GetAsync(http.User, refresh: false, assigned.Keys.ToArray(), token);
        var now = worklist.GeneratedAt;
        var items = SelectItems(worklist.Items, assigned.Keys.ToHashSet(), now, limit, renewalsOnly, id, search);

        var contractIds = items.Where(IsRenewal)
            .Where(item => item.TargetId.HasValue).Select(item => item.TargetId!.Value).Distinct().ToArray();
        var contractFacts = new Dictionary<Guid, RenewalFacts>();
        if (contractIds.Length != 0)
        {
            await using var db = await services.GetRequiredService<PlatformTenantDbContextFactory<SalesPlattformDbContext>>()
                .OpenReadOnlyAsync(token);
            contractFacts = await db.Context.SalesContracts.AsNoTracking()
                .Where(contract => contractIds.Contains(contract.Id) && contract.IsActive && contract.SourceDeletedAt == null)
                .Select(contract => new RenewalFacts(contract.Id, contract.Customer!.LastContactAt, contract.RecurringAmount, contract.Currency))
                .ToDictionaryAsync(contract => contract.Id, token);
        }

        var signals = items.Select(item =>
        {
            var assignment = assigned.GetValueOrDefault(item.Id);
            var facts = item.TargetId is Guid target && IsRenewal(item) ? contractFacts.GetValueOrDefault(target) : null;
            return new SalesStrategySignal(item.Id, item.Title, item.WorkItemTypeName,
                item.Reason ?? "Offener Vorgang in der Sales-Arbeitsliste", item.DueAt, item.PriorityBand,
                assignment is not null, SafeCrmUrl(assignment?.CrmTaskUrl ?? item.CrmTaskUrl ?? item.ExternalUrl),
                facts?.LastContact, facts?.Amount, facts?.Currency);
        }).ToArray();
        return new SalesStrategyResult(now, worklist.LastRefreshAt, signals);
    }

    internal static WorklistItemDto[] SelectItems(IEnumerable<WorklistItemDto> items, IReadOnlySet<Guid> assigned,
        DateTimeOffset now, int limit, bool renewalsOnly, Guid? id = null, string? search = null)
        => items.Where(item => id is null || item.Id == id)
            .Where(item => string.IsNullOrWhiteSpace(search) || item.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.AvailableFrom is null || item.AvailableFrom <= now)
            .Where(item => renewalsOnly ? IsRenewal(item) : IsCallCandidate(item, assigned.Contains(item.Id)))
            .Where(item => !IsRenewal(item) || item.DueAt >= now)
            .OrderByDescending(item => !renewalsOnly && assigned.Contains(item.Id))
            .ThenByDescending(item => item.PriorityScore)
            .ThenBy(item => item.DueAt ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.Id)
            .Take(limit).ToArray();

    private static bool IsRenewal(WorklistItemDto item)
        => item.TargetType == "contract" && item.WorkItemType is "contract-renewal" or "contract-renewal-critical";

    private static bool IsCallCandidate(WorklistItemDto item, bool assigned)
        => assigned || IsRenewal(item) || item.WorkItemType is "customer-stale" or "lead-reactivation"
            or "lead-first-response" or "call-follow-up" or "deal-stalled" or "deal-reactivation" or "offer-follow-up";

    private static string? SafeCrmUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var link) && link.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(link.UserInfo) ? link.GetLeftPart(UriPartial.Path) : null;

    private sealed record RenewalFacts(Guid Id, DateTimeOffset? LastContact, decimal? Amount, string? Currency);
}
