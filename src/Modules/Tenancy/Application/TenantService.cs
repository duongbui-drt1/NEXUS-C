using Microsoft.EntityFrameworkCore;
using Nexus.Modules.Tenancy.Domain;
using Nexus.Modules.Tenancy.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Events;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;
using Nexus.SharedKernel.Security;

namespace Nexus.Modules.Tenancy.Application;

public sealed record TenantDto(
    Guid Id,
    string Code,
    string Name,
    string Slug,
    string Status,
    string TimeZone,
    string DefaultCurrency,
    string CountryCode,
    string? SuspendedReason,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint RowVersion);

public sealed record CreateTenantRequest(
    string Code,
    string Name,
    string Slug,
    string TimeZone = "UTC",
    string DefaultCurrency = "USD",
    string CountryCode = "US");

public sealed record UpdateTenantRequest(
    string Name,
    string TimeZone,
    string DefaultCurrency,
    string CountryCode,
    uint? ExpectedRowVersion = null);

public sealed record SuspendTenantRequest(string Reason);

public sealed record MembershipDto(
    Guid Id,
    Guid UserId,
    Guid TenantId,
    string Status,
    DateTimeOffset JoinedAt,
    DateTimeOffset? RevokedAt);

public interface ITenantService
{
    Task<Result<TenantDto>> CreateAsync(CreateTenantRequest request, CancellationToken ct = default);
    Task<Result<TenantDto>> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<Result<PagedResult<TenantDto>>> ListAsync(PagedRequest paging, string? status = null, CancellationToken ct = default);
    Task<Result<TenantDto>> UpdateAsync(Guid tenantId, UpdateTenantRequest request, CancellationToken ct = default);
    Task<Result<TenantDto>> SuspendAsync(Guid tenantId, SuspendTenantRequest request, CancellationToken ct = default);
    Task<Result<TenantDto>> ActivateAsync(Guid tenantId, CancellationToken ct = default);
    Task<Result<MembershipDto>> AddMembershipAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task<Result> RevokeMembershipAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TenantDto>>> ListAccessibleTenantsAsync(Guid userId, CancellationToken ct = default);
}

