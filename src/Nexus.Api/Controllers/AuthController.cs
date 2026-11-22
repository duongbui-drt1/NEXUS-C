using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Identity.Application;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Results;

namespace Nexus.Api.Controllers;

public sealed record AuthorizePolicyRequest(string PermissionCode, string? ScopeType = null, string? ScopeId = null, string? ApplicationCode = null);

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    IAuthService authService,
    IServiceClientService serviceClientService,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IPermissionEvaluator permissionEvaluator,
    IOrganizationScopeReader organizationScopeReader) : ControllerBase
{
    public const string RefreshCookieName = "__nexus_rt";
    public const string CsrfHeaderName = "X-Nexus-CSRF";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        if (result.IsSuccess && IsCookieAuthMode())
        {
            AppendRefreshCookie(result.Value.RefreshToken, result.Value.RefreshTokenExpiresAt);
        }
        return result.ToActionResult(HttpContext);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RefreshRequest? request, CancellationToken ct)
    {
        var token = request?.RefreshToken;
        var usedCookie = false;

        if (string.IsNullOrWhiteSpace(token) && Request.Cookies.TryGetValue(RefreshCookieName, out var cookieToken))
        {
            if (!HasValidCsrfHeader())
            {
                return ApiResultMapper.ToProblemResult(
                    Error.Forbidden("CSRF_HEADER_REQUIRED", $"Cookie-based refresh requires '{CsrfHeaderName}: 1' header."),
                    HttpContext);
            }
            token = cookieToken;
            usedCookie = true;
        }

        var result = await authService.RefreshAsync(new RefreshRequest(token ?? string.Empty), ct);
        if (result.IsSuccess && (usedCookie || IsCookieAuthMode()))
        {
            AppendRefreshCookie(result.Value.RefreshToken, result.Value.RefreshTokenExpiresAt);
        }
        else if (result.IsFailure && usedCookie)
        {
            ClearRefreshCookie();
        }

        return result.ToActionResult(HttpContext);
    }

    /// <summary>
    /// OAuth 2.0 Client Credentials token endpoint for service-to-service communication (NEXUS F, E, M).
    /// </summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> IssueServiceToken([FromBody] ClientCredentialsTokenRequest request, CancellationToken ct)
        => (await serviceClientService.IssueClientCredentialsTokenAsync(request, ct)).ToActionResult(HttpContext);

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.ContainsKey(RefreshCookieName))
        {
            if (!HasValidCsrfHeader())
            {
                return ApiResultMapper.ToProblemResult(
                    Error.Forbidden("CSRF_HEADER_REQUIRED", $"Cookie-based logout requires '{CsrfHeaderName}: 1' header."),
                    HttpContext);
            }
            ClearRefreshCookie();
        }

        return (await authService.LogoutAsync(ct)).ToActionResult(HttpContext);
    }

    [HttpPost("switch-tenant")]
    [Authorize]
    public async Task<IActionResult> SwitchTenant([FromBody] SwitchTenantRequest request, CancellationToken ct)
    {
        var result = await authService.SwitchTenantAsync(request, ct);
        if (result.IsSuccess && (IsCookieAuthMode() || Request.Cookies.ContainsKey(RefreshCookieName)))
        {
            AppendRefreshCookie(result.Value.RefreshToken, result.Value.RefreshTokenExpiresAt);
        }
        return result.ToActionResult(HttpContext);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
        => (await authService.ChangePasswordAsync(request, ct)).ToActionResult(HttpContext);

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
        => (await authService.ResetPasswordAsync(request, ct)).ToActionResult(HttpContext);

    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
        => (await authService.VerifyEmailAsync(request, ct)).ToActionResult(HttpContext);

    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> ListMySessions(CancellationToken ct)
        => (await authService.ListMySessionsAsync(ct)).ToActionResult(HttpContext);

    [HttpDelete("sessions/{sessionId:guid}")]
    [Authorize]
    public async Task<IActionResult> RevokeMySession(Guid sessionId, CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue) return Unauthorized();
        return (await authService.RevokeSessionAsync(currentUser.UserId.Value, sessionId, "UserRevoked", ct)).ToActionResult(HttpContext);
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var data = new
        {
            currentUser.UserId,
            currentUser.Email,
            currentUser.TenantId,
            currentUser.SessionId,
            currentUser.Scope,
            currentUser.IsPlatformUser,
            currentUser.IsServicePrincipal,
            currentUser.ServiceClientId
        };
        return Ok(ApiResponse<object>.Ok(data, ApiResultMapper.GetCorrelationId(HttpContext)));
    }

    /// <summary>
    /// Returns the authenticated principal's effective roles and permissions in the current Platform or Tenant scope.
    /// Never accepts a client-supplied userId or tenantId parameter.
    /// </summary>
    [HttpGet("permissions")]
    [Authorize]
    public async Task<IActionResult> EffectivePermissions(CancellationToken ct)
    {
        if (currentUser.IsServicePrincipal)
        {
            var svcData = new
            {
                UserId = currentUser.UserId,
                ServiceClientId = currentUser.ServiceClientId,
                TenantId = tenantContext.TenantId ?? currentUser.TenantId,
                Scope = "service",
                IsPlatformUser = false,
                Roles = Array.Empty<string>(),
                Permissions = currentUser.GrantedScopes
            };
            return Ok(ApiResponse<object>.Ok(svcData, ApiResultMapper.GetCorrelationId(HttpContext)));
        }

        if (!currentUser.UserId.HasValue)
        {
            return Unauthorized();
        }

        var effectiveTenantId = tenantContext.TenantId ?? currentUser.TenantId;
        var isPlatformScope = currentUser.IsPlatformUser && !effectiveTenantId.HasValue;

        var roles = await permissionEvaluator.GetRoleNamesAsync(currentUser.UserId.Value, effectiveTenantId, isPlatformScope, ct);
        var permissions = await permissionEvaluator.GetPermissionsAsync(currentUser.UserId.Value, effectiveTenantId, isPlatformScope, ct);

        var data = new
        {
            UserId = currentUser.UserId.Value,
            TenantId = effectiveTenantId,
            Scope = isPlatformScope ? "platform" : "tenant",
            IsPlatformUser = currentUser.IsPlatformUser,
            Roles = roles,
            Permissions = permissions
        };

        return Ok(ApiResponse<object>.Ok(data, ApiResultMapper.GetCorrelationId(HttpContext)));
    }

    /// <summary>
    /// Returns the authenticated user's effective organizational scope (LegalEntity, Branch, OrganizationUnit subtree) in the current tenant.
    /// </summary>
    [HttpGet("organization-scope")]
    [Authorize]
    public async Task<IActionResult> OrganizationScope(CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue || !tenantContext.HasTenant)
        {
            return ApiResultMapper.ToProblemResult(
                Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required to resolve organizational scope."),
                HttpContext);
        }

        var scope = await organizationScopeReader.GetEffectiveScopeAsync(currentUser.UserId.Value, tenantContext.TenantId!.Value, ct);
        return Ok(ApiResponse<EffectiveOrganizationScope>.Ok(scope, ApiResultMapper.GetCorrelationId(HttpContext)));
    }

    /// <summary>
    /// Authoritative RBAC/ABAC/Organization-scoped policy evaluation endpoint for NEXUS ecosystem applications.
    /// </summary>
    [HttpPost("authorize")]
    [Authorize]
    public async Task<IActionResult> EvaluatePolicy([FromBody] AuthorizePolicyRequest request, CancellationToken ct)
    {
        if (currentUser.IsServicePrincipal)
        {
            var allowed = currentUser.GrantedScopes.Contains(request.PermissionCode, StringComparer.OrdinalIgnoreCase);
            var svcDecision = new AuthorizationDecision(allowed, allowed ? "ALLOWED_BY_SERVICE_SCOPE" : "SERVICE_SCOPE_DENIED");
            return Ok(ApiResponse<AuthorizationDecision>.Ok(svcDecision, ApiResultMapper.GetCorrelationId(HttpContext)));
        }

        if (!currentUser.UserId.HasValue)
        {
            return Unauthorized();
        }

        var effectiveTenantId = tenantContext.TenantId ?? currentUser.TenantId;
        var isPlatformScope = currentUser.IsPlatformUser && !effectiveTenantId.HasValue;

        var effectiveAppCode = !string.IsNullOrWhiteSpace(request.ApplicationCode)
            ? request.ApplicationCode.Trim()
            : Request.Headers["X-Application-Code"].FirstOrDefault()?.Trim() ?? currentUser.ApplicationCode;

        var decision = await permissionEvaluator.EvaluatePolicyAsync(new AuthorizationCheckRequest(
            currentUser.UserId.Value,
            effectiveTenantId,
            request.PermissionCode,
            isPlatformScope,
            request.ScopeType,
            request.ScopeId,
            effectiveAppCode), ct);

        return Ok(ApiResponse<AuthorizationDecision>.Ok(decision, ApiResultMapper.GetCorrelationId(HttpContext)));
    }

    private bool IsCookieAuthMode()
        => string.Equals(Request.Headers["X-Nexus-Auth-Mode"].FirstOrDefault(), "cookie", StringComparison.OrdinalIgnoreCase);

    private bool HasValidCsrfHeader()
        => string.Equals(Request.Headers[CsrfHeaderName].FirstOrDefault(), "1", StringComparison.Ordinal);

    private void AppendRefreshCookie(string refreshToken, DateTimeOffset expiresAt)
    {
        Response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = expiresAt,
            IsEssential = true
        });
    }

    private void ClearRefreshCookie()
    {
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth"
        });
    }
}
