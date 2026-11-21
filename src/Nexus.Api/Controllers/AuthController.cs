using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Identity.Application;
using Nexus.SharedKernel.Context;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        => (await authService.LoginAsync(request, ct)).ToActionResult(HttpContext);

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
        => (await authService.RefreshAsync(request, ct)).ToActionResult(HttpContext);

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
        => (await authService.LogoutAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("switch-tenant")]
    [Authorize]
    public async Task<IActionResult> SwitchTenant([FromBody] SwitchTenantRequest request, CancellationToken ct)
        => (await authService.SwitchTenantAsync(request, ct)).ToActionResult(HttpContext);

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
            currentUser.IsPlatformUser
        };
        return Ok(ApiResponse<object>.Ok(data, ApiResultMapper.GetCorrelationId(HttpContext)));
    }
}
