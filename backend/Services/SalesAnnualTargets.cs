using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

public sealed class AnnualTargetConflictException(string message) : Exception(message);

public sealed record AnnualTargetEntry(Guid OwnerId, string Name, decimal? Amount);
public sealed record AnnualTargetsResponse(DateOnly StartsAt, DateOnly EndsAt, string Revision,
    IReadOnlyCollection<AnnualTargetEntry> Entries);
public sealed record SaveAnnualTargetEntry(Guid OwnerId, decimal? Amount);
public sealed record SaveAnnualTargetsRequest(string Revision, IReadOnlyCollection<SaveAnnualTargetEntry> Entries);

public sealed partial class SalesReportService
{
    public static bool CanManageAnnualTargets(ClaimsPrincipal user)
        => SalesDashboardLayoutService.HasAnyRole(user, "sales-manager", "sales-management");

    public static void ValidateAnnualTargets(SaveAnnualTargetsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Revision) || request.Entries is null || request.Entries.Count is 0 or > 1000)
            throw new ArgumentException("Bitte die Zielplanung neu öffnen und mindestens einen Mitarbeiter auswählen.");
        if (request.Entries.Any(e => e is null))
            throw new ArgumentException("Ungültiger Mitarbeitereintrag.");
        if (request.Entries.Select(e => e.OwnerId).Distinct().Count() != request.Entries.Count
            || request.Entries.Any(e => e.OwnerId == Guid.Empty))
            throw new ArgumentException("Jeder Mitarbeiter darf nur einmal vorkommen.");
        if (request.Entries.Any(e => e.Amount is { } amount
            && (amount < 0 || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount)))
            throw new ArgumentException("Ziele müssen positive Euro-Beträge oder 0 mit höchstens zwei Nachkommastellen sein.");
    }

    public async Task<AnnualTargetsResponse> GetAnnualTargetsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!CanManageAnnualTargets(user)) throw new UnauthorizedAccessException();
        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var period = await LoadPeriodAsync(session.Context, "year", DateTimeOffset.UtcNow, cancellationToken);
        var targets = await LoadAnnualTargetsAsync(session.Context, period.FiscalYearId, cancellationToken);
        return await DescribeAnnualTargetsAsync(session.Context, period, targets, cancellationToken);
    }

    public async Task SaveAnnualTargetsAsync(SaveAnnualTargetsRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!CanManageAnnualTargets(user)) throw new UnauthorizedAccessException();
        ValidateAnnualTargets(request);
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var db = session.Context;
        // Serializable also protects first creation: nullable period IDs do not
        // provide a uniqueness guarantee in the existing PostgreSQL index.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var period = await LoadPeriodAsync(db, "year", DateTimeOffset.UtcNow, cancellationToken);
        var targets = await LoadAnnualTargetsAsync(db, period.FiscalYearId, cancellationToken);
        var current = await DescribeAnnualTargetsAsync(db, period, targets, cancellationToken);
        if (request.Revision != current.Revision)
            throw new AnnualTargetConflictException("Die Zielplanung wurde inzwischen geändert. Bitte neu öffnen.");
        if (request.Entries.Any(e => !current.Entries.Any(c => c.OwnerId == e.OwnerId)))
            throw new ArgumentException("Ein ausgewählter Mitarbeiter ist nicht mehr verfügbar.");

        var yearId = period.FiscalYearId;
        if (yearId is null)
        {
            if (await db.SalesFiscalYears.AnyAsync(y => y.StartsAt <= period.FiscalYearEnd && y.EndsAt >= period.FiscalYearStart, cancellationToken))
                throw new AnnualTargetConflictException("Ein vorhandenes Geschäftsjahr überschneidet sich mit dem Kalenderjahr. Bitte die Geschäftsjahreskonfiguration prüfen.");
            var year = new SalesFiscalYear { Id = Guid.NewGuid(), Name = period.FiscalYearStart.Year.ToString(),
                StartsAt = period.FiscalYearStart, EndsAt = period.FiscalYearEnd, TimeZone = "UTC" };
            db.SalesFiscalYears.Add(year);
            yearId = year.Id;
        }
        foreach (var entry in request.Entries)
        {
            var target = targets.SingleOrDefault(t => t.OwnerId == entry.OwnerId);
            if (entry.Amount is null)
            {
                if (target is not null) db.SalesTargets.Remove(target);
                continue;
            }
            if (target is null)
            {
                target = new SalesTarget { Id = Guid.NewGuid(), FiscalYearId = yearId.Value,
                    OwnerId = entry.OwnerId, TargetType = "revenue", ValidFrom = period.FiscalYearStart, ValidTo = period.FiscalYearEnd };
                db.SalesTargets.Add(target);
            }
            target.TargetValue = entry.Amount.Value;
            target.Currency = "EUR";
            target.ApprovedAt = DateTimeOffset.UtcNow;
            target.ApprovedBy = user.FindFirstValue("sub");
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<SalesTarget[]> LoadAnnualTargetsAsync(SalesPlattformDbContext db, Guid? yearId, CancellationToken cancellationToken)
    {
        var targets = await db.SalesTargets.Where(t => t.FiscalYearId == yearId && t.TargetPeriodId == null).ToArrayAsync(cancellationToken);
        return targets.Where(t => IsRevenueTarget(t.TargetType)).ToArray();
    }

    private static async Task<AnnualTargetsResponse> DescribeAnnualTargetsAsync(SalesPlattformDbContext db,
        ReportPeriod period, SalesTarget[] targets, CancellationToken cancellationToken)
    {
        if (targets.GroupBy(t => t.OwnerId).Any(g => g.Count() > 1))
            throw new AnnualTargetConflictException("Für einen Mitarbeiter sind mehrere Jahresumsatzziele vorhanden. Bitte die Zielplanung bereinigen.");
        if (targets.Any(t => !string.IsNullOrWhiteSpace(t.Currency) && !t.Currency.Trim().Equals("EUR", StringComparison.OrdinalIgnoreCase)))
            throw new AnnualTargetConflictException("Die vorhandene Zielplanung enthält andere Währungen als EUR und kann hier nicht überschrieben werden.");
        var existingOwners = targets.Select(t => t.OwnerId).ToArray();
        var owners = await db.SalesOwners.AsNoTracking()
            .Where(o => (o.IsActive && o.SourceDeletedAt == null) || existingOwners.Contains(o.Id))
            .OrderBy(o => o.DisplayName).ToArrayAsync(cancellationToken);
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            period.FiscalYearId, period.FiscalYearStart, period.FiscalYearEnd,
            Targets = targets.OrderBy(t => t.Id).Select(t => new { t.Id, t.OwnerId, t.TargetValue, t.Currency, t.ApprovedAt }),
            Owners = owners.Select(o => o.Id).OrderBy(id => id)
        }))));
        return new(period.FiscalYearStart, period.FiscalYearEnd, revision,
            owners.Select(o => new AnnualTargetEntry(o.Id, o.DisplayName, targets.SingleOrDefault(t => t.OwnerId == o.Id)?.TargetValue)).ToArray());
    }
}
