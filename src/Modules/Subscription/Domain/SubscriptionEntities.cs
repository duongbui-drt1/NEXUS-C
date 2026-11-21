using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Subscription.Domain;

public enum SubscriptionStatus
{
    Trial = 1,
    Active = 2,
    PastDue = 3,
    Cancelled = 4,
    Expired = 5
}

public sealed class Plan : AuditableEntity
{
    private Plan() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal MonthlyPrice { get; private set; }
    public string Currency { get; private set; } = "USD";
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
            IsActive = true
        };
    }
}

public sealed class PlanFeature : Entity
{
    private PlanFeature() { }

    public Guid PlanId { get; private set; }
    public string FeatureCode { get; private set; } = string.Empty; // e.g., NEXUS_F, NEXUS_E, NEXUS_M, AI_GATEWAY
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
    public string ProductCode { get; private set; } = string.Empty; // NEXUS_C, NEXUS_F, NEXUS_E, NEXUS_M
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

    public void Update(bool isEnabled, long? quotaLimit, DateTimeOffset expiresAt)
    {
        IsEnabled = isEnabled;
        QuotaLimit = quotaLimit;
        ExpiresAt = expiresAt;
    }
}

public sealed class UsageRecord : TenantScopedEntity
{
    private UsageRecord() { }

    public string FeatureCode { get; private set; } = string.Empty;
    public long Quantity { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static UsageRecord Create(Guid tenantId, string featureCode, long quantity, DateTimeOffset utcNow)
    {
        var rec = new UsageRecord
        {
            Id = Guid.CreateVersion7(),
            FeatureCode = featureCode.Trim().ToUpperInvariant(),
            Quantity = quantity,
            RecordedAt = utcNow
        };
        rec.AssignTenant(tenantId);
        return rec;
    }
}
