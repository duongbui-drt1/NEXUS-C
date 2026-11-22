using Microsoft.EntityFrameworkCore;
using Nexus.Modules.Configuration.Domain;
using Nexus.Modules.Configuration.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Configuration.Application;

public sealed record RateLimitPolicyDto(
    Guid Id,
    string Name,
    string RoutePattern,
    string ScopeType,
    int PermitLimit,
    int WindowSeconds,
    int BurstLimit,
    bool IsEnabled,
    int Priority,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateRateLimitPolicyCommand(
    string Name,
    string RoutePattern,
    string ScopeType,
    int PermitLimit,
    int WindowSeconds,
    int BurstLimit = 0,
    bool IsEnabled = true,
    int Priority = 100,
    string? Description = null);

public sealed record UpdateRateLimitPolicyCommand(
    string RoutePattern,
    string ScopeType,
    int PermitLimit,
    int WindowSeconds,
    int BurstLimit,
    bool IsEnabled,
    int Priority,
    string? Description);

public interface IRateLimitPolicyService
{
    Task<IReadOnlyList<RateLimitPolicyDto>> ListPoliciesAsync(CancellationToken ct = default);
    Task<Result<RateLimitPolicyDto>> CreatePolicyAsync(CreateRateLimitPolicyCommand cmd, CancellationToken ct = default);
    Task<Result<RateLimitPolicyDto>> UpdatePolicyAsync(Guid id, UpdateRateLimitPolicyCommand cmd, CancellationToken ct = default);
    Task<Result> DeletePolicyAsync(Guid id, CancellationToken ct = default);
    Task SeedDefaultPoliciesAsync(CancellationToken ct = default);
}

public sealed class RateLimitPolicyCache
{
    private readonly object _lock = new();
    private IReadOnlyList<RateLimitPolicySnapshot>? _policies;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public bool TryGet(DateTimeOffset now, out IReadOnlyList<RateLimitPolicySnapshot> policies)
    {
        lock (_lock)
        {
            if (_policies is not null && now < _expiresAt)
            {
                policies = _policies;
                return true;
            }
        }
        policies = [];
        return false;
    }

    public void Set(IReadOnlyList<RateLimitPolicySnapshot> policies, DateTimeOffset expiresAt)
    {
        lock (_lock)
        {
            _policies = policies;
            _expiresAt = expiresAt;
        }
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _policies = null;
            _expiresAt = DateTimeOffset.MinValue;
        }
    }
}

