using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Subscription.Domain;

public enum SubscriptionStatus
{
    Trial = 1,
    Active = 2,
    PastDue = 3,
    Cancelled = 4,
    Expired = 5,
    Suspended = 6
}

public enum ApplicationStatus
{
    Registered = 1,
    Active = 2,
    Deprecated = 3,
    Retired = 4
}

public sealed class RegisteredApplication : AuditableEntity
{
    private RegisteredApplication() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string CurrentVersion { get; private set; } = "1.0.0";
    public string ReleaseChannel { get; private set; } = "Stable";
    public ApplicationStatus Status { get; private set; } = ApplicationStatus.Registered;
    public string MinPlatformVersion { get; private set; } = "1.0.0";
    public string RequiredCapabilities { get; private set; } = string.Empty;
    public string ModuleDependencies { get; private set; } = string.Empty;
    public string? CompatibilityMetadataJson { get; private set; }

    public IReadOnlyList<string> GetRequiredCapabilities()
        => RequiredCapabilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IReadOnlyList<string> GetModuleDependencies()
        => ModuleDependencies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Result<RegisteredApplication> Create(
        string code,
        string name,
        string description,
        string currentVersion,
        string releaseChannel,
        ApplicationStatus status,
        string minPlatformVersion,
        IReadOnlyList<string>? requiredCapabilities = null,
        IReadOnlyList<string>? moduleDependencies = null,
        string? compatibilityMetadataJson = null)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32)
        {
            return Error.Validation("APP_INVALID_CODE", "Application code is required (max 32 chars).");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Error.Validation("APP_INVALID_NAME", "Application name is required (max 200 chars).");
        }

        return new RegisteredApplication
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            CurrentVersion = string.IsNullOrWhiteSpace(currentVersion) ? "1.0.0" : currentVersion.Trim(),
            ReleaseChannel = string.IsNullOrWhiteSpace(releaseChannel) ? "Stable" : releaseChannel.Trim(),
            Status = status,
            MinPlatformVersion = string.IsNullOrWhiteSpace(minPlatformVersion) ? "1.0.0" : minPlatformVersion.Trim(),
            RequiredCapabilities = requiredCapabilities is null ? string.Empty : string.Join(',', requiredCapabilities.Distinct(StringComparer.OrdinalIgnoreCase)),
            ModuleDependencies = moduleDependencies is null ? string.Empty : string.Join(',', moduleDependencies.Select(d => d.Trim().ToUpperInvariant()).Distinct()),
            CompatibilityMetadataJson = string.IsNullOrWhiteSpace(compatibilityMetadataJson) ? null : compatibilityMetadataJson.Trim()
        };
    }

    public Result Update(
        string name,
        string description,
        string currentVersion,
        string releaseChannel,
        ApplicationStatus status,
        string minPlatformVersion,
        IReadOnlyList<string>? requiredCapabilities,
        IReadOnlyList<string>? moduleDependencies,
        string? compatibilityMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure(Error.Validation("APP_INVALID_NAME", "Application name is required (max 200 chars)."));
        }

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        CurrentVersion = string.IsNullOrWhiteSpace(currentVersion) ? CurrentVersion : currentVersion.Trim();
        ReleaseChannel = string.IsNullOrWhiteSpace(releaseChannel) ? ReleaseChannel : releaseChannel.Trim();
        Status = status;
        MinPlatformVersion = string.IsNullOrWhiteSpace(minPlatformVersion) ? MinPlatformVersion : minPlatformVersion.Trim();
        if (requiredCapabilities is not null)
        {
            RequiredCapabilities = string.Join(',', requiredCapabilities.Distinct(StringComparer.OrdinalIgnoreCase));
        }
        if (moduleDependencies is not null)
        {
            ModuleDependencies = string.Join(',', moduleDependencies.Select(d => d.Trim().ToUpperInvariant()).Distinct());
        }
        CompatibilityMetadataJson = string.IsNullOrWhiteSpace(compatibilityMetadataJson) ? CompatibilityMetadataJson : compatibilityMetadataJson.Trim();
        return Result.Success();
    }
}

public sealed class TenantApplicationActivation : TenantScopedEntity
{
    private TenantApplicationActivation() { }

    public string ApplicationCode { get; private set; } = string.Empty;
    public bool IsActivated { get; private set; } = true;
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public Guid? LastChangedByUserId { get; private set; }

    public static TenantApplicationActivation ActivateNew(Guid tenantId, string applicationCode, Guid? actorUserId, DateTimeOffset utcNow)
    {
        var act = new TenantApplicationActivation
        {
            Id = Guid.CreateVersion7(),
            ApplicationCode = applicationCode.Trim().ToUpperInvariant(),
            IsActivated = true,
            ActivatedAt = utcNow,
            LastChangedByUserId = actorUserId
        };
        act.AssignTenant(tenantId);
        return act;
    }

    public void Activate(Guid? actorUserId, DateTimeOffset utcNow)
    {
        IsActivated = true;
        ActivatedAt = utcNow;
        DeactivatedAt = null;
        LastChangedByUserId = actorUserId;
    }

    public void Deactivate(Guid? actorUserId, DateTimeOffset utcNow)
    {
        IsActivated = false;
        DeactivatedAt = utcNow;
        LastChangedByUserId = actorUserId;
    }
}

