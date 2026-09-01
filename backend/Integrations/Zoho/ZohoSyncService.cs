using System.Text.Json;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations.Repositories;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoSyncAlreadyRunningException : InvalidOperationException
{
    public ZohoSyncAlreadyRunningException()
        : base("Für diesen Mandanten läuft bereits ein Zoho-Import.")
    {
    }
}

public sealed class ZohoSyncService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ICrmAdapter adapter,
    ICrmRecordMapper recordMapper,
    ISalesCrmRepositoryFactory repositoryFactory,
    ZohoConnectionStore connectionStore,
    ZohoSyncJobStore jobStore,
    ILogger<ZohoSyncService> logger)
{
    public async Task<ZohoSyncJobStartResult> StartAsync(
        IReadOnlyCollection<string>? requestedModules,
        Guid tenantId,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var repository = repositoryFactory.Create(session.Context);
        var modules = NormalizeModules(requestedModules);
        if (await repository.HasActiveSyncRunAsync(
                adapter.ProviderKey,
                "default",
                cancellationToken))
        {
            throw new ZohoSyncAlreadyRunningException();
        }

        var run = new IntegrationSyncRun
        {
            Id = Guid.NewGuid(),
            ProviderKey = adapter.ProviderKey,
            ConnectionKey = "default",
            Mode = "full",
            Status = "queued",
            RequestedModulesJson = JsonSerializer.Serialize(modules),
            QueuedAt = DateTimeOffset.UtcNow,
            RequestedBy = requestedBy
        };
        repository.AddSyncRun(run);
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            await jobStore.EnqueueAsync(
                new ZohoSyncJobWorkItem(run.Id, tenantId, requestedBy),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Status = "failed";
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Error = "Der Importauftrag konnte nicht in die Hintergrundwarteschlange eingestellt werden: "
                + exception.Message[..Math.Min(exception.Message.Length, 3500)];
            await repository.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(run.Error, exception);
        }

        return new ZohoSyncJobStartResult(run.Id, run.Status);
    }

    public async Task<ZohoSyncJobSnapshot?> GetSnapshotAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var repository = repositoryFactory.Create(session.Context);
        var run = await repository.GetSyncRunAsync(
            runId,
            includeItems: true,
            asNoTracking: true,
            cancellationToken);
        return run is null ? null : ZohoSyncJobSnapshotMapper.Map(run);
    }

    public async Task<ZohoSyncJobSnapshot?> GetActiveSnapshotAndRecoverAsync(
        Guid tenantId,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var repository = repositoryFactory.Create(session.Context);
        var activeRuns = (await repository.GetActiveSyncRunsAsync(
            adapter.ProviderKey,
            "default",
            includeItems: true,
            asNoTracking: false,
            cancellationToken)).ToArray();
        if (activeRuns.Length == 0)
            return null;

        // A worker that is already handling a job keeps it scheduled in the
        // process. Any additional active rows are stale duplicates from a
        // previous start/rebuild and must not block the durable queue.
        var run = activeRuns.FirstOrDefault(item => jobStore.IsScheduled(item.Id))
            ?? activeRuns[0];
        foreach (var duplicate in activeRuns.Where(item => item.Id != run.Id))
        {
            duplicate.Status = "failed";
            duplicate.FinishedAt = DateTimeOffset.UtcNow;
            duplicate.CurrentModule = null;
            duplicate.Error = "Der Lauf wurde beendet, weil bereits ein anderer Zoho-Import aktiv war.";
            foreach (var item in duplicate.Items.Where(item => item.Status is "queued" or "running"))
            {
                item.Status = "failed";
                item.FinishedAt = duplicate.FinishedAt;
                item.Error = duplicate.Error;
            }
        }

        if (!jobStore.IsScheduled(run.Id))
        {
            if (run.Status == "running")
                ResetInterruptedRun(run);

            await repository.SaveChangesAsync(cancellationToken);
            try
            {
                await jobStore.EnqueueAsync(
                    new ZohoSyncJobWorkItem(run.Id, tenantId, requestedBy),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                run.Status = "failed";
                run.FinishedAt = DateTimeOffset.UtcNow;
                run.Error = "Der vorhandene Importauftrag konnte nicht wieder in die Warteschlange eingestellt werden: "
                    + exception.Message[..Math.Min(exception.Message.Length, 3500)];
                await repository.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException(run.Error, exception);
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        return ZohoSyncJobSnapshotMapper.Map(run);
    }

    internal async Task RunAsync(
        ZohoSyncJobWorkItem workItem,
        Func<ZohoSyncJobSnapshot, Task> publish,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var repository = repositoryFactory.Create(session.Context);
        var run = await repository.GetSyncRunAsync(
            workItem.RunId,
            includeItems: true,
            asNoTracking: false,
            cancellationToken);
        if (run is null)
        {
            logger.LogWarning("Zoho import job {RunId} was not found in the tenant database.", workItem.RunId);
            return;
        }
        if (run.Status is not ("queued" or "running"))
        {
            logger.LogInformation("Zoho import job {RunId} is already in state {Status}; skipping it.", workItem.RunId, run.Status);
            return;
        }

        if (run.Status == "running")
            ResetInterruptedRun(run);

        run.Status = "running";
        run.StartedAt = DateTimeOffset.UtcNow;
        run.WorkerId = Environment.MachineName;
        await repository.SaveChangesAsync(cancellationToken);
        await NotifyAsync(publish, run);

        try
        {
            var modules = NormalizeModules(JsonSerializer.Deserialize<string[]>(run.RequestedModulesJson));
            var availableModules = await adapter.GetModulesAsync(cancellationToken);
            var unavailable = modules
                .Where(module => !availableModules.Contains(module, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (unavailable.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Diese Zoho-Module sind nicht verfügbar: {string.Join(", ", unavailable)}.");
            }

            foreach (var module in modules)
            {
                run.CurrentModule = module;
                var runItem = await repository.GetOrCreateSyncRunItemAsync(run, module, cancellationToken);
                runItem.Status = "running";
                runItem.StartedAt ??= DateTimeOffset.UtcNow;
                await repository.SaveChangesAsync(cancellationToken);
                await NotifyAsync(publish, run);

                var fields = await ResolveFieldsAsync(module, cancellationToken);
                var records = (await adapter.GetRecordsAsync(module, fields, cancellationToken)).ToArray();
                run.RecordsRead += records.Length;
                runItem.RecordsRead = records.Length;
                await repository.SaveChangesAsync(cancellationToken);
                await NotifyAsync(publish, run);

                for (var index = 0; index < records.Length; index++)
                {
                    var record = records[index];
                    try
                    {
                        var canonical = recordMapper.Map(record);
                        await repository.UpsertAsync(canonical, run.Id, cancellationToken);
                        await repository.SaveChangesAsync(cancellationToken);
                        run.RecordsWritten++;
                        runItem.RecordsWritten++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        repository.DetachRecordChanges();
                        run.RecordsFailed++;
                        runItem.RecordsFailed++;
                        repository.AddSyncError(run.Id, runItem.Id, module, record.ExternalId, exception);
                        logger.LogError(
                            exception,
                            "Zoho record {Module}/{ExternalId} could not be imported.",
                            record.Module,
                            record.ExternalId);
                        await repository.SaveChangesAsync(cancellationToken);
                    }

                    if ((index + 1) % 10 == 0 || index == records.Length - 1)
                        await NotifyAsync(publish, run);
                }

                var cursor = await repository.GetOrCreateCursorAsync(
                    adapter.ProviderKey,
                    "default",
                    recordMapper.GetEntityType(module),
                    cancellationToken);
                cursor.LastModifiedAt = records
                    .Where(record => record.ModifiedAt.HasValue)
                    .Select(record => record.ModifiedAt)
                    .Max();
                cursor.LastExternalId = records.LastOrDefault()?.ExternalId;
                cursor.UpdatedAt = DateTimeOffset.UtcNow;
                runItem.Status = runItem.RecordsFailed == 0 ? "succeeded" : "completed_with_errors";
                runItem.FinishedAt = DateTimeOffset.UtcNow;
                await repository.SaveChangesAsync(cancellationToken);
                await NotifyAsync(publish, run);
            }

            run.Status = run.RecordsFailed == 0 ? "succeeded" : "completed_with_errors";
            run.CurrentModule = null;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Error = run.RecordsFailed == 0
                ? null
                : $"{run.RecordsFailed} Datensätze konnten nicht importiert werden.";
            await repository.SaveChangesAsync(cancellationToken);
            await connectionStore.MarkSyncAsync(cancellationToken);
            await NotifyAsync(publish, run);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Status = "failed";
            run.CurrentModule = null;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Error = exception.Message[..Math.Min(exception.Message.Length, 4000)];
            var currentItem = run.Items.SingleOrDefault(item => item.Status == "running");
            if (currentItem is not null)
            {
                currentItem.Status = "failed";
                currentItem.FinishedAt = DateTimeOffset.UtcNow;
                currentItem.Error = run.Error;
            }
            await repository.SaveChangesAsync(cancellationToken);
            await NotifyAsync(publish, run);
            throw;
        }
    }

    public static string[] NormalizeModules(IReadOnlyCollection<string>? requestedModules)
    {
        var modules = (requestedModules is { Count: > 0 }
                ? requestedModules
                : ["Accounts", "Deals", "Leads"])
            .Select(module => module.Trim())
            .Where(module => module.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (modules.Length == 0)
            throw new InvalidOperationException("Es wurde kein Zoho-Modul für den Import angegeben.");
        return modules;
    }

    private static void ResetInterruptedRun(IntegrationSyncRun run)
    {
        run.Status = "queued";
        run.StartedAt = null;
        run.FinishedAt = null;
        run.CurrentModule = null;
        run.RecordsRead = 0;
        run.RecordsWritten = 0;
        run.RecordsFailed = 0;
        run.RetryCount++;
        run.LeaseUntil = null;
        run.WorkerId = null;
        run.Error = null;
        foreach (var item in run.Items)
        {
            item.Status = "queued";
            item.StartedAt = null;
            item.FinishedAt = null;
            item.RecordsRead = 0;
            item.RecordsWritten = 0;
            item.RecordsFailed = 0;
            item.Error = null;
        }
    }

    private async Task<IReadOnlyCollection<string>> ResolveFieldsAsync(
        string module,
        CancellationToken cancellationToken)
    {
        var metadata = await adapter.GetFieldsAsync(module, cancellationToken);
        var actualNames = metadata
            .Select(field => field.ApiName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        return recordMapper.GetPreferredFields(module)
            .Concat(actualNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
    }

    private async Task NotifyAsync(
        Func<ZohoSyncJobSnapshot, Task> publish,
        IntegrationSyncRun run)
    {
        try
        {
            await publish(ZohoSyncJobSnapshotMapper.Map(run));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Could not publish Zoho import update for job {RunId}.", run.Id);
        }
    }
}
