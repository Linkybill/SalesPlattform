using System.Text.Json;
using IdentityPlatform.Shared.Jobs;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Services;

namespace SalesPlattform.Backend.Integrations;

public sealed class CrmSynchronizationService(
    CrmSynchronizationAdapterRegistry adapters,
    CrmBusinessChangeProcessor businessChanges,
    ICrmApiUsageRecorder apiUsage,
    ILogger<CrmSynchronizationService> logger)
{
    private const decimal CrmSynchronizationProgressEnd = 65m;

    public async Task<PlatformJobResult> ExecuteAsync(
        string mode,
        PlatformJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        using var usageScope = apiUsage.BeginScope(
            context.TenantId,
            context.RunId,
            context.RequestedBy ?? "system:platform-job",
            CrmApiUsageOrigins.Job);
        try
        {
        await context.Logger.InfoAsync(
            "CRM-Synchronisation wird vorbereitet.",
            "Initialisierung",
            JsonSerializer.SerializeToElement(new { mode } ),
            cancellationToken);

        ICrmSynchronizationAdapter adapter;
        try
        {
            adapter = await adapters.ResolveCurrentAsync(cancellationToken);
        }
        catch (CrmProviderNotConfiguredException exception)
        {
            await context.Logger.WarningAsync(
                $"CRM-Synchronisation übersprungen: {exception.Message}",
                "Provider-Auswahl",
                JsonSerializer.SerializeToElement(new
                {
                    skipped = true,
                    reason = "crm-provider-not-configured",
                    mode
                }),
                cancellationToken);
            return PlatformJobResult.SuccessWithWarnings(
                $"CRM-Synchronisation übersprungen: {exception.Message}",
                JsonSerializer.SerializeToElement(new
                {
                    skipped = true,
                    reason = "crm-provider-not-configured",
                    mode
                }));
        }

        await context.Logger.InfoAsync(
            $"CRM-Provider '{adapter.ProviderKey}' wurde ausgewählt.",
            "Provider-Auswahl",
            cancellationToken: cancellationToken);

        CrmSynchronizationResult result;
        try
        {
            result = await adapter.SynchronizeAsync(
                new CrmSynchronizationRequest(
                    context.RunId,
                    context.TenantId,
                    mode,
                    context.RequestedBy ?? "system:platform-job"),
                new PlatformProgressSink(context.Progress, context.Logger),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await context.Logger.ErrorAsync(
                $"CRM-Synchronisation abgebrochen: {exception.Message}",
                "Fehler",
                cancellationToken: cancellationToken);
            throw;
        }

        var business = await businessChanges.ProcessAsync(
            context,
            new CrmBusinessChangeRequest(
                result.ProviderKey,
                "default",
                result.Mode,
                result.ChangedRecords,
                context.RequestedBy),
            cancellationToken);
        var evaluation = business.Evaluation;
        var taskMirrorResult = business.TaskMirror;
        var notifications = business.Notifications;
        var completionDetails = JsonSerializer.SerializeToElement(new
        {
            phase = "completed",
            sync = result.Details,
            business
        });

        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "Abschluss",
                Message: "CRM-Synchronisation und Nachverarbeitung abgeschlossen.",
                ProgressPercent: 100m,
                ItemsProcessed: result.RecordsWritten,
                ItemsTotal: result.RecordsRead,
                ItemsFailed: result.RecordsFailed + taskMirrorResult.Failed + notifications.Failed,
                Details: completionDetails),
            cancellationToken);

        var completionMessage = result.Message
            ?? (result.HasWarnings
                ? $"CRM-Synchronisation mit {result.RecordsFailed} Fehlern abgeschlossen."
                : "CRM-Synchronisation abgeschlossen.");
        if (result.HasWarnings)
        {
            await context.Logger.WarningAsync(
                completionMessage,
                "Abschluss",
                completionDetails,
                cancellationToken);
        }
        else
        {
            await context.Logger.InfoAsync(
                completionMessage,
                "Abschluss",
                completionDetails,
                cancellationToken);
        }

        var hasWarnings = result.HasWarnings
            || taskMirrorResult.HasWarnings
            || notifications.Failed > 0;
        return hasWarnings
            ? PlatformJobResult.SuccessWithWarnings(result.Message, completionDetails)
            : PlatformJobResult.Success(result.Message, completionDetails);
        }
        finally
        {
            var summary = apiUsage.GetPendingSummary();
            await context.Logger.InfoAsync(
                $"CRM-API-Verbrauch erfasst: {summary.Requests} Requests, {summary.EstimatedUnits} Einheiten, {summary.FailedRequests} fehlgeschlagen.",
                "API-Verbrauch",
                JsonSerializer.SerializeToElement(summary),
                CancellationToken.None);
            try
            {
                await apiUsage.FlushAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "CRM-API-Verbrauch für den Lauf {RunId} konnte nicht gespeichert werden.", context.RunId);
            }
        }
    }

    private sealed class PlatformProgressSink(
        IPlatformJobProgressReporter reporter,
        IPlatformJobLogger logger) : ICrmSynchronizationProgressSink
    {
        private string? lastStep;
        private string? lastMessage;
        private long lastFailed;

        public Task ReportAsync(
            CrmSynchronizationProgress progress,
            CancellationToken cancellationToken = default)
            => ReportAndLogAsync(progress, cancellationToken);

        private async Task ReportAndLogAsync(
            CrmSynchronizationProgress progress,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(lastStep, progress.Step, StringComparison.Ordinal)
                || !string.Equals(lastMessage, progress.Message, StringComparison.Ordinal)
                || progress.RecordsFailed > lastFailed)
            {
                if (progress.RecordsFailed > lastFailed)
                {
                    await logger.WarningAsync(
                        progress.Message ?? "Bei der CRM-Synchronisation sind Fehler aufgetreten.",
                        progress.Step,
                        progress.Details,
                        cancellationToken);
                }
                else
                {
                    await logger.InfoAsync(
                        progress.Message ?? "CRM-Synchronisation wird ausgeführt.",
                        progress.Step,
                        progress.Details,
                        cancellationToken);
                }
            }

            lastStep = progress.Step;
            lastMessage = progress.Message;
            lastFailed = progress.RecordsFailed;
            var recordsProcessed = progress.RecordsWritten + progress.RecordsFailed;
            var progressPercent = progress.RecordsRead > 0
                ? Math.Clamp(
                    recordsProcessed * CrmSynchronizationProgressEnd / progress.RecordsRead,
                    0,
                    CrmSynchronizationProgressEnd)
                : 0m;
            await reporter.ReportAsync(
                new PlatformJobProgress(
                    Step: progress.Step,
                    Message: progress.Message,
                    ProgressPercent: progressPercent,
                    ItemsProcessed: recordsProcessed,
                    ItemsTotal: progress.RecordsRead,
                    ItemsFailed: progress.RecordsFailed,
                    Details: progress.Details),
                cancellationToken);
        }
    }
}
