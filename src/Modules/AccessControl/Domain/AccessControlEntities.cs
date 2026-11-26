using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;
using Nexus.SharedKernel.Security;

namespace Nexus.Modules.AccessControl.Domain;

public enum RoleScopeType
{
    Platform = 1,
    Tenant = 2
}

public enum AuthorizationEffect
{
    Allow = 1,
    Deny = 2
}

public sealed class Role : AuditableEntity
{
    private Role() { }

    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public RoleScopeType Scope { get; private set; }
    public bool IsSystemRole { get; private set; }
    public string? ApplicationCode { get; private set; }
    public bool IsSharedReusable { get; private set; }

    public ICollection<RolePermission> RolePermissions { get; private set; } = [];

    public static Result<Role> Create(
        string name,
        string description,
        RoleScopeType scope,
        Guid? tenantId,
        bool isSystemRole = false,
        string? applicationCode = null,
        bool isSharedReusable = false)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            return Error.Validation("ROLE_INVALID_NAME", "Role name is required (max 100 chars).");
        }

        if (scope == RoleScopeType.Platform && tenantId.HasValue)
        {
            return Error.Validation("ROLE_INVALID_SCOPE", "Platform roles cannot be bound to a tenant.");
        }

        if (!isSystemRole && scope == RoleScopeType.Tenant && !tenantId.HasValue)
        {
            return Error.Validation("ROLE_TENANT_REQUIRED", "Custom tenant roles must belong to a tenant.");
        }

        var normalizedApp = string.IsNullOrWhiteSpace(applicationCode) ? null : applicationCode.Trim().ToUpperInvariant();
        if (normalizedApp is not null && isSharedReusable)
        {
            return Error.Validation("ROLE_CONFLICTING_APP_SCOPE", "An application-scoped role cannot simultaneously be marked as shared reusable across applications.");
        }

        return new Role
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Name = name.Trim(),
            NormalizedName = name.Trim().ToUpperInvariant(),
            Description = description?.Trim() ?? string.Empty,
            Scope = scope,
            IsSystemRole = isSystemRole,
            ApplicationCode = normalizedApp,
            IsSharedReusable = isSharedReusable
        };
    }
}

public sealed class Permission : Entity
{
    private Permission() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public PermissionScope Scope { get; private set; }
    public string Category { get; private set; } = string.Empty;

    public static Permission Create(string code, string name, PermissionScope scope, string category)
        => new()
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim(),
            Name = name.Trim(),
            Scope = scope,
            Category = category.Trim()
        };

    public bool UpdateMetadata(string name, PermissionScope scope, string category)
    {
        var trimmedName = name.Trim();
        var trimmedCategory = category.Trim();
        if (Name == trimmedName && Scope == scope && Category == trimmedCategory)
        {
            return false;
        }

        Name = trimmedName;
        Scope = scope;
        Category = trimmedCategory;
        return true;
    }
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}

public sealed class UserRole : AuditableEntity
{
    private UserRole() { }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? TenantId { get; private set; }
    public string? ResourceScopeType { get; private set; }
    public string? ResourceScopeId { get; private set; }
    public AuthorizationEffect Effect { get; private set; } = AuthorizationEffect.Allow;

    public Role? Role { get; private set; }

    public static UserRole Assign(
        Guid userId,
        Guid roleId,
        Guid? tenantId,
        string? resourceScopeType = null,
        string? resourceScopeId = null,
        AuthorizationEffect effect = AuthorizationEffect.Allow)
        => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            RoleId = roleId,
            TenantId = tenantId,
            ResourceScopeType = string.IsNullOrWhiteSpace(resourceScopeType) ? null : resourceScopeType.Trim(),
            ResourceScopeId = string.IsNullOrWhiteSpace(resourceScopeId) ? null : resourceScopeId.Trim(),
            Effect = effect
        };
}
