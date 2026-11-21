using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Notification.Domain;

public enum NotificationChannel
{
    InApp = 1,
    Email = 2
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
    public NotificationChannel Channel { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DeliveryStatus Status { get; private set; } = DeliveryStatus.Pending;
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    public static NotificationMessage Create(Guid tenantId, Guid? userId, string? email, NotificationChannel channel, string subject, string body)
    {
        var msg = new NotificationMessage
        {
            Id = Guid.CreateVersion7(),
            RecipientUserId = userId,
            RecipientEmail = email?.Trim().ToLowerInvariant(),
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
