using IdentityPlatform.Shared.Jobs;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Services.Mail;

namespace SalesPlattform.Backend.Services;

public sealed class SalesNotificationDeliveryService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    SalesMailSettingsService mailSettings,
    SalesMailDeliveryProviderRegistry providers,
    ILogger<SalesNotificationDeliveryService> logger)
{
    private const string Pending = "pending";
    private const string Sending = "sending";
    private const string Sent = "sent";
    private const string Failed = "failed";
    private const int BatchSize = 50;

    public async Task<SalesNotificationDeliveryResult> ProcessAsync(
        PlatformJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var db = session.Context;
        var now = DateTimeOffset.UtcNow;

        var resetCount = await db.SalesNotifications
            .Where(notification => notification.Channel == "email"
                && notification.DeliveryStatus == Sending
                && notification.LockedUntil <= now)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(notification => notification.DeliveryStatus, Pending)
                .SetProperty(notification => notification.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(notification => notification.NextAttemptAt, now), cancellationToken);
        if (resetCount > 0)
        {
            logger.LogWarning(
                "Reset {Count} stale Sales notification claim(s) for tenant {TenantId}.",
                resetCount,
                context.TenantId);
        }

        var settings = await mailSettings.GetAsync(
            context.TenantId,
            context.RequestedBy,
            cancellationToken);
        if (!settings.Enabled)
        {
            await context.Logger.InfoAsync(
                "E-Mail-Versand ist für diesen Mandanten deaktiviert.",
                "Benachrichtigungen",
                cancellationToken: cancellationToken);
            return new SalesNotificationDeliveryResult(0, 0, 0, 0);
        }

        var provider = providers.Resolve(settings.Provider);
        var candidates = await db.SalesNotifications
            .AsNoTracking()
            .Where(notification => notification.Channel == "email"
                && (notification.DeliveryStatus == Pending || notification.DeliveryStatus == Failed)
                && (notification.NextAttemptAt == null || notification.NextAttemptAt <= now))
            .OrderBy(notification => notification.CreatedAt)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);

        await context.Logger.InfoAsync(
            $"{candidates.Length} E-Mail-Benachrichtigung(en) zur Verarbeitung gefunden.",
            "Benachrichtigungen",
            cancellationToken: cancellationToken);

        var sent = 0;
        var failed = 0;
        var skipped = 0;
        for (var index = 0; index < candidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var notification = candidates[index];
            var claimed = await ClaimAsync(db, notification.Id, now, cancellationToken);
            if (!claimed)
            {
                skipped++;
                continue;
            }

            if (await WasAlreadySentTodayAsync(db, notification, now, cancellationToken))
            {
                skipped++;
                await MarkSuppressedAsync(
                    db,
                    notification.Id,
                    "Für diesen Vorgang und Empfänger wurde heute bereits eine Benachrichtigung versendet.",
                    cancellationToken);
                continue;
            }

            var recipient = await ResolveRecipientAsync(db, notification, cancellationToken);
            if (string.IsNullOrWhiteSpace(recipient))
            {
                failed++;
                await MarkFailedAsync(
                    db,
                    notification.Id,
                    notification.AttemptCount + 1,
                    "Kein gültiger E-Mail-Empfänger für die Benachrichtigung hinterlegt.",
                    now,
                    cancellationToken);
                continue;
            }

            try
            {
                await provider.SendAsync(
                    new SalesMailDeliveryMessage(
                        [recipient],
                        notification.Subject ?? notification.Title ?? "SalesPlattform-Benachrichtigung",
                        notification.BodyHtml ?? "<p>Neue SalesPlattform-Benachrichtigung.</p>"),
                    settings,
                    cancellationToken);
                await MarkSentAsync(db, notification.Id, now, cancellationToken);
                sent++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                logger.LogWarning(
                    exception,
                    "Sales notification {NotificationId} could not be delivered.",
                    notification.Id);
                await MarkFailedAsync(
                    db,
                    notification.Id,
                    notification.AttemptCount + 1,
                    exception.Message,
                    now,
                    cancellationToken);
            }

            await context.Progress.ReportAsync(
                new PlatformJobProgress(
                    Step: "Benachrichtigungen",
                    Message: $"{index + 1} von {candidates.Length} Benachrichtigungen verarbeitet.",
                    ProgressPercent: candidates.Length == 0 ? 100 : (index + 1) * 100m / candidates.Length,
                    ItemsProcessed: index + 1,
                    ItemsTotal: candidates.Length,
                    ItemsFailed: failed),
                cancellationToken);
        }

        return new SalesNotificationDeliveryResult(candidates.Length, sent, failed, skipped);
    }

    private static async Task<bool> WasAlreadySentTodayAsync(
        SalesPlattformDbContext db,
        SalesNotification notification,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var sentNotifications = await db.SalesNotifications
            .AsNoTracking()
            .Where(other => other.Id != notification.Id
                && other.Channel == "email"
                && other.DeliveryStatus == Sent
                && other.SentAt >= dayStart
                && other.SentAt < dayEnd
                && other.RecipientSubject == notification.RecipientSubject
                && (other.WorkItemId == notification.WorkItemId
                    || other.NotificationKey == notification.NotificationKey))
            .Select(other => new { other.RecipientEmail })
            .ToArrayAsync(cancellationToken);

        return sentNotifications.Any(other => string.Equals(
            NormalizeEmail(other.RecipientEmail),
            NormalizeEmail(notification.RecipientEmail),
            StringComparison.OrdinalIgnoreCase));
    }

    private static Task<int> MarkSuppressedAsync(
        SalesPlattformDbContext db,
        Guid notificationId,
        string reason,
        CancellationToken cancellationToken)
        => db.SalesNotifications
            .Where(notification => notification.Id == notificationId && notification.DeliveryStatus == Sending)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(notification => notification.DeliveryStatus, "suppressed")
                .SetProperty(notification => notification.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(notification => notification.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(notification => notification.LastError, reason), cancellationToken);

    private static string NormalizeEmail(string? email)
        => email?.Trim() ?? string.Empty;

    private static async Task<bool> ClaimAsync(
        SalesPlattformDbContext db,
        Guid notificationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => await db.SalesNotifications
            .Where(notification => notification.Id == notificationId
                && (notification.DeliveryStatus == Pending || notification.DeliveryStatus == Failed)
                && (notification.NextAttemptAt == null || notification.NextAttemptAt <= now))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(notification => notification.DeliveryStatus, Sending)
                .SetProperty(notification => notification.AttemptCount, notification => notification.AttemptCount + 1)
                .SetProperty(notification => notification.LockedUntil, now.AddMinutes(15)), cancellationToken) == 1;

    private static async Task<string?> ResolveRecipientAsync(
        SalesPlattformDbContext db,
        SalesNotification notification,
        CancellationToken cancellationToken)
    {
        if (notification.RecipientSubject.StartsWith("sales-owner:", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(notification.RecipientSubject["sales-owner:".Length..], out var ownerId))
        {
            var ownerEmail = await db.SalesOwners
                .AsNoTracking()
                .Where(owner => owner.Id == ownerId && owner.IsActive)
                .Select(owner => owner.Email)
                .SingleOrDefaultAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(ownerEmail) ? notification.RecipientEmail : ownerEmail;
        }

        return notification.RecipientEmail;
    }

    private static Task<int> MarkSentAsync(
        SalesPlattformDbContext db,
        Guid notificationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => db.SalesNotifications
            .Where(notification => notification.Id == notificationId && notification.DeliveryStatus == Sending)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(notification => notification.DeliveryStatus, Sent)
                .SetProperty(notification => notification.SentAt, now)
                .SetProperty(notification => notification.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(notification => notification.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(notification => notification.LastError, (string?)null), cancellationToken);

    private static Task<int> MarkFailedAsync(
        SalesPlattformDbContext db,
        Guid notificationId,
        int attemptCount,
        string error,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => db.SalesNotifications
            .Where(notification => notification.Id == notificationId && notification.DeliveryStatus == Sending)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(notification => notification.DeliveryStatus, Failed)
                .SetProperty(notification => notification.AttemptCount, attemptCount)
                .SetProperty(notification => notification.NextAttemptAt, now.AddMinutes(NextRetryMinutes(attemptCount)))
                .SetProperty(notification => notification.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(notification => notification.LastError, error.Length > 4000 ? error[..4000] : error), cancellationToken);

    private static int NextRetryMinutes(int attemptCount)
        => Math.Min(60, Math.Max(5, attemptCount * 5));
}

public sealed record SalesNotificationDeliveryResult(
    int Examined,
    int Sent,
    int Failed,
    int Skipped);
