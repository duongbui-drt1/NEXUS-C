using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Messaging;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;

namespace Nexus.BuildingBlocks.Web;

/// <summary>
/// Distributed, policy-driven API rate limiter backed by IDistributedCache (Redis in production/multi-instance,
/// in-memory distributed cache in local single-node development).
/// Enforces layered partition keys for Anonymous IP, Human Session, Service Client, API Key, and Expensive operations.
/// </summary>
public sealed class DistributedRateLimitingMiddleware(
    RequestDelegate next,
    IDistributedCache cache,
    ILogger<DistributedRateLimitingMiddleware> logger)
{
    public const string DecisionItemKey = "Nexus.RateLimitDecision";
    public const string PolicyItemKey = "Nexus.RateLimitPolicy";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IRateLimitPolicyProvider policyProvider)
    {
        var path = context.Request.Path.Value ?? "/";

        // Health checks, OpenAPI, and static docs are exempt from rate limiting
        if (IsExemptPath(path))
        {
            context.Items[DecisionItemKey] = "Bypassed";
            await next(context);
            return;
        }

        IReadOnlyList<RateLimitPolicySnapshot> policies;
        try
        {
            policies = await policyProvider.GetActivePoliciesAsync(context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load distributed rate-limit policies; falling back to allow.");
            context.Items[DecisionItemKey] = "Allowed";
            await next(context);
            return;
        }

        var authMethod = context.User.FindFirst("auth_method")?.Value;
        var matchedPolicy = SelectPolicy(policies, path, currentUser, authMethod);

        if (matchedPolicy is null)
        {
            context.Items[DecisionItemKey] = "Allowed";
            await next(context);
            return;
        }

        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowSeconds = Math.Max(1, matchedPolicy.WindowSeconds);
        var windowBucket = nowEpoch / windowSeconds;
        var windowResetSeconds = (int)((windowBucket + 1) * windowSeconds - nowEpoch);
        if (windowResetSeconds < 1)
        {
            windowResetSeconds = 1;
        }

        var partitionKey = BuildPartitionKey(matchedPolicy, context, currentUser, tenantContext, windowBucket);
        var effectiveLimit = matchedPolicy.PermitLimit + Math.Max(0, matchedPolicy.BurstLimit);

        var currentCount = await IncrementCounterAsync(partitionKey, TimeSpan.FromSeconds(windowSeconds + 2), context.RequestAborted);
        var remaining = Math.Max(0, effectiveLimit - currentCount);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-RateLimit-Limit"] = effectiveLimit.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-RateLimit-Policy"] = matchedPolicy.Name;
            return Task.CompletedTask;
        });

        if (currentCount > effectiveLimit)
        {
            context.Items[DecisionItemKey] = "Throttled";
            context.Items[PolicyItemKey] = matchedPolicy.Name;
            context.Items["Nexus.ErrorCode"] = "RATE_LIMIT_EXCEEDED";

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = windowResetSeconds.ToString(CultureInfo.InvariantCulture);
            context.Response.ContentType = "application/problem+json";

            var correlationId = ApiResultMapper.GetCorrelationId(context);
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://errors.nexus.internal/rate-limit-exceeded",
                title = "Too Many Requests",
                status = 429,
                detail = $"Rate limit policy '{matchedPolicy.Name}' exceeded. Retry after {windowResetSeconds} seconds.",
                errorCode = "RATE_LIMIT_EXCEEDED",
                policy = matchedPolicy.Name,
                retryAfterSeconds = windowResetSeconds,
                correlationId
            }, context.RequestAborted);
            return;
        }

        context.Items[DecisionItemKey] = "Allowed";
        context.Items[PolicyItemKey] = matchedPolicy.Name;
        await next(context);
    }

    private async Task<int> IncrementCounterAsync(string cacheKey, TimeSpan ttl, CancellationToken ct)
    {
        // Local semaphore guards intra-node read-modify-write; IDistributedCache shares state across nodes (Redis).
        var gate = _keyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var raw = await cache.GetAsync(cacheKey, ct);
            var count = 0;
            if (raw is { Length: > 0 } && int.TryParse(Encoding.UTF8.GetString(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                count = parsed;
            }

            count++;
            await cache.SetAsync(
                cacheKey,
                Encoding.UTF8.GetBytes(count.ToString(CultureInfo.InvariantCulture)),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                ct);

            return count;
        }
        finally
        {
            gate.Release();
            if (_keyLocks.Count > 20_000)
            {
                _keyLocks.Clear();
            }
        }
    }

    private static bool IsExemptPath(string path)
        => path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith("/openapi/", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase);

    private static RateLimitPolicySnapshot? SelectPolicy(
        IReadOnlyList<RateLimitPolicySnapshot> policies,
        string path,
        ICurrentUser currentUser,
        string? authMethod)
    {
        foreach (var policy in policies)
        {
            if (!MatchesRoute(policy.RoutePattern, path))
            {
                continue;
            }

            var scopeMatches = policy.ScopeType switch
            {
                "Expensive" => true,
                "AnonymousIp" => !currentUser.IsAuthenticated,
                "ApiKey" => currentUser.IsServicePrincipal && string.Equals(authMethod, "api_key", StringComparison.OrdinalIgnoreCase),
                "ServiceClient" => currentUser.IsServicePrincipal && !string.Equals(authMethod, "api_key", StringComparison.OrdinalIgnoreCase),
                "HumanSession" => currentUser.IsAuthenticated && !currentUser.IsServicePrincipal,
                _ => false
            };

            if (scopeMatches)
            {
                return policy;
            }
        }

        return null;
    }

    private static bool MatchesRoute(string pattern, string path)
    {
        if (string.Equals(pattern, "*", StringComparison.Ordinal) ||
            string.Equals(pattern, "/*", StringComparison.Ordinal))
        {
            return true;
        }

        if (pattern.EndsWith("/*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1]; // retain trailing '/'
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                   path.Equals(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(pattern, path, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPartitionKey(
        RateLimitPolicySnapshot policy,
        HttpContext context,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        long windowBucket)
    {
        var tid = tenantContext.TenantId?.ToString("N") ?? "platform";
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";

        var identityPart = policy.ScopeType switch
        {
            "AnonymousIp" => $"anon:{ip}",
            "ApiKey" => $"apikey:{currentUser.ServiceClientId ?? "unknown"}:{tid}",
            "ServiceClient" => $"svc:{currentUser.ServiceClientId ?? "unknown"}:{currentUser.ApplicationCode ?? "core"}:{tid}",
            "HumanSession" => $"human:{currentUser.UserId?.ToString("N") ?? ip}:{tid}",
            "Expensive" => currentUser.IsServicePrincipal
                ? $"exp:svc:{currentUser.ServiceClientId ?? "unknown"}:{tid}"
                : $"exp:usr:{currentUser.UserId?.ToString("N") ?? ip}:{tid}",
            _ => $"gen:{ip}"
        };

        return $"rl:{policy.Name}:{identityPart}:{windowBucket}";
    }
}

/// <summary>
/// Records bounded, sanitized API request telemetry into messaging.api_request_logs.
/// Strictly NEVER reads or persists Authorization headers, cookies, API keys, passwords, query strings, or bodies.
/// </summary>
public sealed partial class ApiRequestLoggingMiddleware(
    RequestDelegate next,
    IServiceScopeFactory scopeFactory,
    ILogger<ApiRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, ITenantContext tenantContext)
    {
        var path = context.Request.Path.Value ?? "/";
        if (path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/openapi/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();
            await PersistTelemetryAsync(context, currentUser, tenantContext, sw.Elapsed.TotalMilliseconds);
        }
    }

    private async Task PersistTelemetryAsync(
        HttpContext context,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        double durationMs)
    {
        try
        {
            var routeEndpoint = context.GetEndpoint() as RouteEndpoint;
            var rawRoute = routeEndpoint?.RoutePattern.RawText;
            var normalizedRoute = !string.IsNullOrWhiteSpace(rawRoute)
                ? "/" + rawRoute.TrimStart('/')
                : SanitizePath(context.Request.Path.Value ?? "/");

            var actorType = !currentUser.IsAuthenticated
                ? "Anonymous"
                : currentUser.IsServicePrincipal
                    ? "Service"
                    : "Human";

            var decision = context.Items.TryGetValue(DistributedRateLimitingMiddleware.DecisionItemKey, out var d) && d is string ds
                ? ds
                : "Allowed";
            var policy = context.Items.TryGetValue(DistributedRateLimitingMiddleware.PolicyItemKey, out var p) && p is string ps
                ? ps
                : null;
            var errorCode = context.Items.TryGetValue("Nexus.ErrorCode", out var e) && e is string es
                ? es
                : null;

            var appCode = currentUser.ApplicationCode
                          ?? context.Request.Headers["X-Application-Code"].FirstOrDefault()?.Trim().ToUpperInvariant();

            var entry = ApiRequestLog.Record(
                DateTimeOffset.UtcNow,
                context.Request.Method,
                normalizedRoute,
                context.Response.StatusCode,
                durationMs,
                ApiResultMapper.GetCorrelationId(context),
                appCode,
                currentUser.ServiceClientId,
                tenantContext.TenantId ?? currentUser.TenantId,
                currentUser.IsServicePrincipal ? null : currentUser.UserId,
                actorType,
                decision,
                policy,
                errorCode);

            await using var scope = scopeFactory.CreateAsyncScope();
            var logService = scope.ServiceProvider.GetRequiredService<IApiRequestLogService>();
            await logService.WriteAsync(entry, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Telemetry persistence must never break API responses
            logger.LogDebug(ex, "Failed to persist API request telemetry entry.");
        }
    }

    private static string SanitizePath(string path)
    {
        // Strip any query string defensively and mask GUID segments so raw IDs don't explode cardinality
        var noQuery = path.Split('?', 2)[0];
        return GuidSegmentRegex().Replace(noQuery, "{id}");
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled)]
    private static partial Regex GuidSegmentRegex();
}
