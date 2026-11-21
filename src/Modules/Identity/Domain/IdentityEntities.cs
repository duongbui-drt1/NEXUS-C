using Microsoft.AspNetCore.Identity;
using Nexus.SharedKernel.Primitives;

namespace Nexus.Modules.Identity.Domain;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;
    public bool IsPlatformUser { get; set; }
    public bool MfaEnforced { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisabledAt { get; set; }
    public string? DisabledReason { get; set; }
}

public sealed class UserProfile : AuditableEntity
{
    private UserProfile() { }

    public Guid UserId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public string PreferredLocale { get; private set; } = "en-US";
    public string TimeZone { get; private set; } = "UTC";

    public static UserProfile Create(Guid userId, string fullName, string? displayName = null)
        => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FullName = fullName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? fullName.Trim() : displayName.Trim()
        };

    public void Update(string fullName, string? displayName, string? preferredLocale, string? timeZone)
    {
        if (!string.IsNullOrWhiteSpace(fullName)) FullName = fullName.Trim();
        if (!string.IsNullOrWhiteSpace(displayName)) DisplayName = displayName.Trim();
        if (!string.IsNullOrWhiteSpace(preferredLocale)) PreferredLocale = preferredLocale.Trim();
        if (!string.IsNullOrWhiteSpace(timeZone)) TimeZone = timeZone.Trim();
    }
}

public sealed class UserSession : Entity
{
    private UserSession() { }

    public Guid UserId { get; private set; }
    public Guid? TenantId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }

    public bool IsRevoked => RevokedAt.HasValue;

    public static UserSession Create(Guid userId, Guid? tenantId, string? ipAddress, string? userAgent, DateTimeOffset utcNow, TimeSpan lifetime)
        => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TenantId = tenantId,
            IpAddress = ipAddress,
            UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
            CreatedAt = utcNow,
            LastSeenAt = utcNow,
            ExpiresAt = utcNow.Add(lifetime)
        };

    public void Touch(DateTimeOffset utcNow, Guid? tenantId = null)
    {
        LastSeenAt = utcNow;
        if (tenantId.HasValue)
        {
            TenantId = tenantId;
        }
    }

    public void Revoke(string reason, DateTimeOffset utcNow)
    {
        if (!RevokedAt.HasValue)
        {
            RevokedAt = utcNow;
            RevokedReason = reason;
        }
    }
}

public sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid TokenFamilyId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string? RevokedReason { get; private set; }
    public uint RowVersion { get; private set; }

    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsRotated => ReplacedByTokenId.HasValue;

    public static RefreshToken Create(
        Guid userId,
        Guid? tenantId,
        Guid sessionId,
        Guid tokenFamilyId,
        string tokenHash,
        DateTimeOffset utcNow,
        TimeSpan lifetime)
        => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TenantId = tenantId,
            SessionId = sessionId,
            TokenFamilyId = tokenFamilyId,
            TokenHash = tokenHash,
            CreatedAt = utcNow,
            ExpiresAt = utcNow.Add(lifetime)
        };

    public void MarkRotated(Guid replacementTokenId, DateTimeOffset utcNow)
    {
        ReplacedByTokenId = replacementTokenId;
        RevokedAt = utcNow;
        RevokedReason = "Rotated";
    }

    public void Revoke(string reason, DateTimeOffset utcNow)
    {
        if (!RevokedAt.HasValue)
        {
            RevokedAt = utcNow;
            RevokedReason = reason;
        }
    }
}

public sealed class ExternalIdentity : AuditableEntity
{
    private ExternalIdentity() { }

    public Guid UserId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string ProviderSubject { get; private set; } = string.Empty;
    public string? ProviderEmail { get; private set; }

    public static ExternalIdentity Create(Guid userId, string provider, string subject, string? email)
        => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Provider = provider.Trim(),
            ProviderSubject = subject.Trim(),
            ProviderEmail = email?.Trim().ToLowerInvariant()
        };
}

public sealed class LoginAttempt : Entity
{
    private LoginAttempt() { }

    public Guid? UserId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public bool Succeeded { get; private set; }
    public string? FailureReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset AttemptedAt { get; private set; }

    public static LoginAttempt Record(Guid? userId, string email, bool succeeded, string? failureReason, string? ip, string? ua, DateTimeOffset utcNow)
        => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Email = email.Trim().ToLowerInvariant(),
            Succeeded = succeeded,
            FailureReason = failureReason,
            IpAddress = ip,
            UserAgent = ua?.Length > 500 ? ua[..500] : ua,
            AttemptedAt = utcNow
        };
}
