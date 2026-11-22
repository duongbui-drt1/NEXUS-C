using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Events;
using Nexus.SharedKernel.Primitives;

namespace Nexus.BuildingBlocks.Messaging;

public enum OutboxStatus
{
    Pending = 1,
    Processing = 2,
    Published = 3,
    DeadLettered = 4
}

public sealed class OutboxMessage : Entity
{
    private OutboxMessage() { }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid? TenantId { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string Producer { get; private set; } = "nexus-c";
    public string? CausationId { get; private set; }
    public int SchemaVersion { get; private set; }
    public string PayloadJson { get; private set; } = "{}";
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; } = DateTimeOffset.UtcNow;
    public int RetryCount { get; private set; }
    public OutboxStatus Status { get; private set; } = OutboxStatus.Pending;
    public string? LastError { get; private set; }

    public static OutboxMessage FromEnvelope(EventEnvelope envelope)
    {
        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            TenantId = envelope.TenantId,
            CorrelationId = envelope.CorrelationId,
            Producer = string.IsNullOrWhiteSpace(envelope.Producer) ? "nexus-c" : envelope.Producer.Trim(),
            CausationId = string.IsNullOrWhiteSpace(envelope.CausationId) ? null : envelope.CausationId.Trim(),
            SchemaVersion = envelope.SchemaVersion,
            PayloadJson = envelope.Payload.GetRawText(),
            OccurredAt = envelope.OccurredAt,
            CreatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending
        };
    }

    public EventEnvelope ToEnvelope()
    {
        using var doc = JsonDocument.Parse(PayloadJson);
        return new EventEnvelope(
            EventId,
            EventType,
            TenantId,
            OccurredAt,
            CorrelationId,
            SchemaVersion,
            doc.RootElement.Clone(),
            Producer,
            CausationId);
    }

    public void MarkPublished(DateTimeOffset utcNow)
    {
        Status = OutboxStatus.Published;
        ProcessedAt = utcNow;
        LastError = null;
    }

    public void RecordFailure(string error, DateTimeOffset utcNow, int maxRetries)
    {
        RetryCount++;
        LastError = error.Length > 1900 ? error[..1900] : error;

        if (RetryCount >= maxRetries)
        {
            Status = OutboxStatus.DeadLettered;
            ProcessedAt = utcNow;
        }
        else
        {
            Status = OutboxStatus.Pending;
            var backoffSeconds = Math.Min(300, (int)Math.Pow(2, RetryCount) * 5);
            NextAttemptAt = utcNow.AddSeconds(backoffSeconds);
        }
    }

    public void RequeueForRetry(DateTimeOffset utcNow)
    {
        Status = OutboxStatus.Pending;
        RetryCount = 0;
        ProcessedAt = null;
        NextAttemptAt = utcNow;
        LastError = null;
    }
}

public sealed class InboxMessage : Entity
{
    private InboxMessage() { }

    public Guid EventId { get; private set; }
    public string ConsumerName { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public Guid? TenantId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    public static InboxMessage Create(Guid eventId, string consumerName, string eventType, Guid? tenantId, DateTimeOffset utcNow)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EventId = eventId,
            ConsumerName = consumerName,
            EventType = eventType,
            TenantId = tenantId,
            ProcessedAt = utcNow
        };
}

public sealed class DeadLetterMessage : Entity
{
    public const int MaxManualRetries = 3;

    private DeadLetterMessage() { }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid? TenantId { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string Producer { get; private set; } = "nexus-c";
    public string? CausationId { get; private set; }
    public string PayloadJson { get; private set; } = "{}";
    public string Reason { get; private set; } = string.Empty;
    public int RetryCount { get; private set; }
    public int ManualRetryCount { get; private set; }
    public Guid? LastRetriedByUserId { get; private set; }
    public DateTimeOffset? LastRetriedAt { get; private set; }
    public string? RetryHistoryJson { get; private set; }
    public bool IsResolved { get; private set; }
    public DateTimeOffset FailedAt { get; private set; }

    public static DeadLetterMessage FromOutbox(OutboxMessage msg, string reason, DateTimeOffset utcNow)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EventId = msg.EventId,
            EventType = msg.EventType,
            TenantId = msg.TenantId,
            CorrelationId = msg.CorrelationId,
            Producer = msg.Producer,
            CausationId = msg.CausationId,
            PayloadJson = msg.PayloadJson,
            Reason = reason.Length > 1900 ? reason[..1900] : reason,
            RetryCount = msg.RetryCount,
            ManualRetryCount = 0,
            IsResolved = false,
            FailedAt = utcNow
        };

    public bool CanManualRetry() => !IsResolved && ManualRetryCount < MaxManualRetries;

    public void RecordManualRetry(Guid actorUserId, string? note, DateTimeOffset utcNow)
    {
        ManualRetryCount++;
        LastRetriedByUserId = actorUserId;
        LastRetriedAt = utcNow;
        IsResolved = true;

        var history = string.IsNullOrWhiteSpace(RetryHistoryJson)
            ? new List<object>()
            : JsonSerializer.Deserialize<List<object>>(RetryHistoryJson) ?? [];
        history.Add(new
        {
            Attempt = ManualRetryCount,
            ActorUserId = actorUserId,
            RetriedAt = utcNow,
            Note = note
        });
        RetryHistoryJson = JsonSerializer.Serialize(history, EventJsonOptions.Default);
    }
}

public sealed class IdempotencyRecord : Entity
{
    private IdempotencyRecord() { }

