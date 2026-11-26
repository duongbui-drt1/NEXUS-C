using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Nexus.Modules.AccessControl.Domain;
using Nexus.Modules.AccessControl.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Results;
using Nexus.SharedKernel.Security;

namespace Nexus.Modules.AccessControl.Application;

public sealed record PermissionDto(Guid Id, string Code, string Name, string Scope, string Category);
public sealed record RoleDto(
    Guid Id,
    Guid? TenantId,
    string Name,
    string Description,
    string Scope,
    bool IsSystemRole,
    IReadOnlyList<string> Permissions,
    string? ApplicationCode = null,
    bool IsSharedReusable = false);
public sealed record CreateRoleRequest(
    string Name,
    string Description,
    IReadOnlyList<string> PermissionCodes,
    string? ApplicationCode = null,
    bool IsSharedReusable = false);
public sealed record AssignRoleRequest(
    Guid UserId,
    Guid RoleId,
    string? ResourceScopeType = null,
    string? ResourceScopeId = null,
    AuthorizationEffect Effect = AuthorizationEffect.Allow);
public sealed record UserRoleDto(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    string RoleName,
    Guid? TenantId,
    string Scope,
    string? ResourceScopeType,
    string? ResourceScopeId,
    string Effect = "Allow");

public sealed record SyncApplicationPermissionItem(
    string Code,
    string Name,
    string Category,
    string Scope = "Tenant");
public sealed record SyncApplicationPermissionsRequest(IReadOnlyList<SyncApplicationPermissionItem> Permissions);
public sealed record SyncApplicationPermissionsResponse(
    string ApplicationCode,
    int AddedCount,
    int UpdatedCount,
    int TotalSynced,
    IReadOnlyList<PermissionDto> Permissions);

public interface IAccessControlService
{
    Task SeedDefaultsAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<PermissionDto>>> ListPermissionsAsync(CancellationToken ct = default);
    Task<Result<SyncApplicationPermissionsResponse>> SyncApplicationPermissionsAsync(string applicationCode, SyncApplicationPermissionsRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<RoleDto>>> ListRolesAsync(CancellationToken ct = default);
    Task<Result<RoleDto>> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct = default);
    Task<Result<UserRoleDto>> AssignRoleAsync(AssignRoleRequest request, CancellationToken ct = default);
    Task<Result> RevokeRoleAsync(Guid userRoleId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<UserRoleDto>>> ListUserRolesAsync(Guid userId, CancellationToken ct = default);
}

public sealed class AccessControlService(
    AccessControlDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IUserDirectoryReader userDirectory,
    ITenantAccessValidator tenantValidator,
    IOrganizationScopeReader orgScopeReader,
    IDistributedCache cache,
    IAuditWriter auditWriter) : IAccessControlService, IPermissionEvaluator
{
    private static readonly HashSet<string> ValidScopeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Platform", "Tenant", "LegalEntity", "Branch", "OrganizationUnit", "Resource"
    };

    private static string BuildPermCacheKey(Guid userId, Guid? tenantId, bool isPlatformScope)
        => $"perm:v1:{(isPlatformScope && !tenantId.HasValue ? "platform" : tenantId?.ToString("N") ?? "none")}:{userId:N}";

    private async Task InvalidateUserPermissionCacheAsync(Guid userId, Guid? tenantId, CancellationToken ct)
    {
        await cache.RemoveAsync(BuildPermCacheKey(userId, tenantId, isPlatformScope: !tenantId.HasValue), ct);
        await cache.RemoveAsync(BuildPermCacheKey(userId, tenantId, isPlatformScope: false), ct);
        await cache.RemoveAsync(BuildPermCacheKey(userId, null, isPlatformScope: true), ct);
    }

