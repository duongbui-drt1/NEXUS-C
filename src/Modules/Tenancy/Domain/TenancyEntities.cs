using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Tenancy.Domain;

public enum MembershipStatus
{
    Invited = 1,
    Active = 2,
    Revoked = 3
}

public sealed class Tenant : AuditableEntity
{
    private Tenant() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public TenantLifecycleStatus Status { get; private set; } = TenantLifecycleStatus.Active;
    public string TimeZone { get; private set; } = "UTC";
    public string DefaultCurrency { get; private set; } = "USD";
    public string CountryCode { get; private set; } = "US";
    public string? SuspendedReason { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }

    public static Result<Tenant> Create(
        string code,
        string name,
        string slug,
        string timeZone,
        string defaultCurrency,
        string countryCode)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32)
        {
            return Error.Validation("TENANT_INVALID_CODE", "Tenant code is required and must be at most 32 characters.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Error.Validation("TENANT_INVALID_NAME", "Tenant name is required and must be at most 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(slug) || slug.Trim().Length > 100)
        {
            return Error.Validation("TENANT_INVALID_SLUG", "Tenant slug is required and must be at most 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(defaultCurrency) || defaultCurrency.Trim().Length != 3)
        {
            return Error.Validation("TENANT_INVALID_CURRENCY", "DefaultCurrency must be a 3-letter ISO currency code.");
        }

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
        {
            return Error.Validation("TENANT_INVALID_COUNTRY", "CountryCode must be a 2-letter ISO country code.");
        }

        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Status = TenantLifecycleStatus.Active,
            TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone.Trim(),
            DefaultCurrency = defaultCurrency.Trim().ToUpperInvariant(),
            CountryCode = countryCode.Trim().ToUpperInvariant()
        };
    }

    public Result Update(string name, string timeZone, string defaultCurrency, string countryCode)
    {
        if (Status == TenantLifecycleStatus.Archived)
        {
            return Result.Failure(Error.BusinessRule("TENANT_ARCHIVED", "Archived tenants cannot be updated."));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure(Error.Validation("TENANT_INVALID_NAME", "Tenant name is required and must be at most 200 characters."));
        }

        if (string.IsNullOrWhiteSpace(defaultCurrency) || defaultCurrency.Trim().Length != 3)
        {
            return Result.Failure(Error.Validation("TENANT_INVALID_CURRENCY", "DefaultCurrency must be a 3-letter ISO currency code."));
        }

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
        {
            return Result.Failure(Error.Validation("TENANT_INVALID_COUNTRY", "CountryCode must be a 2-letter ISO country code."));
        }

        Name = name.Trim();
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? TimeZone : timeZone.Trim();
        DefaultCurrency = defaultCurrency.Trim().ToUpperInvariant();
        CountryCode = countryCode.Trim().ToUpperInvariant();
        return Result.Success();
    }

    public Result Suspend(string reason, DateTimeOffset utcNow)
    {
        if (Status == TenantLifecycleStatus.Suspended)
        {
            return Result.Failure(Error.Conflict("TENANT_ALREADY_SUSPENDED", "Tenant is already suspended."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("TENANT_SUSPEND_REASON_REQUIRED", "A suspension reason is required."));
        }

        Status = TenantLifecycleStatus.Suspended;
        SuspendedReason = reason.Trim();
        SuspendedAt = utcNow;
        return Result.Success();
    }

    public Result Activate()
    {
        if (Status == TenantLifecycleStatus.Active)
        {
            return Result.Failure(Error.Conflict("TENANT_ALREADY_ACTIVE", "Tenant is already active."));
        }

        Status = TenantLifecycleStatus.Active;
        SuspendedReason = null;
        SuspendedAt = null;
        return Result.Success();
    }
}

public sealed class UserTenantMembership : TenantScopedEntity
{
    private UserTenantMembership() { }

    public Guid UserId { get; private set; }
    public MembershipStatus Status { get; private set; } = MembershipStatus.Active;
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static UserTenantMembership Create(Guid userId, Guid tenantId, DateTimeOffset utcNow, MembershipStatus status = MembershipStatus.Active)
    {
        var membership = new UserTenantMembership
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Status = status,
            JoinedAt = utcNow
        };
        membership.AssignTenant(tenantId);
        return membership;
    }

    public void Activate(DateTimeOffset utcNow)
    {
        Status = MembershipStatus.Active;
        RevokedAt = null;
        if (JoinedAt == default)
        {
            JoinedAt = utcNow;
        }
    }

    public void Revoke(DateTimeOffset utcNow)
    {
        Status = MembershipStatus.Revoked;
        RevokedAt = utcNow;
    }
}
