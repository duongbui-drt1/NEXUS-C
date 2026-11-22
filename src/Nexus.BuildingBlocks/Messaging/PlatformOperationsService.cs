using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Observability;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;

namespace Nexus.BuildingBlocks.Messaging;

public sealed record OutboxMessageDto(
    Guid Id,
    Guid EventId,
    string EventType,
    string Producer,
    Guid? TenantId,
    string CorrelationId,
    string? CausationId,
    int SchemaVersion,
    string Status,
    int RetryCount,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset NextAttemptAt,
    string? LastError);

public sealed record InboxMessageDto(
    Guid Id,
    Guid EventId,
    string ConsumerName,
    string EventType,
    Guid? TenantId,
    DateTimeOffset ProcessedAt);

public sealed record DeadLetterMessageDto(
    Guid Id,
    Guid EventId,
    string EventType,
    string Producer,
    Guid? TenantId,
    string CorrelationId,
    string? CausationId,
    string Reason,
    int RetryCount,
    int ManualRetryCount,
    int MaxManualRetries,
    Guid? LastRetriedByUserId,
    DateTimeOffset? LastRetriedAt,
    string? RetryHistoryJson,
    bool IsResolved,
    DateTimeOffset FailedAt);

public interface IPlatformOperationsService
{
    Task<Result<PagedResult<OutboxMessageDto>>> ListOutboxAsync(PagedRequest paging, string? status = null, string? eventType = null, Guid? tenantId = null, CancellationToken ct = default);
    Task<Result<PagedResult<InboxMessageDto>>> ListInboxAsync(PagedRequest paging, string? consumerName = null, string? eventType = null, Guid? tenantId = null, CancellationToken ct = default);
    Task<Result<PagedResult<DeadLetterMessageDto>>> ListDeadLettersAsync(PagedRequest paging, bool? resolved = null, string? eventType = null, Guid? tenantId = null, CancellationToken ct = default);
    Task<Result<DeadLetterMessageDto>> RetryDeadLetterAsync(Guid deadLetterId, string? note = null, CancellationToken ct = default);
    Result<IReadOnlyList<BackgroundJobStatusDto>> ListBackgroundJobs();
}

