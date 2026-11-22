using Microsoft.AspNetCore.Identity;
using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Identity.Domain;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;
    public bool IsPlatformUser { get; set; }
    public bool MfaEnforced { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSignInAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public string? DisabledReason { get; set; }
}

public sealed class UserProfile : AuditableEntity
{
    private UserProfile() { }

    public Guid UserId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? DisplayName { get; private set; }
    public string PreferredLocale { get; private set; } = "en-US";
    public string TimeZone { get; private set; } = "UTC";
    public string? CountryCode { get; private set; }
    public string? Region { get; private set; }
    public string? AdminNotes { get; private set; }

    public static UserProfile Create(Guid userId, string fullName, string? displayName = null)
        => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FullName = fullName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? fullName.Trim() : displayName.Trim()
        };

    public void Update(
        string? fullName,
        string? displayName,
        string? preferredLocale,
        string? timeZone,
        string? firstName = null,
        string? lastName = null,
        string? countryCode = null,
        string? region = null,
        string? adminNotes = null)
    {
        if (!string.IsNullOrWhiteSpace(fullName)) FullName = fullName.Trim();
        if (firstName is not null) FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim();
        if (lastName is not null) LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();
        if (displayName is not null) DisplayName = string.IsNullOrWhiteSpace(displayName) ? FullName : displayName.Trim();
        if (!string.IsNullOrWhiteSpace(preferredLocale)) PreferredLocale = preferredLocale.Trim();
        if (!string.IsNullOrWhiteSpace(timeZone)) TimeZone = timeZone.Trim();
        if (countryCode is not null) CountryCode = string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant();
        if (region is not null) Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
        if (adminNotes is not null) AdminNotes = string.IsNullOrWhiteSpace(adminNotes) ? null : adminNotes.Trim();
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
    public bool EmailVerified { get; private set; }
    public bool IsBanned { get; private set; }
    public string SyncStatus { get; private set; } = "Synced";
    public DateTimeOffset? SyncedAt { get; private set; }
    public DateTimeOffset? LastProviderEventAt { get; private set; }
    public string? LastError { get; private set; }

    public static ExternalIdentity Create(
        Guid userId,
        string provider,
        string subject,
        string? email,
        bool emailVerified = false,
        DateTimeOffset? eventTimestamp = null)
        => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Provider = provider.Trim(),
            ProviderSubject = subject.Trim(),
            ProviderEmail = email?.Trim().ToLowerInvariant(),
            EmailVerified = emailVerified,
            IsBanned = false,
            SyncStatus = "Synced",
            SyncedAt = DateTimeOffset.UtcNow,
            LastProviderEventAt = eventTimestamp
        };

    public bool CanApplyEvent(DateTimeOffset eventTimestamp)
        => !LastProviderEventAt.HasValue || eventTimestamp >= LastProviderEventAt.Value;

    public void ApplySyncUpdate(string? email, bool emailVerified, bool isBanned, DateTimeOffset eventTimestamp, DateTimeOffset syncedAt)
    {
        if (!string.IsNullOrWhiteSpace(email)) ProviderEmail = email.Trim().ToLowerInvariant();
        EmailVerified = emailVerified;
        IsBanned = isBanned;
        SyncStatus = isBanned ? "Banned" : "Synced";
        SyncedAt = syncedAt;
        LastProviderEventAt = eventTimestamp;
        LastError = null;
    }

    public void MarkDeactivated(DateTimeOffset eventTimestamp, DateTimeOffset syncedAt)
    {
        SyncStatus = "Deactivated";
        SyncedAt = syncedAt;
        LastProviderEventAt = eventTimestamp;
        LastError = null;
    }
}

public sealed class ClerkWebhookEvent : Entity
{
    private ClerkWebhookEvent() { }

    public string SvixId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string? ProviderSubject { get; private set; }
    public DateTimeOffset EventTimestamp { get; private set; }
    public string Status { get; private set; } = "Received";
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static ClerkWebhookEvent Create(string svixId, string eventType, string? subject, DateTimeOffset eventTimestamp, DateTimeOffset receivedAt)
        => new()
        {
            Id = Guid.CreateVersion7(),
            SvixId = svixId.Trim(),
            EventType = eventType.Trim(),
            ProviderSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim(),
            EventTimestamp = eventTimestamp,
            Status = "Received",
            ReceivedAt = receivedAt
        };

