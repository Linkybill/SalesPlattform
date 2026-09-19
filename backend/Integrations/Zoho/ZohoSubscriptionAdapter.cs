using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdentityPlatform.Shared.Database;
using IdentityPlatform.Shared.Jobs;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations.Repositories;
using SalesPlattform.Backend.Services;

namespace SalesPlattform.Backend.Integrations.Zoho;

/// <summary>
/// Zoho-specific implementation of the provider-neutral CRM hook update
/// boundary. One subscription is maintained per relevant Zoho module.
/// </summary>
public sealed class ZohoCrmHookUpdateService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ZohoCrmAdapter crm,
    ZohoConfigurationService configuration,
    ZohoConnectionStore connections,
    ZohoSchemaCacheService schemaCache,
    ICrmRecordMapper recordMapper,
    ISalesCrmRepositoryFactory repositoryFactory,
    SalesApplicationSettingsService applicationSettings,
    ICrmBusinessChangeProcessor businessChanges,
    ICrmApiUsageRecorder apiUsage,
    ZohoWebhookSettingsService webhookSettings,
    ILogger<ZohoCrmHookUpdateService> logger)
    : ICrmHookUpdateService
{
    private const string ConnectionKey = "default";
    private const string ActiveStatus = "active";
    private const string QueuedStatus = "queued";
    private const string FailedStatus = "failed";
    private const string ProcessingStatus = "processing";
    private const string ProcessedStatus = "processed";
    private const int EventBatchSize = 100;
    // Renew before the next daily run, with headroom for scheduling delays.
    private static readonly TimeSpan SubscriptionRenewalLeadTime = TimeSpan.FromHours(36);

    public CrmHookJobRegistration JobRegistration { get; } = new(
        "crm-zoho-hook-update",
        "Zoho-Hooks erneuern",
        "Registriert, erneuert und verarbeitet Zoho-CRM-Hooks für die relevanten Sales-Module.",
        "0 3 * * *");

    internal static readonly string[] RelevantModules =
    [
        "Users",
        "Leads",
        "Accounts",
        "Deals",
        "Products",
        "Calls",
        "Tasks",
        "Events",
        "Meetings",
        "Appointments",
        "Cases",
        "Quotes",
        "Sales_Orders",
        "Invoices"
    ];

    public string ProviderKey => CrmProviders.Zoho;

    public async Task<CrmHookUpdateResult> ExecuteAsync(
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
            var configurationResult = await configuration.ResolveCurrentAsync(cancellationToken);
            if (string.Equals(
                    configurationResult.ChangeDetectionMode,
                    CrmChangeDetectionModes.CrawlOnly,
                    StringComparison.OrdinalIgnoreCase))
            {
                await context.Logger.InfoAsync(
                    "Zoho-Hooks sind für diesen Mandanten deaktiviert; der Incremental-Crawl bleibt aktiv.",
                    "CRM-Hooks",
                    cancellationToken: cancellationToken);
                return EmptyResult();
            }

            var connection = await connections.GetActiveAsync(cancellationToken);
            if (connection is null)
            {
                await context.Logger.InfoAsync(
                    "Zoho-Hooks übersprungen: keine aktive Zoho-Verbindung vorhanden.",
                    "CRM-Hooks",
                    cancellationToken: cancellationToken);
                return EmptyResult();
            }

            var webhookConfiguration = await webhookSettings.ResolveCurrentAsync(cancellationToken);
            if (webhookConfiguration.BaseUri is not { } webhookBaseUrl)
            {
                var urlError = webhookConfiguration.Error!;
                await context.Logger.WarningAsync(
                    $"Zoho-Hooks nicht registriert: {urlError}",
                    "CRM-Hooks",
                    JsonSerializer.SerializeToElement(new
                    {
                        reason = "webhook-url-not-configured",
                        source = webhookConfiguration.Source
                    }),
                    cancellationToken);
                return new CrmHookUpdateResult(
                    ProviderKey,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    [urlError]);
            }

            var schema = await schemaCache.GetCachedAsync(cancellationToken);
            if (schema is null)
            {
                const string message =
                    "Zoho-Hooks können erst registriert werden, wenn der Job 'Zoho-Schema cachen' einmal erfolgreich gelaufen ist.";
                await context.Logger.WarningAsync(message, "CRM-Hooks", cancellationToken: cancellationToken);
                return new CrmHookUpdateResult(
                    ProviderKey,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    [message]);
            }

            var availableModules = RelevantModules
                .Where(module => schema.AvailableModules.Contains(module, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var skippedModules = RelevantModules
                .Except(availableModules, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await context.Logger.InfoAsync(
                $"Zoho-Hooks werden für {availableModules.Length} fachlich relevante Module geprüft.",
                "CRM-Hooks",
                JsonSerializer.SerializeToElement(new
                {
                    modules = availableModules,
                    skippedModules,
                    reason = "module-not-in-local-schema-cache"
                }),
                cancellationToken);

            await using var session = await dbFactory.OpenAsync(cancellationToken);
            var db = session.Context;
            var subscriptions = await db.IntegrationSubscriptions
                .Where(item => item.ProviderKey == ProviderKey
                    && item.ConnectionKey == ConnectionKey)
                .ToDictionaryAsync(item => item.Module, StringComparer.OrdinalIgnoreCase, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var created = 0;
            var renewed = 0;
            var unchanged = 0;
            var warnings = new List<string>();

            foreach (var module in availableModules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                subscriptions.TryGetValue(module, out var subscription);
                var notifyUrl = BuildTenantWebhookUrl(webhookBaseUrl, context.TenantId);
                if (CanKeepSubscription(subscription, notifyUrl, now))
                {
                    subscription!.LastCheckedAt = now;
                    unchanged++;
                    continue;
                }

                try
                {
                    var oldChannelId = subscription?.ChannelId;
                    var token = CreateVerificationToken();
                    var requestedChannelId = CreateChannelId();
                    var registration = await crm.RegisterNotificationsAsync(
                        notifyUrl,
                        token,
                        requestedChannelId,
                        module,
                        cancellationToken);

                    if (!string.IsNullOrWhiteSpace(oldChannelId)
                        && !string.Equals(oldChannelId, registration.ChannelId, StringComparison.Ordinal))
                    {
                        try
                        {
                            await crm.DisableNotificationsAsync(oldChannelId, cancellationToken);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            warnings.Add($"Alte Zoho-Subscription für '{module}' konnte nicht deaktiviert werden: {exception.Message}");
                            logger.LogWarning(
                                exception,
                                "Old Zoho notification channel for {Module} could not be disabled.",
                                module);
                        }
                    }

                    if (subscription is null)
                    {
                        subscription = new IntegrationSubscription
                        {
                            Id = Guid.NewGuid(),
                            TenantId = context.TenantId,
                            ProviderKey = ProviderKey,
                            ConnectionKey = ConnectionKey,
                            Module = module,
                            EventsJson = JsonSerializer.Serialize(Operations(module)),
                            ChannelId = registration.ChannelId,
                            VerificationTokenHash = HashToken(token),
                            NotifyUrl = notifyUrl,
                            Status = ActiveStatus
                        };
                        db.IntegrationSubscriptions.Add(subscription);
                        subscriptions[module] = subscription;
                        created++;
                    }
                    else
                    {
                        subscription.EventsJson = JsonSerializer.Serialize(Operations(module));
                        subscription.ChannelId = registration.ChannelId;
                        subscription.VerificationTokenHash = HashToken(token);
                        subscription.NotifyUrl = notifyUrl;
                        subscription.Status = ActiveStatus;
                        renewed++;
                    }

                    subscription.ExpiresAt = registration.ExpiresAt ?? now.AddDays(6);
                    subscription.LastCheckedAt = now;
                    subscription.LastRenewedAt = now;
                    subscription.Error = null;
                    await db.SaveChangesAsync(cancellationToken);
                    await context.Logger.InfoAsync(
                        $"Zoho-Hook für '{module}' ist aktiv bis {subscription.ExpiresAt:O}.",
                        "CRM-Hooks",
                        JsonSerializer.SerializeToElement(new
                        {
                            module,
                            channelId = subscription.ChannelId,
                            expiresAt = subscription.ExpiresAt,
                            operations = Operations(module)
                        }),
                        cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    var message = $"Zoho-Hook für '{module}' konnte nicht registriert werden: {exception.Message}";
                    warnings.Add(message);
                    if (subscription is not null)
                    {
                        subscription.Status = "failed";
                        subscription.LastCheckedAt = now;
                        subscription.Error = message[..Math.Min(message.Length, 4000)];
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    await context.Logger.WarningAsync(
                        message,
                        "CRM-Hooks",
                        JsonSerializer.SerializeToElement(new { module, error = exception.Message }),
                        cancellationToken);
                }
            }

            var eventResult = await ProcessQueuedEventsAsync(
                context,
                db,
                cancellationToken);
            warnings.AddRange(eventResult.Warnings);
            return new CrmHookUpdateResult(
                ProviderKey,
                created,
                renewed,
                unchanged,
                eventResult.Queued,
                eventResult.Processed,
                eventResult.Failed,
                warnings);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("nicht als CRM-Integration ausgewählt", StringComparison.OrdinalIgnoreCase))
        {
            await context.Logger.InfoAsync(
                "Zoho-Hooks übersprungen: Zoho CRM ist für diesen Mandanten nicht ausgewählt.",
                "CRM-Hooks",
                cancellationToken: cancellationToken);
            return EmptyResult();
        }
        finally
        {
            var summary = apiUsage.GetPendingSummary();
            await context.Logger.InfoAsync(
                $"CRM-API-Verbrauch der Hook-Verwaltung: {summary.Requests} Requests, {summary.EstimatedUnits} Einheiten, {summary.FailedRequests} fehlgeschlagen.",
                "API-Verbrauch",
                JsonSerializer.SerializeToElement(summary),
                CancellationToken.None);
            try
            {
                await apiUsage.FlushAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "CRM-API-Verbrauch der Zoho-Hook-Verwaltung konnte nicht gespeichert werden.");
            }
        }
    }

    private async Task<WebhookProcessingResult> ProcessQueuedEventsAsync(
        PlatformJobExecutionContext context,
        SalesPlattformDbContext db,
        CancellationToken cancellationToken)
    {
        var events = await db.IntegrationWebhookEvents
            .Where(item => item.ProviderKey == ProviderKey
                && item.ConnectionKey == ConnectionKey
                && (item.Status == QueuedStatus
                    || (item.Status == FailedStatus && item.AttemptCount < 5)))
            .OrderBy(item => item.ReceivedAt)
            .Take(EventBatchSize)
            .ToArrayAsync(cancellationToken);
        if (events.Length == 0)
            return new WebhookProcessingResult(0, 0, 0, []);

        await context.Logger.InfoAsync(
            $"{events.Length} Zoho-Hook-Ereignis(se) werden gezielt verarbeitet; kein vollständiger Crawl wird gestartet.",
            "CRM-Hook-Ereignisse",
            JsonSerializer.SerializeToElement(new
            {
                queued = events.Length,
                maxBatchSize = EventBatchSize,
                processing = "affected-records-only"
            }),
            cancellationToken);

        var syncRun = await GetOrCreateWebhookRunAsync(db, context, events, cancellationToken);
        var repository = repositoryFactory.Create(db);
        var changes = new HashSet<CrmSynchronizationChange>();
        var processed = 0;
        var failed = 0;
        var warnings = new List<string>();
        var threshold = await applicationSettings.GetCallConversationThresholdSecondsAsync(
            context.TenantId,
            "system:zoho-webhook",
            cancellationToken);

        for (var index = 0; index < events.Length; index++)
        {
            var webhookEvent = events[index];
            try
            {
                webhookEvent.Status = ProcessingStatus;
                webhookEvent.AttemptCount++;
                await db.SaveChangesAsync(cancellationToken);
                var payload = ZohoWebhookPayload.ParseStored(webhookEvent.PayloadJson);
                await context.Logger.InfoAsync(
                    "Zoho-Hook-Import gestartet.", "CRM-Hook-Ereignisse",
                    JsonSerializer.SerializeToElement(new { eventId = webhookEvent.Id,
                        module = payload.Module, operation = payload.Operation,
                        attempt = webhookEvent.AttemptCount, records = payload.RecordIds.Count }), cancellationToken);
                var runItem = await repository.GetOrCreateSyncRunItemAsync(
                    syncRun,
                    payload.Module,
                    cancellationToken);
                runItem.Status = ProcessingStatus;
                runItem.StartedAt ??= DateTimeOffset.UtcNow;

                foreach (var externalId in payload.RecordIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    runItem.RecordsRead++;
                    syncRun.RecordsRead++;
                    if (payload.Operation == "delete")
                    {
                        var deleted = new CrmDeletedRecord(
                            ProviderKey,
                            payload.Module,
                            recordMapper.GetEntityType(payload.Module),
                            ZohoCrmAdapter.CanonicalizeExternalId(payload.Module, externalId),
                            DateTimeOffset.UtcNow,
                            ConnectionKey);
                        if (await repository.MarkDeletedAsync(deleted, syncRun.Id, cancellationToken))
                        {
                            changes.Add(new CrmSynchronizationChange(
                                deleted.Provider,
                                deleted.ConnectionKey,
                                deleted.EntityType,
                                deleted.ExternalId,
                                "deleted"));
                        }
                        runItem.RecordsWritten++;
                        syncRun.RecordsWritten++;
                        continue;
                    }

                    var record = await crm.GetRecordAsync(
                        payload.Module,
                        externalId,
                        recordMapper.GetPreferredFields(payload.Module),
                        cancellationToken)
                        ?? throw new InvalidOperationException(
                            $"Zoho lieferte den Datensatz '{payload.Module}/{externalId}' nicht mehr zurück.");
                    var canonical = recordMapper.Map(record);
                    await repository.UpsertAsync(
                        canonical,
                        syncRun.Id,
                        threshold,
                        cancellationToken);
                    changes.Add(new CrmSynchronizationChange(
                        canonical.ProviderKey,
                        canonical.ConnectionKey,
                        canonical.EntityType,
                        canonical.ExternalId,
                        "upserted"));
                    runItem.RecordsWritten++;
                    syncRun.RecordsWritten++;
                }

                runItem.Status = "succeeded";
                runItem.FinishedAt = DateTimeOffset.UtcNow;
                webhookEvent.Status = ProcessedStatus;
                webhookEvent.ProcessedAt = DateTimeOffset.UtcNow;
                webhookEvent.Error = null;
                processed++;
                await db.SaveChangesAsync(cancellationToken);
                await context.Logger.InfoAsync(
                    $"Zoho-Hook verarbeitet: {payload.Module} {payload.Operation}, {payload.RecordIds.Count} betroffene Datensätze.",
                    "CRM-Hook-Ereignisse",
                    JsonSerializer.SerializeToElement(new
                    {
                        eventId = webhookEvent.Id,
                        attempt = webhookEvent.AttemptCount,
                        module = payload.Module,
                        operation = payload.Operation,
                        records = payload.RecordIds.Count,
                        remaining = events.Length - index - 1
                    }),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                var message = ZohoWebhookOverviewService.SafeError(exception.Message)!;
                warnings.Add(message);
                repository.DetachRecordChanges();
                webhookEvent.Status = FailedStatus;
                webhookEvent.Error = message;
                webhookEvent.ProcessedAt = null;
                db.IntegrationWebhookEvents.Update(webhookEvent);
                syncRun.RecordsFailed++;
                await db.SaveChangesAsync(cancellationToken);
                await context.Logger.WarningAsync(
                    $"Zoho-Hook konnte nicht verarbeitet werden: {message}",
                    "CRM-Hook-Ereignisse",
                    JsonSerializer.SerializeToElement(new { eventId = webhookEvent.Id,
                        eventType = webhookEvent.EventType, attempt = webhookEvent.AttemptCount,
                        retryPending = webhookEvent.AttemptCount < 5, errorType = exception.GetType().Name,
                        remaining = events.Length - index - 1 }),
                    cancellationToken);
            }

            await context.Progress.ReportAsync(
                new PlatformJobProgress(
                    Step: "CRM-Hook-Ereignisse",
                    Message: $"{index + 1} von {events.Length} Hook-Ereignissen verarbeitet.",
                    ProgressPercent: (index + 1) * 60m / events.Length,
                    ItemsProcessed: index + 1,
                    ItemsTotal: events.Length,
                    ItemsFailed: failed),
                cancellationToken);
        }

        if (changes.Count > 0)
        {
            syncRun.Status = failed == 0 ? "succeeded" : "completed_with_errors";
            syncRun.FinishedAt = DateTimeOffset.UtcNow;
            syncRun.CurrentModule = null;
            syncRun.Error = failed == 0 ? null : $"{failed} Hook-Ereignis(se) konnten nicht verarbeitet werden.";
            await db.SaveChangesAsync(cancellationToken);
            await businessChanges.ProcessAsync(
                context,
                new CrmBusinessChangeRequest(
                    ProviderKey,
                    ConnectionKey,
                    CrmSyncModes.Incremental,
                    changes,
                    "system:zoho-webhook"),
                cancellationToken);
        }
        else
        {
            syncRun.Status = failed == 0 ? "succeeded" : "completed_with_errors";
            syncRun.FinishedAt = DateTimeOffset.UtcNow;
            syncRun.CurrentModule = null;
            syncRun.Error = failed == 0 ? null : $"{failed} Hook-Ereignis(se) konnten nicht verarbeitet werden.";
            await db.SaveChangesAsync(cancellationToken);
        }

        return new WebhookProcessingResult(events.Length, processed, failed, warnings);
    }

    private async Task<IntegrationSyncRun> GetOrCreateWebhookRunAsync(
        SalesPlattformDbContext db,
        PlatformJobExecutionContext context,
        IReadOnlyCollection<IntegrationWebhookEvent> events,
        CancellationToken cancellationToken)
    {
        var run = await db.IntegrationSyncRuns
            .SingleOrDefaultAsync(item => item.Id == context.RunId, cancellationToken);
        if (run is not null)
            return run;

        run = new IntegrationSyncRun
        {
            Id = context.RunId,
            TenantId = context.TenantId,
            ProviderKey = ProviderKey,
            ConnectionKey = ConnectionKey,
            Mode = "webhook",
            Status = "running",
            RequestedModulesJson = JsonSerializer.Serialize(
                events.Select(item => item.EventType.Split('.', 2)[0]).Distinct(StringComparer.OrdinalIgnoreCase)),
            RequestedBy = "system:zoho-webhook",
            QueuedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
            WorkerId = Environment.MachineName
        };
        db.IntegrationSyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    internal static bool CanKeepSubscription(IntegrationSubscription? subscription, string notifyUrl, DateTimeOffset now)
        => subscription?.ExpiresAt > now.Add(SubscriptionRenewalLeadTime)
            && string.Equals(subscription.NotifyUrl, notifyUrl, StringComparison.Ordinal)
            && string.Equals(subscription.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase);

    internal static bool TryValidateWebhookUrl(string? webhookUrl, out Uri uri, out string error)
    {
        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out uri!)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.EndsWith("/api/integrations/zoho/webhook", StringComparison.Ordinal))
        {
            error = "In den Sales-AppSettings unter Zoho Webhook-URL (zoho.webhookUrl) eine öffentliche HTTP(S)-URL zum /api/integrations/zoho/webhook ohne Zugangsdaten, Query oder Fragment eintragen. Leer verwendet den Deployment-Standard.";
            uri = null!;
            return false;
        }

        if (uri.IsLoopback
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            error = "Die Zoho Webhook-URL zeigt auf localhost bzw. eine lokale Domain und kann von Zoho nicht erreicht werden. Die Mandanteneinstellung zoho.webhookUrl prüfen.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string BuildTenantWebhookUrl(Uri baseUri, Guid tenantId)
    {
        var separator = string.IsNullOrWhiteSpace(baseUri.Query) ? "?" : "&";
        return $"{baseUri.AbsoluteUri}{separator}tenant_id={Uri.EscapeDataString(tenantId.ToString("D"))}";
    }

    private static string CreateChannelId()
    {
        var random = BitConverter.ToInt64(RandomNumberGenerator.GetBytes(sizeof(long)), 0)
            & long.MaxValue;
        return (1_000_000_000L + random % 8_000_000_000L).ToString();
    }

    private static string CreateVerificationToken()
        => "sp_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    internal static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string[] Operations(string module)
        => module.Equals("Users", StringComparison.OrdinalIgnoreCase)
            ? [module + ".all"]
            : [module + ".create", module + ".edit", module + ".delete"];

    private static CrmHookUpdateResult EmptyResult()
        => new(CrmProviders.Zoho, 0, 0, 0, 0, 0, 0, []);

    private sealed record WebhookProcessingResult(
        int Queued,
        int Processed,
        int Failed,
        IReadOnlyCollection<string> Warnings);
}

public sealed class ZohoWebhookReceiver(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ILogger<ZohoWebhookReceiver> logger)
{
    internal static bool HasValidSubscriptionToken(IntegrationSubscription? subscription, string token, DateTimeOffset now)
    {
        if (subscription is null || subscription.Status != "active"
            || subscription.ExpiresAt is null || subscription.ExpiresAt <= now
            || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(subscription.VerificationTokenHash)) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(subscription.VerificationTokenHash),
                SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }
        catch (FormatException) { return false; }
    }

    public async Task<ZohoWebhookReceipt> ReceiveAsync(
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        var parsed = ZohoWebhookPayload.Parse(payload.GetRawText());
        if (parsed.Module.Equals("Contacts", StringComparison.OrdinalIgnoreCase))
        {
            // Contacts/Ansprechpartner are deliberately outside the SalesPlattform
            // domain. This also protects us from a stale Zoho channel that may
            // still exist until it expires or is disabled manually.
            throw new InvalidOperationException(
                "Zoho-Webhook für Contacts wird nicht verarbeitet; Kontakte sind in der SalesPlattform deaktiviert.");
        }

        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var db = session.Context;
        var subscription = await db.IntegrationSubscriptions
            .SingleOrDefaultAsync(item => item.ProviderKey == CrmProviders.Zoho
                && item.ConnectionKey == "default"
                && item.ChannelId == parsed.ChannelId
                && item.Module == parsed.Module
                && item.Status == "active", cancellationToken);
        if (!HasValidSubscriptionToken(subscription, parsed.Token, DateTimeOffset.UtcNow))
        {
            logger.LogWarning("Rejected Zoho webhook: subscription verification failed.");
            throw new UnauthorizedAccessException("Zoho-Webhook konnte nicht verifiziert werden.");
        }

        var externalEventId = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(parsed.PayloadJson + "|" + parsed.ChannelId))).ToLowerInvariant();
        var existing = await db.IntegrationWebhookEvents
            .SingleOrDefaultAsync(item => item.ProviderKey == CrmProviders.Zoho
                && item.ConnectionKey == "default"
                && item.ExternalEventId == externalEventId, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Zoho webhook duplicate ignored: EventId {EventId}, Status {Status}.", existing.Id, existing.Status);
            return new ZohoWebhookReceipt(existing.Id, false, existing.Status);
        }

        var webhookEvent = new IntegrationWebhookEvent
        {
            Id = Guid.NewGuid(),
            ProviderKey = CrmProviders.Zoho,
            ConnectionKey = "default",
            EventType = $"{parsed.Module}.{parsed.Operation}",
            ExternalEventId = externalEventId,
            PayloadJson = parsed.ToStoredJson(),
            Status = "queued",
            AttemptCount = 0,
            ReceivedAt = DateTimeOffset.UtcNow
        };
        db.IntegrationWebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Zoho webhook queued: EventId {EventId}, Module {Module}, Operation {Operation}, Records {Records}.",
            webhookEvent.Id, parsed.Module, parsed.Operation, parsed.RecordIds.Count);
        return new ZohoWebhookReceipt(webhookEvent.Id, true, webhookEvent.Status);
    }
}

public sealed record ZohoWebhookReceipt(Guid EventId, bool Queued, string Status);

internal sealed record ZohoWebhookPayload(
    string Module,
    string Operation,
    string ChannelId,
    string Token,
    IReadOnlyCollection<string> RecordIds,
    string PayloadJson)
{
    // Verification is mandatory at the HTTP boundary, but secrets are not needed by the durable queue.
    public static ZohoWebhookPayload Parse(string payloadJson) => ParseCore(payloadJson, requireToken: true);
    public static ZohoWebhookPayload ParseStored(string payloadJson) => ParseCore(payloadJson, requireToken: false);
    public string ToStoredJson() => JsonSerializer.Serialize(new
    {
        module = Module, operation = Operation, channel_id = ChannelId, ids = RecordIds
    });

    private static ZohoWebhookPayload ParseCore(string payloadJson, bool requireToken)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var module = ReadString(root, "module");
        var channelId = ReadString(root, "channel_id");
        var token = ReadString(root, "token");
        if (string.IsNullOrWhiteSpace(module)
            || string.IsNullOrWhiteSpace(channelId)
            || (requireToken && string.IsNullOrWhiteSpace(token)))
        {
            throw new InvalidOperationException("Zoho-Webhook enthält kein Modul, keine Channel-ID oder keinen Verification-Token.");
        }

        var operation = ReadString(root, "operation")?.Trim().ToLowerInvariant() switch
        {
            "insert" or "create" => "create",
            "update" or "edit" => "edit",
            "delete" => "delete",
            _ => throw new InvalidOperationException("Zoho-Webhook enthält keine unterstützte Operation.")
        };
        var recordIds = root.TryGetProperty("ids", out var ids)
            && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : [];
        if (recordIds.Length == 0)
            throw new InvalidOperationException("Zoho-Webhook enthält keine betroffene Remote-ID.");

        return new ZohoWebhookPayload(
            module,
            operation,
            channelId,
            token ?? string.Empty,
            recordIds,
            payloadJson);
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}
