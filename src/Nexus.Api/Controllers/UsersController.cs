using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Identity.Application;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

public sealed record DisableUserRequest(string Reason);

[ApiController]
[Route("api/v1/platform/users")]
public sealed class UsersController(IAuthService authService, IClerkSyncService clerkSyncService) : ControllerBase
{
    private static readonly JsonSerializerOptions ProfileJsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly HashSet<string> ForbiddenProfileFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "role", "roles", "roleId", "roleIds", "roleName", "roleNames",
        "permission", "permissions", "permissionCodes",
        "tenantId", "tenantIds", "membership", "memberships",
        "isPlatformUser", "entitlements", "scopes", "password", "passwordHash", "securityStamp"
    };

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

    [HttpGet("{userId:guid}")]
    [RequirePermission(Permissions.UsersRead)]
    public async Task<IActionResult> GetDetail(Guid userId, CancellationToken ct)
        => (await authService.GetUserDetailAsync(userId, ct)).ToActionResult(HttpContext);

    /// <summary>
    /// Updates approved user profile metadata ONLY. Strictly rejects role, permission, membership, and privilege fields (mass-assignment defense).
    /// </summary>
    [HttpPut("{userId:guid}/profile")]
    [RequirePermission(Permissions.UsersProfileUpdate)]
    public async Task<IActionResult> UpdateProfile(Guid userId, [FromBody] JsonElement rawBody, CancellationToken ct)
    {
        if (rawBody.ValueKind != JsonValueKind.Object)
        {
            return ApiResultMapper.ToProblemResult(Error.Validation("PROFILE_INVALID_BODY", "Profile payload must be a JSON object."), HttpContext);
        }

        foreach (var prop in rawBody.EnumerateObject())
        {
            if (ForbiddenProfileFields.Contains(prop.Name))
            {
                return ApiResultMapper.ToProblemResult(
                    Error.BusinessRule(
                        "PROFILE_PRIVILEGE_FIELDS_FORBIDDEN",
                        $"Field '{prop.Name}' cannot be modified through profile editing. Role, permission, and membership changes require separate authorization workflows."),
                    HttpContext);
            }
        }

        var request = rawBody.Deserialize<UpdateUserProfileRequest>(ProfileJsonOpts) ?? new UpdateUserProfileRequest();
        return (await authService.UpdateUserProfileAsync(userId, request, ct)).ToActionResult(HttpContext);
    }

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

    [HttpGet("clerk/sync-status")]
    [RequirePermission(Permissions.UsersRead)]
    public async Task<IActionResult> GetClerkSyncStatus(CancellationToken ct)
        => (await clerkSyncService.GetSyncStatusAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("clerk/link")]
    [RequirePermission(Permissions.UsersManage)]
    public async Task<IActionResult> LinkClerkSubject([FromBody] LinkClerkAccountRequest request, CancellationToken ct)
        => (await clerkSyncService.LinkUserToClerkSubjectAsync(request, ct)).ToActionResult(HttpContext);

    [HttpPost("clerk/reconcile")]
    [RequirePermission(Permissions.UsersManage)]
    public async Task<IActionResult> ReconcileClerk(CancellationToken ct)
        => (await clerkSyncService.ReconcileFromClerkApiAsync(ct)).ToActionResult(HttpContext);
}
