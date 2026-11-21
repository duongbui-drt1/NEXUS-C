using Microsoft.EntityFrameworkCore;
using Nexus.Modules.Configuration.Domain;
using Nexus.Modules.Configuration.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Configuration.Application;

public sealed record SettingDto(string Key, string Value, string Category, int Version, DateTimeOffset UpdatedAt);
public sealed record UpsertSettingRequest(string Key, string Value, string Category = "General");
public sealed record FeatureFlagDto(string Key, bool IsEnabled, string? Description, int Version, bool IsTenantOverride);
public sealed record UpsertFeatureFlagRequest(string Key, bool IsEnabled, string? Description = null);

public interface IConfigurationService
{
    Task<Result<SettingDto>> UpsertTenantSettingAsync(UpsertSettingRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SettingDto>>> ListTenantSettingsAsync(string? category = null, CancellationToken ct = default);
    Task<Result<SettingDto>> UpsertPlatformSettingAsync(UpsertSettingRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SettingDto>>> ListPlatformSettingsAsync(CancellationToken ct = default);
    Task<Result<FeatureFlagDto>> UpsertFeatureFlagAsync(UpsertFeatureFlagRequest request, bool isPlatformDefault, CancellationToken ct = default);
    Task<Result<bool>> EvaluateFeatureFlagAsync(string key, CancellationToken ct = default);
    Task<Result<IReadOnlyList<FeatureFlagDto>>> ListFeatureFlagsAsync(CancellationToken ct = default);
}

public sealed class ConfigurationService(
    ConfigurationDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock,
    IAuditWriter auditWriter) : IConfigurationService
{
    public async Task<Result<SettingDto>> UpsertTenantSettingAsync(UpsertSettingRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var normalizedKey = request.Key?.Trim().ToLowerInvariant() ?? string.Empty;
        var existing = await db.TenantSettings.FirstOrDefaultAsync(x => x.Key == normalizedKey, ct);
        string? prevValue = null;

        if (existing is null)
        {
            var created = TenantSetting.Create(tenantId, request.Key!, request.Value, request.Category);
            if (created.IsFailure) return Result.Failure<SettingDto>(created.Error);
            existing = created.Value;
            db.TenantSettings.Add(existing);
        }
        else
        {
            prevValue = existing.Value;
            existing.UpdateValue(request.Value);
        }

        db.SettingHistories.Add(SettingHistory.Record(tenantId, "Tenant", existing.Key, prevValue, existing.Value, existing.Version, currentUser.UserId, clock.UtcNow));
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "configuration.tenant_setting.upsert", "TenantSetting", existing.Key, tenantId, "Success",
            BeforeState: prevValue is null ? null : new { existing.Key, Value = prevValue },
            AfterState: new { existing.Key, existing.Value, existing.Version }), ct);

        return new SettingDto(existing.Key, existing.Value, existing.Category, existing.Version, existing.UpdatedAt);
    }

    public async Task<Result<IReadOnlyList<SettingDto>>> ListTenantSettingsAsync(string? category = null, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var query = db.TenantSettings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim();
            query = query.Where(x => x.Category == cat);
        }

        var list = await query.OrderBy(x => x.Key)
            .Select(x => new SettingDto(x.Key, x.Value, x.Category, x.Version, x.UpdatedAt))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<SettingDto>> UpsertPlatformSettingAsync(UpsertSettingRequest request, CancellationToken ct = default)
    {
        var normalizedKey = request.Key?.Trim().ToLowerInvariant() ?? string.Empty;
        var existing = await db.PlatformSettings.FirstOrDefaultAsync(x => x.Key == normalizedKey, ct);
        string? prevValue = null;

        if (existing is null)
        {
            var created = PlatformSetting.Create(request.Key!, request.Value, request.Category);
            if (created.IsFailure) return Result.Failure<SettingDto>(created.Error);
            existing = created.Value;
            db.PlatformSettings.Add(existing);
        }
        else
        {
            prevValue = existing.Value;
            existing.UpdateValue(request.Value);
        }

        db.SettingHistories.Add(SettingHistory.Record(null, "Platform", existing.Key, prevValue, existing.Value, existing.Version, currentUser.UserId, clock.UtcNow));
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "configuration.platform_setting.upsert", "PlatformSetting", existing.Key, null, "Success",
            AfterState: new { existing.Key, existing.Value, existing.Version }), ct);

        return new SettingDto(existing.Key, existing.Value, existing.Category, existing.Version, existing.UpdatedAt);
    }

    public async Task<Result<IReadOnlyList<SettingDto>>> ListPlatformSettingsAsync(CancellationToken ct = default)
    {
        var list = await db.PlatformSettings.AsNoTracking()
            .OrderBy(x => x.Key)
            .Select(x => new SettingDto(x.Key, x.Value, x.Category, x.Version, x.UpdatedAt))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<FeatureFlagDto>> UpsertFeatureFlagAsync(UpsertFeatureFlagRequest request, bool isPlatformDefault, CancellationToken ct = default)
    {
        Guid? targetTenantId = isPlatformDefault ? null : tenantContext.TenantId;
        if (!isPlatformDefault && !targetTenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required for tenant feature flag overrides.");
        }

        var key = request.Key?.Trim().ToLowerInvariant() ?? string.Empty;
        var existing = await db.FeatureFlags.FirstOrDefaultAsync(f => f.TenantId == targetTenantId && f.Key == key, ct);

        if (existing is null)
        {
            var created = FeatureFlag.Create(targetTenantId, request.Key!, request.IsEnabled, request.Description);
            if (created.IsFailure) return Result.Failure<FeatureFlagDto>(created.Error);
            existing = created.Value;
            db.FeatureFlags.Add(existing);
        }
        else
        {
            existing.SetEnabled(request.IsEnabled, request.Description);
        }

        await db.SaveChangesAsync(ct);
        return new FeatureFlagDto(existing.Key, existing.IsEnabled, existing.Description, existing.Version, targetTenantId.HasValue);
    }

    public async Task<Result<bool>> EvaluateFeatureFlagAsync(string key, CancellationToken ct = default)
    {
        var normalized = key.Trim().ToLowerInvariant();
        if (tenantContext.HasTenant)
        {
            var tenantOverride = await db.FeatureFlags.AsNoTracking()
                .FirstOrDefaultAsync(f => f.TenantId == tenantContext.TenantId && f.Key == normalized, ct);
            if (tenantOverride is not null)
            {
                return tenantOverride.IsEnabled;
            }
        }

        var platformFlag = await db.FeatureFlags.AsNoTracking()
            .FirstOrDefaultAsync(f => f.TenantId == null && f.Key == normalized, ct);

        return platformFlag?.IsEnabled ?? false;
    }

    public async Task<Result<IReadOnlyList<FeatureFlagDto>>> ListFeatureFlagsAsync(CancellationToken ct = default)
    {
        var tid = tenantContext.TenantId;
        var flags = await db.FeatureFlags.AsNoTracking()
            .Where(f => f.TenantId == null || (tid.HasValue && f.TenantId == tid.Value))
            .OrderBy(f => f.Key)
            .ToListAsync(ct);

        var merged = flags
            .GroupBy(f => f.Key)
            .Select(g => g.FirstOrDefault(x => x.TenantId == tid) ?? g.First())
            .Select(f => new FeatureFlagDto(f.Key, f.IsEnabled, f.Description, f.Version, f.TenantId.HasValue))
            .OrderBy(f => f.Key)
            .ToList();

        return merged;
    }
}
