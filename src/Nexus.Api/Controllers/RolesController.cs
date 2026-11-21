using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.AccessControl.Application;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/roles")]
public sealed class RolesController(IAccessControlService accessControl) : ControllerBase
{
    [HttpGet("permissions")]
    [RequirePermission(Permissions.RolesRead)]
    public async Task<IActionResult> ListPermissions(CancellationToken ct)
        => (await accessControl.ListPermissionsAsync(ct)).ToActionResult(HttpContext);

    [HttpGet]
    [RequirePermission(Permissions.RolesRead)]
    public async Task<IActionResult> ListRoles(CancellationToken ct)
        => (await accessControl.ListRolesAsync(ct)).ToActionResult(HttpContext);

    [HttpPost]
    [RequirePermission(Permissions.RolesCreate)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request, CancellationToken ct)
        => (await accessControl.CreateRoleAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("assign")]
    [RequirePermission(Permissions.RolesAssign)]
    public async Task<IActionResult> AssignRole([FromBody] AssignRoleRequest request, CancellationToken ct)
        => (await accessControl.AssignRoleAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpDelete("assignments/{userRoleId:guid}")]
    [RequirePermission(Permissions.RolesAssign)]
    public async Task<IActionResult> RevokeRole(Guid userRoleId, CancellationToken ct)
        => (await accessControl.RevokeRoleAsync(userRoleId, ct)).ToActionResult(HttpContext);

    [HttpGet("users/{userId:guid}")]
    [RequirePermission(Permissions.RolesRead)]
    public async Task<IActionResult> ListUserRoles(Guid userId, CancellationToken ct)
        => (await accessControl.ListUserRolesAsync(userId, ct)).ToActionResult(HttpContext);
}
