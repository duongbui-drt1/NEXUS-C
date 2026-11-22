using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Nexus.Modules.Subscription.Domain;
using Nexus.Modules.Subscription.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Events;
using Nexus.SharedKernel.Results;
using Npgsql;

namespace Nexus.Modules.Subscription.Application;

public sealed record RegisteredApplicationDto(
    Guid Id,
    string Code,
    string Name,
    string Description,
    string CurrentVersion,
    string ReleaseChannel,
    string Status,
    string MinPlatformVersion,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> ModuleDependencies,
    string? CompatibilityMetadataJson);

public sealed record RegisterApplicationRequest(
    string Code,
    string Name,
    string Description,
    string CurrentVersion = "1.0.0",
    string ReleaseChannel = "Stable",
    ApplicationStatus Status = ApplicationStatus.Registered,
    string MinPlatformVersion = "1.0.0",
    IReadOnlyList<string>? RequiredCapabilities = null,
    IReadOnlyList<string>? ModuleDependencies = null,
    string? CompatibilityMetadataJson = null);

public sealed record UpdateApplicationRequest(
    string Name,
    string Description,
    string CurrentVersion,
    string ReleaseChannel,
    ApplicationStatus Status,
    string MinPlatformVersion,
    IReadOnlyList<string>? RequiredCapabilities = null,
    IReadOnlyList<string>? ModuleDependencies = null,
    string? CompatibilityMetadataJson = null);

public sealed record TenantApplicationDto(
    string ApplicationCode,
    string ApplicationName,
    string CurrentVersion,
    string RegistryStatus,
    bool IsEntitled,
    bool IsActivated,
    string? EntitlementReason,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DeactivatedAt);

public sealed record PlanFeatureInput(string FeatureCode, bool IsEnabled = true, long? QuotaLimit = null);
public sealed record CreatePlanRequest(string Code, string Name, string Description, decimal MonthlyPrice, string Currency, IReadOnlyList<PlanFeatureInput> Features);
public sealed record PlanDto(Guid Id, string Code, string Name, string Description, decimal MonthlyPrice, string Currency, bool IsActive, IReadOnlyList<PlanFeatureInput> Features, int Version = 1);
public sealed record AssignSubscriptionRequest(Guid TenantId, Guid PlanId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int SeatCount = 10);
public sealed record EntitlementDto(string FeatureCode, bool IsEnabled, long? QuotaLimit, DateTimeOffset ExpiresAt);
public sealed record TenantSubscriptionDto(Guid Id, Guid TenantId, Guid PlanId, string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<EntitlementDto> Entitlements);

public sealed record EntitlementEvaluationDto(
    Guid TenantId,
    string FeatureCode,
    bool IsEntitled,
    long? QuotaLimit,
    long CurrentUsage,
    string SubscriptionStatus,
    bool? FeatureFlagOverride,
    DateTimeOffset? ExpiresAt,
    string Reason);

public sealed record RecordUsageRequest(string FeatureCode, long Quantity, string? IdempotencyKey = null);
public sealed record UsageSummaryDto(string FeatureCode, long TotalQuantity, int RecordCount, DateTimeOffset? LastRecordedAt);