    public async Task SeedDefaultsAsync(CancellationToken ct = default)
    {
        var existingPermissions = await db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        foreach (var (code, name, scope, category) in Permissions.Catalog)
        {
            if (!existingPermissions.ContainsKey(code))
            {
                var perm = Permission.Create(code, name, scope, category);
                db.Permissions.Add(perm);
                existingPermissions[code] = perm;
            }
        }
        await db.SaveChangesAsync(ct);

        var roleDefinitions = new Dictionary<string, (string Description, RoleScopeType Scope, string[] Perms)>
        {
            [SystemRoles.PlatformSuperAdmin] = (
                "Full NEXUS Platform Administrator (platform scope; cross-tenant actions audited)",
                RoleScopeType.Platform,
                Permissions.Catalog.Select(c => c.Code).ToArray()),

            [SystemRoles.PlatformSupport] = (
                "Read-only platform support engineer",
                RoleScopeType.Platform,
                [
                    Permissions.TenantsRead, Permissions.ApplicationsRead, Permissions.UsersRead, Permissions.RolesRead,
                    Permissions.AuditRead, Permissions.AuditReadAllTenants, Permissions.SubscriptionsRead,
                    Permissions.ConfigurationRead, Permissions.ApiRead, Permissions.ApiLogsRead, Permissions.OperationsRead
                ]),

            [SystemRoles.TenantOwner] = (
                "Owner of a tenant organization (all tenant-scoped permissions)",
                RoleScopeType.Tenant,
                Permissions.Catalog.Where(c => c.Scope is PermissionScope.Tenant or PermissionScope.Both).Select(c => c.Code).ToArray()),

            [SystemRoles.TenantAdmin] = (
                "Tenant administrator with explicit least-privilege operational permissions",
                RoleScopeType.Tenant,
                [
                    Permissions.ApplicationsRead, Permissions.SubscriptionsRead,
                    Permissions.OrganizationsRead, Permissions.OrganizationsManage,
                    Permissions.UsersRead, Permissions.UsersCreate, Permissions.UsersManage, Permissions.UsersProfileUpdate,
                    Permissions.RolesRead, Permissions.RolesAssign,
                    Permissions.WorkflowsRead, Permissions.WorkflowsManage,
                    Permissions.ConfigurationRead, Permissions.ConfigurationManage,
                    Permissions.IntegrationsRead, Permissions.IntegrationsManage,
                    Permissions.NotificationsRead, Permissions.NotificationsManage,
                    Permissions.FilesRead, Permissions.FilesUpload, Permissions.FilesDelete,
                    Permissions.ApiRead, Permissions.ApiLogsRead
                ]),

            [SystemRoles.CFO] = (
                "Chief Financial Officer (approver & auditor)",
                RoleScopeType.Tenant,
                [
                    Permissions.OrganizationsRead, Permissions.AuditRead,
                    Permissions.WorkflowsRead, Permissions.WorkflowsApprove,
                    Permissions.SubscriptionsRead, Permissions.ApplicationsRead, Permissions.ConfigurationRead,
                    Permissions.NotificationsRead, Permissions.FilesRead, Permissions.FilesUpload
                ]),

            [SystemRoles.Accountant] = (
                "Accountant (maker role; cannot approve own workflows)",
                RoleScopeType.Tenant,
                [
                    Permissions.OrganizationsRead, Permissions.WorkflowsRead, Permissions.WorkflowsCreate,
                    Permissions.NotificationsRead, Permissions.FilesRead, Permissions.FilesUpload
                ]),

            [SystemRoles.LogisticsDirector] = (
                "Logistics Director",
                RoleScopeType.Tenant,
                [
                    Permissions.OrganizationsRead, Permissions.OrganizationsManage,
                    Permissions.WorkflowsRead, Permissions.WorkflowsApprove,
                    Permissions.IntegrationsRead, Permissions.IntegrationsManage,
                    Permissions.NotificationsRead, Permissions.FilesRead, Permissions.FilesUpload
                ]),

            [SystemRoles.WarehouseManager] = (
                "Warehouse Manager",
                RoleScopeType.Tenant,
                [
                    Permissions.OrganizationsRead, Permissions.WorkflowsRead, Permissions.WorkflowsCreate,
                    Permissions.NotificationsRead, Permissions.FilesRead, Permissions.FilesUpload
                ]),

            [SystemRoles.Employee] = (
                "Standard Tenant Employee",
                RoleScopeType.Tenant,
                [
                    Permissions.OrganizationsRead, Permissions.WorkflowsRead, Permissions.WorkflowsCreate,
                    Permissions.NotificationsRead, Permissions.FilesRead, Permissions.FilesUpload
                ]),

            [SystemRoles.Auditor] = (
                "Read-only Compliance Auditor",
                RoleScopeType.Tenant,
                [
                    Permissions.AuditRead, Permissions.OrganizationsRead, Permissions.UsersRead,
                    Permissions.RolesRead, Permissions.WorkflowsRead, Permissions.SubscriptionsRead,
                    Permissions.ApplicationsRead, Permissions.ConfigurationRead, Permissions.IntegrationsRead, Permissions.FilesRead
                ])
        };

        var existingRoles = await db.Roles
            .Include(r => r.RolePermissions)
            .Where(r => r.IsSystemRole && r.TenantId == null)
            .ToDictionaryAsync(r => r.NormalizedName, ct);

        foreach (var (roleName, def) in roleDefinitions)
        {
            var normalized = roleName.ToUpperInvariant();
            if (!existingRoles.TryGetValue(normalized, out var role))
            {
                role = Role.Create(roleName, def.Description, def.Scope, tenantId: null, isSystemRole: true).Value;
                db.Roles.Add(role);
                existingRoles[normalized] = role;
            }

            var currentPermIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
            foreach (var permCode in def.Perms)
            {
                if (existingPermissions.TryGetValue(permCode, out var perm) && !currentPermIds.Contains(perm.Id))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<Result<IReadOnlyList<PermissionDto>>> ListPermissionsAsync(CancellationToken ct = default)
    {
        var query = db.Permissions.AsNoTracking();
        if (!currentUser.IsPlatformUser)
        {
            query = query.Where(p => p.Scope != PermissionScope.Platform);
        }

        var list = await query
            .OrderBy(p => p.Category).ThenBy(p => p.Code)
            .Select(p => new PermissionDto(p.Id, p.Code, p.Name, p.Scope.ToString(), p.Category))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<SyncApplicationPermissionsResponse>> SyncApplicationPermissionsAsync(
        string applicationCode,
        SyncApplicationPermissionsRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(applicationCode))
        {
            return Error.Validation("APPLICATION_CODE_REQUIRED", "Application code is required.");
        }

        var normalizedApp = applicationCode.Trim().ToUpperInvariant();
        if (normalizedApp == "NEXUS_C")
        {
            return Error.Forbidden("PLATFORM_PERMISSIONS_PROTECTED", "Core NEXUS_C platform permissions cannot be modified via application sync.");
        }

        if (request?.Permissions is null || request.Permissions.Count == 0)
        {
            return Error.Validation("PERMISSIONS_REQUIRED", "At least one permission item is required.");
        }

        if (request.Permissions.Count > 200)
        {
            return Error.Validation("PERMISSIONS_LIMIT_EXCEEDED", "A maximum of 200 permissions may be synced per request.");
        }

        var normalizedItems = new List<(string Code, string Name, PermissionScope Scope, string Category)>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in request.Permissions)
        {
            if (string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Category))
            {
                return Error.Validation("PERMISSION_INVALID_ITEM", "Permission code, name, and category are required.");
            }

            var code = item.Code.Trim().ToLowerInvariant();
            if (code.StartsWith("platform.", StringComparison.OrdinalIgnoreCase))
            {
                return Error.Forbidden("PERMISSION_PREFIX_FORBIDDEN", $"Permission '{code}' cannot use reserved 'platform.' prefix.");
            }

            if (code.Contains('*') || !code.Contains('.') || code.Length > 100)
            {
                return Error.Validation("PERMISSION_INVALID_CODE", $"Permission code '{code}' must be namespaced (e.g., 'm.dashboard.read'), max 100 chars, without wildcards.");
            }

            if (!seenCodes.Add(code))
            {
                return Error.Validation("PERMISSION_DUPLICATE_CODE", $"Duplicate permission code '{code}' in sync manifest.");
            }

            var scope = Enum.TryParse<PermissionScope>(item.Scope, ignoreCase: true, out var parsedScope)
                ? parsedScope
                : PermissionScope.Tenant;
            if (scope == PermissionScope.Platform)
            {
                return Error.Forbidden("PERMISSION_PLATFORM_SCOPE_FORBIDDEN", $"Application '{normalizedApp}' cannot register Platform-only permission '{code}'.");
            }

            normalizedItems.Add((code, item.Name.Trim(), scope, item.Category.Trim()));
        }

        var targetCodes = normalizedItems.Select(x => x.Code).ToList();
        var existing = await db.Permissions
            .Where(p => targetCodes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);

        var addedCount = 0;
        var updatedCount = 0;
        var syncedEntities = new List<Permission>();

        foreach (var (code, name, scope, category) in normalizedItems)
        {
            if (existing.TryGetValue(code, out var perm))
            {
                if (perm.UpdateMetadata(name, scope, category))
                {
                    updatedCount++;
                }
                syncedEntities.Add(perm);
            }
            else
            {
                var created = Permission.Create(code, name, scope, category);
                db.Permissions.Add(created);
                syncedEntities.Add(created);
                addedCount++;
            }
        }

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "application.permissions.synced", "Application", normalizedApp, null, "Success",
            AfterState: new { normalizedApp, addedCount, updatedCount, codes = targetCodes }), ct);

