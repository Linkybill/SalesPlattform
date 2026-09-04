using System.Text.Json;
using IdentityPlatform.Shared.Database;
using IdentityPlatform.Shared.Jobs;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations.Repositories;

namespace SalesPlattform.Backend.Services;

/// <summary>
/// Applies the fachliche consequences of canonical CRM changes. The service
/// is intentionally shared by full/incremental crawls and webhook consumers:
/// provider adapters are responsible only for fetching and mapping data.
/// </summary>
public sealed class CrmBusinessChangeProcessor(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ISalesCrmRepositoryFactory repositoryFactory,
    SalesApplicationSettingsService applicationSettings,
    WorklistService worklist,
    CrmTaskMirrorService taskMirror,
    SalesSnapshotService snapshots,
    SalesNotificationDeliveryService notifications)
    : ICrmBusinessChangeProcessor
{
    private const decimal RulesProgressStart = 65m;
    private const decimal RulesProgressEnd = 85m;
    private const decimal TasksProgressEnd = 92m;
    private const decimal SnapshotProgressEnd = 96m;

    public async Task<CrmBusinessChangeResult> ProcessAsync(
        PlatformJobExecutionContext context,
        CrmBusinessChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = request.RequestedBy ?? context.RequestedBy ?? $"system:{request.ProviderKey}-change";
        var changes = request.Changes;
        var fullEvaluation = string.Equals(request.Mode, CrmSyncModes.Full, StringComparison.OrdinalIgnoreCase);

        await context.Logger.InfoAsync(
            $"Fachliche CRM-Nachverarbeitung gestartet: {changes.Count} betroffene Änderung(en).",
            "Fachliche Nachverarbeitung",
            JsonSerializer.SerializeToElement(new
            {
                phase = "business-change-processing",
                request.ProviderKey,
                request.ConnectionKey,
                request.Mode,
                actor,
                changedRecords = changes.Count,
                fullEvaluation,
                pipeline = new[] { "normalisieren", "regeln", "crm-aufgaben", "kennzahlen", "benachrichtigungen" }
            }),
            cancellationToken);

        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "Fachliche Nachverarbeitung",
                Message: "Fachliche Änderungen werden vorbereitet.",
                ProgressPercent: RulesProgressStart,
                ItemsProcessed: 0,
                ItemsTotal: changes.Count,
                ItemsFailed: 0,
                Details: JsonSerializer.SerializeToElement(new
                {
                    phase = "business-change-processing",
                    changedRecords = changes.Count,
                    fullEvaluation
                })),
            cancellationToken);

        // Materialized call markers are rebuilt before rules read the canonical
        // model. This is deliberately not a database lock: CRM sync and hooks
        // use the existing tenant-scoped concurrency rules only.
        await using (var session = await dbFactory.OpenAsync(cancellationToken))
        {
            var repository = repositoryFactory.Create(session.Context);
            await repository.BackfillLeadActivityMarkersAsync(cancellationToken);
            var threshold = await applicationSettings.GetCallConversationThresholdSecondsAsync(
                context.TenantId,
                actor,
                cancellationToken);
            await repository.RecalculateLeadCallCountersAsync(
                changes,
                threshold,
                cancellationToken);
        }

        var evaluation = fullEvaluation || changes.Count > 0
            ? await EvaluateRulesAsync(context, request, changes, actor, cancellationToken)
            : new WorklistEvaluationResult(0, 0, 0, false);

        await context.Logger.InfoAsync(
            "CRM-Aufgaben werden aus den aktuellen Arbeitsvorgängen gespiegelt.",
            "CRM-Aufgaben",
            JsonSerializer.SerializeToElement(new
            {
                phase = "crm-task-mirror",
                status = "started",
                evaluation.CreatedCount,
                evaluation.ResolvedCount
            }),
            cancellationToken);
        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "CRM-Aufgaben",
                Message: "Aktive Arbeitsvorgänge werden mit CRM-Aufgaben abgeglichen.",
                ProgressPercent: RulesProgressEnd,
                ItemsProcessed: 0,
                ItemsTotal: null,
                ItemsFailed: 0),
            cancellationToken);

        var taskMirrorResult = await taskMirror.EnsureActiveTasksAsync(
            context.TenantId,
            request.ProviderKey,
            request.ConnectionKey,
            cancellationToken);
        var taskMirrorMessage =
            $"CRM-Aufgaben-Abgleich abgeschlossen: {taskMirrorResult.Created} erstellt, "
            + $"{taskMirrorResult.Updated} aktualisiert, {taskMirrorResult.Failed} fehlgeschlagen, "
            + $"{taskMirrorResult.Unchanged} unverändert, "
            + $"{taskMirrorResult.BaselineEstablished} Baselines gesetzt, "
            + $"{taskMirrorResult.Skipped} zurückgestellt.";
        if (taskMirrorResult.HasWarnings)
        {
            await context.Logger.WarningAsync(
                taskMirrorMessage,
                "CRM-Aufgaben",
                JsonSerializer.SerializeToElement(taskMirrorResult),
                cancellationToken);
        }
        else
        {
            await context.Logger.InfoAsync(
                taskMirrorMessage,
                "CRM-Aufgaben",
                JsonSerializer.SerializeToElement(taskMirrorResult),
                cancellationToken);
        }

        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "CRM-Aufgaben",
                Message: taskMirrorMessage,
                ProgressPercent: TasksProgressEnd,
                ItemsProcessed: taskMirrorResult.Created
                    + taskMirrorResult.Updated
                    + taskMirrorResult.Unchanged
                    + taskMirrorResult.BaselineEstablished
                    + taskMirrorResult.Failed
                    + taskMirrorResult.Skipped,
                ItemsTotal: taskMirrorResult.ActiveItems,
                ItemsFailed: taskMirrorResult.Failed,
                Details: JsonSerializer.SerializeToElement(taskMirrorResult)),
            cancellationToken);

        await context.Logger.InfoAsync(
            "Kennzahlen-Snapshot wird aus dem aktuellen Fachmodell neu berechnet.",
            "Kennzahlen",
            JsonSerializer.SerializeToElement(new
            {
                phase = "snapshot",
                status = "started"
            }),
            cancellationToken);
        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "Kennzahlen",
                Message: "Belastbare Kennzahlen und Tages-Snapshots werden berechnet.",
                ProgressPercent: TasksProgressEnd,
                ItemsProcessed: 0,
                ItemsTotal: null,
                ItemsFailed: 0),
            cancellationToken);
        var snapshot = await snapshots.CreateDailyAsync(context, cancellationToken);
        await context.Logger.InfoAsync(
            $"Kennzahlen aktualisiert: {snapshot.DealCount} Deals, {snapshot.OpenDealCount} offene Deals, {snapshot.ActivityCount} Aktivitäten.",
            "Kennzahlen",
            JsonSerializer.SerializeToElement(new
            {
                request.Mode,
                snapshot.SnapshotDate,
                snapshot.DealCount,
                snapshot.OpenDealCount,
                snapshot.ActivityCount,
                refreshed = snapshot.AlreadyPresent
            }),
            cancellationToken);
        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "Kennzahlen",
                Message: "Kennzahlen-Snapshot aktualisiert.",
                ProgressPercent: SnapshotProgressEnd,
                ItemsProcessed: snapshot.DealCount,
                ItemsTotal: snapshot.DealCount,
                ItemsFailed: 0,
                Details: JsonSerializer.SerializeToElement(new
                {
                    phase = "snapshot",
                    snapshot.SnapshotDate,
                    snapshot.DealCount,
                    snapshot.OpenDealCount,
                    snapshot.ActivityCount
                })),
            cancellationToken);

        await context.Logger.InfoAsync(
            "Benachrichtigungen werden unmittelbar im selben CRM-Lauf verarbeitet.",
            "Benachrichtigungen",
            JsonSerializer.SerializeToElement(new
            {
                phase = "notifications",
                status = "started"
            }),
            cancellationToken);
        var delivered = await notifications.ProcessAsync(context, cancellationToken);
        await context.Logger.InfoAsync(
            $"Benachrichtigungen verarbeitet: {delivered.Sent} versendet, {delivered.Failed} fehlgeschlagen, {delivered.Skipped} übersprungen/unterdrückt.",
            "Benachrichtigungen",
            JsonSerializer.SerializeToElement(delivered),
            cancellationToken);
        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "Benachrichtigungen",
                Message: "Benachrichtigungen wurden verarbeitet.",
                ProgressPercent: 100m,
                ItemsProcessed: delivered.Examined,
                ItemsTotal: delivered.Examined,
                ItemsFailed: delivered.Failed,
                Details: JsonSerializer.SerializeToElement(delivered)),
            cancellationToken);

        return new CrmBusinessChangeResult(
            evaluation,
            taskMirrorResult,
            snapshot,
            delivered);
    }

    private async Task<WorklistEvaluationResult> EvaluateRulesAsync(
        PlatformJobExecutionContext context,
        CrmBusinessChangeRequest request,
        IReadOnlyCollection<CrmSynchronizationChange> changes,
        string actor,
        CancellationToken cancellationToken)
    {
        await context.Logger.InfoAsync(
            $"Regelbewertung wird für {changes.Count} betroffene CRM-Datensätze gestartet.",
            "Regelbewertung",
            JsonSerializer.SerializeToElement(new
            {
                phase = "rule-evaluation",
                request.Mode,
                fullEvaluation = string.Equals(request.Mode, CrmSyncModes.Full, StringComparison.OrdinalIgnoreCase),
                changedRecords = changes.Count,
                message = "Die Regeln werden auf das kanonische Sales-Datenmodell angewendet."
            }),
            cancellationToken);
        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "Regelbewertung",
                Message: "Regeln werden überprüft; betroffene Vorgänge werden ermittelt.",
                ProgressPercent: RulesProgressStart,
                ItemsProcessed: 0,
                ItemsTotal: changes.Count == 0 ? null : changes.Count,
                ItemsFailed: 0),
            cancellationToken);

        var evaluation = await worklist.EvaluateAfterSyncAsync(
            context.TenantId,
            request.Mode,
            changes,
            actor,
            context.Progress,
            context.Logger,
            cancellationToken);
        await context.Logger.InfoAsync(
            evaluation.FullEvaluation
                ? $"Arbeitsliste vollständig bewertet: {evaluation.EvaluatedCount} Treffer."
                : $"Arbeitsliste gezielt bewertet: {evaluation.EvaluatedCount} Treffer, {evaluation.ResolvedCount} Vorgänge automatisch aufgelöst.",
            "Regelbewertung",
            JsonSerializer.SerializeToElement(new
            {
                request.Mode,
                evaluation.FullEvaluation,
                changedRecords = changes.Count,
                evaluation.EvaluatedCount,
                evaluation.CreatedCount,
                evaluation.ResolvedCount
            }),
            cancellationToken);
        return evaluation;
    }
}