public interface ISubscriptionService
{
    Task SeedApplicationsAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<RegisteredApplicationDto>>> ListApplicationsAsync(CancellationToken ct = default);
    Task<Result<RegisteredApplicationDto>> RegisterApplicationAsync(RegisterApplicationRequest request, CancellationToken ct = default);
    Task<Result<RegisteredApplicationDto>> UpdateApplicationAsync(string code, UpdateApplicationRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TenantApplicationDto>>> ListTenantApplicationsAsync(CancellationToken ct = default);
    Task<Result<TenantApplicationDto>> SetTenantApplicationActivationAsync(string applicationCode, bool activate, Guid? targetTenantId = null, CancellationToken ct = default);

    Task<Result<PlanDto>> CreatePlanAsync(CreatePlanRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PlanDto>>> ListPlansAsync(CancellationToken ct = default);
    Task<Result<TenantSubscriptionDto>> AssignPlanToTenantAsync(AssignSubscriptionRequest request, CancellationToken ct = default);
    Task<Result<TenantSubscriptionDto>> UpdateSubscriptionStatusAsync(Guid tenantId, SubscriptionStatus newStatus, string? reason = null, CancellationToken ct = default);
    Task<Result<TenantSubscriptionDto>> GetCurrentTenantSubscriptionAsync(CancellationToken ct = default);
    Task<Result<bool>> CheckEntitlementAsync(Guid tenantId, string featureCode, CancellationToken ct = default);
    Task<Result<EntitlementEvaluationDto>> EvaluateEntitlementAsync(Guid tenantId, string featureCode, CancellationToken ct = default);
    Task<Result<IReadOnlyList<EntitlementEvaluationDto>>> ListCurrentTenantEntitlementsAsync(CancellationToken ct = default);
    Task<Result<UsageSummaryDto>> RecordUsageIdempotentAsync(RecordUsageRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<UsageSummaryDto>>> ListUsageSummaryAsync(CancellationToken ct = default);
}

public sealed class SubscriptionService(
    SubscriptionDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IFeatureFlagEvaluator featureFlagEvaluator,
    IDistributedCache cache,
    ICorrelationContext correlationContext,
    ISystemClock clock,
    IAuditWriter auditWriter) : ISubscriptionService, IEntitlementChecker
{
    private static string BuildEntitlementCacheKey(Guid tenantId, string featureCode)
        => $"ent:v1:{tenantId:N}:{featureCode.Trim().ToUpperInvariant()}";

    public async Task SeedApplicationsAsync(CancellationToken ct = default)
    {
        var existing = await db.Applications.Select(a => a.Code).ToListAsync(ct);
        var set = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seeds = new (string Code, string Name, string Desc, string Channel, ApplicationStatus Status, string[] Caps, string[] Deps)[]
        {
            ("NEXUS_C", "NEXUS C — Center", "Shared multi-tenant foundation platform: Identity, Tenancy, Organization, RBAC/ABAC, Workflow, Audit, Events, Integrations, Registry & Entitlements.", "Core", ApplicationStatus.Active,
                ["identity", "tenancy", "organization", "access-control", "workflow", "audit", "events", "entitlements"], []),
            ("NEXUS_F", "NEXUS F — Finance", "Enterprise finance, general ledger, tax compliance, invoicing and financial control.", "Stable", ApplicationStatus.Registered,
                ["identity", "tenancy", "organization", "workflow", "audit", "events", "files"], ["NEXUS_C"]),
            ("NEXUS_E", "NEXUS E — Express", "Logistics, warehouse (WMS), transportation (TMS), fleet and shipment operations.", "Stable", ApplicationStatus.Registered,
                ["identity", "tenancy", "organization", "workflow", "audit", "events", "integrations"], ["NEXUS_C"]),
            ("NEXUS_M", "NEXUS M — Management", "Executive management, enterprise governance, business planning and KPI analytics.", "Stable", ApplicationStatus.Registered,
                ["identity", "tenancy", "organization", "audit", "events", "configuration"], ["NEXUS_C"])
        };

        foreach (var s in seeds)
        {
            if (set.Contains(s.Code)) continue;
            var app = RegisteredApplication.Create(
                s.Code, s.Name, s.Desc, "1.0.0", s.Channel, s.Status, "1.0.0", s.Caps, s.Deps,
                "{\"contractVersion\":\"1.0\",\"deploymentHealthTrackedSeparately\":true}").Value;
            db.Applications.Add(app);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<Result<IReadOnlyList<RegisteredApplicationDto>>> ListApplicationsAsync(CancellationToken ct = default)
    {
        var apps = await db.Applications.AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct);
        return apps.Select(MapApp).ToList();
    }

    public async Task<Result<RegisteredApplicationDto>> RegisterApplicationAsync(RegisterApplicationRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsPlatformUser)
        {
            return Error.Forbidden("APP_REGISTRY_PLATFORM_ONLY", "Only platform administrators may register ecosystem applications.");
        }

        var created = RegisteredApplication.Create(
            request.Code, request.Name, request.Description, request.CurrentVersion, request.ReleaseChannel,
            request.Status, request.MinPlatformVersion, request.RequiredCapabilities, request.ModuleDependencies,
            request.CompatibilityMetadataJson);
        if (created.IsFailure) return Result.Failure<RegisteredApplicationDto>(created.Error);

        var app = created.Value;
        if (await db.Applications.AsNoTracking().AnyAsync(a => a.Code == app.Code, ct))
        {
            return Error.Conflict("APP_DUPLICATE", $"Application '{app.Code}' is already registered.");
        }

        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "platform.application.register", "RegisteredApplication", app.Code, null, "Success",
            AfterState: MapApp(app), IsCrossTenantPlatformAccess: true), ct);

        return MapApp(app);
    }

    public async Task<Result<RegisteredApplicationDto>> UpdateApplicationAsync(string code, UpdateApplicationRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsPlatformUser)
        {
            return Error.Forbidden("APP_REGISTRY_PLATFORM_ONLY", "Only platform administrators may update application registry metadata.");
        }

        var normalized = code.Trim().ToUpperInvariant();
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Code == normalized, ct);
        if (app is null) return Error.NotFound("APP_NOT_FOUND", $"Application '{normalized}' was not found.");

