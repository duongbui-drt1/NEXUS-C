using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Audit.Domain;
using Nexus.Modules.Audit.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Audit.Application;

public sealed record AuditEventDto(
    Guid Id,
    Guid? ActorUserId,
    string? ActorEmail,
    Guid? TenantId,
    string Action,
    string ResourceType,
    string? ResourceId,
    DateTimeOffset OccurredAt,
    string? IpAddress,
    string CorrelationId,
    string Result,
    string? Reason,
    string? BeforeMetadataJson,
    string? AfterMetadataJson,
    bool IsCrossTenantPlatformAccess);

public interface IAuditQueryService
{
    Task<Result<PagedResult<AuditEventDto>>> QueryAsync(
        PagedRequest paging,
        string? action = null,
        string? resourceType = null,
        Guid? filterTenantId = null,
        CancellationToken ct = default);
}

public sealed class AuditService(
    AuditDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ICorrelationContext correlationContext,
    ISystemClock clock) : IAuditWriter, IAuditQueryService
{
    public async Task WriteAsync(AuditEntryRequest entry, CancellationToken ct = default)
    {
        var beforeJson = SensitiveDataRedactor.SanitizeToJson(entry.BeforeState);
        var afterJson = SensitiveDataRedactor.SanitizeToJson(entry.AfterState);

        var ev = AuditEvent.Create(
            actorUserId: currentUser.UserId,
            actorEmail: currentUser.Email,
            tenantId: entry.TenantId ?? tenantContext.TenantId,
            action: entry.Action,
            resourceType: entry.ResourceType,
            resourceId: entry.ResourceId,
            occurredAt: clock.UtcNow,
            ipAddress: currentUser.IpAddress,
            userAgent: currentUser.UserAgent,
            correlationId: correlationContext.CorrelationId,
            result: entry.Result,
            reason: entry.Reason,
            beforeJson: beforeJson,
            afterJson: afterJson,
            isCrossTenantPlatformAccess: entry.IsCrossTenantPlatformAccess);

        db.AuditEvents.Add(ev);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Result<PagedResult<AuditEventDto>>> QueryAsync(
        PagedRequest paging,
        string? action = null,
        string? resourceType = null,
        Guid? filterTenantId = null,
        CancellationToken ct = default)
    {
        var query = db.AuditEvents.AsNoTracking();

        if (!currentUser.IsPlatformUser)
        {
            if (!tenantContext.HasTenant)
            {
                return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required to query tenant audit logs.");
            }

            var tid = tenantContext.TenantId!.Value;
            query = query.Where(x => x.TenantId == tid);
        }
        else if (filterTenantId.HasValue)
        {
            query = query.Where(x => x.TenantId == filterTenantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(x => x.Action == action.Trim());
        }

        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            query = query.Where(x => x.ResourceType == resourceType.Trim());
        }

        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.OccurredAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(x => new AuditEventDto(
                x.Id, x.ActorUserId, x.ActorEmail, x.TenantId, x.Action,
                x.ResourceType, x.ResourceId, x.OccurredAt, x.IpAddress,
                x.CorrelationId, x.Result, x.Reason, x.BeforeMetadataJson,
                x.AfterMetadataJson, x.IsCrossTenantPlatformAccess))
            .ToListAsync(ct);

        return new PagedResult<AuditEventDto>(items, paging.Page, paging.PageSize, total);
    }
}