        var resultDtos = syncedEntities
            .OrderBy(p => p.Category).ThenBy(p => p.Code)
            .Select(p => new PermissionDto(p.Id, p.Code, p.Name, p.Scope.ToString(), p.Category))
            .ToList();

        return new SyncApplicationPermissionsResponse(
            normalizedApp,
            addedCount,
            updatedCount,
            resultDtos.Count,
            resultDtos);
    }

    public async Task<Result<IReadOnlyList<RoleDto>>> ListRolesAsync(CancellationToken ct = default)
    {
        var query = db.Roles.AsNoTracking()
            .Include(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .AsQueryable();

        if (!currentUser.IsPlatformUser)
        {
            var tid = tenantContext.TenantId;
            query = query.Where(r => r.Scope == RoleScopeType.Tenant && (r.TenantId == null || r.TenantId == tid));
        }

        var roles = await query.OrderBy(r => r.Name).ToListAsync(ct);
        return roles.Select(r => new RoleDto(
            r.Id,
            r.TenantId,
            r.Name,
            r.Description,
            r.Scope.ToString(),
            r.IsSystemRole,
            r.RolePermissions.Where(rp => rp.Permission != null).Select(rp => rp.Permission!.Code).OrderBy(c => c).ToList(),
            r.ApplicationCode,
            r.IsSharedReusable
        )).ToList();
    }

    public async Task<Result<RoleDto>> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct = default)
    {
        var isPlatform = currentUser.IsPlatformUser && !tenantContext.HasTenant;
        var scope = isPlatform ? RoleScopeType.Platform : RoleScopeType.Tenant;
        var tenantId = isPlatform ? null : tenantContext.TenantId;

        var created = Role.Create(request.Name, request.Description, scope, tenantId, isSystemRole: false, request.ApplicationCode, request.IsSharedReusable);
        if (created.IsFailure) return Result.Failure<RoleDto>(created.Error);

        var role = created.Value;
        var duplicate = await db.Roles.AsNoTracking()
            .AnyAsync(r => r.NormalizedName == role.NormalizedName && r.TenantId == tenantId, ct);
        if (duplicate)
        {
            return Error.Conflict("ROLE_DUPLICATE", $"Role '{role.Name}' already exists in this scope.");
        }

        var requestedCodes = request.PermissionCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var perms = await db.Permissions.Where(p => requestedCodes.Contains(p.Code)).ToListAsync(ct);
        if (perms.Count != requestedCodes.Count)
        {
            return Error.Validation("ROLE_INVALID_PERMISSIONS", "One or more permission codes are invalid.");
        }

        if (scope == RoleScopeType.Tenant && perms.Any(p => p.Scope == PermissionScope.Platform))
        {
            return Error.Forbidden("ROLE_PRIVILEGE_ESCALATION", "Tenant roles cannot be granted platform-only permissions.");
        }

        db.Roles.Add(role);
        foreach (var perm in perms)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
        }

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "access_control.role.create", "Role", role.Id.ToString("D"), tenantId, "Success",
            AfterState: new { role.Name, Scope = role.Scope.ToString(), role.ApplicationCode, role.IsSharedReusable, Permissions = requestedCodes }), ct);

        return new RoleDto(role.Id, role.TenantId, role.Name, role.Description, role.Scope.ToString(), role.IsSystemRole, requestedCodes, role.ApplicationCode, role.IsSharedReusable);
    }

    public async Task<Result<UserRoleDto>> AssignRoleAsync(AssignRoleRequest request, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.ResourceScopeType) && !ValidScopeTypes.Contains(request.ResourceScopeType.Trim()))
        {
            return Error.Validation("ROLE_INVALID_RESOURCE_SCOPE", $"Unsupported ResourceScopeType '{request.ResourceScopeType}'. Supported: Platform, Tenant, LegalEntity, Branch, OrganizationUnit, Resource.");
        }

        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RoleId, ct);
        if (role is null)
        {
            return Error.NotFound("ROLE_NOT_FOUND", "Role was not found.");
        }

        if (!currentUser.IsPlatformUser)
        {
            if (role.Scope == RoleScopeType.Platform)
            {
                return Error.Forbidden("ROLE_SCOPE_VIOLATION", "Tenant administrators cannot assign platform-scoped roles.");
            }

            if (role.TenantId.HasValue && role.TenantId != tenantContext.TenantId)
            {
                return Error.Forbidden("ROLE_CROSS_TENANT_DENIED", "Cannot assign a custom role belonging to another tenant.");
            }
        }

        Guid? effectiveTenantId = role.Scope == RoleScopeType.Platform ? null : tenantContext.TenantId ?? role.TenantId;
        if (role.Scope == RoleScopeType.Tenant && !effectiveTenantId.HasValue)
        {
            return Error.Validation("TENANT_CONTEXT_REQUIRED", "A tenant context is required to assign a tenant-scoped role.");
        }

        var targetUser = await userDirectory.GetByIdAsync(request.UserId, ct);
        if (targetUser is null)
        {
            return Error.NotFound("USER_NOT_FOUND", $"User '{request.UserId}' was not found.");
        }

        if (role.Scope == RoleScopeType.Tenant && effectiveTenantId.HasValue)
        {
            var membership = await tenantValidator.ValidateMembershipAsync(request.UserId, effectiveTenantId.Value, ct);
            if (membership is null || !membership.IsActiveMember)
            {
                return Error.Forbidden(
                    "USER_NOT_TENANT_MEMBER",
                    "Cannot assign a tenant-scoped role to a user who does not have an active membership in that tenant.");
            }
        }

        var normScopeType = string.IsNullOrWhiteSpace(request.ResourceScopeType) ? null : request.ResourceScopeType.Trim();
        var normScopeId = string.IsNullOrWhiteSpace(request.ResourceScopeId) ? null : request.ResourceScopeId.Trim();

        var exists = await db.UserRoles.AsNoTracking()
            .AnyAsync(ur => ur.UserId == request.UserId
                            && ur.RoleId == role.Id
                            && ur.TenantId == effectiveTenantId
                            && ur.ResourceScopeType == normScopeType
                            && ur.ResourceScopeId == normScopeId
                            && ur.Effect == request.Effect, ct);
        if (exists)
        {
            return Error.Conflict("USER_ROLE_EXISTS", "User already holds this role assignment in the target scope.");
        }

        var userRole = UserRole.Assign(request.UserId, role.Id, effectiveTenantId, normScopeType, normScopeId, request.Effect);
        db.UserRoles.Add(userRole);
        await db.SaveChangesAsync(ct);

        await InvalidateUserPermissionCacheAsync(request.UserId, effectiveTenantId, ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "access_control.role.assign", "UserRole", userRole.Id.ToString("D"), effectiveTenantId, "Success",
            AfterState: new { userRole.UserId, Role = role.Name, userRole.TenantId, userRole.ResourceScopeType, userRole.ResourceScopeId, Effect = userRole.Effect.ToString() }), ct);

        return new UserRoleDto(
            userRole.Id, userRole.UserId, role.Id, role.Name, userRole.TenantId,
            role.Scope.ToString(), userRole.ResourceScopeType, userRole.ResourceScopeId, userRole.Effect.ToString());
    }

    public async Task<Result> RevokeRoleAsync(Guid userRoleId, CancellationToken ct = default)
    {
        var assignment = await db.UserRoles.Include(ur => ur.Role).FirstOrDefaultAsync(ur => ur.Id == userRoleId, ct);
        if (assignment is null)
        {
            return Result.Failure(Error.NotFound("USER_ROLE_NOT_FOUND", "Role assignment was not found."));
        }

        if (!currentUser.IsPlatformUser && assignment.TenantId != tenantContext.TenantId)
        {
            return Result.Failure(Error.Forbidden("ROLE_CROSS_TENANT_DENIED", "Cannot revoke a role outside your tenant."));
        }

        db.UserRoles.Remove(assignment);
        await db.SaveChangesAsync(ct);

        await InvalidateUserPermissionCacheAsync(assignment.UserId, assignment.TenantId, ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "access_control.role.revoke", "UserRole", userRoleId.ToString("D"), assignment.TenantId, "Success",
            BeforeState: new { assignment.UserId, Role = assignment.Role?.Name, assignment.TenantId, assignment.ResourceScopeType, assignment.ResourceScopeId, Effect = assignment.Effect.ToString() }), ct);

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<UserRoleDto>>> ListUserRolesAsync(Guid userId, CancellationToken ct = default)
    {
        var query = db.UserRoles.AsNoTracking().Include(ur => ur.Role).Where(ur => ur.UserId == userId);
        if (!currentUser.IsPlatformUser)
        {
            if (!tenantContext.HasTenant)
            {
                return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
            }

            var tid = tenantContext.TenantId!.Value;
            query = query.Where(ur => ur.TenantId == tid && ur.Role!.Scope == RoleScopeType.Tenant);
        }

        var list = await query
            .Select(ur => new UserRoleDto(
                ur.Id, ur.UserId, ur.RoleId, ur.Role!.Name, ur.TenantId,
                ur.Role.Scope.ToString(), ur.ResourceScopeType, ur.ResourceScopeId, ur.Effect.ToString()))
            .ToListAsync(ct);

        return list;
    }

    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid userId, Guid? tenantId, bool isPlatformScope, CancellationToken ct = default)
    {
        using var platformRoleScope = isPlatformScope && tenantId.HasValue
            ? tenantContext.BeginScope(tenantId, platformBypass: true, bypassReason: "platform-admin-tenant-context-rbac-eval")
            : null;

        var roleQuery = db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.Effect == AuthorizationEffect.Allow);

        if (isPlatformScope && !tenantId.HasValue)
        {
            roleQuery = roleQuery.Where(ur => ur.TenantId == null && ur.Role!.Scope == RoleScopeType.Platform);
        }
        else if (isPlatformScope && tenantId.HasValue)
        {
            roleQuery = roleQuery.Where(ur =>
                (ur.TenantId == null && ur.Role!.Scope == RoleScopeType.Platform) ||
                (ur.TenantId == tenantId.Value && ur.Role!.Scope == RoleScopeType.Tenant));
        }
        else if (tenantId.HasValue)
        {
            roleQuery = roleQuery.Where(ur => ur.TenantId == tenantId.Value && ur.Role!.Scope == RoleScopeType.Tenant);
        }
        else
        {
            return [];
        }

        return await roleQuery.Select(ur => ur.Role!.Name).Distinct().OrderBy(n => n).ToListAsync(ct);
    }

    // === IPermissionEvaluator ===

    public async Task<bool> HasPermissionAsync(Guid userId, Guid? tenantId, string permissionCode, bool isPlatformScope, CancellationToken ct = default)
    {
        var perms = await GetPermissionsAsync(userId, tenantId, isPlatformScope, ct);
        return perms.Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, Guid? tenantId, bool isPlatformScope, CancellationToken ct = default)
    {
        var cacheKey = BuildPermCacheKey(userId, tenantId, isPlatformScope);
        try
        {
            var cached = await cache.GetStringAsync(cacheKey, ct);
            if (!string.IsNullOrEmpty(cached))
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(cached);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // Fall through to authoritative database evaluation if cache is transiently unavailable
        }

        List<UserRole> assignments;
        using (isPlatformScope && tenantId.HasValue
            ? tenantContext.BeginScope(tenantId, platformBypass: true, bypassReason: "platform-admin-tenant-context-rbac-eval")
            : null)
        {
            var roleQuery = db.UserRoles.AsNoTracking()
                .Include(ur => ur.Role!)
                .ThenInclude(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
                .Where(ur => ur.UserId == userId);

            if (isPlatformScope && !tenantId.HasValue)
            {
                roleQuery = roleQuery.Where(ur => ur.TenantId == null && ur.Role!.Scope == RoleScopeType.Platform);
            }
            else if (isPlatformScope && tenantId.HasValue)
            {
                roleQuery = roleQuery.Where(ur =>
                    (ur.TenantId == null && ur.Role!.Scope == RoleScopeType.Platform) ||
                    (ur.TenantId == tenantId.Value && ur.Role!.Scope == RoleScopeType.Tenant));
            }
            else if (tenantId.HasValue)
            {
                roleQuery = roleQuery.Where(ur => ur.TenantId == tenantId.Value && ur.Role!.Scope == RoleScopeType.Tenant);
            }
            else
            {
                return [];
            }

            assignments = await roleQuery.ToListAsync(ct);
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitlyDenied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ur in assignments)
        {
            if (ur.Role is null) continue;
            var codes = ur.Role.RolePermissions
                .Where(rp => rp.Permission is not null)
                .Select(rp => rp.Permission!.Code);

            if (ur.Effect == AuthorizationEffect.Deny && string.IsNullOrWhiteSpace(ur.ResourceScopeType))
            {
                foreach (var c in codes) explicitlyDenied.Add(c);
            }
            else if (ur.Effect == AuthorizationEffect.Allow)
            {
                foreach (var c in codes) allowed.Add(c);
            }
        }

        allowed.ExceptWith(explicitlyDenied);

        var permCodes = allowed.OrderBy(c => c).ToList();

        if (!isPlatformScope)
        {
            var platformOnly = Permissions.Catalog
                .Where(c => c.Scope == PermissionScope.Platform)
                .Select(c => c.Code)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            permCodes = permCodes.Where(c => !platformOnly.Contains(c)).ToList();
        }

        try
        {
            await cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(permCodes),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2) },
                ct);
        }
        catch
        {
            // Non-fatal cache write failure
        }

        return permCodes;
    }

    public async Task<AuthorizationDecision> EvaluatePolicyAsync(AuthorizationCheckRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.PermissionCode))
        {
            return new AuthorizationDecision(false, "PERMISSION_CODE_REQUIRED");
        }

        var platformOnly = Permissions.Catalog
            .Where(c => c.Scope == PermissionScope.Platform)
            .Select(c => c.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!request.IsPlatformScope && platformOnly.Contains(request.PermissionCode))
        {
            return new AuthorizationDecision(false, "PLATFORM_SCOPE_REQUIRED");
        }

        List<UserRole> assignments;
        using (request.IsPlatformScope && request.TenantId.HasValue
            ? tenantContext.BeginScope(request.TenantId, platformBypass: true, bypassReason: "platform-admin-tenant-context-rbac-eval")
            : null)
        {
            var query = db.UserRoles.AsNoTracking()
                .Include(ur => ur.Role!)
                .ThenInclude(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
                .Where(ur => ur.UserId == request.UserId);

            if (request.IsPlatformScope && !request.TenantId.HasValue)
            {
                query = query.Where(ur => ur.TenantId == null && ur.Role!.Scope == RoleScopeType.Platform);
            }
            else if (request.IsPlatformScope && request.TenantId.HasValue)
            {
                query = query.Where(ur =>
                    (ur.TenantId == null && ur.Role!.Scope == RoleScopeType.Platform) ||
                    (ur.TenantId == request.TenantId.Value && ur.Role!.Scope == RoleScopeType.Tenant));
            }
            else if (request.TenantId.HasValue)
            {
                query = query.Where(ur => ur.TenantId == request.TenantId.Value && ur.Role!.Scope == RoleScopeType.Tenant);
            }
            else
            {
                return new AuthorizationDecision(false, "SECURITY_CONTEXT_MISSING");
            }

            assignments = await query.ToListAsync(ct);
        }

        // Enforce strict Application-Scope Isolation:
        // An application-bound role (e.g., NEXUS_F) MUST NEVER authorize operations in another application (e.g., NEXUS_M).
        var targetApp = string.IsNullOrWhiteSpace(request.ApplicationCode) ? null : request.ApplicationCode.Trim().ToUpperInvariant();
        var appFilteredAssignments = assignments.Where(ur =>
        {
            var roleApp = ur.Role?.ApplicationCode;
            if (string.IsNullOrWhiteSpace(roleApp))
            {
                return true; // Core / shared role applies across apps unless restricted by explicit permissions
            }
            if (targetApp is null)
            {
                return string.Equals(roleApp, "NEXUS_C", StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(roleApp, targetApp, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        var matchingRoleAssignments = appFilteredAssignments
            .Where(ur => ur.Role?.RolePermissions.Any(rp =>
                rp.Permission != null && string.Equals(rp.Permission.Code, request.PermissionCode, StringComparison.OrdinalIgnoreCase)) == true)
            .ToList();

        if (matchingRoleAssignments.Count == 0)
        {
            var blockedByAppScope = assignments.Any(ur =>
                !string.IsNullOrWhiteSpace(ur.Role?.ApplicationCode) &&
                ur.Role.RolePermissions.Any(rp =>
                    rp.Permission != null && string.Equals(rp.Permission.Code, request.PermissionCode, StringComparison.OrdinalIgnoreCase)));

            return new AuthorizationDecision(false, blockedByAppScope ? "APPLICATION_SCOPE_DENIED" : "PERMISSION_NOT_GRANTED");
        }

        // 1. Evaluate Explicit Deny first (applies globally or for matching scope)
        foreach (var deny in matchingRoleAssignments.Where(ur => ur.Effect == AuthorizationEffect.Deny))
        {
            if (ScopeMatches(deny, request.ScopeType, request.ScopeId))
            {
                return new AuthorizationDecision(false, "EXPLICIT_DENY", ExplicitDeny: true);
            }
        }

        // 2. Evaluate Allow assignments
        var allows = matchingRoleAssignments.Where(ur => ur.Effect == AuthorizationEffect.Allow).ToList();
        if (allows.Count == 0)
        {
            return new AuthorizationDecision(false, "PERMISSION_NOT_GRANTED");
        }

        // Unscoped check (no specific org/resource target requested)
        if (string.IsNullOrWhiteSpace(request.ScopeType) ||
            string.Equals(request.ScopeType, "Platform", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.ScopeType, "Tenant", StringComparison.OrdinalIgnoreCase))
        {
            return new AuthorizationDecision(true, "ALLOWED_BY_ROLE");
        }

        // Explicit resource/org scope match from a scoped UserRole assignment
        if (allows.Any(a => !string.IsNullOrWhiteSpace(a.ResourceScopeType) && ScopeMatches(a, request.ScopeType, request.ScopeId)))
        {
            return new AuthorizationDecision(true, "ALLOWED_BY_SCOPED_ROLE");
        }

        // If ALL allow assignments for this permission are scoped to OTHER resources, deny immediately!
        var hasUnscopedAllow = allows.Any(a => string.IsNullOrWhiteSpace(a.ResourceScopeType));
        if (!hasUnscopedAllow)
        {
            return new AuthorizationDecision(false, "RESOURCE_SCOPE_DENIED");
        }

        // For Tenant-scoped organizational targets (LegalEntity / Branch / OrganizationUnit),
        // enforce Organization hierarchy isolation when user has explicit organizational assignments and is not an org-wide manager!
        if (!request.IsPlatformScope && request.TenantId.HasValue && Guid.TryParse(request.ScopeId, out var targetOrgGuid))
        {
            var isOrgScopeCheck = request.ScopeType.Equals("Branch", StringComparison.OrdinalIgnoreCase)
                                  || request.ScopeType.Equals("LegalEntity", StringComparison.OrdinalIgnoreCase)
                                  || request.ScopeType.Equals("OrganizationUnit", StringComparison.OrdinalIgnoreCase);

            if (isOrgScopeCheck)
            {
                var isTenantWideOrgManager = assignments.Any(ur =>
                    ur.Effect == AuthorizationEffect.Allow &&
                    string.IsNullOrWhiteSpace(ur.ResourceScopeType) &&
                    ur.Role?.RolePermissions.Any(rp => rp.Permission?.Code == Permissions.OrganizationsManage) == true);

                if (!isTenantWideOrgManager)
                {
                    var effectiveOrg = await orgScopeReader.GetEffectiveScopeAsync(request.UserId, request.TenantId.Value, ct);
                    var hasAnyAssignment = effectiveOrg.BranchIds.Count > 0 || effectiveOrg.OrganizationUnitIds.Count > 0;
                    if (hasAnyAssignment)
                    {
                        var inScope = request.ScopeType.ToLowerInvariant() switch
                        {
                            "branch" => effectiveOrg.BranchIds.Contains(targetOrgGuid),
                            "legalentity" => effectiveOrg.LegalEntityIds.Contains(targetOrgGuid),
                            "organizationunit" => effectiveOrg.OrganizationUnitIds.Contains(targetOrgGuid),
                            _ => false
                        };

                        if (!inScope)
                        {
                            return new AuthorizationDecision(false, "ORGANIZATION_SCOPE_DENIED");
                        }
                    }
                }
            }
        }

        return new AuthorizationDecision(true, "ALLOWED_BY_TENANT_ROLE");
    }

    private static bool ScopeMatches(UserRole assignment, string? reqScopeType, string? reqScopeId)
    {
        if (string.IsNullOrWhiteSpace(assignment.ResourceScopeType))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(reqScopeType))
        {
            return false;
        }

        if (!string.Equals(assignment.ResourceScopeType, reqScopeType.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(assignment.ResourceScopeId))
        {
            return true;
        }

        return string.Equals(assignment.ResourceScopeId, reqScopeId?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public async Task AssignRoleByNameAsync(Guid userId, Guid? tenantId, string roleName, CancellationToken ct = default)
    {
        var normalized = roleName.Trim().ToUpperInvariant();
        var role = await db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.NormalizedName == normalized && (r.TenantId == null || r.TenantId == tenantId), ct);

        if (role is null)
        {
            throw new DomainException(Error.NotFound("ROLE_NOT_FOUND", $"Role '{roleName}' was not found."));
        }

        var targetTenantId = role.Scope == RoleScopeType.Platform ? null : tenantId;
        var exists = await db.UserRoles.AsNoTracking()
            .AnyAsync(ur => ur.UserId == userId
                            && ur.RoleId == role.Id
                            && ur.TenantId == targetTenantId
                            && ur.ResourceScopeType == null
                            && ur.Effect == AuthorizationEffect.Allow, ct);

        if (!exists)
        {
            db.UserRoles.Add(UserRole.Assign(userId, role.Id, targetTenantId));
            await db.SaveChangesAsync(ct);
            await InvalidateUserPermissionCacheAsync(userId, targetTenantId, ct);
        }
    }
}
