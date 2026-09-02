using System.Net;
using System.Text.Json;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

/// <summary>
/// Creates idempotent email outbox entries for rules whose fachliche action is
/// a notification. The rule engine remains unaware of SMTP or any future mail
/// client.
/// </summary>
public sealed class SalesNotificationOutboxService(
    Mail.SalesMailSettingsService mailSettings,
    ILogger<SalesNotificationOutboxService> logger)
{
    public Task<IReadOnlyCollection<string>> GetManagementRecipientsAsync(
        Guid tenantId,
        string? actor,
        CancellationToken cancellationToken = default)
        => mailSettings.GetManagementRecipientsAsync(tenantId, actor, cancellationToken);

    public void EnqueueRuleNotification(
        SalesPlattformDbContext db,
        SalesWorkItem workItem,
        WorkItemNotification notification,
        SalesOwner? owner,
        IReadOnlyCollection<string> managementRecipients,
        ISet<string> knownNotificationKeys,
        DateTimeOffset now)
    {
        var recipients = notification.RuleCode switch
        {
            "R-09" when notification.Escalated => managementRecipients
                .Select(email => (Subject: "sales-management", Email: email))
                .ToArray(),
            "R-09" when !string.IsNullOrWhiteSpace(owner?.Email) =>
                new[] { (Subject: $"sales-owner:{owner.Id:D}", Email: owner.Email) },
            "R-11" => managementRecipients
                .Select(email => (Subject: "sales-management", Email: email))
                .ToArray(),
            _ => []
        };

        if (recipients.Length == 0)
        {
            logger.LogDebug(
                "No configured notification recipient for rule {RuleCode}, target {TargetType}/{TargetId}.",
                notification.RuleCode,
                notification.TargetType,
                notification.TargetId);
            return;
        }

        var level = notification.RuleCode == "R-09" && notification.Escalated ? 2 : 1;
        var notificationDay = now.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var recipient in recipients)
        {
            // The date is part of the idempotency key deliberately: a rule may
            // notify again on a later day, but never creates a second notification
            // for the same event/target/recipient on the same day.
            var key = $"{notificationDay}:{notification.RuleCode}:{notification.TargetType}:{notification.TargetId:D}:{level}:{recipient.Subject}:{recipient.Email}";
            if (!knownNotificationKeys.Add(key))
                continue;

            var payload = JsonSerializer.Serialize(new
            {
                ruleCode = notification.RuleCode,
                targetType = notification.TargetType,
                targetId = notification.TargetId,
                workItemId = workItem.Id,
                ownerId = workItem.OwnerId,
                priority = workItem.PriorityScore,
                dueAt = workItem.DueAt,
                reason = workItem.Reason
            });
            db.SalesNotifications.Add(new SalesNotification
            {
                Id = Guid.NewGuid(),
                TenantId = workItem.TenantId,
                NotificationKey = key,
                RecipientSubject = recipient.Subject,
                RecipientEmail = recipient.Email,
                Channel = "email",
                WorkItemId = workItem.Id,
                Title = workItem.Title,
                Subject = $"SalesPlattform: {workItem.Title}",
                BodyHtml = BuildBody(workItem, notification, recipient.Email),
                PayloadJson = payload,
                DueAt = workItem.DueAt,
                EscalationLevel = level,
                DeliveryStatus = "pending",
                NextAttemptAt = now,
                CreatedAt = now
            });
        }
    }

    private static string BuildBody(
        SalesWorkItem workItem,
        WorkItemNotification notification,
        string recipientEmail)
    {
        var title = WebUtility.HtmlEncode(workItem.Title);
        var reason = WebUtility.HtmlEncode(workItem.Reason ?? "Für diesen Vorgang liegt eine neue Benachrichtigung vor.");
        var ruleCode = WebUtility.HtmlEncode(notification.RuleCode);
        var recipient = WebUtility.HtmlEncode(recipientEmail);
        var priority = WebUtility.HtmlEncode((workItem.PriorityScore ?? 0).ToString("0.##"));
        return $"""
            <html><body style="font-family:Arial,sans-serif;color:#17233b">
            <h2>{title}</h2>
            <p>{reason}</p>
            <p><strong>Regel:</strong> {ruleCode}<br />
            <strong>Priorität:</strong> {priority}<br />
            <strong>Empfänger:</strong> {recipient}</p>
            <p>Bitte den Vorgang in der SalesPlattform prüfen. CRM-Daten bleiben die fachliche Quelle.</p>
            </body></html>
            """;
    }
}

public sealed record WorkItemNotification(
    string RuleCode,
    string TargetType,
    Guid TargetId,
    bool Escalated);