public sealed class Plan : AuditableEntity
{
    private Plan() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal MonthlyPrice { get; private set; }
    public string Currency { get; private set; } = "USD";
    public int Version { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;

    public ICollection<PlanFeature> Features { get; private set; } = [];

    public static Result<Plan> Create(string code, string name, string description, decimal monthlyPrice, string currency)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("PLAN_INVALID_INPUT", "Plan code and name are required.");
        }

        if (monthlyPrice < 0m)
        {
            return Error.Validation("PLAN_INVALID_PRICE", "MonthlyPrice cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            return Error.Validation("PLAN_INVALID_CURRENCY", "Currency must be a 3-letter ISO code.");
        }

        return new Plan
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            MonthlyPrice = decimal.Round(monthlyPrice, 2, MidpointRounding.AwayFromZero),
            Currency = currency.Trim().ToUpperInvariant(),
            Version = 1,
            IsActive = true
        };
    }
}

public sealed class PlanFeature : Entity
{
    private PlanFeature() { }

    public Guid PlanId { get; private set; }
    public string FeatureCode { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; } = true;
    public long? QuotaLimit { get; private set; }

    public static PlanFeature Create(Guid planId, string featureCode, bool isEnabled, long? quotaLimit)
        => new()
        {
            Id = Guid.CreateVersion7(),
            PlanId = planId,
            FeatureCode = featureCode.Trim().ToUpperInvariant(),
            IsEnabled = isEnabled,
            QuotaLimit = quotaLimit
        };
}

public sealed class TenantSubscription : TenantScopedEntity
{
    private TenantSubscription() { }

    public Guid PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; } = SubscriptionStatus.Active;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public static Result<TenantSubscription> Create(Guid tenantId, Guid planId, DateTimeOffset startsAt, DateTimeOffset endsAt, SubscriptionStatus status = SubscriptionStatus.Active)
    {
        if (endsAt <= startsAt)
        {
            return Error.Validation("SUBSCRIPTION_INVALID_PERIOD", "Subscription end date must be after start date.");
        }

        var sub = new TenantSubscription
        {
            Id = Guid.CreateVersion7(),
            PlanId = planId,
            Status = status,
            StartsAt = startsAt,
            EndsAt = endsAt
        };
        sub.AssignTenant(tenantId);
        return sub;
    }

    public bool IsCurrentlyValid(DateTimeOffset utcNow)
        => Status is SubscriptionStatus.Active or SubscriptionStatus.Trial && StartsAt <= utcNow && EndsAt > utcNow;

    public void Expire() => Status = SubscriptionStatus.Expired;
    public void Suspend() => Status = SubscriptionStatus.Suspended;
    public void Activate() => Status = SubscriptionStatus.Active;
    public void Cancel(DateTimeOffset utcNow)
    {
        Status = SubscriptionStatus.Cancelled;
        CancelledAt = utcNow;
    }
}

public sealed class SubscriptionItem : TenantScopedEntity
{
    private SubscriptionItem() { }

    public Guid SubscriptionId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public int SeatCount { get; private set; }
    public decimal UnitPrice { get; private set; }

    public static SubscriptionItem Create(Guid tenantId, Guid subscriptionId, string productCode, int seatCount, decimal unitPrice)
    {
        var item = new SubscriptionItem
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscriptionId,
            ProductCode = productCode.Trim().ToUpperInvariant(),
            SeatCount = Math.Max(1, seatCount),
            UnitPrice = decimal.Round(Math.Max(0m, unitPrice), 2, MidpointRounding.AwayFromZero)
        };
        item.AssignTenant(tenantId);
        return item;
    }
}

public sealed class Entitlement : TenantScopedEntity
{
    private Entitlement() { }

    public Guid SubscriptionId { get; private set; }
    public string FeatureCode { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public long? QuotaLimit { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static Entitlement Create(Guid tenantId, Guid subscriptionId, string featureCode, bool isEnabled, long? quotaLimit, DateTimeOffset expiresAt)
    {
        var e = new Entitlement
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscriptionId,
            FeatureCode = featureCode.Trim().ToUpperInvariant(),
            IsEnabled = isEnabled,
            QuotaLimit = quotaLimit,
            ExpiresAt = expiresAt
        };
        e.AssignTenant(tenantId);
        return e;
    }

    public void Update(Guid subscriptionId, bool isEnabled, long? quotaLimit, DateTimeOffset expiresAt)
    {
        SubscriptionId = subscriptionId;
        IsEnabled = isEnabled;
        QuotaLimit = quotaLimit;
        ExpiresAt = expiresAt;
    }

    public void Revoke(DateTimeOffset utcNow)
    {
        IsEnabled = false;
        ExpiresAt = utcNow;
    }
}

public sealed class UsageRecord : TenantScopedEntity
{
    private UsageRecord() { }

    public string FeatureCode { get; private set; } = string.Empty;
    public long Quantity { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static UsageRecord Create(Guid tenantId, string featureCode, long quantity, DateTimeOffset utcNow, string? idempotencyKey = null)
    {
        var rec = new UsageRecord
        {
            Id = Guid.CreateVersion7(),
            FeatureCode = featureCode.Trim().ToUpperInvariant(),
            Quantity = Math.Max(0, quantity),
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            RecordedAt = utcNow
        };
        rec.AssignTenant(tenantId);
        return rec;
    }
}
