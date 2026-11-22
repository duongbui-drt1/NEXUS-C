using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Messaging;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Results;
using Npgsql;

namespace Nexus.BuildingBlocks.Web;

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "Nexus.CorrelationId";

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = !string.IsNullOrWhiteSpace(incoming) && incoming.Length <= 100
            ? incoming.Trim()
            : Guid.NewGuid().ToString("D");

        correlationContext.SetCorrelationId(correlationId);
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            headers["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });

        await next(context);
    }
}

/// <summary>
/// Resolves and validates tenant context strictly from authenticated JWT claims, OAuth 2.0 service principals,
/// scoped API keys, and server-side membership records.
/// Rejects tenant header spoofing, revoked sessions/clients/keys, locked/disabled users, and suspended tenants.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
{
    public const string ApiKeyHeaderName = "X-Api-Key";

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        ITenantAccessValidator tenantValidator,
        IUserDirectoryReader userDirectory,
        IServicePrincipalValidator serviceValidator)
    {
        // 1. Optional scoped API key authentication when no Bearer JWT authenticated the request
        if (!currentUser.IsAuthenticated)
        {
            var rawApiKey = context.Request.Headers[ApiKeyHeaderName].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(rawApiKey))
            {
                var keyRes = await serviceValidator.ValidateApiKeyAsync(rawApiKey, context.RequestAborted);
                if (!keyRes.IsValid)
                {
                    await WriteProblemAsync(context, Error.Unauthorized("AUTH_INVALID_API_KEY", keyRes.FailureReason ?? "API key is invalid or revoked."));
                    return;
                }

                var claims = new List<Claim>
                {
                    new("sub", keyRes.ClientDbId!.Value.ToString("D")),
                    new("client_id", keyRes.ClientId ?? "api-key"),
                    new("scope", "service"),
                    new("auth_method", "api_key")
                };

                if (keyRes.BoundTenantId.HasValue)
                {
                    claims.Add(new Claim("tid", keyRes.BoundTenantId.Value.ToString("D")));
                }

                foreach (var scp in keyRes.AllowedScopes)
                {
                    claims.Add(new Claim("scp", scp));
                }

                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
            }
            else
            {
                await next(context);
                return;
            }
        }

        // 2. Service-to-service OAuth 2.0 Client Credentials / Scoped API Key path
        if (currentUser.IsServicePrincipal)
        {
            var authMethod = context.User.FindFirstValue("auth_method");
            Guid? boundTenantId = currentUser.TenantId;

            if (!string.Equals(authMethod, "api_key", StringComparison.OrdinalIgnoreCase))
            {
                var clientId = currentUser.ServiceClientId;
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    await WriteProblemAsync(context, Error.Unauthorized("AUTH_INVALID_SERVICE_TOKEN", "Service token is missing client_id."));
                    return;
                }

                var clientStatus = await serviceValidator.ValidateServiceClientAsync(clientId, context.RequestAborted);
                if (!clientStatus.IsValid)
                {
                    await WriteProblemAsync(context, Error.Unauthorized("AUTH_SERVICE_CLIENT_REVOKED", clientStatus.FailureReason ?? "Service client is revoked or inactive."));
                    return;
                }

                // Enforce live intersection between JWT token scopes and current client AllowedScopes
                var activeScopes = clientStatus.AllowedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var tokenScopes = currentUser.GrantedScopes.Where(s => activeScopes.Contains(s)).ToList();
                var filteredClaims = context.User.Claims.Where(c => c.Type != "scp").ToList();
                filteredClaims.AddRange(tokenScopes.Select(s => new Claim("scp", s)));
                context.User = new ClaimsPrincipal(new ClaimsIdentity(filteredClaims, context.User.Identity?.AuthenticationType ?? "Bearer"));

                if (clientStatus.BoundTenantId.HasValue)
                {
                    if (boundTenantId.HasValue && boundTenantId.Value != clientStatus.BoundTenantId.Value)
                    {
                        await WriteProblemAsync(context, Error.Forbidden("TENANT_SPOOFING_DETECTED", "Service client is bound to a different tenant."));
                        return;
                    }
                    boundTenantId = clientStatus.BoundTenantId.Value;
                }
            }

            // Anti-spoofing check on X-Tenant-Id header for service principals
            var svcHeader = context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                            ?? context.Request.Headers["Tenant-ID"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(svcHeader))
            {
                if (!Guid.TryParse(svcHeader, out var requestedTid))
                {
                    await WriteProblemAsync(context, Error.Validation("INVALID_TENANT_HEADER", "Invalid X-Tenant-Id header format."));
                    return;
                }

                if (boundTenantId.HasValue && boundTenantId.Value != requestedTid)
                {
                    await WriteProblemAsync(context, Error.Forbidden("TENANT_SPOOFING_DETECTED", "Service principal cannot override its bound tenant context."));
                    return;
                }

                boundTenantId = requestedTid;
            }

            if (boundTenantId.HasValue)
            {
                var tenantInfo = await tenantValidator.ValidateMembershipAsync(Guid.Empty, boundTenantId.Value, context.RequestAborted);
                if (tenantInfo is null)
                {
                    await WriteProblemAsync(context, Error.NotFound("TENANT_NOT_FOUND", "Target tenant was not found."));
                    return;
                }

                if (tenantInfo.TenantStatus == TenantLifecycleStatus.Suspended)
                {
                    await WriteProblemAsync(context, Error.Forbidden("TENANT_SUSPENDED", "This tenant is currently suspended. Business operations are disabled."));
                    return;
                }

                if (tenantInfo.TenantStatus == TenantLifecycleStatus.Archived)
                {
                    await WriteProblemAsync(context, Error.Forbidden("TENANT_ARCHIVED", "This tenant has been archived."));
                    return;
                }

                tenantContext.SetTenant(tenantInfo.TenantId);
            }

            using (logger.BeginScope(new Dictionary<string, object?>
            {
                ["ActorType"] = "Service",
                ["ServiceClientId"] = currentUser.ServiceClientId ?? "unknown",
                ["TenantId"] = tenantContext.TenantId?.ToString("D") ?? "platform"
            }))
            {
                await next(context);
            }
            return;
        }

        // 3. Human user authentication & tenancy verification path
        if (!currentUser.UserId.HasValue)
        {
            await WriteProblemAsync(context, Error.Unauthorized("AUTH_INVALID_TOKEN", "Authenticated principal is missing subject identifier."));
            return;
        }

        var user = await userDirectory.GetByIdAsync(currentUser.UserId.Value, context.RequestAborted);
        if (user is null || !user.IsActive || user.IsLockedOut)
        {
            await WriteProblemAsync(context, Error.Unauthorized("AUTH_ACCOUNT_INACTIVE", "User account is disabled or locked."));
            return;
        }

        if (currentUser.SessionId.HasValue)
        {
            var sessionActive = await userDirectory.IsSessionActiveAsync(currentUser.UserId.Value, currentUser.SessionId.Value, context.RequestAborted);
            if (!sessionActive)
            {
                await WriteProblemAsync(context, Error.Unauthorized("AUTH_SESSION_REVOKED", "User session has been revoked."));
                return;
            }
        }

        var spoofHeader = context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                          ?? context.Request.Headers["Tenant-ID"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(spoofHeader))
        {
            if (!currentUser.IsPlatformUser)
            {
                if (!Guid.TryParse(spoofHeader, out var headerTenantId) ||
                    !currentUser.TenantId.HasValue ||
                    headerTenantId != currentUser.TenantId.Value)
                {
                    logger.LogWarning(
                        "Rejected tenant header spoofing attempt by user {UserId}: header={HeaderTenant}, claim={ClaimTenant}",
                        currentUser.UserId, spoofHeader, currentUser.TenantId);

                    await WriteProblemAsync(context, Error.Forbidden(
                        "TENANT_SPOOFING_DETECTED",
                        "Client-supplied tenant headers cannot override authenticated tenant context."));
                    return;
                }
            }
        }

        if (currentUser.TenantId.HasValue)
        {
            var access = await tenantValidator.ValidateMembershipAsync(
                currentUser.UserId.Value,
                currentUser.TenantId.Value,
                context.RequestAborted);

            if (access is null || !access.IsActiveMember)
            {
                await WriteProblemAsync(context, Error.Forbidden(
                    "TENANT_MEMBERSHIP_REQUIRED",
                    "User does not have an active membership in the requested tenant."));
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            var isAuthEndpoint = path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase);

            if (access.TenantStatus == TenantLifecycleStatus.Suspended && !isAuthEndpoint)
            {
                await WriteProblemAsync(context, Error.Forbidden(
                    "TENANT_SUSPENDED",
                    "This tenant is currently suspended. Business operations are disabled."));
                return;
            }

            if (access.TenantStatus == TenantLifecycleStatus.Archived && !isAuthEndpoint)
            {
                await WriteProblemAsync(context, Error.Forbidden(
                    "TENANT_ARCHIVED",
                    "This tenant has been archived."));
                return;
            }

            tenantContext.SetTenant(access.TenantId);
        }

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["ActorType"] = currentUser.IsPlatformUser ? "PlatformUser" : "TenantUser",
            ["UserId"] = currentUser.UserId.Value.ToString("D"),
            ["TenantId"] = tenantContext.TenantId?.ToString("D") ?? "platform"
        }))
        {
            await next(context);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, Error error)
    {
        var result = ApiResultMapper.ToProblemResult(error, context);
        context.Response.StatusCode = result.StatusCode ?? StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(result.Value, context.RequestAborted);
    }
}