public sealed class TenantService(
    TenancyDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IPermissionEvaluator permissionEvaluator,
    IUserDirectoryReader userDirectory,
    ICorrelationContext correlationContext,
    ISystemClock clock,
    IAuditWriter auditWriter) : ITenantService
{
    public async Task<Result<TenantDto>> CreateAsync(CreateTenantRequest request, CancellationToken ct = default)
    {
        var created = Tenant.Create(
            request.Code,
            request.Name,
            request.Slug,
            request.TimeZone,
            request.DefaultCurrency,
            request.CountryCode);

        if (created.IsFailure)
        {
            return Result.Failure<TenantDto>(created.Error);
        }

        var tenant = created.Value;
        var exists = await db.Tenants.AsNoTracking()
            .AnyAsync(t => t.Code == tenant.Code || t.Slug == tenant.Slug, ct);
        if (exists)
        {
            return Error.Conflict("TENANT_DUPLICATE", "A tenant with the same code or slug already exists.");
        }

        db.Tenants.Add(tenant);
        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.TenantCreatedV1,
            tenant.Id,
            correlationContext.CorrelationId,
            new { tenant.Id, tenant.Code, tenant.Name, tenant.Slug, tenant.DefaultCurrency, tenant.CountryCode },
            occurredAt: clock.UtcNow));

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            Action: "platform.tenant.create",
            ResourceType: "Tenant",
            ResourceId: tenant.Id.ToString("D"),
            TenantId: tenant.Id,
            Result: "Success",
            AfterState: Map(tenant)), ct);

        return Map(tenant);
    }

    public async Task<Result<TenantDto>> GetByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return Error.NotFound("TENANT_NOT_FOUND", $"Tenant '{tenantId}' was not found.");
        }

        return Map(tenant);
    }

    public async Task<Result<PagedResult<TenantDto>>> ListAsync(PagedRequest paging, string? status = null, CancellationToken ct = default)
    {
        var query = db.Tenants.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TenantLifecycleStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(t => t.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var term = paging.Search.Trim().ToLowerInvariant();
            query = query.Where(t =>
                t.Name.ToLower().Contains(term) ||
                t.Code.ToLower().Contains(term) ||
                t.Slug.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(ct);

        query = (paging.SortBy?.ToLowerInvariant(), paging.SortDescending) switch
        {
            ("name", true) => query.OrderByDescending(t => t.Name),
            ("name", false) => query.OrderBy(t => t.Name),
            ("code", true) => query.OrderByDescending(t => t.Code),
            ("code", false) => query.OrderBy(t => t.Code),
            (_, false) => query.OrderBy(t => t.CreatedAt),
            _ => query.OrderByDescending(t => t.CreatedAt)
        };

        var items = await query.Skip(paging.Skip).Take(paging.PageSize).Select(t => Map(t)).ToListAsync(ct);
        return new PagedResult<TenantDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<Result<TenantDto>> UpdateAsync(Guid tenantId, UpdateTenantRequest request, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return Error.NotFound("TENANT_NOT_FOUND", $"Tenant '{tenantId}' was not found.");
        }

        if (request.ExpectedRowVersion.HasValue)
        {
            db.Entry(tenant).Property(x => x.RowVersion).OriginalValue = request.ExpectedRowVersion.Value;
        }

        var before = Map(tenant);
        var updated = tenant.Update(request.Name, request.TimeZone, request.DefaultCurrency, request.CountryCode);
        if (updated.IsFailure)
        {
            return Result.Failure<TenantDto>(updated.Error);
        }

        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.TenantUpdatedV1,
            tenant.Id,
            correlationContext.CorrelationId,
            new { tenant.Id, tenant.Name, tenant.TimeZone, tenant.DefaultCurrency, tenant.CountryCode },
            occurredAt: clock.UtcNow));

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            Action: "platform.tenant.update",
            ResourceType: "Tenant",
            ResourceId: tenant.Id.ToString("D"),
            TenantId: tenant.Id,
            Result: "Success",
            BeforeState: before,
            AfterState: Map(tenant)), ct);

        return Map(tenant);
    }

    public async Task<Result<TenantDto>> SuspendAsync(Guid tenantId, SuspendTenantRequest request, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return Error.NotFound("TENANT_NOT_FOUND", $"Tenant '{tenantId}' was not found.");
        }

        var before = Map(tenant);
        var res = tenant.Suspend(request.Reason, clock.UtcNow);
        if (res.IsFailure)
        {
            return Result.Failure<TenantDto>(res.Error);
        }

        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.TenantSuspendedV1,
            tenant.Id,
            correlationContext.CorrelationId,
            new { tenant.Id, tenant.Code, request.Reason, tenant.SuspendedAt },
            occurredAt: clock.UtcNow));

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            Action: "platform.tenant.suspend",
            ResourceType: "Tenant",
            ResourceId: tenant.Id.ToString("D"),
            TenantId: tenant.Id,
            Result: "Success",
            Reason: request.Reason,
            BeforeState: before,
            AfterState: Map(tenant),
            IsCrossTenantPlatformAccess: true), ct);

        return Map(tenant);
    }

    public async Task<Result<TenantDto>> ActivateAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return Error.NotFound("TENANT_NOT_FOUND", $"Tenant '{tenantId}' was not found.");
        }

        var before = Map(tenant);
        var res = tenant.Activate();
        if (res.IsFailure)
        {
            return Result.Failure<TenantDto>(res.Error);
        }

        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.TenantActivatedV1,
            tenant.Id,
            correlationContext.CorrelationId,
            new { tenant.Id, tenant.Code },
            occurredAt: clock.UtcNow));

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            Action: "platform.tenant.activate",
            ResourceType: "Tenant",
            ResourceId: tenant.Id.ToString("D"),
            TenantId: tenant.Id,
            Result: "Success",
            BeforeState: before,
            AfterState: Map(tenant),
            IsCrossTenantPlatformAccess: true), ct);

        return Map(tenant);
    }

    private async Task<Result> AuthorizeMembershipManagementAsync(Guid targetTenantId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
        {
            return Result.Failure(Error.Unauthorized("AUTH_REQUIRED", "Authentication is required to manage tenant memberships."));
        }

        if (currentUser.IsPlatformUser)
        {
            var canManageCrossTenant = await permissionEvaluator.HasPermissionAsync(
                currentUser.UserId.Value,
                null,
                Permissions.MembershipsManageCrossTenant,
                isPlatformScope: true,
                ct);

            if (!canManageCrossTenant)
            {
                return Result.Failure(Error.Forbidden(
                    "MEMBERSHIP_CROSS_TENANT_DENIED",
                    "Cross-tenant membership management requires the platform.memberships.manage permission."));
            }

            return Result.Success();
        }

        // Tenant-scoped actor: MUST operate strictly inside their own authenticated tenant context!
        if (!tenantContext.HasTenant || tenantContext.TenantId!.Value != targetTenantId)
        {
            return Result.Failure(Error.Forbidden(
                "TENANT_MEMBERSHIP_SCOPE_VIOLATION",
                "Tenant administrators can only manage memberships within their own authenticated tenant."));
        }

        var canManageLocalUsers = await permissionEvaluator.HasPermissionAsync(
            currentUser.UserId.Value,
            targetTenantId,
            Permissions.UsersManage,
            isPlatformScope: false,
            ct);

        if (!canManageLocalUsers)
        {
            return Result.Failure(Error.Forbidden(
                "MEMBERSHIP_PERMISSION_DENIED",
                "Caller lacks platform.users.manage permission in the target tenant."));
        }

        return Result.Success();
    }

    public async Task<Result<MembershipDto>> AddMembershipAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var authz = await AuthorizeMembershipManagementAsync(tenantId, ct);
        if (authz.IsFailure)
        {
            return Result.Failure<MembershipDto>(authz.Error);
        }

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return Error.NotFound("TENANT_NOT_FOUND", $"Tenant '{tenantId}' was not found.");
        }

        if (tenant.Status == TenantLifecycleStatus.Archived)
        {
            return Error.BusinessRule("TENANT_ARCHIVED", "Cannot grant membership in an archived tenant.");
        }

        var targetUser = await userDirectory.GetByIdAsync(userId, ct);
        if (targetUser is null)
        {
            return Error.NotFound("USER_NOT_FOUND", $"User '{userId}' was not found.");
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var existing = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);
        if (existing is not null)
        {
            if (existing.Status == MembershipStatus.Active)
            {
                return Error.Conflict("MEMBERSHIP_EXISTS", "User is already an active member of this tenant.");
            }

            existing.Activate(clock.UtcNow);
            await db.SaveChangesAsync(ct);

            await auditWriter.WriteAsync(new AuditEntryRequest(
                Action: "platform.membership.grant",
                ResourceType: "UserTenantMembership",
                ResourceId: existing.Id.ToString("D"),
                TenantId: tenantId,
                Result: "Success",
                AfterState: new { existing.UserId, existing.TenantId, Status = existing.Status.ToString() },
                IsCrossTenantPlatformAccess: currentUser.IsPlatformUser), ct);

            return Map(existing);
        }

        var membership = UserTenantMembership.Create(userId, tenantId, clock.UtcNow);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            Action: "platform.membership.grant",
            ResourceType: "UserTenantMembership",
            ResourceId: membership.Id.ToString("D"),
            TenantId: tenantId,
            Result: "Success",
            AfterState: new { membership.UserId, membership.TenantId, Status = membership.Status.ToString() },
            IsCrossTenantPlatformAccess: currentUser.IsPlatformUser), ct);

        return Map(membership);
    }

    public async Task<Result> RevokeMembershipAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var authz = await AuthorizeMembershipManagementAsync(tenantId, ct);
        if (authz.IsFailure)
        {
            return authz;
        }

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return Result.Failure(Error.NotFound("TENANT_NOT_FOUND", $"Tenant '{tenantId}' was not found."));
        }

        if (tenant.Status == TenantLifecycleStatus.Archived)
        {
            return Result.Failure(Error.BusinessRule("TENANT_ARCHIVED", "Cannot modify memberships of an archived tenant."));
        }

        var targetUser = await userDirectory.GetByIdAsync(userId, ct);
        if (targetUser is null)
        {
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", $"User '{userId}' was not found."));
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var existing = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);
        if (existing is null)
        {
            return Result.Failure(Error.NotFound("MEMBERSHIP_NOT_FOUND", "Tenant membership was not found."));
        }

        existing.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            Action: "platform.membership.revoke",
            ResourceType: "UserTenantMembership",
            ResourceId: existing.Id.ToString("D"),
            TenantId: tenantId,
            Result: "Success",
            AfterState: new { existing.UserId, existing.TenantId, Status = existing.Status.ToString() },
            IsCrossTenantPlatformAccess: currentUser.IsPlatformUser), ct);

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<TenantDto>>> ListAccessibleTenantsAsync(Guid userId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(null, platformBypass: true, bypassReason: "list-accessible-tenants");

        if (currentUser.IsPlatformUser)
        {
            var allTenants = await db.Tenants.AsNoTracking()
                .Where(t => t.Status != TenantLifecycleStatus.Archived)
                .OrderBy(t => t.Code)
                .Select(t => Map(t))
                .ToListAsync(ct);
            return allTenants;
        }

        var tenantIds = await db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Select(m => m.TenantId)
            .ToListAsync(ct);

        var tenants = await db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .OrderBy(t => t.Name)
            .Select(t => Map(t))
            .ToListAsync(ct);

        return tenants;
    }

    private static TenantDto Map(Tenant t) => new(
        t.Id, t.Code, t.Name, t.Slug, t.Status.ToString(),
        t.TimeZone, t.DefaultCurrency, t.CountryCode,
        t.SuspendedReason, t.SuspendedAt, t.CreatedAt, t.UpdatedAt, t.RowVersion);

    private static MembershipDto Map(UserTenantMembership m) => new(
        m.Id, m.UserId, m.TenantId, m.Status.ToString(), m.JoinedAt, m.RevokedAt);
}