        var before = MapApp(app);
        var res = app.Update(request.Name, request.Description, request.CurrentVersion, request.ReleaseChannel,
            request.Status, request.MinPlatformVersion, request.RequiredCapabilities, request.ModuleDependencies,
            request.CompatibilityMetadataJson);
        if (res.IsFailure) return Result.Failure<RegisteredApplicationDto>(res.Error);

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "platform.application.update", "RegisteredApplication", app.Code, null, "Success",
            BeforeState: before, AfterState: MapApp(app), IsCrossTenantPlatformAccess: true), ct);

        return MapApp(app);
    }

    public async Task<Result<IReadOnlyList<TenantApplicationDto>>> ListTenantApplicationsAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var apps = await db.Applications.AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct);
        var activations = await db.TenantApplications.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .ToDictionaryAsync(a => a.ApplicationCode, ct);

        var list = new List<TenantApplicationDto>();
        foreach (var app in apps)
        {
            var eval = await EvaluateEntitlementInternalAsync(tenantId, app.Code, ct);
            activations.TryGetValue(app.Code, out var act);
            var isActivated = app.Code == "NEXUS_C" || (act?.IsActivated ?? eval.IsEntitled);

            list.Add(new TenantApplicationDto(
                app.Code,
                app.Name,
                app.CurrentVersion,
                app.Status.ToString(),
                app.Code == "NEXUS_C" || eval.IsEntitled,
                isActivated,
                app.Code == "NEXUS_C" ? "CORE_FOUNDATION" : eval.Reason,
                act?.ActivatedAt,
                act?.DeactivatedAt));
        }

        return list;
    }

    public async Task<Result<TenantApplicationDto>> SetTenantApplicationActivationAsync(
        string applicationCode,
        bool activate,
        Guid? targetTenantId = null,
        CancellationToken ct = default)
    {
        var normalized = applicationCode.Trim().ToUpperInvariant();
        if (normalized == "NEXUS_C" && !activate)
        {
            return Error.BusinessRule("APP_CORE_CANNOT_DEACTIVATE", "NEXUS_C core platform cannot be deactivated.");
        }

        Guid effectiveTenantId;
        if (currentUser.IsPlatformUser)
        {
            effectiveTenantId = targetTenantId ?? tenantContext.TenantId ?? Guid.Empty;
            if (effectiveTenantId == Guid.Empty)
            {
                return Error.Validation("TENANT_ID_REQUIRED", "Target tenantId is required.");
            }
        }
        else
        {
            if (!tenantContext.HasTenant || (targetTenantId.HasValue && targetTenantId.Value != tenantContext.TenantId!.Value))
            {
                return Error.Forbidden("CROSS_TENANT_APP_ACCESS_DENIED", "Cannot modify application activation for another tenant.");
            }
            effectiveTenantId = tenantContext.TenantId!.Value;
        }

        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Code == normalized, ct);
        if (app is null) return Error.NotFound("APP_NOT_FOUND", $"Application '{normalized}' is not registered.");
        if (activate && app.Status == ApplicationStatus.Retired)
        {
            return Error.BusinessRule("APP_RETIRED", $"Application '{normalized}' is retired and cannot be activated.");
        }

        using var scope = tenantContext.BeginScope(effectiveTenantId);
        var now = clock.UtcNow;

        if (activate)
        {
            // Verify tenant has an active subscription entitlement for this application (except core NEXUS_C)
            if (normalized != "NEXUS_C")
            {
                var subEval = await EvaluateSubscriptionEntitlementOnlyAsync(effectiveTenantId, normalized, ct);
                if (!subEval.IsEntitled)
                {
                    return Error.Forbidden(
                        "APP_ENTITLEMENT_REQUIRED",
                        $"Tenant does not have an active subscription entitlement for '{normalized}' ({subEval.Reason}).");
                }
            }

            // Verify declared module dependencies are active
            foreach (var dep in app.GetModuleDependencies())
            {
                if (dep == "NEXUS_C") continue;
                var depAct = await db.TenantApplications.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.TenantId == effectiveTenantId && a.ApplicationCode == dep, ct);
                if (depAct is null || !depAct.IsActivated)
                {
                    return Error.BusinessRule("APP_DEPENDENCY_NOT_ACTIVE", $"Application '{normalized}' requires dependency '{dep}' to be activated first.");
                }
            }
        }

        var existing = await db.TenantApplications.FirstOrDefaultAsync(
            a => a.TenantId == effectiveTenantId && a.ApplicationCode == normalized, ct);

        if (existing is null)
        {
            existing = TenantApplicationActivation.ActivateNew(effectiveTenantId, normalized, currentUser.UserId, now);
            if (!activate) existing.Deactivate(currentUser.UserId, now);
            db.TenantApplications.Add(existing);
        }
        else if (activate)
        {
            existing.Activate(currentUser.UserId, now);
        }
        else
        {
            existing.Deactivate(currentUser.UserId, now);
        }

        db.StageOutboxEvent(EventEnvelope.Create(
            activate ? PlatformEventTypes.ApplicationActivatedV1 : PlatformEventTypes.ApplicationDeactivatedV1,
            effectiveTenantId,
            correlationContext.CorrelationId,
            new { TenantId = effectiveTenantId, ApplicationCode = normalized, IsActivated = activate },
            occurredAt: now));

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(BuildEntitlementCacheKey(effectiveTenantId, normalized), ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            activate ? "platform.application.activate" : "platform.application.deactivate",
            "TenantApplicationActivation",
            $"{effectiveTenantId:D}:{normalized}",
            effectiveTenantId,
            "Success",
            AfterState: new { ApplicationCode = normalized, existing.IsActivated },
            IsCrossTenantPlatformAccess: currentUser.IsPlatformUser), ct);

        var eval = await EvaluateEntitlementInternalAsync(effectiveTenantId, normalized, ct);
        return new TenantApplicationDto(
            app.Code, app.Name, app.CurrentVersion, app.Status.ToString(),
            eval.IsEntitled, existing.IsActivated, eval.Reason, existing.ActivatedAt, existing.DeactivatedAt);
    }

    // === Plans & Subscriptions ===

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

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "subscription.plan.create", "Plan", plan.Id.ToString("D"), null, "Success",
            AfterState: new { plan.Code, plan.Name, plan.MonthlyPrice, plan.Currency }), ct);

        return new PlanDto(plan.Id, plan.Code, plan.Name, plan.Description, plan.MonthlyPrice, plan.Currency, plan.IsActive, request.Features, plan.Version);
    }

    public async Task<Result<IReadOnlyList<PlanDto>>> ListPlansAsync(CancellationToken ct = default)
    {
        var plans = await db.Plans.AsNoTracking().Include(p => p.Features).OrderBy(p => p.MonthlyPrice).ToListAsync(ct);
        return plans.Select(p => new PlanDto(
            p.Id, p.Code, p.Name, p.Description, p.MonthlyPrice, p.Currency, p.IsActive,
            p.Features.Select(f => new PlanFeatureInput(f.FeatureCode, f.IsEnabled, f.QuotaLimit)).ToList(), p.Version)).ToList();
    }

    public async Task<Result<TenantSubscriptionDto>> AssignPlanToTenantAsync(AssignSubscriptionRequest request, CancellationToken ct = default)
    {
        // P0 Security Defense-in-Depth: only platform administrators (or explicit platform bypass) can assign subscriptions cross-tenant!
        if (!tenantContext.IsPlatformBypass && !currentUser.IsPlatformUser)
        {
            return Error.Forbidden("SUBSCRIPTION_PLATFORM_ONLY", "Only platform administrators may assign tenant subscriptions.");
        }

        var plan = await db.Plans.AsNoTracking().Include(p => p.Features).FirstOrDefaultAsync(p => p.Id == request.PlanId, ct);
        if (plan is null) return Error.NotFound("PLAN_NOT_FOUND", "Subscription plan was not found.");

        using var scope = tenantContext.BeginScope(request.TenantId);
        var now = clock.UtcNow;

        var activeSubs = await db.Subscriptions
            .Where(s => s.TenantId == request.TenantId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .ToListAsync(ct);

        foreach (var old in activeSubs)
        {
            old.Cancel(now);
        }

        var createdSub = TenantSubscription.Create(request.TenantId, plan.Id, request.StartsAt, request.EndsAt);
        if (createdSub.IsFailure) return Result.Failure<TenantSubscriptionDto>(createdSub.Error);

        var sub = createdSub.Value;
        db.Subscriptions.Add(sub);
        db.SubscriptionItems.Add(SubscriptionItem.Create(request.TenantId, sub.Id, "NEXUS_C", request.SeatCount, plan.MonthlyPrice));

        var existingEntitlements = await db.Entitlements
            .Where(e => e.TenantId == request.TenantId)
            .ToDictionaryAsync(e => e.FeatureCode, StringComparer.OrdinalIgnoreCase, ct);

        var newPlanFeatureCodes = plan.Features.Select(f => f.FeatureCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // P0 Data Integrity Fix: revoke/disable stale entitlements that are NOT present in the new plan!
        foreach (var stale in existingEntitlements.Values.Where(e => !newPlanFeatureCodes.Contains(e.FeatureCode)))
        {
            stale.Revoke(now);
            await cache.RemoveAsync(BuildEntitlementCacheKey(request.TenantId, stale.FeatureCode), ct);
        }

        var resultEntitlements = new List<EntitlementDto>();
        foreach (var feature in plan.Features)
        {
            if (existingEntitlements.TryGetValue(feature.FeatureCode, out var ent))
            {
                ent.Update(sub.Id, feature.IsEnabled, feature.QuotaLimit, sub.EndsAt);
            }
            else
            {
                ent = Entitlement.Create(request.TenantId, sub.Id, feature.FeatureCode, feature.IsEnabled, feature.QuotaLimit, sub.EndsAt);
                db.Entitlements.Add(ent);
            }

            await cache.RemoveAsync(BuildEntitlementCacheKey(request.TenantId, feature.FeatureCode), ct);
            resultEntitlements.Add(new EntitlementDto(feature.FeatureCode, feature.IsEnabled, feature.QuotaLimit, sub.EndsAt));
        }

        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.SubscriptionActivatedV1,
            request.TenantId,
            correlationContext.CorrelationId,
            new { sub.Id, request.TenantId, PlanCode = plan.Code, sub.StartsAt, sub.EndsAt },
            occurredAt: now));

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "subscription.assign", "TenantSubscription", sub.Id.ToString("D"), request.TenantId, "Success",
            AfterState: new { PlanCode = plan.Code, sub.StartsAt, sub.EndsAt, Features = newPlanFeatureCodes.OrderBy(c => c).ToList() },
            IsCrossTenantPlatformAccess: true), ct);

        return new TenantSubscriptionDto(sub.Id, sub.TenantId, sub.PlanId, sub.Status.ToString(), sub.StartsAt, sub.EndsAt, resultEntitlements);
    }

    public async Task<Result<TenantSubscriptionDto>> UpdateSubscriptionStatusAsync(Guid tenantId, SubscriptionStatus newStatus, string? reason = null, CancellationToken ct = default)
    {
        if (!currentUser.IsPlatformUser)
        {
            return Error.Forbidden("SUBSCRIPTION_PLATFORM_ONLY", "Only platform administrators may modify subscription lifecycle status.");
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var sub = await db.Subscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (sub is null) return Error.NotFound("SUBSCRIPTION_NOT_FOUND", "No subscription found for this tenant.");

        var now = clock.UtcNow;
        var prevStatus = sub.Status.ToString();
        switch (newStatus)
        {
            case SubscriptionStatus.Cancelled:
                sub.Cancel(now);
                break;
            case SubscriptionStatus.Suspended:
                sub.Suspend();
                break;
            case SubscriptionStatus.Expired:
                sub.Expire();
                break;
            case SubscriptionStatus.Active:
                sub.Activate();
                break;
            default:
                return Error.Validation("SUBSCRIPTION_INVALID_STATUS", $"Unsupported target status '{newStatus}'.");
        }

        var entitlements = await db.Entitlements.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => new EntitlementDto(e.FeatureCode, e.IsEnabled, e.QuotaLimit, e.ExpiresAt))
            .ToListAsync(ct);

        foreach (var e in entitlements)
        {
            await cache.RemoveAsync(BuildEntitlementCacheKey(tenantId, e.FeatureCode), ct);
        }

        if (newStatus is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
        {
            db.StageOutboxEvent(EventEnvelope.Create(
                newStatus == SubscriptionStatus.Cancelled ? PlatformEventTypes.SubscriptionCancelledV1 : PlatformEventTypes.SubscriptionExpiredV1,
                tenantId,
                correlationContext.CorrelationId,
                new { sub.Id, tenantId, Status = newStatus.ToString(), Reason = reason },
                occurredAt: now));
        }

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "subscription.status.update", "TenantSubscription", sub.Id.ToString("D"), tenantId, "Success",
            Reason: reason,
            BeforeState: new { Status = prevStatus },
            AfterState: new { Status = sub.Status.ToString() },
            IsCrossTenantPlatformAccess: true), ct);

        return new TenantSubscriptionDto(sub.Id, sub.TenantId, sub.PlanId, sub.Status.ToString(), sub.StartsAt, sub.EndsAt, entitlements);
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
            .Where(e => e.TenantId == tenantId && e.IsEnabled)
            .Select(e => new EntitlementDto(e.FeatureCode, e.IsEnabled, e.QuotaLimit, e.ExpiresAt))
            .ToListAsync(ct);

        return new TenantSubscriptionDto(sub.Id, sub.TenantId, sub.PlanId, sub.Status.ToString(), sub.StartsAt, sub.EndsAt, entitlements);
    }

    public async Task<Result<bool>> CheckEntitlementAsync(Guid tenantId, string featureCode, CancellationToken ct = default)
    {
        var eval = await EvaluateEntitlementAsync(tenantId, featureCode, ct);
        return eval.IsFailure ? Result.Failure<bool>(eval.Error) : eval.Value.IsEntitled;
    }

    public async Task<Result<EntitlementEvaluationDto>> EvaluateEntitlementAsync(Guid tenantId, string featureCode, CancellationToken ct = default)
    {
        if (!tenantContext.IsPlatformBypass && !currentUser.IsPlatformUser &&
            (!tenantContext.HasTenant || tenantContext.TenantId!.Value != tenantId))
        {
            return Error.Forbidden("CROSS_TENANT_ENTITLEMENT_DENIED", "Cannot evaluate entitlements for another tenant.");
        }

        return await EvaluateEntitlementInternalAsync(tenantId, featureCode, ct);
    }

    public async Task<Result<IReadOnlyList<EntitlementEvaluationDto>>> ListCurrentTenantEntitlementsAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var codes = await db.Entitlements.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => e.FeatureCode)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        var results = new List<EntitlementEvaluationDto>();
        foreach (var code in codes)
        {
            results.Add(await EvaluateEntitlementInternalAsync(tenantId, code, ct));
        }

        return results;
    }

    public async Task<Result<UsageSummaryDto>> RecordUsageIdempotentAsync(RecordUsageRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        if (string.IsNullOrWhiteSpace(request.FeatureCode) || request.Quantity < 0)
        {
            return Error.Validation("USAGE_INVALID_INPUT", "FeatureCode is required and Quantity must be non-negative.");
        }

        var code = request.FeatureCode.Trim().ToUpperInvariant();
        var idemKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();

        if (idemKey is not null)
        {
            var exists = await db.UsageRecords.AsNoTracking()
                .AnyAsync(u => u.TenantId == tenantId && u.IdempotencyKey == idemKey, ct);
            if (!exists)
            {
                db.UsageRecords.Add(UsageRecord.Create(tenantId, code, request.Quantity, clock.UtcNow, idemKey));
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    db.ChangeTracker.Clear();
                }
            }
        }
        else
        {
            db.UsageRecords.Add(UsageRecord.Create(tenantId, code, request.Quantity, clock.UtcNow));
            await db.SaveChangesAsync(ct);
        }

        var summary = await db.UsageRecords.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.FeatureCode == code)
            .GroupBy(u => u.FeatureCode)
            .Select(g => new UsageSummaryDto(g.Key, g.Sum(x => x.Quantity), g.Count(), g.Max(x => x.RecordedAt)))
            .FirstAsync(ct);

        return summary;
    }

    public async Task<Result<IReadOnlyList<UsageSummaryDto>>> ListUsageSummaryAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var list = await db.UsageRecords.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .GroupBy(u => u.FeatureCode)
            .Select(g => new UsageSummaryDto(g.Key, g.Sum(x => x.Quantity), g.Count(), g.Max(x => x.RecordedAt)))
            .OrderBy(x => x.FeatureCode)
            .ToListAsync(ct);

        return list;
    }

    // === IEntitlementChecker ===

    public async Task<bool> HasFeatureAsync(Guid tenantId, string featureCode, CancellationToken ct = default)
    {
        var eval = await EvaluateEntitlementInternalAsync(tenantId, featureCode, ct);
        return eval.IsEntitled;
    }

    public async Task<long?> GetFeatureLimitAsync(Guid tenantId, string featureCode, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var normalized = featureCode.Trim().ToUpperInvariant();
        var ent = await db.Entitlements.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.FeatureCode == normalized && e.IsEnabled, ct);
        return ent?.QuotaLimit;
    }

    public async Task RecordUsageAsync(Guid tenantId, string featureCode, long quantity, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        db.UsageRecords.Add(UsageRecord.Create(tenantId, featureCode, quantity, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    private async Task<EntitlementEvaluationDto> EvaluateSubscriptionEntitlementOnlyAsync(Guid tenantId, string normalizedCode, CancellationToken ct)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var now = clock.UtcNow;

        var sub = await db.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (sub is null)
        {
            return new EntitlementEvaluationDto(tenantId, normalizedCode, false, null, 0, "None", null, null, "NO_ACTIVE_SUBSCRIPTION");
        }

        if (!sub.IsCurrentlyValid(now))
        {
            return new EntitlementEvaluationDto(tenantId, normalizedCode, false, null, 0, sub.Status.ToString(), null, sub.EndsAt,
                sub.EndsAt <= now ? "SUBSCRIPTION_EXPIRED" : $"SUBSCRIPTION_{sub.Status.ToString().ToUpperInvariant()}");
        }

        var ent = await db.Entitlements.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.FeatureCode == normalizedCode, ct);

        if (ent is null || !ent.IsEnabled || ent.ExpiresAt <= now)
        {
            return new EntitlementEvaluationDto(tenantId, normalizedCode, false, ent?.QuotaLimit, 0, sub.Status.ToString(), null, ent?.ExpiresAt, "FEATURE_NOT_ENTITLED");
        }

        return new EntitlementEvaluationDto(tenantId, normalizedCode, true, ent.QuotaLimit, 0, sub.Status.ToString(), null, ent.ExpiresAt, "OK");
    }

    private async Task<EntitlementEvaluationDto> EvaluateEntitlementInternalAsync(Guid tenantId, string featureCode, CancellationToken ct)
    {
        var normalized = featureCode.Trim().ToUpperInvariant();
        var baseEval = await EvaluateSubscriptionEntitlementOnlyAsync(tenantId, normalized, ct);
        if (!baseEval.IsEntitled)
        {
            return baseEval;
        }

        using var scope = tenantContext.BeginScope(tenantId);

        // Check explicit tenant application deactivation if this featureCode matches a registered application
        var appAct = await db.TenantApplications.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.ApplicationCode == normalized, ct);
        if (appAct is not null && !appAct.IsActivated)
        {
            return baseEval with { IsEntitled = false, Reason = "APPLICATION_DEACTIVATED" };
        }

        // Check tenant/global feature flag kill-switch compatibility
        var flagOverride = await featureFlagEvaluator.EvaluateFlagOverrideAsync(tenantId, normalized, ct);
        if (flagOverride == false)
        {
            return baseEval with { IsEntitled = false, FeatureFlagOverride = false, Reason = "FEATURE_FLAG_DISABLED" };
        }

        // Check usage quota
        var currentUsage = await db.UsageRecords.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.FeatureCode == normalized)
            .SumAsync(u => (long?)u.Quantity, ct) ?? 0L;

        if (baseEval.QuotaLimit.HasValue && currentUsage >= baseEval.QuotaLimit.Value)
        {
            return baseEval with { IsEntitled = false, CurrentUsage = currentUsage, FeatureFlagOverride = flagOverride, Reason = "QUOTA_EXCEEDED" };
        }

        return baseEval with { CurrentUsage = currentUsage, FeatureFlagOverride = flagOverride, Reason = "OK" };
    }

    private static RegisteredApplicationDto MapApp(RegisteredApplication a) => new(
        a.Id, a.Code, a.Name, a.Description, a.CurrentVersion, a.ReleaseChannel,
        a.Status.ToString(), a.MinPlatformVersion, a.GetRequiredCapabilities(),
        a.GetModuleDependencies(), a.CompatibilityMetadataJson);
}
