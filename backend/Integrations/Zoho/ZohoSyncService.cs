using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations;
using SalesPlattform.Backend.Integrations.Repositories;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoSyncService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ZohoCrmAdapter adapter,
    ZohoCrmRecordMapper recordMapper,
    ISalesCrmRepositoryFactory repositoryFactory,
    ZohoConnectionStore connectionStore,
    ZohoConfigurationService configuration,
    PlatformJobLivenessClient platformJobLiveness,
    ILogger<ZohoSyncService> logger) : ICrmSynchronizationAdapter
{
    // Related lists are still part of the same CRM run. A small, bounded
    // amount of parallelism keeps Zoho/API limits under control while avoiding
    // one sequential network round-trip per Account/Contact/Lead/Deal.
    private const int RelatedFetchConcurrency = 4;
    private const int RelatedWriteBatchSize = 100;

    public string ProviderKey => CrmProviders.Zoho;

    public async Task<CrmSynchronizationResult> SynchronizeAsync(
        CrmSynchronizationRequest request,
        ICrmSynchronizationProgressSink progress,
        CancellationToken cancellationToken = default)
    {
        await configuration.ResolveCurrentAsync(cancellationToken);
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var repository = repositoryFactory.Create(session.Context);
        var run = await repository.GetSyncRunAsync(
            request.RunId,
            includeItems: true,
            asNoTracking: false,
            cancellationToken);
        if (run is null)
        {
            var now = DateTimeOffset.UtcNow;
            var activeRuns = await repository.GetActiveSyncRunsAsync(
                adapter.ProviderKey,
                "default",
                includeItems: true,
                asNoTracking: false,
                cancellationToken);
            var staleRuns = new List<IntegrationSyncRun>();
            IntegrationSyncRun? blockingRun = null;
            foreach (var candidate in activeRuns)
            {
                var platformIsActive = await platformJobLiveness.IsActiveAsync(candidate.Id, cancellationToken);
                var fallbackIsActive = candidate.Status == "running"
                    ? candidate.LeaseUntil is not null && candidate.LeaseUntil > now
                    : candidate.QueuedAt > now.Subtract(TimeSpan.FromMinutes(5));
                if (platformIsActive ?? fallbackIsActive)
                {
                    blockingRun = candidate;
                    break;
                }

                staleRuns.Add(candidate);
            }
            if (blockingRun is not null)
            {
                var skippedDetails = JsonSerializer.SerializeToElement(new
                {
                    provider = adapter.ProviderKey,
                    skipped = true,
                    reason = "another_sync_is_active",
                    blockingRun = new
                    {
                        id = blockingRun.Id,
                        blockingRun.Mode,
                        blockingRun.Status,
                        blockingRun.CurrentModule,
                        blockingRun.RecordsRead,
                        blockingRun.RecordsWritten,
                        blockingRun.RecordsFailed,
                        blockingRun.LeaseUntil,
                        blockingRun.WorkerId
                    }
                });
                return new CrmSynchronizationResult(
                    adapter.ProviderKey,
                    NormalizeMode(request.Mode),
                    0,
                    0,
                    1,
                    "Der Lauf wurde übersprungen, weil für diesen Mandanten bereits eine CRM-Synchronisation aktiv ist.",
                    skippedDetails);
            }

            foreach (var staleRun in staleRuns)
            {
                staleRun.Status = "failed";
                staleRun.CurrentModule = null;
                staleRun.FinishedAt = now;
                staleRun.LeaseUntil = null;
                staleRun.WorkerId = null;
                staleRun.Error = "Dieser verwaiste CRM-Lauf wurde beim Start eines neuen Plattformjobs beendet, weil der zugehörige Plattformlauf nicht mehr aktiv ist.";
                foreach (var item in staleRun.Items.Where(item => item.Status is "queued" or "running"))
                {
                    item.Status = "failed";
                    item.FinishedAt = now;
                    item.Error = staleRun.Error;
                }
            }
            if (staleRuns.Count > 0)
                await repository.SaveChangesAsync(cancellationToken);

            run = new IntegrationSyncRun
            {
                Id = request.RunId,
                ProviderKey = adapter.ProviderKey,
                ConnectionKey = "default",
                Mode = NormalizeMode(request.Mode),
                Status = "queued",
                RequestedModulesJson = JsonSerializer.Serialize(NormalizeModules(request.RequestedModules)),
                QueuedAt = DateTimeOffset.UtcNow,
                RequestedBy = request.RequestedBy
            };
            repository.AddSyncRun(run);
            await repository.SaveChangesAsync(cancellationToken);
        }
        else if (!string.Equals(run.ProviderKey, adapter.ProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Der CRM-Lauf {request.RunId:D} gehört zum Provider '{run.ProviderKey}' und kann nicht durch '{adapter.ProviderKey}' fortgesetzt werden.");
        }

        await RunAsync(
            new ZohoSynchronizationWorkItem(request.RunId, request.TenantId, request.RequestedBy),
            snapshot => ReportProgressAsync(progress, snapshot, cancellationToken),
            cancellationToken);
        var completed = await GetSnapshotAsync(
                request.RunId,
                cancellationToken,
                includeWrittenRecords: true)
            ?? throw new InvalidOperationException("Der abgeschlossene CRM-Lauf konnte nicht mehr geladen werden.");
        var details = ToDetails(completed);
        return new CrmSynchronizationResult(
            adapter.ProviderKey,
            completed.Mode,
            completed.RecordsRead,
            completed.RecordsWritten,
            completed.RecordsFailed,
            completed.RecordsFailed == 0
                ? $"CRM-Synchronisation abgeschlossen: {completed.RecordsWritten} Datensätze geschrieben."
                : $"CRM-Synchronisation mit {completed.RecordsFailed} Fehlern abgeschlossen.",
            details);
    }

    private async Task<ZohoSynchronizationSnapshot?> GetSnapshotAsync(
        Guid runId,
        CancellationToken cancellationToken = default,
        bool includeWrittenRecords = false)
    {
        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var repository = repositoryFactory.Create(session.Context);
        var run = await repository.GetSyncRunAsync(
            runId,
            includeItems: true,
            asNoTracking: true,
            cancellationToken);
        if (run is null) return null;

        var writtenRecords = includeWrittenRecords
            ? await session.Context.IntegrationRawRecords
                .AsNoTracking()
                .Where(item => item.SyncRunId == runId)
                .OrderBy(item => item.EntityType)
                .ThenBy(item => item.ExternalId)
                .Select(item => new ZohoSynchronizationWrittenRecordSnapshot(
                    item.EntityType,
                    item.ExternalId,
                    item.PayloadJson,
                    item.ExternalModifiedAt,
                    item.SyncedAt))
                .ToArrayAsync(cancellationToken)
            : [];
        return ZohoSynchronizationSnapshotMapper.Map(run, writtenRecords);
    }

    internal async Task RunAsync(
        ZohoSynchronizationWorkItem workItem,
        Func<ZohoSynchronizationSnapshot, Task> publish,
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

        await repository.ClearSyncErrorsAsync(run.Id, cancellationToken);
        if (run.Status == "running")
            ResetInterruptedRun(run);
        else
            ClearRunErrors(run);

        run.Status = "running";
        run.StartedAt = DateTimeOffset.UtcNow;
        run.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(15);
        run.WorkerId = Environment.MachineName;
        await repository.SaveChangesAsync(cancellationToken);
        await NotifyAsync(publish, run);

        try
        {
            var modules = NormalizeModules(JsonSerializer.Deserialize<string[]>(run.RequestedModulesJson));
            var isIncremental = string.Equals(run.Mode, CrmSyncModes.Incremental, StringComparison.OrdinalIgnoreCase);
            var availableModules = await adapter.GetModulesAsync(cancellationToken);
            var sourceRecords = new Dictionary<string, IReadOnlyCollection<CrmExternalRecord>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var module in modules)
            {
                run.CurrentModule = module;
                var runItem = await repository.GetOrCreateSyncRunItemAsync(run, module, cancellationToken);
                runItem.Status = "running";
                runItem.StartedAt ??= DateTimeOffset.UtcNow;
                await repository.SaveChangesAsync(cancellationToken);
                await NotifyAsync(publish, run);

                if (!IsSyntheticModule(module)
                    && !availableModules.Contains(module, StringComparer.OrdinalIgnoreCase))
                {
                    runItem.Status = "failed";
                    runItem.Error = $"Zoho stellt das Modul '{module}' für diesen OAuth-Zugang nicht als verfügbar bereit. "
                        + "Bitte die Zoho-Modulberechtigung und danach die Verbindung erneuern.";
                    runItem.FinishedAt = DateTimeOffset.UtcNow;
                    run.RecordsFailed++;
                    await repository.SaveChangesAsync(cancellationToken);
                    await NotifyAsync(publish, run);
                    continue;
                }

                if (IsRelatedModule(module))
                {
                    // E-mails and stage history are related lists in Zoho. They
                    // are loaded after their parent modules so every relation
                    // can be resolved against the canonical database.
                    runItem.Status = "queued";
                    await repository.SaveChangesAsync(cancellationToken);
                    continue;
                }

                try
                {
                    var cursor = await repository.GetOrCreateCursorAsync(
                        adapter.ProviderKey,
                        "default",
                        CursorKey(module),
                        cancellationToken);
                    var modifiedSince = isIncremental
                        ? GetIncrementalSince(cursor.LastModifiedAt)
                        : null;
                    // The watermark is captured before the read. Changes
                    // arriving while this module is being read are therefore
                    // picked up by the next run (with a small overlap).
                    var watermark = DateTimeOffset.UtcNow;
                    var fields = await ResolveFieldsAsync(module, cancellationToken);
                    var records = (await adapter.GetRecordsAsync(module, fields, modifiedSince, cancellationToken)).ToArray();
                    sourceRecords[module] = records;
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
                            runItem.Error = FormatItemError(runItem.RecordsFailed, exception);
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

                    if (isIncremental)
                    {
                        foreach (var deleted in await adapter.GetDeletedRecordsAsync(
                                     module,
                                     modifiedSince,
                                     cancellationToken))
                        {
                            await repository.MarkDeletedAsync(deleted, run.Id, cancellationToken);
                        }
                    }

                    if (runItem.RecordsFailed == 0)
                    {
                        runItem.Error = null;
                        cursor.LastModifiedAt = watermark;
                        cursor.LastExternalId = records.LastOrDefault()?.ExternalId;
                        cursor.LastSuccessfulRunId = run.Id;
                        cursor.LastStartedAt = runItem.StartedAt;
                        cursor.LastError = null;
                        cursor.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                    else
                    {
                        cursor.LastError = $"{runItem.RecordsFailed} Datensätze konnten im Modul nicht verarbeitet werden.";
                        cursor.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                    runItem.Status = runItem.RecordsFailed == 0 ? "succeeded" : "completed_with_errors";
                    runItem.FinishedAt = DateTimeOffset.UtcNow;
                    await repository.SaveChangesAsync(cancellationToken);
                    await NotifyAsync(publish, run);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    run.RecordsFailed++;
                    runItem.RecordsFailed++;
                    runItem.Status = "failed";
                    runItem.Error = FormatItemError(runItem.RecordsFailed, exception);
                    runItem.FinishedAt = DateTimeOffset.UtcNow;
                    repository.AddSyncError(run.Id, runItem.Id, module, null, exception);
                    logger.LogWarning(exception, "Zoho module {Module} could not be imported.", module);
                    await repository.SaveChangesAsync(cancellationToken);
                    await NotifyAsync(publish, run);
                }
            }

            await ImportRelatedRecordsAsync(
                run,
                modules,
                isIncremental,
                sourceRecords,
                repository,
                publish,
                cancellationToken);

            run.Status = run.RecordsFailed == 0 ? "succeeded" : "completed_with_errors";
            run.CurrentModule = null;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.LeaseUntil = null;
            run.Error = run.RecordsFailed == 0
                ? null
                : $"{run.RecordsFailed} Datensätze konnten nicht importiert werden.";
            await repository.SaveChangesAsync(cancellationToken);
            if (run.Status == "succeeded")
                await connectionStore.MarkSyncAsync(cancellationToken);
            await NotifyAsync(publish, run);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Status = "failed";
            run.CurrentModule = null;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.LeaseUntil = null;
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
                :
                [
                    "Users", "Accounts", "Contacts", "Leads", "Products", "Pipelines",
                    "PipelineStages", "Deals", "DealStageHistory", "Calls", "Tasks", "Events", "Emails"
                ])
            .Select(module => module.Trim())
            .Where(module => module.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (modules.Length == 0)
            throw new InvalidOperationException("Es wurde kein Zoho-Modul für den Import angegeben.");
        return modules;
    }

    private static string NormalizeMode(string? requestedMode)
        => string.IsNullOrWhiteSpace(requestedMode)
            ? CrmSyncModes.Full
            : requestedMode.Trim().ToLowerInvariant() switch
            {
                CrmSyncModes.Full => CrmSyncModes.Full,
                CrmSyncModes.Incremental => CrmSyncModes.Incremental,
                _ => throw new InvalidOperationException($"Der Importmodus '{requestedMode}' wird nicht unterstützt.")
            };

    private static string CursorKey(string module)
        => $"source:{module.Trim().ToLowerInvariant()}";

    private static DateTimeOffset? GetIncrementalSince(DateTimeOffset? lastModifiedAt)
        => lastModifiedAt?.Subtract(TimeSpan.FromMinutes(2));

    private static string FormatItemError(int failedCount, Exception exception)
    {
        var message = IntegrationErrorFormatter.Describe(exception, 3600);
        return $"{failedCount} Datensätze fehlgeschlagen. Letzter Fehler: {message}";
    }

    private static bool IsSyntheticModule(string module)
        => module.Equals("Pipelines", StringComparison.OrdinalIgnoreCase)
            || module.Equals("PipelineStages", StringComparison.OrdinalIgnoreCase)
            || module.Equals("DealStageHistory", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Emails", StringComparison.OrdinalIgnoreCase);

    private static bool IsRelatedModule(string module)
        => module.Equals("DealStageHistory", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Emails", StringComparison.OrdinalIgnoreCase);

    private async Task ImportRelatedRecordsAsync(
        IntegrationSyncRun run,
        IReadOnlyCollection<string> modules,
        bool isIncremental,
        IReadOnlyDictionary<string, IReadOnlyCollection<CrmExternalRecord>> sourceRecords,
        ISalesCrmRepository repository,
        Func<ZohoSynchronizationSnapshot, Task> publish,
        CancellationToken cancellationToken)
    {
        if (modules.Contains("Emails", StringComparer.OrdinalIgnoreCase))
        {
            await ImportRelatedModuleAsync(
                run,
                "Emails",
                ["Accounts", "Contacts", "Leads", "Deals"],
                "Emails",
                isIncremental,
                sourceRecords,
                repository,
                publish,
                cancellationToken);
        }

        if (modules.Contains("DealStageHistory", StringComparer.OrdinalIgnoreCase))
        {
            await ImportRelatedModuleAsync(
                run,
                "DealStageHistory",
                ["Deals"],
                "Stage_History",
                isIncremental,
                sourceRecords,
                repository,
                publish,
                cancellationToken);
        }
    }

    private async Task ImportRelatedModuleAsync(
        IntegrationSyncRun run,
        string module,
        IReadOnlyCollection<string> parentModules,
        string relatedList,
        bool isIncremental,
        IReadOnlyDictionary<string, IReadOnlyCollection<CrmExternalRecord>> sourceRecords,
        ISalesCrmRepository repository,
        Func<ZohoSynchronizationSnapshot, Task> publish,
        CancellationToken cancellationToken)
    {
        var runItem = await repository.GetOrCreateSyncRunItemAsync(run, module, cancellationToken);
        run.CurrentModule = module;
        runItem.Status = "running";
        runItem.StartedAt ??= DateTimeOffset.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        await NotifyAsync(publish, run);

        var cursor = await repository.GetOrCreateCursorAsync(
            adapter.ProviderKey,
            "default",
            CursorKey(module),
            cancellationToken);
        var modifiedSince = isIncremental
            ? GetIncrementalSince(cursor.LastModifiedAt)
            : null;
        var watermark = DateTimeOffset.UtcNow;
        var fields = recordMapper.GetPreferredFields(module);

        var parentWorkItems = parentModules
            .SelectMany(parentModule => sourceRecords.TryGetValue(parentModule, out var parents)
                ? parents.Select(parent => new RelatedParentWorkItem(parentModule, parent.ExternalId))
                : [])
            .Where(item => !string.IsNullOrWhiteSpace(item.ParentId))
            .DistinctBy(item => $"{item.ParentModule}:{item.ParentId}", StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using var relatedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var resultChannel = Channel.CreateBounded<RelatedParentResult>(
            new BoundedChannelOptions(RelatedFetchConcurrency * 2)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        var fetchTask = ProduceRelatedRecordsAsync(
            parentWorkItems,
            relatedList,
            fields,
            modifiedSince,
            resultChannel.Writer,
            relatedCancellation.Token);

        var completedParents = 0;
        try
        {
            await foreach (var result in resultChannel.Reader.ReadAllAsync(relatedCancellation.Token))
            {
                completedParents++;
                if (result.Error is not null)
                {
                    run.RecordsFailed++;
                    runItem.RecordsFailed++;
                    runItem.Error = FormatItemError(runItem.RecordsFailed, result.Error);
                    repository.AddSyncError(
                        run.Id,
                        runItem.Id,
                        module,
                        result.Parent.ParentId,
                        result.Error);
                    logger.LogWarning(
                        result.Error,
                        "Zoho related records {RelatedList} for {ParentModule}/{ExternalId} could not be imported.",
                        relatedList,
                        result.Parent.ParentModule,
                        result.Parent.ParentId);
                    await repository.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    // A provider can expose the same related activity through
                    // more than one relation. Keep all parent relations, but
                    // do not process an accidental duplicate from one response
                    // twice.
                    var records = result.Records
                        .GroupBy(record => record.ExternalId, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .ToArray();
                    run.RecordsRead += records.Length;
                    runItem.RecordsRead += records.Length;
                    await PersistRelatedRecordsAsync(
                        run,
                        runItem,
                        module,
                        records,
                        repository,
                        publish,
                        cancellationToken);
                    logger.LogInformation(
                        "Zoho related list {RelatedList}: {ParentModule}/{ExternalId} geliefert {RecordCount} Datensätze ({CompletedParents}/{TotalParents} Elternobjekte).",
                        relatedList,
                        result.Parent.ParentModule,
                        result.Parent.ParentId,
                        records.Length,
                        completedParents,
                        parentWorkItems.Length);
                }

                await NotifyAsync(publish, run);
            }

            await fetchTask;
        }
        finally
        {
            relatedCancellation.Cancel();
            try
            {
                await fetchTask;
            }
            catch (OperationCanceledException) when (relatedCancellation.IsCancellationRequested)
            {
                // Cancellation is expected when persistence fails or the
                // platform cancels the CRM run.
            }
        }

        if (runItem.RecordsFailed == 0)
        {
            runItem.Error = null;
            cursor.LastModifiedAt = watermark;
            cursor.LastSuccessfulRunId = run.Id;
            cursor.LastStartedAt = runItem.StartedAt;
            cursor.LastError = null;
        }
        else
        {
            cursor.LastError = $"{runItem.RecordsFailed} Related-Datensätze konnten nicht verarbeitet werden.";
        }
        cursor.UpdatedAt = DateTimeOffset.UtcNow;
        runItem.Status = runItem.RecordsFailed == 0 ? "succeeded" : "completed_with_errors";
        runItem.FinishedAt = DateTimeOffset.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        await NotifyAsync(publish, run);
    }

    private async Task ProduceRelatedRecordsAsync(
        IReadOnlyCollection<RelatedParentWorkItem> parents,
        string relatedList,
        IReadOnlyCollection<string> fields,
        DateTimeOffset? modifiedSince,
        ChannelWriter<RelatedParentResult> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await Parallel.ForEachAsync(
                parents,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = RelatedFetchConcurrency,
                    CancellationToken = cancellationToken
                },
                async (parent, token) =>
                {
                    try
                    {
                        var records = await adapter.GetRelatedRecordsAsync(
                            parent.ParentModule,
                            parent.ParentId,
                            relatedList,
                            fields,
                            modifiedSince,
                            token);
                        await writer.WriteAsync(
                            new RelatedParentResult(parent, records, null),
                            token);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        await writer.WriteAsync(
                            new RelatedParentResult(parent, [], exception),
                            token);
                    }
                });
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }

    private async Task PersistRelatedRecordsAsync(
        IntegrationSyncRun run,
        IntegrationSyncRunItem runItem,
        string module,
        IReadOnlyCollection<CrmExternalRecord> records,
        ISalesCrmRepository repository,
        Func<ZohoSynchronizationSnapshot, Task> publish,
        CancellationToken cancellationToken)
    {
        var recordsArray = records.ToArray();
        for (var offset = 0; offset < recordsArray.Length; offset += RelatedWriteBatchSize)
        {
            var batch = recordsArray
                .Skip(offset)
                .Take(RelatedWriteBatchSize)
                .ToArray();
            try
            {
                foreach (var record in batch)
                {
                    var canonical = recordMapper.Map(record);
                    await repository.UpsertAsync(canonical, run.Id, cancellationToken);
                }

                await repository.SaveChangesAsync(cancellationToken);
                run.RecordsWritten += batch.Length;
                runItem.RecordsWritten += batch.Length;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Keep the existing per-record error isolation. Batching is
                // the fast path; if a batch is invalid, retry only this batch
                // one record at a time so one bad email cannot hide the rest.
                repository.DetachRecordChanges();
                await PersistRelatedRecordsIndividuallyAsync(
                    run,
                    runItem,
                    module,
                    batch,
                    repository,
                    cancellationToken);
            }

            await NotifyAsync(publish, run);
        }
    }

    private async Task PersistRelatedRecordsIndividuallyAsync(
        IntegrationSyncRun run,
        IntegrationSyncRunItem runItem,
        string module,
        IReadOnlyCollection<CrmExternalRecord> records,
        ISalesCrmRepository repository,
        CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
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
                runItem.Error = FormatItemError(runItem.RecordsFailed, exception);
                repository.AddSyncError(run.Id, runItem.Id, module, record.ExternalId, exception);
                logger.LogError(
                    exception,
                    "Zoho related record {Module}/{ExternalId} could not be imported.",
                    record.Module,
                    record.ExternalId);
                await repository.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private sealed record RelatedParentWorkItem(string ParentModule, string ParentId);

    private sealed record RelatedParentResult(
        RelatedParentWorkItem Parent,
        IReadOnlyCollection<CrmExternalRecord> Records,
        Exception? Error);

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
        ClearRunErrors(run);
        foreach (var item in run.Items)
        {
            item.Status = "queued";
            item.StartedAt = null;
            item.FinishedAt = null;
            item.RecordsRead = 0;
            item.RecordsWritten = 0;
            item.RecordsFailed = 0;
        }
    }

    private static void ClearRunErrors(IntegrationSyncRun run)
    {
        run.Error = null;
        run.Errors.Clear();
        foreach (var item in run.Items)
        {
            item.Error = null;
            item.Errors.Clear();
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
        var actualNameSet = actualNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return recordMapper.GetPreferredFields(module)
            .Where(name => actualNameSet.Count == 0 || name.Equals("id", StringComparison.OrdinalIgnoreCase) || actualNameSet.Contains(name))
            .Concat(actualNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
    }

    private async Task NotifyAsync(
        Func<ZohoSynchronizationSnapshot, Task> publish,
        IntegrationSyncRun run)
    {
        try
        {
            if (run.Status == "running")
                run.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(15);
            await publish(ZohoSynchronizationSnapshotMapper.Map(run));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Could not publish Zoho import update for job {RunId}.", run.Id);
        }
    }

    private static Task ReportProgressAsync(
        ICrmSynchronizationProgressSink progress,
        ZohoSynchronizationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var current = snapshot.Items.FirstOrDefault(item =>
            string.Equals(item.Module, snapshot.CurrentModule, StringComparison.OrdinalIgnoreCase));
        var message = BuildProgressMessage(snapshot, current);
        return progress.ReportAsync(
            new CrmSynchronizationProgress(
                snapshot.CurrentModule,
                message,
                snapshot.RecordsRead,
                snapshot.RecordsWritten,
                snapshot.RecordsFailed,
                ToDetails(snapshot)),
            cancellationToken);
    }

    private static string BuildProgressMessage(
        ZohoSynchronizationSnapshot snapshot,
        ZohoSynchronizationModuleSnapshot? current)
    {
        if (!string.IsNullOrWhiteSpace(current?.Error))
            return current.Error;
        if (!string.IsNullOrWhiteSpace(snapshot.Error))
            return snapshot.Error;

        if (current is null)
        {
            if (snapshot.FinishedAt is null
                && snapshot.Items.All(item => item.Status.Equals("queued", StringComparison.OrdinalIgnoreCase)))
            {
                return $"Synchronisationsplan ({ModeLabel(snapshot.Mode)}): "
                    + $"{FormatCount(snapshot.Modules.Count)} Bereiche werden synchronisiert: "
                    + string.Join(", ", snapshot.Modules);
            }

            return snapshot.FinishedAt is null
                ? "CRM-Synchronisation wird abgeschlossen."
                : "CRM-Synchronisation abgeschlossen.";
        }

        var processed = current.RecordsWritten + current.RecordsFailed;
        var remaining = RecordsRemaining(current);
        if (current.Status.Equals("queued", StringComparison.OrdinalIgnoreCase))
            return $"CRM-Modul {current.Module} ist eingeplant.";

        if (current.Status is "succeeded" or "completed_with_errors" or "failed")
        {
            return $"CRM-Modul {current.Module} abgeschlossen: "
                + $"{FormatCount(current.RecordsRead)} gelesen, "
                + $"{FormatCount(current.RecordsWritten)} geschrieben, "
                + $"{FormatCount(current.RecordsFailed)} Fehler, "
                + $"{FormatRemaining(remaining)}.";
        }

        if (current.RecordsRead <= 0)
            return $"CRM-Modul {current.Module} wird gelesen; die Anzahl der Einträge wird ermittelt.";

        return $"CRM-Modul {current.Module}: "
            + $"{FormatCount(processed)} von {FormatCount(current.RecordsRead)} verarbeitet, "
            + $"{FormatRemaining(remaining)} "
            + $"({FormatCount(current.RecordsWritten)} geschrieben, "
            + $"{FormatCount(current.RecordsFailed)} Fehler).";
    }

    private static int? RecordsRemaining(ZohoSynchronizationModuleSnapshot item)
    {
        if (item.RecordsRead > 0)
            return Math.Max(0, item.RecordsRead - item.RecordsWritten - item.RecordsFailed);
        return item.Status is "succeeded" or "completed_with_errors" or "failed" ? 0 : null;
    }

    private static string FormatRemaining(int? remaining)
        => remaining is null
            ? "Restmenge noch nicht ermittelt"
            : $"{FormatCount(remaining.Value)} übrig";

    private static string FormatCount(int value)
        => value.ToString("N0", CultureInfo.GetCultureInfo("de-DE"));

    private static string ModeLabel(string mode)
        => mode.Equals(CrmSyncModes.Incremental, StringComparison.OrdinalIgnoreCase)
            ? "inkrementell"
            : "Vollimport";

    private static JsonElement ToDetails(ZohoSynchronizationSnapshot snapshot)
        => JsonSerializer.SerializeToElement(new
        {
            provider = CrmProviders.Zoho,
            localRunId = snapshot.RunId,
            mode = snapshot.Mode,
            recordsRead = snapshot.RecordsRead,
            recordsWritten = snapshot.RecordsWritten,
            recordsFailed = snapshot.RecordsFailed,
            writtenRecords = snapshot.WrittenRecords.Select(record => new
            {
                entityType = record.EntityType,
                externalId = record.ExternalId,
                externalModifiedAt = record.ExternalModifiedAt,
                syncedAt = record.SyncedAt,
                payload = ParsePayload(record.PayloadJson)
            }).ToArray(),
            modules = snapshot.Modules.Select(module =>
            {
                var item = snapshot.Items.FirstOrDefault(candidate =>
                    string.Equals(candidate.Module, module, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    key = module,
                    status = item?.Status ?? "queued",
                    recordsRead = item?.RecordsRead ?? 0,
                    recordsWritten = item?.RecordsWritten ?? 0,
                    recordsFailed = item?.RecordsFailed ?? 0,
                    recordsProcessed = item is null
                        ? 0
                        : item.RecordsWritten + item.RecordsFailed,
                    recordsRemaining = item is null ? null : RecordsRemaining(item),
                    error = item?.Error,
                    errors = item?.Errors.Select(error => new
                    {
                        error.ExternalId,
                        error.ErrorCode,
                        error.Message,
                        error.Retryable,
                        error.OccurredAt
                    }).ToArray() ?? []
                };
            }).ToArray()
        });

    private static object ParsePayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return payloadJson;
        }
    }
}
