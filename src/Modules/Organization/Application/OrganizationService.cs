using Microsoft.EntityFrameworkCore;
using Nexus.Modules.Organization.Domain;
using Nexus.Modules.Organization.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Organization.Application;

public sealed record LegalEntityDto(
    Guid Id,
    Guid TenantId,
    string Code,
    string LegalName,
    string? TaxIdentificationNumber,
    string? Address,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CreateLegalEntityRequest(string Code, string LegalName, string? TaxIdentificationNumber, string? Address);
public sealed record UpdateLegalEntityRequest(string LegalName, string? TaxIdentificationNumber, string? Address, OrganizationStatus Status = OrganizationStatus.Active);

public sealed record BranchDto(
    Guid Id,
    Guid TenantId,
    Guid LegalEntityId,
    string Code,
    string Name,
    string? Address,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CreateBranchRequest(Guid LegalEntityId, string Code, string Name, string? Address);
public sealed record UpdateBranchRequest(string Name, string? Address, OrganizationStatus Status = OrganizationStatus.Active);

public sealed record OrganizationUnitDto(
    Guid Id,
    Guid TenantId,
    Guid? ParentId,
    string Code,
    string Name,
    string UnitType,
    DateTimeOffset CreatedAt,
    string Status = "Active");

public sealed record CreateOrganizationUnitRequest(Guid? ParentId, string Code, string Name, OrganizationUnitType UnitType);
public sealed record UpdateOrganizationUnitRequest(Guid? ParentId, string Name, OrganizationUnitType UnitType, OrganizationStatus Status = OrganizationStatus.Active);

public sealed record UserOrganizationAssignmentDto(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    Guid? BranchId,
    Guid? OrganizationUnitId,
    string? Title,
    bool IsPrimary,
    bool IsActive = true,
    DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveTo = null);

public sealed record AssignUserOrganizationRequest(
    Guid UserId,
    Guid? BranchId,
    Guid? OrganizationUnitId,
    string? Title,
    bool IsPrimary,
    DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveTo = null);

public interface IOrganizationService
{
    Task<Result<LegalEntityDto>> CreateLegalEntityAsync(CreateLegalEntityRequest request, CancellationToken ct = default);
    Task<Result<LegalEntityDto>> GetLegalEntityAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<LegalEntityDto>>> ListLegalEntitiesAsync(PagedRequest paging, CancellationToken ct = default);
    Task<Result<LegalEntityDto>> UpdateLegalEntityAsync(Guid id, UpdateLegalEntityRequest request, CancellationToken ct = default);
    Task<Result> DeleteLegalEntityAsync(Guid id, CancellationToken ct = default);

    Task<Result<BranchDto>> CreateBranchAsync(CreateBranchRequest request, CancellationToken ct = default);
    Task<Result<BranchDto>> GetBranchAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<BranchDto>>> ListBranchesAsync(PagedRequest paging, Guid? legalEntityId = null, CancellationToken ct = default);
    Task<Result<BranchDto>> UpdateBranchAsync(Guid id, UpdateBranchRequest request, CancellationToken ct = default);
    Task<Result> DeleteBranchAsync(Guid id, CancellationToken ct = default);

    Task<Result<OrganizationUnitDto>> CreateUnitAsync(CreateOrganizationUnitRequest request, CancellationToken ct = default);
    Task<Result<OrganizationUnitDto>> UpdateUnitAsync(Guid id, UpdateOrganizationUnitRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<OrganizationUnitDto>>> ListUnitsAsync(CancellationToken ct = default);

    Task<Result<UserOrganizationAssignmentDto>> AssignUserAsync(AssignUserOrganizationRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<UserOrganizationAssignmentDto>>> ListAssignmentsAsync(Guid? userId = null, bool includeInactive = false, CancellationToken ct = default);
    Task<Result> RevokeAssignmentAsync(Guid assignmentId, CancellationToken ct = default);
    Task<Result<EffectiveOrganizationScope>> GetUserEffectiveScopeAsync(Guid userId, CancellationToken ct = default);
}

public sealed class OrganizationService(
    OrganizationDbContext db,
    ITenantContext tenantContext,
    IUserDirectoryReader userDirectory,
    ITenantAccessValidator tenantValidator,
    ISystemClock clock,
    IAuditWriter auditWriter) : IOrganizationService, IOrganizationScopeReader
{
    private Result<Guid> RequireTenant()
    {
        if (!tenantContext.HasTenant)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "An active tenant context is required for organization operations.");
        }

        return tenantContext.TenantId!.Value;
    }

    public async Task<Result<LegalEntityDto>> CreateLegalEntityAsync(CreateLegalEntityRequest request, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<LegalEntityDto>(tenantRes.Error);

        var created = LegalEntity.Create(tenantRes.Value, request.Code, request.LegalName, request.TaxIdentificationNumber, request.Address);
        if (created.IsFailure) return Result.Failure<LegalEntityDto>(created.Error);

        var entity = created.Value;
        if (await db.LegalEntities.AsNoTracking().AnyAsync(x => x.Code == entity.Code, ct))
        {
            return Error.Conflict("ORG_LEGAL_ENTITY_DUPLICATE", $"Legal entity code '{entity.Code}' already exists in this tenant.");
        }

        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "organization.legal_entity.create", "LegalEntity", entity.Id.ToString("D"), tenantRes.Value, "Success", AfterState: Map(entity)), ct);

        return Map(entity);
    }

    public async Task<Result<LegalEntityDto>> GetLegalEntityAsync(Guid id, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<LegalEntityDto>(tenantRes.Error);

        var entity = await db.LegalEntities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null
            ? Error.NotFound("ORG_LEGAL_ENTITY_NOT_FOUND", $"Legal entity '{id}' was not found.")
            : Map(entity);
    }

    public async Task<Result<PagedResult<LegalEntityDto>>> ListLegalEntitiesAsync(PagedRequest paging, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<PagedResult<LegalEntityDto>>(tenantRes.Error);

        var query = db.LegalEntities.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var term = paging.Search.Trim().ToLowerInvariant();
            query = query.Where(x => x.LegalName.ToLower().Contains(term) || x.Code.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderBy(x => x.LegalName).Skip(paging.Skip).Take(paging.PageSize).Select(x => Map(x)).ToListAsync(ct);
        return new PagedResult<LegalEntityDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<Result<LegalEntityDto>> UpdateLegalEntityAsync(Guid id, UpdateLegalEntityRequest request, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<LegalEntityDto>(tenantRes.Error);

        var entity = await db.LegalEntities.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
        {
            return Error.NotFound("ORG_LEGAL_ENTITY_NOT_FOUND", $"Legal entity '{id}' was not found.");
        }

        var before = Map(entity);
        var res = entity.Update(request.LegalName, request.TaxIdentificationNumber, request.Address, request.Status);
        if (res.IsFailure) return Result.Failure<LegalEntityDto>(res.Error);

        await db.SaveChangesAsync(ct);
        await auditWriter.WriteAsync(new AuditEntryRequest(
            "organization.legal_entity.update", "LegalEntity", entity.Id.ToString("D"), tenantRes.Value, "Success", BeforeState: before, AfterState: Map(entity)), ct);

        return Map(entity);
    }

    public async Task<Result> DeleteLegalEntityAsync(Guid id, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure(tenantRes.Error);

        var entity = await db.LegalEntities.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
        {
            return Result.Failure(Error.NotFound("ORG_LEGAL_ENTITY_NOT_FOUND", $"Legal entity '{id}' was not found."));
        }

        var hasBranches = await db.Branches.AsNoTracking().AnyAsync(b => b.LegalEntityId == id, ct);
        if (hasBranches)
        {
            return Result.Failure(Error.Conflict("ORG_LEGAL_ENTITY_IN_USE", "Cannot delete a legal entity that still has branches."));
        }

        db.LegalEntities.Remove(entity);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "organization.legal_entity.delete", "LegalEntity", id.ToString("D"), tenantRes.Value, "Success"), ct);

        return Result.Success();
    }

    public async Task<Result<BranchDto>> CreateBranchAsync(CreateBranchRequest request, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<BranchDto>(tenantRes.Error);

        var leExists = await db.LegalEntities.AsNoTracking().AnyAsync(x => x.Id == request.LegalEntityId && x.Status == OrganizationStatus.Active, ct);
        if (!leExists)
        {
            return Error.NotFound("ORG_LEGAL_ENTITY_NOT_FOUND", "Specified active legal entity does not exist in the current tenant.");
        }

        var created = Branch.Create(tenantRes.Value, request.LegalEntityId, request.Code, request.Name, request.Address);
        if (created.IsFailure) return Result.Failure<BranchDto>(created.Error);

        var branch = created.Value;
        if (await db.Branches.AsNoTracking().AnyAsync(x => x.Code == branch.Code, ct))
        {
            return Error.Conflict("ORG_BRANCH_DUPLICATE", $"Branch code '{branch.Code}' already exists in this tenant.");
        }

        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "organization.branch.create", "Branch", branch.Id.ToString("D"), tenantRes.Value, "Success", AfterState: Map(branch)), ct);

        return Map(branch);
    }

    public async Task<Result<BranchDto>> GetBranchAsync(Guid id, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<BranchDto>(tenantRes.Error);

        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return branch is null ? Error.NotFound("ORG_BRANCH_NOT_FOUND", $"Branch '{id}' was not found.") : Map(branch);
    }

    public async Task<Result<PagedResult<BranchDto>>> ListBranchesAsync(PagedRequest paging, Guid? legalEntityId = null, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<PagedResult<BranchDto>>(tenantRes.Error);

        var query = db.Branches.AsNoTracking();
        if (legalEntityId.HasValue)
        {
            query = query.Where(x => x.LegalEntityId == legalEntityId.Value);
        }

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderBy(x => x.Name).Skip(paging.Skip).Take(paging.PageSize).Select(x => Map(x)).ToListAsync(ct);
        return new PagedResult<BranchDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<Result<BranchDto>> UpdateBranchAsync(Guid id, UpdateBranchRequest request, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<BranchDto>(tenantRes.Error);

        var branch = await db.Branches.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (branch is null) return Error.NotFound("ORG_BRANCH_NOT_FOUND", $"Branch '{id}' was not found.");

        var before = Map(branch);
        var res = branch.Update(request.Name, request.Address, request.Status);
        if (res.IsFailure) return Result.Failure<BranchDto>(res.Error);

        await db.SaveChangesAsync(ct);
        await auditWriter.WriteAsync(new AuditEntryRequest(
            "organization.branch.update", "Branch", branch.Id.ToString("D"), tenantRes.Value, "Success", BeforeState: before, AfterState: Map(branch)), ct);

        return Map(branch);
    }

    public async Task<Result> DeleteBranchAsync(Guid id, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure(tenantRes.Error);

        var branch = await db.Branches.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (branch is null) return Result.Failure(Error.NotFound("ORG_BRANCH_NOT_FOUND", $"Branch '{id}' was not found."));

        db.Branches.Remove(branch);
        await db.SaveChangesAsync(ct);
        await auditWriter.WriteAsync(new AuditEntryRequest(
            "organization.branch.delete", "Branch", id.ToString("D"), tenantRes.Value, "Success"), ct);

        return Result.Success();
    }

    public async Task<Result<OrganizationUnitDto>> CreateUnitAsync(CreateOrganizationUnitRequest request, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<OrganizationUnitDto>(tenantRes.Error);

        if (request.ParentId.HasValue)
        {
            var parentExists = await db.OrganizationUnits.AsNoTracking().AnyAsync(u => u.Id == request.ParentId.Value && u.Status == OrganizationStatus.Active, ct);
            if (!parentExists)
            {
                return Error.NotFound("ORG_PARENT_NOT_FOUND", "Specified active parent organization unit does not exist in this tenant.");
            }
        }

        var created = OrganizationUnit.Create(tenantRes.Value, request.ParentId, request.Code, request.Name, request.UnitType);
        if (created.IsFailure) return Result.Failure<OrganizationUnitDto>(created.Error);

        var unit = created.Value;
        if (await db.OrganizationUnits.AsNoTracking().AnyAsync(u => u.Code == unit.Code, ct))
        {
            return Error.Conflict("ORG_UNIT_DUPLICATE", $"Organization unit code '{unit.Code}' already exists in this tenant.");
        }

        db.OrganizationUnits.Add(unit);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "organization.unit.create", "OrganizationUnit", unit.Id.ToString("D"), tenantRes.Value, "Success", AfterState: Map(unit)), ct);

        return Map(unit);
    }

    public async Task<Result<OrganizationUnitDto>> UpdateUnitAsync(Guid id, UpdateOrganizationUnitRequest request, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<OrganizationUnitDto>(tenantRes.Error);

        var unit = await db.OrganizationUnits.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null) return Error.NotFound("ORG_UNIT_NOT_FOUND", $"Organization unit '{id}' was not found.");

        if (request.ParentId.HasValue)
        {
            var allUnits = await db.OrganizationUnits.AsNoTracking()
                .Select(u => new { u.Id, u.ParentId })
                .ToDictionaryAsync(u => u.Id, u => u.ParentId, ct);

            if (!allUnits.ContainsKey(request.ParentId.Value))
            {
                return Error.NotFound("ORG_PARENT_NOT_FOUND", "Specified parent organization unit does not exist in this tenant.");
            }

            var cursor = request.ParentId;
            while (cursor.HasValue)
            {
                if (cursor.Value == id)
                {
                    return Error.BusinessRule("ORG_HIERARCHY_CYCLE", "Circular organization unit hierarchy is not allowed.");
                }

                cursor = allUnits.TryGetValue(cursor.Value, out var nextParent) ? nextParent : null;
            }
        }

        var before = Map(unit);
        var res = unit.Update(request.ParentId, request.Name, request.UnitType, request.Status);
        if (res.IsFailure) return Result.Failure<OrganizationUnitDto>(res.Error);

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "organization.unit.update", "OrganizationUnit", unit.Id.ToString("D"), tenantRes.Value, "Success", BeforeState: before, AfterState: Map(unit)), ct);

        return Map(unit);
    }

    public async Task<Result<IReadOnlyList<OrganizationUnitDto>>> ListUnitsAsync(CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<IReadOnlyList<OrganizationUnitDto>>(tenantRes.Error);

        var items = await db.OrganizationUnits.AsNoTracking()
            .OrderBy(u => u.Code)
            .Select(u => Map(u))
            .ToListAsync(ct);

        return items;
    }

    public async Task<Result<UserOrganizationAssignmentDto>> AssignUserAsync(AssignUserOrganizationRequest request, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<UserOrganizationAssignmentDto>(tenantRes.Error);
        var tenantId = tenantRes.Value;

        if (!request.BranchId.HasValue && !request.OrganizationUnitId.HasValue)
        {
            return Error.Validation("ORG_ASSIGNMENT_TARGET_REQUIRED", "At least one of BranchId or OrganizationUnitId must be specified.");
        }

        if (request.EffectiveFrom.HasValue && request.EffectiveTo.HasValue && request.EffectiveTo.Value <= request.EffectiveFrom.Value)
        {
            return Error.Validation("ORG_INVALID_EFFECTIVE_RANGE", "EffectiveTo must be after EffectiveFrom.");
        }

        // Security P0: verify target user exists AND holds an active membership in this exact tenant!
        var targetUser = await userDirectory.GetByIdAsync(request.UserId, ct);
        if (targetUser is null)
        {
            return Error.NotFound("USER_NOT_FOUND", $"User '{request.UserId}' was not found.");
        }

        var membership = await tenantValidator.ValidateMembershipAsync(request.UserId, tenantId, ct);
        if (membership is null || !membership.IsActiveMember)
        {
            return Error.Forbidden("USER_NOT_TENANT_MEMBER", "Cannot assign a user who does not hold an active membership in the current tenant.");
        }

        if (request.BranchId.HasValue && !await db.Branches.AsNoTracking().AnyAsync(b => b.Id == request.BranchId.Value && b.Status == OrganizationStatus.Active, ct))
        {
            return Error.NotFound("ORG_BRANCH_NOT_FOUND", "Specified active branch does not exist in this tenant.");
        }

        if (request.OrganizationUnitId.HasValue && !await db.OrganizationUnits.AsNoTracking().AnyAsync(u => u.Id == request.OrganizationUnitId.Value && u.Status == OrganizationStatus.Active, ct))
        {
            return Error.NotFound("ORG_UNIT_NOT_FOUND", "Specified active organization unit does not exist in this tenant.");
        }

        var assignment = UserOrganizationAssignment.Create(
            tenantId,
            request.UserId,
            request.BranchId,
            request.OrganizationUnitId,
            request.Title,
            request.IsPrimary,
            request.EffectiveFrom ?? clock.UtcNow,
            request.EffectiveTo);

        db.UserAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        var dto = MapAssignment(assignment);
        await auditWriter.WriteAsync(new AuditEntryRequest(
            "organization.assignment.create", "UserOrganizationAssignment", assignment.Id.ToString("D"), tenantId, "Success", AfterState: dto), ct);

        return dto;
    }

    public async Task<Result<IReadOnlyList<UserOrganizationAssignmentDto>>> ListAssignmentsAsync(Guid? userId = null, bool includeInactive = false, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<IReadOnlyList<UserOrganizationAssignmentDto>>(tenantRes.Error);

        var query = db.UserAssignments.AsNoTracking();
        if (userId.HasValue)
        {
            query = query.Where(a => a.UserId == userId.Value);
        }

        if (!includeInactive)
        {
            var now = clock.UtcNow;
            query = query.Where(a => a.IsActive && a.EffectiveFrom <= now && (a.EffectiveTo == null || a.EffectiveTo > now));
        }

        var items = await query.OrderByDescending(a => a.IsPrimary).ThenByDescending(a => a.EffectiveFrom)
            .Select(a => MapAssignment(a))
            .ToListAsync(ct);

        return items;
    }

    public async Task<Result> RevokeAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure(tenantRes.Error);

        var assignment = await db.UserAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
        if (assignment is null)
        {
            return Result.Failure(Error.NotFound("ORG_ASSIGNMENT_NOT_FOUND", "Organization assignment was not found."));
        }

        var before = MapAssignment(assignment);
        assignment.Deactivate(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "organization.assignment.revoke", "UserOrganizationAssignment", assignmentId.ToString("D"), tenantRes.Value, "Success",
            BeforeState: before, AfterState: MapAssignment(assignment)), ct);

        return Result.Success();
    }

    public async Task<Result<EffectiveOrganizationScope>> GetUserEffectiveScopeAsync(Guid userId, CancellationToken ct = default)
    {
        var tenantRes = RequireTenant();
        if (tenantRes.IsFailure) return Result.Failure<EffectiveOrganizationScope>(tenantRes.Error);

        return await GetEffectiveScopeAsync(userId, tenantRes.Value, ct);
    }

    public async Task<EffectiveOrganizationScope> GetEffectiveScopeAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId, platformBypass: false);
        var now = clock.UtcNow;

        var activeAssignments = await db.UserAssignments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.UserId == userId && a.IsActive && a.EffectiveFrom <= now && (a.EffectiveTo == null || a.EffectiveTo > now))
            .ToListAsync(ct);

        var assignedBranchIds = activeAssignments
            .Where(a => a.BranchId.HasValue)
            .Select(a => a.BranchId!.Value)
            .Distinct()
            .ToList();

        var activeBranches = assignedBranchIds.Count == 0
            ? []
            : await db.Branches.AsNoTracking()
                .Where(b => assignedBranchIds.Contains(b.Id) && b.Status == OrganizationStatus.Active)
                .Select(b => new { b.Id, b.LegalEntityId })
                .ToListAsync(ct);

        var validBranchIds = activeBranches.Select(b => b.Id).Distinct().ToList();
        var legalEntityIds = activeBranches.Select(b => b.LegalEntityId).Distinct().ToList();

        var rootUnitIds = activeAssignments
            .Where(a => a.OrganizationUnitId.HasValue)
            .Select(a => a.OrganizationUnitId!.Value)
            .ToHashSet();

        var effectiveUnitIds = new HashSet<Guid>();
        if (rootUnitIds.Count > 0)
        {
            var activeUnits = await db.OrganizationUnits.AsNoTracking()
                .Where(u => u.Status == OrganizationStatus.Active)
                .Select(u => new { u.Id, u.ParentId })
                .ToListAsync(ct);

            var childrenByParent = activeUnits
                .Where(u => u.ParentId.HasValue)
                .GroupBy(u => u.ParentId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

            var activeSet = activeUnits.Select(u => u.Id).ToHashSet();
            var queue = new Queue<Guid>(rootUnitIds.Where(activeSet.Contains));
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!effectiveUnitIds.Add(current)) continue;
                if (childrenByParent.TryGetValue(current, out var children))
                {
                    foreach (var child in children)
                    {
                        queue.Enqueue(child);
                    }
                }
            }
        }

        return new EffectiveOrganizationScope(userId, tenantId, legalEntityIds, validBranchIds, effectiveUnitIds.ToList());
    }

    private static LegalEntityDto Map(LegalEntity e) => new(
        e.Id, e.TenantId, e.Code, e.LegalName, e.TaxIdentificationNumber, e.Address, e.Status.ToString(), e.CreatedAt);

    private static BranchDto Map(Branch b) => new(
        b.Id, b.TenantId, b.LegalEntityId, b.Code, b.Name, b.Address, b.Status.ToString(), b.CreatedAt);

    private static OrganizationUnitDto Map(OrganizationUnit u) => new(
        u.Id, u.TenantId, u.ParentId, u.Code, u.Name, u.UnitType.ToString(), u.CreatedAt, u.Status.ToString());

    private static UserOrganizationAssignmentDto MapAssignment(UserOrganizationAssignment a) => new(
        a.Id, a.TenantId, a.UserId, a.BranchId, a.OrganizationUnitId, a.Title, a.IsPrimary, a.IsActive, a.EffectiveFrom, a.EffectiveTo);
}
