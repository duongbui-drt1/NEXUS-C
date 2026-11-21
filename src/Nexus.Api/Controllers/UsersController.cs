using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Identity.Application;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

public sealed record DisableUserRequest(string Reason);

[ApiController]
[Route("api/v1/platform/users")]
public sealed class UsersController(IAuthService authService) : ControllerBase
{
    [HttpPost]
    [RequirePermission(Permissions.UsersCreate)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
        => (await authService.CreateUserAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("invite")]
    [RequirePermission(Permissions.UsersCreate)]
    public async Task<IActionResult> Invite([FromBody] InviteUserRequest request, CancellationToken ct)
        => (await authService.InviteUserToCurrentTenantAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet]
    [RequirePermission(Permissions.UsersRead)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken ct = default)
        => (await authService.ListUsersAsync(new PagedRequest { Page = page, PageSize = pageSize, Search = search }, ct)).ToActionResult(HttpContext);

    [HttpPost("{userId:guid}/disable")]
    [RequirePermission(Permissions.UsersManage)]
    public async Task<IActionResult> Disable(Guid userId, [FromBody] DisableUserRequest request, CancellationToken ct)
        => (await authService.DisableUserAsync(userId, request.Reason, ct)).ToActionResult(HttpContext);

    [HttpPost("{userId:guid}/enable")]
    [RequirePermission(Permissions.UsersManage)]
    public async Task<IActionResult> Enable(Guid userId, CancellationToken ct)
        => (await authService.EnableUserAsync(userId, ct)).ToActionResult(HttpContext);

    [HttpDelete("{userId:guid}/sessions/{sessionId:guid}")]
    [RequirePermission(Permissions.UsersManage)]
    public async Task<IActionResult> RevokeUserSession(Guid userId, Guid sessionId, CancellationToken ct)
        => (await authService.RevokeSessionAsync(userId, sessionId, "AdminRevoked", ct)).ToActionResult(HttpContext);
}