    public void MarkProcessed(string status, DateTimeOffset utcNow, string? error = null)
    {
        Status = status;
        ProcessedAt = utcNow;
        ErrorMessage = error?.Length > 1000 ? error[..1000] : error;
    }
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

public enum CredentialClass
{
    CoreInternal = 1,
    SharedEcosystem = 2,
    Application = 3,
    Partner = 4
}

/// <summary>
/// Registered machine-to-machine OAuth 2.0 Client Credentials identity (e.g., NEXUS_F, NEXUS_E, NEXUS_M, Partner).
/// Secrets are stored exclusively as salted SHA-256 hashes — never plaintext. Supports optional rotation overlap grace windows.
/// </summary>
public sealed class ServiceClient : AuditableEntity
{
    private ServiceClient() { }

    public string ClientId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public CredentialClass CredentialClass { get; private set; } = CredentialClass.Application;
    public string? ApplicationCode { get; private set; }
    public Guid? TenantId { get; private set; }
    public string SecretSalt { get; private set; } = string.Empty;
    public string ClientSecretHash { get; private set; } = string.Empty;
    public string SecretPrefix { get; private set; } = string.Empty;
    public string? PreviousSecretSalt { get; private set; }
    public string? PreviousSecretHash { get; private set; }
    public DateTimeOffset? PreviousSecretValidUntil { get; private set; }
    public string AllowedScopes { get; private set; } = string.Empty; // comma-separated explicit permission codes
    public int RateLimitPerMinute { get; private set; } = 300;
    public DateTimeOffset? ExpiresAt { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? SecretRotatedAt { get; private set; }

    public IReadOnlyList<string> GetAllowedScopes()
        => AllowedScopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Result<ServiceClient> Create(
        string clientId,
        string name,
        string? applicationCode,
        Guid? tenantId,
        string secretSalt,
        string secretHash,
        string secretPrefix,
        IReadOnlyList<string> scopes,
        int rateLimitPerMinute = 300,
        CredentialClass credentialClass = CredentialClass.Application,
        DateTimeOffset? expiresAt = null)
    {
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Trim().Length > 100)
        {
            return Error.Validation("SERVICE_CLIENT_INVALID_ID", "ClientId is required (max 100 chars).");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Error.Validation("SERVICE_CLIENT_INVALID_NAME", "Client name is required (max 200 chars).");
        }

        if (scopes.Count == 0)
        {
            return Error.Validation("SERVICE_CLIENT_SCOPES_REQUIRED", "At least one explicit scope is required.");
        }

        var normApp = string.IsNullOrWhiteSpace(applicationCode) ? null : applicationCode.Trim().ToUpperInvariant();
        if (credentialClass == CredentialClass.Partner && string.IsNullOrWhiteSpace(normApp))
        {
            return Error.Validation("SERVICE_CLIENT_APP_REQUIRED", "Partner service credentials must bind to an explicit ApplicationCode.");
        }

        return new ServiceClient
        {
            Id = Guid.CreateVersion7(),
            ClientId = clientId.Trim().ToLowerInvariant(),
            Name = name.Trim(),
            CredentialClass = credentialClass,
            ApplicationCode = normApp,
            TenantId = tenantId,
            SecretSalt = secretSalt,
            ClientSecretHash = secretHash,
            SecretPrefix = secretPrefix,
            AllowedScopes = string.Join(',', scopes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s)),
            RateLimitPerMinute = Math.Clamp(rateLimitPerMinute, 10, 10_000),
            ExpiresAt = expiresAt,
            IsActive = true
        };
    }

