using System.Text.Json;

namespace Nexus.SharedKernel.Events;

public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    Guid? TenantId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    int SchemaVersion,
    JsonElement Payload,
    string Producer = "nexus-c",
    string? CausationId = null)
{
    public static EventEnvelope Create<TPayload>(
        string eventType,
        Guid? tenantId,
        string correlationId,
        TPayload payload,
        int schemaVersion = 1,
        DateTimeOffset? occurredAt = null,
        string producer = "nexus-c",
        string? causationId = null)
    {
        var element = JsonSerializer.SerializeToElement(payload, EventJsonOptions.Default);
        return new EventEnvelope(
            Guid.CreateVersion7(),
            eventType,
            tenantId,
            occurredAt ?? DateTimeOffset.UtcNow,
            correlationId,
            schemaVersion,
            element,
            producer,
            causationId);
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
    public const string SubscriptionCancelledV1 = "platform.subscription.cancelled.v1";

    public const string ApplicationActivatedV1 = "platform.application.activated.v1";
    public const string ApplicationDeactivatedV1 = "platform.application.deactivated.v1";

    public const string WorkflowSubmittedV1 = "platform.workflow.submitted.v1";
    public const string WorkflowApprovedV1 = "platform.workflow.approved.v1";
    public const string WorkflowRejectedV1 = "platform.workflow.rejected.v1";
    public const string WorkflowCancelledV1 = "platform.workflow.cancelled.v1";
    public const string WorkflowDelegatedV1 = "platform.workflow.delegated.v1";
    public const string WorkflowEscalatedV1 = "platform.workflow.escalated.v1";

    public const string ServiceClientCreatedV1 = "platform.service_client.created.v1";
    public const string ServiceClientRevokedV1 = "platform.service_client.revoked.v1";
}
