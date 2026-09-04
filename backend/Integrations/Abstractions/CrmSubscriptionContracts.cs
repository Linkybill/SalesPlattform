using IdentityPlatform.Shared.Jobs;

namespace SalesPlattform.Backend.Integrations.Abstractions;

public static class CrmChangeDetectionModes
{
    public const string HooksPlusCrawl = "hooks-plus-crawl";
    public const string CrawlOnly = "crawl-only";
}

public sealed record CrmHookUpdateResult(
    string ProviderKey,
    int SubscriptionsCreated,
    int SubscriptionsRenewed,
    int SubscriptionsUnchanged,
    int EventsQueued,
    int EventsProcessed,
    int EventsFailed,
    IReadOnlyCollection<string> Warnings)
{
    public bool HasWarnings => Warnings.Count > 0 || EventsFailed > 0;
}

/// <summary>
/// Platform registration metadata for a provider's hook update service.
/// </summary>
public sealed record CrmHookJobRegistration(
    string JobKey,
    string DisplayName,
    string Description,
    string DefaultCronExpression,
    string DefaultTimeZoneId = "Europe/Berlin",
    string ConcurrencyGroup = "crm-hook-processing");

/// <summary>
/// Provider-neutral boundary for hook registration, renewal and queued
/// callback processing. The Platform job knows only this contract; it does
/// not know how a provider registers, renews, or authenticates callbacks.
/// </summary>
public interface ICrmHookUpdateService
{
    string ProviderKey { get; }
    CrmHookJobRegistration JobRegistration { get; }

    Task<CrmHookUpdateResult> ExecuteAsync(
        PlatformJobExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class CrmHookUpdateServiceRegistry(
    IEnumerable<ICrmHookUpdateService> services)
{
    private readonly IReadOnlyDictionary<string, ICrmHookUpdateService> registered =
        services.ToDictionary(service => service.ProviderKey, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ICrmHookUpdateService> All => registered.Values.ToArray();

    public ICrmHookUpdateService Resolve(string providerKey)
        => registered.TryGetValue(providerKey, out var service)
            ? service
            : throw new InvalidOperationException(
                $"Für den CRM-Provider '{providerKey}' ist kein CRM-Hook-Update-Service registriert.");
}