    public void RotateSecret(string newSalt, string newHash, string newPrefix, DateTimeOffset utcNow, int overlapMinutes = 0)
    {
        if (overlapMinutes > 0)
        {
            PreviousSecretSalt = SecretSalt;
            PreviousSecretHash = ClientSecretHash;
            PreviousSecretValidUntil = utcNow.AddMinutes(Math.Clamp(overlapMinutes, 1, 1440));
        }
        else
        {
            PreviousSecretSalt = null;
            PreviousSecretHash = null;
            PreviousSecretValidUntil = null;
        }

        SecretSalt = newSalt;
        ClientSecretHash = newHash;
        SecretPrefix = newPrefix;
        SecretRotatedAt = utcNow;
    }

    public void Revoke(DateTimeOffset utcNow)
    {
        IsActive = false;
        RevokedAt = utcNow;
        PreviousSecretSalt = null;
        PreviousSecretHash = null;
        PreviousSecretValidUntil = null;
    }

    public void MarkUsed(DateTimeOffset utcNow) => LastUsedAt = utcNow;
}

/// <summary>
/// Strictly scoped API key with one-way SHA-256 verification. Never bypasses RBAC permissions or escapes application/tenant scope.
/// </summary>
public sealed class ApiKeyRecord : AuditableEntity
{
    private ApiKeyRecord() { }

    public string Name { get; private set; } = string.Empty;
    public CredentialClass CredentialClass { get; private set; } = CredentialClass.Application;
    public string? ApplicationCode { get; private set; }
    public Guid? ServiceClientId { get; private set; }
    public Guid? TenantId { get; private set; }
    public string KeyPrefix { get; private set; } = string.Empty;
    public string KeyHash { get; private set; } = string.Empty;
    public string? PreviousKeyHash { get; private set; }
    public DateTimeOffset? PreviousKeyValidUntil { get; private set; }
    public string AllowedScopes { get; private set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public Guid? OwnerUserId { get; private set; }

    public IReadOnlyList<string> GetAllowedScopes()
        => AllowedScopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Result<ApiKeyRecord> Create(
        string name,
        Guid? tenantId,
        string keyPrefix,
        string keyHash,
        IReadOnlyList<string> scopes,
        DateTimeOffset? expiresAt,
        Guid? ownerUserId,
        CredentialClass credentialClass = CredentialClass.Application,
        string? applicationCode = null,
        Guid? serviceClientId = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
        {
            return Error.Validation("API_KEY_INVALID_NAME", "API key name is required (max 150 chars).");
        }

        if (scopes.Count == 0)
        {
            return Error.Validation("API_KEY_SCOPES_REQUIRED", "API key must declare at least one explicit permission scope.");
        }

        var normApp = string.IsNullOrWhiteSpace(applicationCode) ? null : applicationCode.Trim().ToUpperInvariant();
        if (credentialClass == CredentialClass.Partner && string.IsNullOrWhiteSpace(normApp))
        {
            return Error.Validation("API_KEY_APP_REQUIRED", "Partner API keys must bind to an explicit ApplicationCode.");
        }

        return new ApiKeyRecord
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            CredentialClass = credentialClass,
            ApplicationCode = normApp,
            ServiceClientId = serviceClientId,
            TenantId = tenantId,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            AllowedScopes = string.Join(',', scopes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s)),
            ExpiresAt = expiresAt,
            IsActive = true,
            OwnerUserId = ownerUserId
        };
    }

    public void Rotate(string newPrefix, string newHash, DateTimeOffset? newExpiresAt, DateTimeOffset? utcNow = null, int overlapMinutes = 0)
    {
        if (overlapMinutes > 0 && utcNow.HasValue)
        {
            PreviousKeyHash = KeyHash;
            PreviousKeyValidUntil = utcNow.Value.AddMinutes(Math.Clamp(overlapMinutes, 1, 1440));
        }
        else
        {
            PreviousKeyHash = null;
            PreviousKeyValidUntil = null;
        }

        KeyPrefix = newPrefix;
        KeyHash = newHash;
        if (newExpiresAt.HasValue) ExpiresAt = newExpiresAt;
    }

    public void Revoke(DateTimeOffset utcNow)
    {
        IsActive = false;
        RevokedAt = utcNow;
        PreviousKeyHash = null;
        PreviousKeyValidUntil = null;
    }

    public void MarkUsed(DateTimeOffset utcNow) => LastUsedAt = utcNow;
}