/// <summary>
/// Enforces HTTP Idempotency-Key semantics for POST/PUT/PATCH mutations backed by PostgreSQL unique constraints.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Idempotency-Key";

    public async Task InvokeAsync(
        HttpContext context,
        MessagingDbContext messagingDb,
        ITenantContext tenantContext,
        ISystemClock clock)
    {
        if (!HttpMethods.IsPost(context.Request.Method) &&
            !HttpMethods.IsPut(context.Request.Method) &&
            !HttpMethods.IsPatch(context.Request.Method))
        {
            await next(context);
            return;
        }

        var key = context.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            await next(context);
            return;
        }

        if (key.Length > 128)
        {
            var err = ApiResultMapper.ToProblemResult(
                Error.Validation("INVALID_IDEMPOTENCY_KEY", "Idempotency-Key must be 128 characters or fewer."),
                context);
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(err.Value, context.RequestAborted);
            return;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var bodyText = await reader.ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;

        var route = $"{context.Request.Method}:{context.Request.Path.Value}";
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{route}|{bodyText}")));
        var tenantId = tenantContext.TenantId;

        var record = IdempotencyRecord.Start(key, tenantId, route, requestHash, clock.UtcNow);
        messagingDb.IdempotencyRecords.Add(record);

        try
        {
            await messagingDb.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            messagingDb.ChangeTracker.Clear();

            var existing = await messagingDb.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == key && x.TenantId == tenantId && x.Route == route, context.RequestAborted);

            if (existing is null)
            {
                await WriteConflictAsync(context, "IDEMPOTENCY_CONFLICT", "Concurrent request with the same Idempotency-Key is in progress.");
                return;
            }

            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                await WriteConflictAsync(context, "IDEMPOTENCY_PAYLOAD_MISMATCH", "Idempotency-Key was already used with a different request payload.");
                return;
            }

            if (!existing.IsCompleted)
            {
                await WriteConflictAsync(context, "IDEMPOTENCY_IN_PROGRESS", "A request with this Idempotency-Key is currently being processed.");
                return;
            }

            context.Response.StatusCode = existing.StatusCode;
            context.Response.ContentType = existing.ContentType;
            context.Response.Headers["X-Idempotent-Replayed"] = "true";
            await context.Response.WriteAsync(existing.ResponseBody, context.RequestAborted);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
            buffer.Position = 0;
            var responseBody = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync(context.RequestAborted);

            if (context.Response.StatusCode >= 500)
            {
                messagingDb.IdempotencyRecords.Remove(record);
                await messagingDb.SaveChangesAsync(context.RequestAborted);
            }
            else
            {
                record.Complete(context.Response.StatusCode, responseBody, context.Response.ContentType ?? "application/json");
                await messagingDb.SaveChangesAsync(context.RequestAborted);
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        catch
        {
            messagingDb.IdempotencyRecords.Remove(record);
            try { await messagingDb.SaveChangesAsync(CancellationToken.None); } catch { /* ignore cleanup failure */ }
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static async Task WriteConflictAsync(HttpContext context, string code, string message)
    {
        var result = ApiResultMapper.ToProblemResult(Error.Conflict(code, message), context);
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(result.Value, context.RequestAborted);
    }
}
