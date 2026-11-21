namespace Nexus.SharedKernel.Context;

public interface ITenantContext
{
    Guid? TenantId { get; }
    bool HasTenant { get; }
    bool IsPlatformBypass { get; }
    string? BypassReason { get; }

    void SetTenant(Guid tenantId);
    IDisposable BeginScope(Guid? tenantId, bool platformBypass = false, string? bypassReason = null);
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? SessionId { get; }
    Guid? TenantId { get; }
    string? Email { get; }
    string? Scope { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformUser { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}

public interface ICorrelationContext
{
    string CorrelationId { get; }
    void SetCorrelationId(string correlationId);
}

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