    public string Key { get; private set; } = string.Empty;
    public Guid? TenantId { get; private set; }
    public string Route { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public int StatusCode { get; private set; }
    public string ResponseBody { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = "application/json";
    public bool IsCompleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static IdempotencyRecord Start(string key, Guid? tenantId, string route, string requestHash, DateTimeOffset utcNow)
        => new()
        {
            Id = Guid.CreateVersion7(),
            Key = key,
            TenantId = tenantId,
            Route = route,
            RequestHash = requestHash,
            IsCompleted = false,
            CreatedAt = utcNow,
            ExpiresAt = utcNow.AddHours(24)
        };

    public void Complete(int statusCode, string responseBody, string contentType)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        ContentType = contentType;
        IsCompleted = true;
    }
}

/// <summary>
/// Bounded operational API request telemetry record.
/// STRICT SECURITY INVARIANT: Never persists Authorization headers, cookies, API keys, passwords,
/// raw query strings, or request/response bodies.
/// </summary>
public sealed class ApiRequestLog : Entity
{
    private ApiRequestLog() { }

    public DateTimeOffset Timestamp { get; private set; }
    public string Method { get; private set; } = string.Empty;
    public string NormalizedRoute { get; private set; } = string.Empty;
    public int StatusCode { get; private set; }
    public double DurationMs { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? ApplicationCode { get; private set; }
    public string? ServiceClientId { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string ActorType { get; private set; } = "Anonymous";
    public string RateLimitDecision { get; private set; } = "Allowed";
    public string? RateLimitPolicy { get; private set; }
    public string? ErrorCode { get; private set; }

    public static ApiRequestLog Record(
        DateTimeOffset timestamp,
        string method,
        string normalizedRoute,
        int statusCode,
        double durationMs,
        string correlationId,
        string? applicationCode,
        string? serviceClientId,
        Guid? tenantId,
        Guid? userId,
        string actorType,
        string rateLimitDecision,
        string? rateLimitPolicy,
        string? errorCode)
    {
        static string? Truncate(string? value, int max)
            => string.IsNullOrWhiteSpace(value) ? null : (value.Trim().Length > max ? value.Trim()[..max] : value.Trim());

        return new ApiRequestLog
        {
            Id = Guid.CreateVersion7(),
            Timestamp = timestamp,
            Method = Truncate(method, 16) ?? "GET",
            NormalizedRoute = Truncate(normalizedRoute, 300) ?? "/",
            StatusCode = statusCode,
            DurationMs = Math.Round(Math.Max(0, durationMs), 2),
            CorrelationId = Truncate(correlationId, 100) ?? string.Empty,
            ApplicationCode = Truncate(applicationCode, 40),
            ServiceClientId = Truncate(serviceClientId, 100),
            TenantId = tenantId,
            UserId = userId,
            ActorType = Truncate(actorType, 32) ?? "Anonymous",
            RateLimitDecision = Truncate(rateLimitDecision, 32) ?? "Allowed",
            RateLimitPolicy = Truncate(rateLimitPolicy, 100),
            ErrorCode = Truncate(errorCode, 100)
        };
    }
}

public sealed class MessagingDbContext(
    DbContextOptions<MessagingDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "messaging";
    protected override bool OwnsOutboxTable => true;

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<DeadLetterMessage> DeadLetterMessages => Set<DeadLetterMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<ApiRequestLog> ApiRequestLogs => Set<ApiRequestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("inbox_messages", "messaging");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.ConsumerName).HasMaxLength(150).IsRequired();
            builder.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            builder.HasIndex(x => new { x.EventId, x.ConsumerName }).IsUnique();
        });

        modelBuilder.Entity<DeadLetterMessage>(builder =>
        {
            builder.ToTable("dead_letter_messages", "messaging");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            builder.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            builder.Property(x => x.Producer).HasMaxLength(100).HasDefaultValue("nexus-c").IsRequired();
            builder.Property(x => x.CausationId).HasMaxLength(100);
            builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
            builder.Property(x => x.RetryHistoryJson).HasColumnType("jsonb");
            builder.HasIndex(x => x.FailedAt);
            builder.HasIndex(x => x.IsResolved);
        });

        modelBuilder.Entity<IdempotencyRecord>(builder =>
        {
            builder.ToTable("idempotency_records", "messaging");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Key).HasMaxLength(150).IsRequired();
            builder.Property(x => x.Route).HasMaxLength(300).IsRequired();
            builder.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
            builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            builder.HasIndex(x => new { x.Key, x.TenantId, x.Route }).IsUnique();
            builder.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<ApiRequestLog>(builder =>
        {
            builder.ToTable("api_request_logs", "messaging");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Method).HasMaxLength(16).IsRequired();
            builder.Property(x => x.NormalizedRoute).HasMaxLength(300).IsRequired();
            builder.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            builder.Property(x => x.ApplicationCode).HasMaxLength(40);
            builder.Property(x => x.ServiceClientId).HasMaxLength(100);
            builder.Property(x => x.ActorType).HasMaxLength(32).IsRequired();
            builder.Property(x => x.RateLimitDecision).HasMaxLength(32).IsRequired();
            builder.Property(x => x.RateLimitPolicy).HasMaxLength(100);
            builder.Property(x => x.ErrorCode).HasMaxLength(100);
            builder.HasIndex(x => x.Timestamp);
            builder.HasIndex(x => new { x.TenantId, x.Timestamp });
            builder.HasIndex(x => new { x.StatusCode, x.Timestamp });
            builder.HasIndex(x => x.CorrelationId);
        });
    }
}
