using Microsoft.EntityFrameworkCore;
using Nexus.Modules.Subscription.Domain;
using Nexus.Modules.Subscription.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Events;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Subscription.Application;

public sealed record PlanFeatureInput(string FeatureCode, bool IsEnabled = true, long? QuotaLimit = null);
public sealed record CreatePlanRequest(string Code, string Name, string Description, decimal MonthlyPrice, string Currency, IReadOnlyList<PlanFeatureInput> Features);
public sealed record PlanDto(Guid Id, string Code, string Name, string Description, decimal MonthlyPrice, string Currency, bool IsActive, IReadOnlyList<PlanFeatureInput> Features);
public sealed record AssignSubscriptionRequest(Guid TenantId, Guid PlanId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int SeatCount = 10);
public sealed record EntitlementDto(string FeatureCode, bool IsEnabled, long? QuotaLimit, DateTimeOffset ExpiresAt);
public sealed record TenantSubscriptionDto(Guid Id, Guid TenantId, Guid PlanId, string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<EntitlementDto> Entitlements);

public interface ISubscriptionService
{
    Task<Result<PlanDto>> CreatePlanAsync(CreatePlanRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PlanDto>>> ListPlansAsync(CancellationToken ct = default);
    Task<Result<TenantSubscriptionDto>> AssignPlanToTenantAsync(AssignSubscriptionRequest request, CancellationToken ct = default);
    Task<Result<TenantSubscriptionDto>> GetCurrentTenantSubscriptionAsync(CancellationToken ct = default);
    Task<Result<bool>> CheckEntitlementAsync(Guid tenantId, string featureCode, CancellationToken ct = default);
}

public sealed class SubscriptionService(
    SubscriptionDbContext db,
    ITenantContext tenantContext,
    ICorrelationContext correlationContext,
    ISystemClock clock,
    IAuditWriter auditWriter) : ISubscriptionService, IEntitlementChecker
{
    public async Task<Result<PlanDto>> CreatePlanAsync(CreatePlanRequest request, CancellationToken ct = default)
    {
        var created = Plan.Create(request.Code, request.Name, request.Description, request.MonthlyPrice, request.Currency);
        if (created.IsFailure) return Result.Failure<PlanDto>(created.Error);

        var plan = created.Value;
        if (await db.Plans.AsNoTracking().AnyAsync(p => p.Code == plan.Code, ct))
        {
            return Error.Conflict("PLAN_DUPLICATE", $"Plan '{plan.Code}' already exists.");
        }

        db.Plans.Add(plan);
        foreach (var feat in request.Features)
        {
            db.PlanFeatures.Add(PlanFeature.Create(plan.Id, feat.FeatureCode, feat.IsEnabled, feat.QuotaLimit));
        }

        await db.SaveChangesAsync(ct);

        return new PlanDto(plan.Id, plan.Code, plan.Name, plan.Description, plan.MonthlyPrice, plan.Currency, plan.IsActive, request.Features);
    }

    public async Task<Result<IReadOnlyList<PlanDto>>> ListPlansAsync(CancellationToken ct = default)
    {
        var plans = await db.Plans.AsNoTracking().Include(p => p.Features).OrderBy(p => p.MonthlyPrice).ToListAsync(ct);
        return plans.Select(p => new PlanDto(
            p.Id, p.Code, p.Name, p.Description, p.MonthlyPrice, p.Currency, p.IsActive,
            p.Features.Select(f => new PlanFeatureInput(f.FeatureCode, f.IsEnabled, f.QuotaLimit)).ToList())).ToList();
    }

    public async Task<Result<TenantSubscriptionDto>> AssignPlanToTenantAsync(AssignSubscriptionRequest request, CancellationToken ct = default)
    {
        var plan = await db.Plans.AsNoTracking().Include(p => p.Features).FirstOrDefaultAsync(p => p.Id == request.PlanId, ct);
        if (plan is null) return Error.NotFound("PLAN_NOT_FOUND", "Subscription plan was not found.");

        using var scope = tenantContext.BeginScope(request.TenantId);

        var activeSubs = await db.Subscriptions
            .Where(s => s.TenantId == request.TenantId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .ToListAsync(ct);

        foreach (var old in activeSubs)
        {
            old.Cancel(clock.UtcNow);
        }

        var createdSub = TenantSubscription.Create(request.TenantId, plan.Id, request.StartsAt, request.EndsAt);
        if (createdSub.IsFailure) return Result.Failure<TenantSubscriptionDto>(createdSub.Error);

        var sub = createdSub.Value;
        db.Subscriptions.Add(sub);
        db.SubscriptionItems.Add(SubscriptionItem.Create(request.TenantId, sub.Id, "NEXUS_C", request.SeatCount, plan.MonthlyPrice));

        var existingEntitlements = await db.Entitlements.Where(e => e.TenantId == request.TenantId).ToDictionaryAsync(e => e.FeatureCode, ct);
        var resultEntitlements = new List<EntitlementDto>();

        foreach (var feature in plan.Features)
        {
            if (existingEntitlements.TryGetValue(feature.FeatureCode, out var ent))
            {
                ent.Update(feature.IsEnabled, feature.QuotaLimit, sub.EndsAt);
            }
            else
            {
                ent = Entitlement.Create(request.TenantId, sub.Id, feature.FeatureCode, feature.IsEnabled, feature.QuotaLimit, sub.EndsAt);
                db.Entitlements.Add(ent);
            }

            resultEntitlements.Add(new EntitlementDto(feature.FeatureCode, feature.IsEnabled, feature.QuotaLimit, sub.EndsAt));
        }

        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.SubscriptionActivatedV1,
            request.TenantId,
            correlationContext.CorrelationId,
            new { sub.Id, request.TenantId, PlanCode = plan.Code, sub.StartsAt, sub.EndsAt },
            occurredAt: clock.UtcNow));

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "subscription.assign", "TenantSubscription", sub.Id.ToString("D"), request.TenantId, "Success",
            AfterState: new { PlanCode = plan.Code, sub.StartsAt, sub.EndsAt }, IsCrossTenantPlatformAccess: true), ct);

        return new TenantSubscriptionDto(sub.Id, sub.TenantId, sub.PlanId, sub.Status.ToString(), sub.StartsAt, sub.EndsAt, resultEntitlements);
    }

    public async Task<Result<TenantSubscriptionDto>> GetCurrentTenantSubscriptionAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var sub = await db.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (sub is null) return Error.NotFound("SUBSCRIPTION_NOT_FOUND", "No subscription found for this tenant.");

        var entitlements = await db.Entitlements.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => new EntitlementDto(e.FeatureCode, e.IsEnabled, e.QuotaLimit, e.ExpiresAt))
            .ToListAsync(ct);

        return new TenantSubscriptionDto(sub.Id, sub.TenantId, sub.PlanId, sub.Status.ToString(), sub.StartsAt, sub.EndsAt, entitlements);
    }

    public async Task<Result<bool>> CheckEntitlementAsync(Guid tenantId, string featureCode, CancellationToken ct = default)
        => await HasFeatureAsync(tenantId, featureCode, ct);

    public async Task<bool> HasFeatureAsync(Guid tenantId, string featureCode, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var normalized = featureCode.Trim().ToUpperInvariant();
        var now = clock.UtcNow;

        var ent = await db.Entitlements.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.FeatureCode == normalized, ct);

        return ent is not null && ent.IsEnabled && ent.ExpiresAt > now;
    }

    public async Task<long?> GetFeatureLimitAsync(Guid tenantId, string featureCode, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var normalized = featureCode.Trim().ToUpperInvariant();
        var ent = await db.Entitlements.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.FeatureCode == normalized, ct);
        return ent?.QuotaLimit;
    }

    public async Task RecordUsageAsync(Guid tenantId, string featureCode, long quantity, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        db.UsageRecords.Add(UsageRecord.Create(tenantId, featureCode, quantity, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }
}
