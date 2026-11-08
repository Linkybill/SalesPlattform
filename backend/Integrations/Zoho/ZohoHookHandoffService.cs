using System.Security.Claims;
using IdentityPlatform.Shared.Database;
using IdentityPlatform.Shared.Installations;
using IdentityPlatform.Shared.Jobs;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoHookHandoffView(Guid Id, Guid TransitionId, long PlacementGeneration, string Status,
    string Message, bool CanConfirmRelay, int Channels, int VerifiedChannels);
public sealed record ZohoHookRelayConfirmation(Guid HandoffId, Guid TransitionId, long PlacementGeneration);

/// <summary>All callback URLs, provider identities and retry state remain in the tenant's local database.</summary>
public sealed class ZohoHookHandoffService(PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ZohoCrmAdapter crm, ZohoWebhookSettingsService settings, IHttpContextAccessor httpContextAccessor,
    InstallationRuntimeOptions? installation = null, InstallationLeaseState? lease = null)
{
    public const string ResumeOperation = "resume-installation-handoff";

    /// <returns>Null only when ordinary hook maintenance may proceed.</returns>
    public async Task<IReadOnlyCollection<string>?> ResumeAsync(PlatformJobExecutionContext context, CancellationToken token, string? pauseReason = null)
    {
        var resumeRequested = context.Parameters is { ValueKind: System.Text.Json.JsonValueKind.Object } parameters
            && parameters.TryGetProperty("operation", out var operation) && operation.ValueKind == System.Text.Json.JsonValueKind.String
            && operation.GetString() == ResumeOperation;
        var technicalRun = context.Trigger == "installationplacementchanged";
        if (installation is null)
            return resumeRequested || technicalRun ? [Message("owner-unconfirmed")] : null;
        var authority = context.Installation;
        if (authority is null || authority.InstallationId != installation.InstallationId || authority.TenantId != context.TenantId
            || lease?.GetPlacementGeneration(context.TenantId) != authority.PlacementGeneration)
            return [Message("owner-unconfirmed")];
        await using var session = await dbFactory.OpenAsync(token);
        var db = session.Context;
        var pending = await db.IntegrationHookHandoffs.Where(x => x.ProviderKey == "zoho" && x.ConnectionKey == "default"
            && x.Status != "completed").ToArrayAsync(token);
        var change = authority.PlacementChange;
        if (change is null)
            return pending.Length > 0 || resumeRequested || technicalRun ? [Message("owner-unconfirmed")] : null;
        if (change.TransitionId == Guid.Empty || change.TargetInstallationId != installation.InstallationId
            || !change.HasValidSource
            || change.DestinationGeneration <= 0 || change.DestinationGeneration > authority.PlacementGeneration)
            return [Message("owner-unconfirmed")];
        if (pending.Any(x => x.TransitionId != change.TransitionId || x.PlacementGeneration != change.DestinationGeneration
            || x.InstallationId != installation.InstallationId)) return [Message("previous-transition-pending")];

        var callback = await settings.ResolveCurrentAsync(token);
        var mode = callback.Source == "installation" ? "installation" : "custom";
        var handoff = await db.IntegrationHookHandoffs.Include(x => x.Channels).SingleOrDefaultAsync(x => x.ProviderKey == "zoho"
            && x.ConnectionKey == "default" && x.TransitionId == change.TransitionId, token);
        if (handoff?.Status == "completed") return resumeRequested || technicalRun ? [] : null;
        if (handoff is null)
        {
            var subscriptions = await db.IntegrationSubscriptions.Where(x => x.ProviderKey == "zoho" && x.ConnectionKey == "default"
                && x.Status == "active").Take(101).ToArrayAsync(token);
            if (subscriptions.Length > 100) return [Message("channel-conflict")];
            if (subscriptions.Length > 0 && callback.BaseUri is null) return [callback.Error ?? Message("configuration-changed")];
            handoff = new IntegrationHookHandoff
            {
                Id = Guid.NewGuid(), TenantId = context.TenantId, TransitionId = change.TransitionId,
                InstallationId = installation.InstallationId, PlacementGeneration = change.DestinationGeneration,
                ProviderKey = "zoho", ConnectionKey = "default", AddressMode = mode,
                TargetBaseUrl = callback.BaseUri?.AbsoluteUri ?? "", CreatedAt = DateTimeOffset.UtcNow,
                Status = mode == "custom" && subscriptions.Length > 0 ? "relay-confirmation-required" : "pending"
            };
            foreach (var subscription in subscriptions)
                handoff.Channels.Add(new IntegrationHookHandoffChannel
                {
                    Id = Guid.NewGuid(), TenantId = context.TenantId, HandoffId = handoff.Id, Module = subscription.Module,
                    ChannelId = subscription.ChannelId, VerificationTokenHash = subscription.VerificationTokenHash,
                    EventsJson = subscription.EventsJson, PreviousNotifyUrl = subscription.NotifyUrl,
                    TargetNotifyUrl = ZohoCrmHookUpdateService.BuildTenantWebhookUrl(callback.BaseUri!, context.TenantId)
                });
            db.IntegrationHookHandoffs.Add(handoff);
            await db.SaveChangesAsync(token); // Freeze the complete inventory before the first provider write.
        }
        if (handoff.InstallationId != installation.InstallationId || handoff.PlacementGeneration != change.DestinationGeneration
            || handoff.AddressMode != mode || handoff.TargetBaseUrl != (callback.BaseUri?.AbsoluteUri ?? ""))
            return [Message("configuration-changed")];
        if (pauseReason is not null && handoff.Channels.Count > 0)
        {
            handoff.ErrorCode = pauseReason;
            await db.SaveChangesAsync(token);
            return [Message(pauseReason)];
        }
        if (mode == "custom" && handoff.Channels.Count > 0 && handoff.RelayConfirmedAt is null)
            return [Message("relay-confirmation-required")];

        foreach (var channel in handoff.Channels.OrderBy(x => x.Module, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (channel.VerifiedAt.HasValue) continue;
            var subscription = await db.IntegrationSubscriptions.SingleOrDefaultAsync(x => x.ProviderKey == "zoho"
                && x.ConnectionKey == "default" && x.Module == channel.Module, token);
            channel.ErrorCode = subscription is null ? "channel-conflict" : await ZohoHookHandoffChannelFlow.ExecuteAsync(channel, subscription,
                ct => crm.ReadNotificationsAsync(channel.ChannelId, channel.Module, ct),
                (events, expiry, ct) => crm.MoveNotificationCallbackAsync(channel.ChannelId, channel.Module, channel.TargetNotifyUrl, events, expiry, ct),
                async ct => { await db.SaveChangesAsync(ct); }, token);
            await db.SaveChangesAsync(token);
        }
        handoff.ErrorCode = handoff.Channels.FirstOrDefault(x => x.ErrorCode is not null)?.ErrorCode;
        if (handoff.Channels.All(x => x.VerifiedAt.HasValue))
        {
            handoff.Status = "completed"; handoff.CompletedAt = DateTimeOffset.UtcNow; handoff.ErrorCode = null;
        }
        else handoff.Status = "pending";
        await db.SaveChangesAsync(token);
        return handoff.Status == "completed" ? [] : [Message(handoff.ErrorCode ?? "verification-pending")];
    }

    public async Task<ZohoHookHandoffView?> ReadCurrentAsync(CancellationToken token)
    {
        if (installation is null) return null;
        var tenantId = TenantId();
        var generation = lease?.GetPlacementGeneration(tenantId);
        if (!generation.HasValue) throw new UnauthorizedAccessException();
        await using var session = await dbFactory.OpenReadOnlyAsync(token);
        var entry = await session.Context.IntegrationHookHandoffs.AsNoTracking().Where(x => x.ProviderKey == "zoho"
            && x.ConnectionKey == "default" && x.InstallationId == installation.InstallationId && x.PlacementGeneration <= generation.Value)
            .OrderByDescending(x => x.PlacementGeneration)
            .Select(x => new { x.Id, x.TransitionId, x.PlacementGeneration, x.Status, x.ErrorCode, x.AddressMode,
                x.RelayConfirmedAt, Count = x.Channels.Count, Verified = x.Channels.Count(c => c.VerifiedAt != null) })
            .FirstOrDefaultAsync(token);
        return entry is null ? null : new(entry.Id, entry.TransitionId, entry.PlacementGeneration, entry.Status,
            Message(entry.ErrorCode ?? entry.Status), entry.AddressMode == "custom" && entry.RelayConfirmedAt is null
                && entry.Status == "relay-confirmation-required", entry.Count, entry.Verified);
    }

    public async Task ConfirmRelayAsync(ZohoHookRelayConfirmation request, CancellationToken token)
    {
        var tenantId = TenantId();
        if (installation is null || lease?.GetPlacementGeneration(tenantId) is not long currentGeneration
            || request.PlacementGeneration > currentGeneration)
            throw new UnauthorizedAccessException();
        var callback = await settings.ResolveCurrentAsync(token);
        await using var session = await dbFactory.OpenAsync(token);
        var entry = await session.Context.IntegrationHookHandoffs.SingleOrDefaultAsync(x => x.Id == request.HandoffId
            && x.TransitionId == request.TransitionId && x.InstallationId == installation.InstallationId
            && x.PlacementGeneration == request.PlacementGeneration, token) ?? throw new InvalidOperationException(Message("owner-unconfirmed"));
        if (entry.AddressMode != "custom" || callback.Source != "tenantApp" || callback.BaseUri?.AbsoluteUri != entry.TargetBaseUrl
            || entry.Status is not ("relay-confirmation-required" or "pending" or "completed")) throw new InvalidOperationException(Message("configuration-changed"));
        if (entry.RelayConfirmedAt.HasValue) return;
        entry.RelayConfirmedAt = DateTimeOffset.UtcNow;
        entry.RelayConfirmedBy = httpContextAccessor.HttpContext?.User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        entry.Status = "pending";
        await session.Context.SaveChangesAsync(token);
    }

    private Guid TenantId() => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id"), out var id)
        && id != Guid.Empty ? id : throw new UnauthorizedAccessException();

    internal static string Message(string code) => code switch
    {
        "completed" => "Übergabe bei Zoho bestätigt; Channels und Tokens bleiben erhalten. Die tatsächliche Callback-Zustellung ist separat zu prüfen.",
        "relay-confirmation-required" => "Die eigene Callback-Adresse bleibt erhalten. Die Weiterleitung des Kunden-Relays auf diese Installation prüfen und bestätigen, danach die Übergabe fortsetzen.",
        "scope-missing" => "ZohoCRM.notifications.UPDATE oder READ fehlt. Zoho erneut verbinden, Berechtigungen bestätigen und die Übergabe fortsetzen; bestehende Channels bleiben erhalten.",
        "hooks-disabled" => "Für bestehende Channels ist die Übergabe noch offen. Hooks sind deaktiviert; aktivieren und die Übergabe fortsetzen. Kein externer Schreibaufruf ausgeführt.",
        "connection-unavailable" => "Bestehende Channels bleiben erhalten. Zoho erneut verbinden und die Übergabe fortsetzen; ohne aktive Verbindung ist keine Übergabe bestätigt.",
        "channel-expired" => "Der bisherige Zoho-Channel ist abgelaufen oder sein Ablauf nicht bestätigt. Die Übergabe bleibt gesperrt; keine automatische Neuregistrierung.",
        "configuration-changed" => "Die Callback-Konfiguration wurde während der Übergabe verändert. Die zur Übergabe gespeicherte Adresse wiederherstellen und erneut prüfen.",
        "previous-transition-pending" => "Eine frühere Callback-Übergabe ist noch offen. Keine neue Registrierung, bis der bestehende Vorgang geklärt ist.",
        "owner-unconfirmed" => "Kein bestätigter Installationswechsel für diesen Mandanten und diese Generation; keine Änderung bei Zoho.",
        "channel-conflict" => "Der vorhandene Channel, Token oder Ereignisumfang stimmt nicht eindeutig überein. Keine automatische Ersetzung; Registrierung gezielt prüfen.",
        _ => "Die Callback-Übergabe ist noch nicht bestätigt. Erneut fortsetzen liest denselben Channel bei Zoho; es werden keine neuen Channels oder Tokens erzeugt."
    };
}
