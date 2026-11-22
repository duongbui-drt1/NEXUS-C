using Nexus.BuildingBlocks.Infrastructure;
using Nexus.BuildingBlocks.Messaging;
using Nexus.Modules.FileStorage.Domain;
using Nexus.Modules.Notification.Domain;
using Nexus.Modules.Workflow.Domain;
using Nexus.SharedKernel.Events;
using Xunit;

namespace Nexus.UnitTests;

public sealed class Foundation10UnitTests
{
    [Fact]
    public void WebhookSignatureHelper_VerifiesValidSignature_AndRejectsTamperedOrReplayedTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        const string payload = "{\"event\":\"platform.tenant.created.v1\",\"id\":\"123\"}";
        var secret = string.Concat("whsec", "_unit_test_dummy_signing_key_12345");

        var sig = WebhookSignatureHelper.ComputeSignature(payload, ts, secret);
        Assert.StartsWith("v1=", sig);
        Assert.True(WebhookSignatureHelper.VerifySignature(payload, ts, sig, secret, now));

        // Tampered payload fails verification
        Assert.False(WebhookSignatureHelper.VerifySignature(payload + " ", ts, sig, secret, now));

        // Replayed stale timestamp (> 300s old) fails verification
        var staleTs = now.AddMinutes(-10).ToUnixTimeSeconds();
        var staleSig = WebhookSignatureHelper.ComputeSignature(payload, staleTs, secret);
        Assert.False(WebhookSignatureHelper.VerifySignature(payload, staleTs, staleSig, secret, now, maxToleranceSeconds: 300));
    }

    [Fact]
    public void EventEnvelope_AndDeadLetterMessage_PreserveProducerCausationAndEnforceManualRetryLimit()
    {
        var tenantId = Guid.NewGuid();
        var env = EventEnvelope.Create(
            PlatformEventTypes.WorkflowEscalatedV1,
            tenantId,
            "corr-100",
            new { InstanceId = Guid.NewGuid() },
            schemaVersion: 1,
            producer: "nexus-c",
            causationId: "cmd-99");

        Assert.Equal("nexus-c", env.Producer);
        Assert.Equal("cmd-99", env.CausationId);

        var outbox = OutboxMessage.FromEnvelope(env);
        var roundTrip = outbox.ToEnvelope();
        Assert.Equal("nexus-c", roundTrip.Producer);
        Assert.Equal("cmd-99", roundTrip.CausationId);

        var dlq = DeadLetterMessage.FromOutbox(outbox, "Broker timeout", DateTimeOffset.UtcNow);
        Assert.True(dlq.CanManualRetry());

        dlq.RecordManualRetry(Guid.NewGuid(), "Manual retry 1", DateTimeOffset.UtcNow);
        Assert.True(dlq.IsResolved);
        Assert.Equal(1, dlq.ManualRetryCount);
        Assert.False(dlq.CanManualRetry());
        Assert.NotNull(dlq.RetryHistoryJson);
    }

    [Fact]
    public void ApprovalDelegation_RejectsSelfDelegationAndInvalidWindows()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var self = ApprovalDelegation.Create(tenantId, userId, userId, null, now, now.AddDays(1), "Self");
        Assert.True(self.IsFailure);
        Assert.Equal("WORKFLOW_SELF_DELEGATION", self.Error.Code);

        var badWindow = ApprovalDelegation.Create(tenantId, userId, Guid.NewGuid(), null, now.AddDays(2), now.AddDays(1), "Backwards");
        Assert.True(badWindow.IsFailure);
        Assert.Equal("WORKFLOW_INVALID_DELEGATION_WINDOW", badWindow.Error.Code);

        var valid = ApprovalDelegation.Create(tenantId, userId, Guid.NewGuid(), null, now.AddMinutes(-5), now.AddMinutes(5), "Vacation").Value;
        Assert.True(valid.IsEffectiveAt(now));
        Assert.False(valid.IsEffectiveAt(now.AddMinutes(10)));
    }

    [Fact]
    public void StoredFileObject_EnforcesRetentionValidationAndExpiration()
    {
        var now = DateTimeOffset.UtcNow;
        var invalidRetention = StoredFileObject.Create(Guid.NewGuid(), "doc.pdf", "application/pdf", 1024, 10_000_000, "abc", "General", retentionDays: 0, utcNow: now);
        Assert.True(invalidRetention.IsFailure);
        Assert.Equal("FILE_INVALID_RETENTION", invalidRetention.Error.Code);

        var valid = StoredFileObject.Create(Guid.NewGuid(), "doc.pdf", "application/pdf", 1024, 10_000_000, "abc", "Finance", retentionDays: 30, utcNow: now).Value;
        Assert.False(valid.IsRetentionExpired(now.AddDays(29)));
        Assert.True(valid.IsRetentionExpired(now.AddDays(31)));
    }

    [Fact]
    public void NotificationMessage_WebhookChannel_EnforcesSsrfGuard()
    {
        var bad = NotificationMessage.Create(Guid.NewGuid(), null, null, NotificationChannel.Webhook, "Alert", "Body", "https://169.254.169.254/latest/meta-data");
        Assert.True(bad.IsFailure);
        Assert.Equal("NOTIFICATION_UNSAFE_WEBHOOK", bad.Error.Code);

        var ok = NotificationMessage.Create(Guid.NewGuid(), null, null, NotificationChannel.Webhook, "Alert", "Body", "https://hooks.partner.example.com/notify");
        Assert.True(ok.IsSuccess);
    }
}