public sealed class PlatformOperationsService(
    MessagingDbContext db,
    ICurrentUser currentUser,
    ISystemClock clock,
    IAuditWriter auditWriter,
    IBackgroundJobTracker jobTracker) : IPlatformOperationsService
{
    public async Task<Result<PagedResult<OutboxMessageDto>>> ListOutboxAsync(
        PagedRequest paging,
        string? status = null,
        string? eventType = null,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        var query = db.OutboxMessages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OutboxStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var et = eventType.Trim();
            query = query.Where(x => x.EventType.Contains(et));
        }

        if (tenantId.HasValue)
        {
            query = query.Where(x => x.TenantId == tenantId.Value);
        }

        var total = await query.LongCountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(x => new OutboxMessageDto(
            x.Id, x.EventId, x.EventType, x.Producer, x.TenantId, x.CorrelationId, x.CausationId,
            x.SchemaVersion, x.Status.ToString(), x.RetryCount, x.OccurredAt, x.CreatedAt,
            x.ProcessedAt, x.NextAttemptAt, RedactError(x.LastError))).ToList();

        return new PagedResult<OutboxMessageDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<Result<PagedResult<InboxMessageDto>>> ListInboxAsync(
        PagedRequest paging,
        string? consumerName = null,
        string? eventType = null,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        var query = db.InboxMessages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(consumerName))
        {
            var cn = consumerName.Trim();
            query = query.Where(x => x.ConsumerName.Contains(cn));
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var et = eventType.Trim();
            query = query.Where(x => x.EventType.Contains(et));
        }

        if (tenantId.HasValue)
        {
            query = query.Where(x => x.TenantId == tenantId.Value);
        }

        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.ProcessedAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(x => new InboxMessageDto(x.Id, x.EventId, x.ConsumerName, x.EventType, x.TenantId, x.ProcessedAt))
            .ToListAsync(ct);

        return new PagedResult<InboxMessageDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<Result<PagedResult<DeadLetterMessageDto>>> ListDeadLettersAsync(
        PagedRequest paging,
        bool? resolved = null,
        string? eventType = null,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        var query = db.DeadLetterMessages.AsNoTracking();

        if (resolved.HasValue)
        {
            query = query.Where(x => x.IsResolved == resolved.Value);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var et = eventType.Trim();
            query = query.Where(x => x.EventType.Contains(et));
        }

        if (tenantId.HasValue)
        {
            query = query.Where(x => x.TenantId == tenantId.Value);
        }

        var total = await query.LongCountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.FailedAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(MapDlq).ToList();
        return new PagedResult<DeadLetterMessageDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<Result<DeadLetterMessageDto>> RetryDeadLetterAsync(Guid deadLetterId, string? note = null, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue || !currentUser.IsPlatformUser)
        {
            return Error.Forbidden("OPERATIONS_PLATFORM_REQUIRED", "Only authorized platform administrators may retry dead-letter messages.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var dlq = await db.DeadLetterMessages.FirstOrDefaultAsync(x => x.Id == deadLetterId, ct);
        if (dlq is null)
        {
            await tx.RollbackAsync(ct);
            return Error.NotFound("DLQ_NOT_FOUND", $"Dead-letter message '{deadLetterId}' was not found.");
        }

        if (!dlq.CanManualRetry())
        {
            await tx.RollbackAsync(ct);
            return Error.BusinessRule(
                "DLQ_RETRY_LIMIT_EXCEEDED",
                dlq.IsResolved
                    ? "This dead-letter message has already been retried/resolved."
                    : $"Dead-letter message has reached the maximum manual retry limit ({DeadLetterMessage.MaxManualRetries}).");
        }

        var now = clock.UtcNow;
        var outbox = await db.OutboxMessages.FirstOrDefaultAsync(x => x.EventId == dlq.EventId, ct);
        if (outbox is not null)
        {
            outbox.RequeueForRetry(now);
        }
        else
        {
            using var doc = System.Text.Json.JsonDocument.Parse(dlq.PayloadJson);
            var env = new Nexus.SharedKernel.Events.EventEnvelope(
                dlq.EventId, dlq.EventType, dlq.TenantId, dlq.FailedAt,
                dlq.CorrelationId, 1, doc.RootElement.Clone(), dlq.Producer, dlq.CausationId);
            db.OutboxMessages.Add(OutboxMessage.FromEnvelope(env));
        }

        dlq.RecordManualRetry(currentUser.UserId.Value, note, now);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        NexusPlatformMetrics.DeadLettersRetried.Add(1, new KeyValuePair<string, object?>("event_type", dlq.EventType));

        await auditWriter.WriteAsync(new AuditEntryRequest(
            Action: "platform.operations.dlq.retry",
            ResourceType: "DeadLetterMessage",
            ResourceId: dlq.Id.ToString("D"),
            TenantId: dlq.TenantId,
            Result: "Success",
            Reason: note ?? "Manual dead-letter retry triggered by platform operator.",
            AfterState: new { dlq.EventId, dlq.EventType, dlq.ManualRetryCount, dlq.IsResolved },
            IsCrossTenantPlatformAccess: true), ct);

        return MapDlq(dlq);
    }

    public Result<IReadOnlyList<BackgroundJobStatusDto>> ListBackgroundJobs()
        => Result.Success(jobTracker.GetSnapshot());

    private static DeadLetterMessageDto MapDlq(DeadLetterMessage x) => new(
        x.Id, x.EventId, x.EventType, x.Producer, x.TenantId, x.CorrelationId, x.CausationId,
        RedactError(x.Reason) ?? string.Empty, x.RetryCount, x.ManualRetryCount,
        DeadLetterMessage.MaxManualRetries, x.LastRetriedByUserId, x.LastRetriedAt,
        x.RetryHistoryJson, x.IsResolved, x.FailedAt);

    private static string? RedactError(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var clean = System.Text.RegularExpressions.Regex.Replace(
            raw,
            @"(Password|Pwd|Secret|Token|Authorization)\s*[=:]\s*[^\s;,""]+",
            "$1=[REDACTED]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return clean.Length > 500 ? clean[..500] : clean;
    }
}
