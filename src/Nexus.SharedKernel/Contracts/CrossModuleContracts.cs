using Nexus.SharedKernel.Events;

namespace Nexus.SharedKernel.Contracts;

public enum TenantLifecycleStatus
{
    Active = 1,
    Suspended = 2,
    Archived = 3
}

public sealed record TenantAccessInfo(
    Guid TenantId,
    string TenantCode,
    TenantLifecycleStatus TenantStatus,
    bool IsActiveMember);

public interface ITenantAccessValidator
{
    Task<TenantAccessInfo?> ValidateMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetAccessibleTenantIdsAsync(Guid userId, CancellationToken ct = default);
    Task<bool> EnsureMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task<bool> RevokeMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
}

public interface IPermissionEvaluator
{
    Task<bool> HasPermissionAsync(Guid userId, Guid? tenantId, string permissionCode, bool isPlatformScope, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, Guid? tenantId, bool isPlatformScope, CancellationToken ct = default);
    Task AssignRoleByNameAsync(Guid userId, Guid? tenantId, string roleName, CancellationToken ct = default);
}

public sealed record AuditEntryRequest(
    string Action,
    string ResourceType,
    string? ResourceId,
    Guid? TenantId,
    string Result,
    string? Reason = null,
    object? BeforeState = null,
    object? AfterState = null,
    bool IsCrossTenantPlatformAccess = false);

public interface IAuditWriter
{
    Task WriteAsync(AuditEntryRequest entry, CancellationToken ct = default);
}

public interface IEntitlementChecker
{
    Task<bool> HasFeatureAsync(Guid tenantId, string featureCode, CancellationToken ct = default);
    Task<long?> GetFeatureLimitAsync(Guid tenantId, string featureCode, CancellationToken ct = default);
    Task RecordUsageAsync(Guid tenantId, string featureCode, long quantity, CancellationToken ct = default);
}

public sealed record UserSummary(Guid UserId, string Email, string FullName, bool IsActive, bool IsLockedOut);

public interface IUserDirectoryReader
{
    Task<UserSummary?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<UserSummary?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
}

public interface IEventOutboxWriter
{
    Task EnqueueAsync(EventEnvelope envelope, CancellationToken ct = default);
}
