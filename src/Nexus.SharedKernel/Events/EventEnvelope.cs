using System.Text.Json;

namespace Nexus.SharedKernel.Events;

public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    Guid? TenantId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    int SchemaVersion,
    JsonElement Payload)
{
    public static EventEnvelope Create<TPayload>(
        string eventType,
        Guid? tenantId,
        string correlationId,
        TPayload payload,
        int schemaVersion = 1,
        DateTimeOffset? occurredAt = null)
    {
        var element = JsonSerializer.SerializeToElement(payload, EventJsonOptions.Default);
        return new EventEnvelope(
            Guid.CreateVersion7(),
            eventType,
            tenantId,
            occurredAt ?? DateTimeOffset.UtcNow,
            correlationId,
            schemaVersion,
            element);
    }
}

public static class EventJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

public static class PlatformEventTypes
{
    public const string TenantCreatedV1 = "platform.tenant.created.v1";
    public const string TenantUpdatedV1 = "platform.tenant.updated.v1";
    public const string TenantSuspendedV1 = "platform.tenant.suspended.v1";
    public const string TenantActivatedV1 = "platform.tenant.activated.v1";

    public const string UserCreatedV1 = "platform.user.created.v1";
    public const string UserInvitedV1 = "platform.user.invited.v1";
    public const string UserDisabledV1 = "platform.user.disabled.v1";

    public const string SubscriptionActivatedV1 = "platform.subscription.activated.v1";
    public const string SubscriptionExpiredV1 = "platform.subscription.expired.v1";

    public const string WorkflowApprovedV1 = "platform.workflow.approved.v1";
    public const string WorkflowRejectedV1 = "platform.workflow.rejected.v1";
}
