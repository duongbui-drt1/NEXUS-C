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

public sealed class CorrelationIdMiddleware(RequestDelegate next)
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

        await next(context);
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
/// Resolves and validates tenant context strictly from authenticated JWT claims and server-side membership records.
/// Rejects tenant header spoofing, revoked sessions, locked/disabled users, and suspended tenants.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        ITenantAccessValidator tenantValidator,
        IUserDirectoryReader userDirectory)
    {
        if (!currentUser.IsAuthenticated)
        {
            await next(context);
            return;
        }

        if (!currentUser.UserId.HasValue)
        {
            await WriteProblemAsync(context, Error.Unauthorized("AUTH_INVALID_TOKEN", "Authenticated principal is missing subject identifier."));
            return;
        }

        // Verify user account is active & not locked out
        var user = await userDirectory.GetByIdAsync(currentUser.UserId.Value, context.RequestAborted);
        if (user is null || !user.IsActive || user.IsLockedOut)
        {
            await WriteProblemAsync(context, Error.Unauthorized("AUTH_ACCOUNT_INACTIVE", "User account is disabled or locked."));
            return;
        }

        // Verify session has not been revoked
        if (currentUser.SessionId.HasValue)
        {
            var sessionActive = await userDirectory.IsSessionActiveAsync(currentUser.UserId.Value, currentUser.SessionId.Value, context.RequestAborted);
            if (!sessionActive)
            {
                await WriteProblemAsync(context, Error.Unauthorized("AUTH_SESSION_REVOKED", "User session has been revoked."));
                return;
            }
        }

        // Anti-spoofing check: if a client sends X-Tenant-Id header that disagrees with the authenticated tenant claim, reject immediately!
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

        // Bind verified tenant context for tenant-scoped tokens
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

            // Allow auth logout/switch/me endpoints even if tenant is suspended, block business endpoints
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

        await next(context);
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

        // Capture response for replay
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
                // Do not permanently lock key on server error so clients may safely retry
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
