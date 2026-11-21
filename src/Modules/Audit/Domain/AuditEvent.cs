using Nexus.SharedKernel.Primitives;

namespace Nexus.Modules.Audit.Domain;

public sealed class AuditEvent : Entity
{
    private AuditEvent() { }

    public Guid? ActorUserId { get; private set; }
    public string? ActorEmail { get; private set; }
    public Guid? TenantId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public string? ResourceId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string Result { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string? BeforeMetadataJson { get; private set; }
    public string? AfterMetadataJson { get; private set; }
    public bool IsCrossTenantPlatformAccess { get; private set; }

    public static AuditEvent Create(
        Guid? actorUserId,
        string? actorEmail,
        Guid? tenantId,
        string action,
        string resourceType,
        string? resourceId,
        DateTimeOffset occurredAt,
        string? ipAddress,
        string? userAgent,
        string correlationId,
        string result,
        string? reason,
        string? beforeJson,
        string? afterJson,
        bool isCrossTenantPlatformAccess)
        => new()
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            TenantId = tenantId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            OccurredAt = occurredAt,
            IpAddress = ipAddress,
            UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
            CorrelationId = correlationId,
            Result = result,
            Reason = reason?.Length > 1000 ? reason[..1000] : reason,
            BeforeMetadataJson = beforeJson,
            AfterMetadataJson = afterJson,
            IsCrossTenantPlatformAccess = isCrossTenantPlatformAccess
        };
}
