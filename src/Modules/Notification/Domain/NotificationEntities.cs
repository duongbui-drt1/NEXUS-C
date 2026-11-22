using Nexus.BuildingBlocks.Infrastructure;
using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Notification.Domain;

public enum NotificationChannel
{
    InApp = 1,
    Email = 2,
    Webhook = 3
}

public enum DeliveryStatus
{
    Pending = 1,
    Delivered = 2,
    Failed = 3,
    Retrying = 4
}

public sealed class NotificationTemplate : TenantScopedEntity
{
    private NotificationTemplate() { }

    public string Code { get; private set; } = string.Empty;
    public NotificationChannel Channel { get; private set; }
    public string SubjectTemplate { get; private set; } = string.Empty;
    public string BodyTemplate { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    public static Result<NotificationTemplate> Create(Guid tenantId, string code, NotificationChannel channel, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
        {
            return Error.Validation("NOTIFICATION_INVALID_TEMPLATE", "Code, subject, and body template are required.");
        }

        var tmpl = new NotificationTemplate
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            Channel = channel,
            SubjectTemplate = subject.Trim(),
            BodyTemplate = body,
            IsActive = true
        };
        tmpl.AssignTenant(tenantId);
        return tmpl;
    }

    public (string Subject, string Body) Render(IReadOnlyDictionary<string, string>? variables)
    {
        var subj = SubjectTemplate;
        var body = BodyTemplate;
        if (variables is not null)
        {
            foreach (var kv in variables)
            {
                var token = "{{" + kv.Key + "}}";
                subj = subj.Replace(token, kv.Value, StringComparison.Ordinal);
                body = body.Replace(token, kv.Value, StringComparison.Ordinal);
            }
        }
        return (subj, body);
    }
}

public sealed class NotificationMessage : TenantScopedEntity
{
    private NotificationMessage() { }

    public Guid? RecipientUserId { get; private set; }
    public string? RecipientEmail { get; private set; }
    public string? WebhookUrl { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DeliveryStatus Status { get; private set; } = DeliveryStatus.Pending;
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    public static Result<NotificationMessage> Create(
        Guid tenantId,
        Guid? userId,
        string? email,
        NotificationChannel channel,
        string subject,
        string body,
        string? webhookUrl = null)
    {
        if (channel == NotificationChannel.Webhook)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl) ||
                !Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) ||
                SsrfGuard.IsUnsafeTarget(uri))
            {
                return Error.Validation("NOTIFICATION_UNSAFE_WEBHOOK", "Webhook notification URL must be a valid public HTTPS URL.");
            }
        }

        var msg = new NotificationMessage
        {
            Id = Guid.CreateVersion7(),
            RecipientUserId = userId,
            RecipientEmail = email?.Trim().ToLowerInvariant(),
            WebhookUrl = string.IsNullOrWhiteSpace(webhookUrl) ? null : webhookUrl.Trim(),
            Channel = channel,
            Subject = subject.Trim(),
            Body = body,
            Status = DeliveryStatus.Pending
        };
        msg.AssignTenant(tenantId);
        return msg;
    }

    public void MarkDelivered(DateTimeOffset utcNow)
    {
        Status = DeliveryStatus.Delivered;
        DeliveredAt = utcNow;
        LastError = null;
    }

    public void RecordFailure(string error, int maxRetries = 3)
    {
        RetryCount++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        Status = RetryCount >= maxRetries ? DeliveryStatus.Failed : DeliveryStatus.Retrying;
    }

    public void MarkRead(DateTimeOffset utcNow)
    {
        IsRead = true;
        ReadAt = utcNow;
    }
}

public sealed class NotificationPreference : TenantScopedEntity
{
    private NotificationPreference() { }

    public Guid UserId { get; private set; }
    public bool InAppEnabled { get; private set; } = true;
    public bool EmailEnabled { get; private set; } = true;
    public bool WebhookEnabled { get; private set; } = false;
    public string DigestFrequency { get; private set; } = "Immediate"; // Immediate, Daily, Weekly