public sealed class TenantAccessValidator(
    TenancyDbContext db,
    ITenantContext tenantContext,
    ISystemClock clock) : ITenantAccessValidator
{
    public async Task<TenantAccessInfo?> ValidateMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId, platformBypass: false);
        var membership = await db.Memberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant is null)
        {
            return null;
        }

        return new TenantAccessInfo(
            tenant.Id,
            tenant.Code,
            tenant.Status,
            membership is { Status: MembershipStatus.Active });
    }

    public async Task<IReadOnlyList<Guid>> GetAccessibleTenantIdsAsync(Guid userId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(null, platformBypass: true, bypassReason: "auth-tenant-lookup");
        return await db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Select(m => m.TenantId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetMemberUserIdsAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId, platformBypass: false);
        return await db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Status == MembershipStatus.Active)
            .Select(m => m.UserId)
            .ToListAsync(ct);
    }

    public async Task<bool> EnsureMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId, platformBypass: false);
        var existing = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);
        if (existing is not null)
        {
            if (existing.Status != MembershipStatus.Active)
            {
                existing.Activate(clock.UtcNow);
                await db.SaveChangesAsync(ct);
            }
            return true;
        }

        db.Memberships.Add(UserTenantMembership.Create(userId, tenantId, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RevokeMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId, platformBypass: false);
        var existing = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);
        if (existing is null)
        {
            return false;
        }

        existing.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
