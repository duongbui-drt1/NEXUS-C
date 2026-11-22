using Microsoft.EntityFrameworkCore;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Results;

namespace Nexus.BuildingBlocks.Messaging;

public sealed record ApiRequestLogDto(
    Guid Id,
    DateTimeOffset Timestamp,
    string Method,
    string NormalizedRoute,
    int StatusCode,
    double DurationMs,
    string CorrelationId,
    string? ApplicationCode,
    string? ServiceClientId,
    Guid? TenantId,
    Guid? UserId,
    string ActorType,
    string RateLimitDecision,
    string? RateLimitPolicy,
    string? ErrorCode);

public sealed record ApiRequestLogQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Route = null,
    int? StatusCode = null,
    string? ApplicationCode = null,
    Guid? TenantId = null,
    string? CorrelationId = null,
    string? RateLimitDecision = null,
    int Page = 1,
    int PageSize = 50);

public sealed record ApiRequestLogPage(
    IReadOnlyList<ApiRequestLogDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public interface IApiRequestLogService
{
    Task WriteAsync(ApiRequestLog entry, CancellationToken ct = default);
    Task<ApiRequestLogPage> QueryAsync(ApiRequestLogQuery query, CancellationToken ct = default);
    Task<Result<int>> PurgeExpiredAsync(int retentionDays, CancellationToken ct = default);
}

public sealed class ApiRequestLogService(
    MessagingDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock) : IApiRequestLogService
{
    public async Task WriteAsync(ApiRequestLog entry, CancellationToken ct = default)
    {
        using var bypass = tenantContext.BeginScope(entry.TenantId, platformBypass: true, bypassReason: "api-request-log-write");
        db.ApiRequestLogs.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ApiRequestLogPage> QueryAsync(ApiRequestLogQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        IQueryable<ApiRequestLog> q = db.ApiRequestLogs.AsNoTracking();

        // Enforce strict tenant isolation for non-platform callers (defense-in-depth alongside PostgreSQL hybrid RLS)
        if (!currentUser.IsPlatformUser && tenantContext.TenantId is { } tid)
        {
            q = q.Where(x => x.TenantId == tid);
        }
        else if (query.TenantId.HasValue)
        {
            q = q.Where(x => x.TenantId == query.TenantId.Value);
        }

        if (query.From.HasValue)
        {
            q = q.Where(x => x.Timestamp >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            q = q.Where(x => x.Timestamp <= query.To.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Route))
        {
            var r = query.Route.Trim();
            q = q.Where(x => x.NormalizedRoute.Contains(r));
        }

        if (query.StatusCode.HasValue)
        {
            q = q.Where(x => x.StatusCode == query.StatusCode.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.ApplicationCode))
        {
            var app = query.ApplicationCode.Trim().ToUpperInvariant();
            q = q.Where(x => x.ApplicationCode == app);
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            var corr = query.CorrelationId.Trim();
            q = q.Where(x => x.CorrelationId == corr);
        }

        if (!string.IsNullOrWhiteSpace(query.RateLimitDecision))
        {
            var dec = query.RateLimitDecision.Trim();
            q = q.Where(x => x.RateLimitDecision == dec);
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(x => x.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ApiRequestLogDto(
                x.Id,
                x.Timestamp,
                x.Method,
                x.NormalizedRoute,
                x.StatusCode,
                x.DurationMs,
                x.CorrelationId,
                x.ApplicationCode,
                x.ServiceClientId,
                x.TenantId,
                x.UserId,
                x.ActorType,
                x.RateLimitDecision,
                x.RateLimitPolicy,
                x.ErrorCode))
            .ToListAsync(ct);

        return new ApiRequestLogPage(items, total, page, pageSize);
    }

    public async Task<Result<int>> PurgeExpiredAsync(int retentionDays, CancellationToken ct = default)
    {
        if (retentionDays < 1 || retentionDays > 3650)
        {
            return Error.Validation("API_LOGS_INVALID_RETENTION", "RetentionDays must be between 1 and 3650.");
        }

        var cutoff = clock.UtcNow.AddDays(-retentionDays);
        using var bypass = tenantContext.BeginScope(null, platformBypass: true, bypassReason: "api-request-log-purge");
        var deleted = await db.ApiRequestLogs
            .Where(x => x.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct);

        return deleted;
    }
}
