using IdentityPlatform.Shared.Jobs;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

public sealed class SalesSnapshotService(
    IdentityPlatform.Shared.Database.PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory)
{
    public async Task<SalesSnapshotResult> CreateDailyAsync(
        PlatformJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var db = session.Context;
        var existing = await db.SalesSnapshotRuns.SingleOrDefaultAsync(
            run => run.SnapshotDate == snapshotDate && run.SnapshotType == "daily",
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var wasExisting = existing is not null;
        var run = existing ?? new SalesSnapshotRun
        {
            Id = Guid.NewGuid(),
            SnapshotDate = snapshotDate,
            SnapshotType = "daily",
            Status = "running",
            StartedAt = now
        };
        if (existing is null)
        {
            db.SalesSnapshotRuns.Add(run);
        }
        else
        {
            // Der Snapshot ist die belastbare Auswertung des aktuellen Datenstands.
            // Nach jedem Full- und Incremental-Sync werden die Tageswerte daher
            // vollständig neu aufgebaut und nicht als veralteter Erfolgsstand behalten.
            await db.SalesPipelineSnapshots
                .Where(snapshot => snapshot.SnapshotRunId == run.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await db.SalesKpiSnapshots
                .Where(snapshot => snapshot.SnapshotRunId == run.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await db.SalesActivitySnapshots
                .Where(snapshot => snapshot.SnapshotRunId == run.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await db.SalesCustomerStatusSnapshots
                .Where(snapshot => snapshot.SnapshotRunId == run.Id)
                .ExecuteDeleteAsync(cancellationToken);

            run.Status = "running";
            run.StartedAt = now;
            run.FinishedAt = null;
            run.Error = null;
        }
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var deals = await db.SalesDeals.AsNoTracking()
                .Include(deal => deal.Pipeline)
                .Include(deal => deal.PipelineStage)
                .Include(deal => deal.Owner)
                .Include(deal => deal.Product)
                    .ThenInclude(product => product!.Category)
                .ToArrayAsync(cancellationToken);
            var activities = await db.SalesActivities.AsNoTracking().ToArrayAsync(cancellationToken);
            var appointments = await db.SalesAppointments.AsNoTracking().ToArrayAsync(cancellationToken);
            var contracts = await db.SalesContracts.AsNoTracking().ToArrayAsync(cancellationToken);
            var customers = await db.SalesCustomers.AsNoTracking().ToArrayAsync(cancellationToken);
            var stageHistory = await db.SalesDealStageHistory.AsNoTracking().ToArrayAsync(cancellationToken);

            var openDeals = deals.Where(deal => deal.IsActive && deal.SourceDeletedAt is null && deal.Status == "open" && !(deal.PipelineStage?.IsTerminal ?? false)).ToArray();
            foreach (var group in openDeals.GroupBy(deal => new { deal.PipelineId, deal.PipelineStageId, deal.OwnerId }))
            {
                if (!group.Key.PipelineId.HasValue || !group.Key.PipelineStageId.HasValue) continue;
                db.SalesPipelineSnapshots.Add(new SalesPipelineSnapshot
                {
                    Id = Guid.NewGuid(),
                    SnapshotRunId = run.Id,
                    SnapshotDate = snapshotDate,
                    PipelineId = group.Key.PipelineId.Value,
                    PipelineStageId = group.Key.PipelineStageId.Value,
                    OwnerId = group.Key.OwnerId,
                    OpenDealCount = group.LongCount(),
                    OpenAmount = group.Sum(deal => deal.Amount ?? 0m),
                    WeightedAmount = group.Sum(deal => (deal.Amount ?? 0m) * (deal.PipelineStage?.Probability ?? 0m) / 100m),
                    Currency = group.Select(deal => deal.Currency).FirstOrDefault(currency => !string.IsNullOrWhiteSpace(currency))
                });
            }

            var yearStart = new DateTimeOffset(snapshotDate.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var monthStart = new DateTimeOffset(snapshotDate.Year, snapshotDate.Month, 1, 0, 0, 0, TimeSpan.Zero);
            AddKpis(db, run.Id, snapshotDate, "year", yearStart, new DateTimeOffset(snapshotDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), deals, contracts, stageHistory, now);
            AddKpis(db, run.Id, snapshotDate, "month", monthStart, new DateTimeOffset(snapshotDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), deals, contracts, stageHistory, now);

            foreach (var group in activities.Where(activity => activity.SourceDeletedAt is null && activity.OccurredAt >= yearStart && activity.OccurredAt < now).GroupBy(activity => new { activity.OwnerId, activity.ActivityType }))
            {
                db.SalesActivitySnapshots.Add(new SalesActivitySnapshot
                {
                    Id = Guid.NewGuid(),
                    SnapshotRunId = run.Id,
                    SnapshotDate = snapshotDate,
                    PeriodStart = DateOnly.FromDateTime(yearStart.UtcDateTime),
                    PeriodEnd = snapshotDate,
                    OwnerId = group.Key.OwnerId,
                    ActivityType = group.Key.ActivityType,
                    PlannedCount = group.LongCount(),
                    CompletedCount = group.LongCount(activity => activity.CountsAsConversation == true || activity.ActivityType != "call"),
                    ReachedCallCount = group.LongCount(activity => activity.ActivityType == "call" && activity.CountsAsConversation == true),
                    UnreachedCallCount = group.LongCount(activity => activity.ActivityType == "call" && activity.CountsAsConversation == false)
                });
            }

            foreach (var group in customers.Where(customer => customer.IsActive && customer.SourceDeletedAt is null).GroupBy(customer => customer.Status))
            {
                db.SalesCustomerStatusSnapshots.Add(new SalesCustomerStatusSnapshot
                {
                    Id = Guid.NewGuid(),
                    SnapshotRunId = run.Id,
                    SnapshotDate = snapshotDate,
                    PeriodStart = DateOnly.FromDateTime(yearStart.UtcDateTime),
                    PeriodEnd = snapshotDate,
                    Status = group.Key,
                    ActiveCount = group.LongCount(),
                    AddedCount = group.LongCount(customer => customer.SourceCreatedAt >= yearStart),
                    LifetimeRevenue = group.Sum(customer => customer.LifetimeRevenue ?? 0m)
                });
            }

            run.Status = "succeeded";
            run.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await context.Logger.InfoAsync(
                $"Tages-Snapshot für {snapshotDate:yyyy-MM-dd} {(wasExisting ? "aktualisiert" : "erstellt")}: {openDeals.Length} offene Deals, {deals.Length} Deals insgesamt.",
                "Snapshot",
                cancellationToken: cancellationToken);
            return new(snapshotDate, deals.Length, openDeals.Length, activities.Length, wasExisting);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Status = "failed";
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Error = exception.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static void AddKpis(
        SalesPlattformDbContext db,
        Guid runId,
        DateOnly snapshotDate,
        string periodType,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyCollection<SalesDeal> deals,
        IReadOnlyCollection<SalesContract> contracts,
        IReadOnlyCollection<SalesDealStageHistory> stageHistory,
        DateTimeOffset now)
    {
        var activeDeals = deals.Where(deal => deal.IsActive && deal.SourceDeletedAt is null).ToArray();
        var won = activeDeals.Where(deal => deal.Status == "won" && InRange(deal.ClosingAt ?? deal.SourceModifiedAt, from, to)).ToArray();
        var lost = activeDeals.Where(deal => deal.Status == "lost" && InRange(deal.ClosingAt ?? deal.SourceModifiedAt, from, to)).ToArray();
        var open = activeDeals.Where(deal => deal.Status == "open" && !(deal.PipelineStage?.IsTerminal ?? false)).ToArray();
        var start = DateOnly.FromDateTime(from.UtcDateTime);
        var end = DateOnly.FromDateTime(to.UtcDateTime.AddDays(-1));
        var wonAndLost = won.Length + lost.Length;
        var firstWon = won.Where(deal => deal.CustomerId.HasValue).GroupBy(deal => deal.CustomerId!.Value).Select(group => group.OrderBy(deal => deal.ClosingAt ?? DateTimeOffset.MaxValue).First()).ToArray();
        var metrics = new (string Key, decimal? Value, long? Count, decimal? Numerator, decimal? Denominator)[]
        {
            ("won-revenue", won.Sum(deal => deal.Amount ?? 0m), null, null, null),
            ("open-pipeline", open.Sum(deal => deal.Amount ?? 0m), null, null, null),
            ("win-rate", wonAndLost == 0 ? 0m : won.Length * 100m / wonAndLost, null, won.Length, wonAndLost),
            ("new-revenue", firstWon.Sum(deal => deal.Amount ?? 0m), null, null, null),
            ("existing-revenue", Math.Max(0m, won.Sum(deal => deal.Amount ?? 0m) - firstWon.Sum(deal => deal.Amount ?? 0m)), null, null, null),
            ("stale-deals", null, open.LongCount(deal => (deal.LastActivityAt ?? deal.SourceCreatedAt) <= now.AddDays(-30)), null, null),
            ("expiring-contracts", null, contracts.LongCount(contract => contract.IsActive && contract.EndAt >= now && contract.EndAt <= now.AddDays(90)), null, null)
        };
        foreach (var metric in metrics)
        {
            db.SalesKpiSnapshots.Add(new SalesKpiSnapshot
            {
                Id = Guid.NewGuid(),
                SnapshotRunId = runId,
                SnapshotDate = snapshotDate,
                PeriodType = periodType,
                PeriodStart = start,
                PeriodEnd = end,
                MetricKey = metric.Key,
                Value = metric.Value,
                CountValue = metric.Count,
                Numerator = metric.Numerator,
                Denominator = metric.Denominator,
                Currency = "EUR"
            });
        }

        foreach (var group in stageHistory.Where(history => InRange(history.EnteredAt, from, to)).GroupBy(history => history.StageKeySnapshot))
        {
            db.SalesKpiSnapshots.Add(new SalesKpiSnapshot
            {
                Id = Guid.NewGuid(),
                SnapshotRunId = runId,
                SnapshotDate = snapshotDate,
                PeriodType = periodType,
                PeriodStart = start,
                PeriodEnd = end,
                MetricKey = "stage-dwell-days",
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { stage = group.Key }),
                Value = (decimal)group.Average(history => Math.Max(0, ((history.ExitedAt ?? now) - history.EnteredAt).TotalDays)),
                CountValue = group.LongCount()
            });
        }
    }

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to)
        => value.HasValue && value.Value >= from && value.Value < to;
}

public sealed record SalesSnapshotResult(DateOnly SnapshotDate, int DealCount, int OpenDealCount, int ActivityCount, bool AlreadyPresent);
