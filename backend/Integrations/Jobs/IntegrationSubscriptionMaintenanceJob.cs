using System.Text.Json;
using IdentityPlatform.Shared.Jobs;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Jobs;

/// <summary>
/// One technical Platform job for all registered CRM hook services. Each
/// provider supplies its own registration/renewal implementation; this job
/// only dispatches to the registry and aggregates the results.
/// </summary>
public sealed class CrmHookUpdateJob(
    CrmHookUpdateServiceRegistry services)
    : IPlatformJob
{
    public async Task<PlatformJobResult> ExecuteAsync(
        PlatformJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<CrmHookUpdateResult>();
        foreach (var service in services.All)
        {
            await context.Logger.InfoAsync(
                $"Hook-Service '{service.ProviderKey}' wird ausgeführt: {service.JobRegistration.DisplayName}.",
                "CRM-Hooks",
                JsonSerializer.SerializeToElement(service.JobRegistration),
                cancellationToken);
            var result = await service.ExecuteAsync(context, cancellationToken);
            results.Add(result);
        }

        var warnings = results
            .SelectMany(result => result.Warnings)
            .ToArray();
        var details = JsonSerializer.SerializeToElement(new
        {
            phase = "crm-hook-update",
            providers = results,
            warnings
        });
        var message = results.Count == 0
            ? "Keine CRM-Hook-Update-Services sind registriert."
            : $"CRM-Hooks: {results.Sum(result => result.SubscriptionsCreated)} neu angelegt, "
                + $"{results.Sum(result => result.SubscriptionsRenewed)} erneuert, "
                + $"{results.Sum(result => result.SubscriptionsUnchanged)} unverändert; "
                + $"{results.Sum(result => result.EventsProcessed)} Ereignisse verarbeitet, {warnings.Length} Warnung(en).";
        return warnings.Length > 0 || results.Any(result => result.EventsFailed > 0)
            ? PlatformJobResult.SuccessWithWarnings(message, details)
            : PlatformJobResult.Success(message, details);
    }
}