public sealed class RateLimitPolicyService(
    ConfigurationDbContext db,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    RateLimitPolicyCache policyCache) : IRateLimitPolicyService, IRateLimitPolicyProvider
{
    public void InvalidateCache() => policyCache.Invalidate();

    public async Task<IReadOnlyList<RateLimitPolicySnapshot>> GetActivePoliciesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (policyCache.TryGet(now, out var cached))
        {
            return cached;
        }

        using var bypass = tenantContext.BeginScope(null, platformBypass: true, bypassReason: "rate-limit-policy-read");
        var policies = await db.RateLimitPolicies.AsNoTracking()
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Name)
            .Select(p => new RateLimitPolicySnapshot(
                p.Id,
                p.Name,
                p.RoutePattern,
                p.ScopeType.ToString(),
                p.PermitLimit,
                p.WindowSeconds,
                p.BurstLimit,
                p.Priority))
            .ToListAsync(ct);

        policyCache.Set(policies, now.AddSeconds(10));
        return policies;
    }

    public async Task<IReadOnlyList<RateLimitPolicyDto>> ListPoliciesAsync(CancellationToken ct = default)
    {
        return await db.RateLimitPolicies.AsNoTracking()
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Name)
            .Select(p => new RateLimitPolicyDto(
                p.Id,
                p.Name,
                p.RoutePattern,
                p.ScopeType.ToString(),
                p.PermitLimit,
                p.WindowSeconds,
                p.BurstLimit,
                p.IsEnabled,
                p.Priority,
                p.Description,
                p.CreatedAt,
                p.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<Result<RateLimitPolicyDto>> CreatePolicyAsync(CreateRateLimitPolicyCommand cmd, CancellationToken ct = default)
    {
        if (!Enum.TryParse<RateLimitScopeType>(cmd.ScopeType, ignoreCase: true, out var scopeType))
        {
            return Error.Validation("RATE_LIMIT_INVALID_SCOPE", "ScopeType must be one of: AnonymousIp, HumanSession, ServiceClient, ApiKey, Expensive.");
        }

        var normalizedName = cmd.Name.Trim().ToLowerInvariant();
        if (await db.RateLimitPolicies.AnyAsync(p => p.Name == normalizedName, ct))
        {
            return Error.Conflict("RATE_LIMIT_DUPLICATE_NAME", $"Rate limit policy '{normalizedName}' already exists.");
        }

        var created = RateLimitPolicyRecord.Create(
            normalizedName,
            cmd.RoutePattern,
            scopeType,
            cmd.PermitLimit,
            cmd.WindowSeconds,
            cmd.BurstLimit,
            cmd.IsEnabled,
            cmd.Priority,
            cmd.Description);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.RateLimitPolicies.Add(created.Value);
        await db.SaveChangesAsync(ct);
        InvalidateCache();

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "RATE_LIMIT_POLICY_CREATED",
            "RateLimitPolicy",
            created.Value.Id.ToString("D"),
            null,
            "Success",
            AfterState: new
            {
                created.Value.Name,
                created.Value.RoutePattern,
                ScopeType = created.Value.ScopeType.ToString(),
                created.Value.PermitLimit,
                created.Value.WindowSeconds,
                created.Value.IsEnabled
            }), ct);

        return Map(created.Value);
    }

    public async Task<Result<RateLimitPolicyDto>> UpdatePolicyAsync(Guid id, UpdateRateLimitPolicyCommand cmd, CancellationToken ct = default)
    {
        if (!Enum.TryParse<RateLimitScopeType>(cmd.ScopeType, ignoreCase: true, out var scopeType))
        {
            return Error.Validation("RATE_LIMIT_INVALID_SCOPE", "ScopeType must be one of: AnonymousIp, HumanSession, ServiceClient, ApiKey, Expensive.");
        }

        var entity = await db.RateLimitPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return Error.NotFound("RATE_LIMIT_NOT_FOUND", "Rate limit policy not found.");
        }

        var before = new { entity.RoutePattern, ScopeType = entity.ScopeType.ToString(), entity.PermitLimit, entity.WindowSeconds, entity.IsEnabled, entity.Priority };
        var res = entity.Update(cmd.RoutePattern, scopeType, cmd.PermitLimit, cmd.WindowSeconds, cmd.BurstLimit, cmd.IsEnabled, cmd.Priority, cmd.Description);
        if (res.IsFailure)
        {
            return res.Error;
        }

        await db.SaveChangesAsync(ct);
        InvalidateCache();

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "RATE_LIMIT_POLICY_UPDATED",
            "RateLimitPolicy",
            entity.Id.ToString("D"),
            null,
            "Success",
            BeforeState: before,
            AfterState: new { entity.RoutePattern, ScopeType = entity.ScopeType.ToString(), entity.PermitLimit, entity.WindowSeconds, entity.IsEnabled, entity.Priority }), ct);

        return Map(entity);
    }

    public async Task<Result> DeletePolicyAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.RateLimitPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return Result.Failure(Error.NotFound("RATE_LIMIT_NOT_FOUND", "Rate limit policy not found."));
        }

        db.RateLimitPolicies.Remove(entity);
        await db.SaveChangesAsync(ct);
        InvalidateCache();

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "RATE_LIMIT_POLICY_DELETED",
            "RateLimitPolicy",
            id.ToString("D"),
            null,
            "Success",
            BeforeState: new { entity.Name, entity.RoutePattern }), ct);

        return Result.Success();
    }

    public async Task SeedDefaultPoliciesAsync(CancellationToken ct = default)
    {
        if (await db.RateLimitPolicies.AnyAsync(ct))
        {
            return;
        }

        (string Name, string Route, RateLimitScopeType Scope, int Permit, int Window, int Priority, string Desc)[] defaults =
        [
            ("auth-anonymous-login", "/api/v1/auth/*", RateLimitScopeType.AnonymousIp, 60, 60, 10, "Protects authentication endpoints from credential stuffing per client IP."),
            ("email-send-expensive", "/api/v1/platform/email/send", RateLimitScopeType.Expensive, 100, 60, 20, "Protects outbound transactional email dispatch endpoint from abuse."),
            ("api-service-client-default", "/api/v1/*", RateLimitScopeType.ServiceClient, 1000, 60, 50, "Default distributed throughput budget per OAuth 2.0 service client."),
            ("api-key-default", "/api/v1/*", RateLimitScopeType.ApiKey, 600, 60, 60, "Default distributed throughput budget per scoped API key."),
            ("api-human-default", "/api/v1/*", RateLimitScopeType.HumanSession, 300, 60, 70, "Default distributed throughput budget per authenticated human session.")
        ];

        foreach (var d in defaults)
        {
            var created = RateLimitPolicyRecord.Create(d.Name, d.Route, d.Scope, d.Permit, d.Window, 0, true, d.Priority, d.Desc);
            if (created.IsSuccess)
            {
                db.RateLimitPolicies.Add(created.Value);
            }
        }

        await db.SaveChangesAsync(ct);
        InvalidateCache();
    }

    private static RateLimitPolicyDto Map(RateLimitPolicyRecord p)
        => new(p.Id, p.Name, p.RoutePattern, p.ScopeType.ToString(), p.PermitLimit, p.WindowSeconds, p.BurstLimit, p.IsEnabled, p.Priority, p.Description, p.CreatedAt, p.UpdatedAt);
}
