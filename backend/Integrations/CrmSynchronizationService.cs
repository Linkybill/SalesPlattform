using System.Text.Json;
using IdentityPlatform.Shared.Jobs;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations;

public sealed class CrmSynchronizationService(
    CrmSynchronizationAdapterRegistry adapters)
{
    public async Task<PlatformJobResult> ExecuteAsync(
        string mode,
        PlatformJobExecutionContext context,
        CancellationToken cancellationToken = default)
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

        var completionMessage = result.Message
            ?? (result.HasWarnings
                ? $"CRM-Synchronisation mit {result.RecordsFailed} Fehlern abgeschlossen."
                : "CRM-Synchronisation abgeschlossen.");
        if (result.HasWarnings)
        {
            await context.Logger.WarningAsync(
                completionMessage,
                "Abschluss",
                result.Details,
                cancellationToken);
        }
        else
        {
            await context.Logger.InfoAsync(
                completionMessage,
                "Abschluss",
                result.Details,
                cancellationToken);
        }

        return result.HasWarnings
            ? PlatformJobResult.SuccessWithWarnings(result.Message, result.Details)
            : PlatformJobResult.Success(result.Message, result.Details);
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
            await reporter.ReportAsync(
                new PlatformJobProgress(
                    Step: progress.Step,
                    Message: progress.Message,
                    ItemsProcessed: progress.RecordsWritten,
                    ItemsTotal: progress.RecordsRead,
                    ItemsFailed: progress.RecordsFailed,
                    Details: progress.Details),
                cancellationToken);
        }
    }
}