    public static NotificationPreference Create(Guid tenantId, Guid userId, bool inApp, bool email, bool webhook, string digestFrequency)
    {
        var pref = new NotificationPreference
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            InAppEnabled = inApp,
            EmailEnabled = email,
            WebhookEnabled = webhook,
            DigestFrequency = string.IsNullOrWhiteSpace(digestFrequency) ? "Immediate" : digestFrequency.Trim()
        };
        pref.AssignTenant(tenantId);
        return pref;
    }

    public void Update(bool inApp, bool email, bool webhook, string digestFrequency)
    {
        InAppEnabled = inApp;
        EmailEnabled = email;
        WebhookEnabled = webhook;
        if (!string.IsNullOrWhiteSpace(digestFrequency))
        {
            DigestFrequency = digestFrequency.Trim();
        }
    }
}

public enum EmailDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Delivered = 3,
    Bounced = 4,
    Complained = 5,
    Suppressed = 6,
    Failed = 7,
    Retrying = 8
}

public sealed class EmailDeliveryRecord : AuditableEntity
{
    private EmailDeliveryRecord() { }

    public Guid? TenantId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string Provider { get; private set; } = "Resend";
    public string? ProviderMessageId { get; private set; }
    public string? TemplateKey { get; private set; }
    public int TemplateVersion { get; private set; } = 1;
    public string RecipientMasked { get; private set; } = string.Empty;
    public string RecipientHash { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public EmailDeliveryStatus Status { get; private set; } = EmailDeliveryStatus.Pending;
    public int AttemptCount { get; private set; }
    public bool IsTransientError { get; private set; }
    public string? LastError { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    public static EmailDeliveryRecord Create(
        Guid? tenantId,
        string provider,
        string recipientMasked,
        string recipientHash,
        string subject,
        string? idempotencyKey = null,
        string? templateKey = null,
        int templateVersion = 1,
        string? correlationId = null)
        => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Provider = string.IsNullOrWhiteSpace(provider) ? "Resend" : provider.Trim(),
            RecipientMasked = recipientMasked,
            RecipientHash = recipientHash,
            Subject = subject.Length > 300 ? subject[..300] : subject,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            TemplateKey = string.IsNullOrWhiteSpace(templateKey) ? null : templateKey.Trim().ToUpperInvariant(),
            TemplateVersion = Math.Max(1, templateVersion),
            CorrelationId = correlationId,
            Status = EmailDeliveryStatus.Pending
        };

    public void MarkSent(string? providerMessageId, DateTimeOffset utcNow)
    {
        AttemptCount++;
        ProviderMessageId = providerMessageId;
        Status = EmailDeliveryStatus.Sent;
        SentAt = utcNow;
        LastError = null;
        IsTransientError = false;
    }

    public void MarkSuppressed(string reason)
    {
        Status = EmailDeliveryStatus.Suppressed;
        LastError = reason;
    }

    public void MarkFailed(string error, bool isTransient)
    {
        AttemptCount++;
        IsTransientError = isTransient;
        LastError = error.Length > 1000 ? error[..1000] : error;
        Status = EmailDeliveryStatus.Failed;
    }

    public void ApplyWebhookStatus(EmailDeliveryStatus newStatus, DateTimeOffset utcNow, string? error = null)
    {
        Status = newStatus;
        if (newStatus == EmailDeliveryStatus.Delivered) DeliveredAt = utcNow;
        if (!string.IsNullOrWhiteSpace(error)) LastError = error.Length > 1000 ? error[..1000] : error;
    }
}

public sealed class EmailSuppression : Entity
{
    private EmailSuppression() { }

    public string RecipientHash { get; private set; } = string.Empty;
    public string RecipientMasked { get; private set; } = string.Empty;
    public string Reason { get; private set; } = "Bounce";
    public string Provider { get; private set; } = "Resend";
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset SuppressedAt { get; private set; }

    public static EmailSuppression Create(string recipientHash, string recipientMasked, string reason, string provider, DateTimeOffset utcNow)
        => new()
        {
            Id = Guid.CreateVersion7(),
            RecipientHash = recipientHash,
            RecipientMasked = recipientMasked,
            Reason = reason,
            Provider = provider,
            IsActive = true,
            SuppressedAt = utcNow
        };

    public void Deactivate() => IsActive = false;
}

public sealed class EmailWebhookEvent : Entity
{
    private EmailWebhookEvent() { }

    public string SvixId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string? ProviderMessageId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    public static EmailWebhookEvent Create(string svixId, string eventType, string? providerMessageId, DateTimeOffset utcNow)
        => new()
        {
            Id = Guid.CreateVersion7(),
            SvixId = svixId.Trim(),
            EventType = eventType.Trim(),
            ProviderMessageId = providerMessageId,
            ProcessedAt = utcNow
        };
}
