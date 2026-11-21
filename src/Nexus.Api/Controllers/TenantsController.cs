using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/tenants")]
public sealed class TenantsController(ITenantService tenantService, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost]
    [RequirePermission(Permissions.TenantsCreate)]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request, CancellationToken ct)
        => (await tenantService.CreateAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet]
    [RequirePermission(Permissions.TenantsRead)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = true,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
        => (await tenantService.ListAsync(new PagedRequest { Page = page, PageSize = pageSize, Search = search, SortBy = sortBy, SortDescending = sortDescending }, status, ct))
            .ToActionResult(HttpContext);

    [HttpGet("accessible")]
    [Authorize]
    public async Task<IActionResult> ListAccessible(CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue) return Unauthorized();
        return (await tenantService.ListAccessibleTenantsAsync(currentUser.UserId.Value, ct)).ToActionResult(HttpContext);
    }

    [HttpGet("{tenantId:guid}")]
    [RequirePermission(Permissions.TenantsRead)]
    public async Task<IActionResult> GetById(Guid tenantId, CancellationToken ct)
        => (await tenantService.GetByIdAsync(tenantId, ct)).ToActionResult(HttpContext);

    [HttpPut("{tenantId:guid}")]
    [RequirePermission(Permissions.TenantsUpdate)]
    public async Task<IActionResult> Update(Guid tenantId, [FromBody] UpdateTenantRequest request, CancellationToken ct)
        => (await tenantService.UpdateAsync(tenantId, request, ct)).ToActionResult(HttpContext);

    [HttpPost("{tenantId:guid}/suspend")]
    [RequirePermission(Permissions.TenantsSuspend)]
    public async Task<IActionResult> Suspend(Guid tenantId, [FromBody] SuspendTenantRequest request, CancellationToken ct)
        => (await tenantService.SuspendAsync(tenantId, request, ct)).ToActionResult(HttpContext);

    [HttpPost("{tenantId:guid}/activate")]
    [RequirePermission(Permissions.TenantsSuspend)]
    public async Task<IActionResult> Activate(Guid tenantId, CancellationToken ct)
        => (await tenantService.ActivateAsync(tenantId, ct)).ToActionResult(HttpContext);

    [HttpPost("{tenantId:guid}/memberships/{userId:guid}")]
    [RequirePermission(Permissions.UsersManage)]
    public async Task<IActionResult> AddMembership(Guid tenantId, Guid userId, CancellationToken ct)
        => (await tenantService.AddMembershipAsync(tenantId, userId, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpDelete("{tenantId:guid}/memberships/{userId:guid}")]
    [RequirePermission(Permissions.UsersManage)]
    public async Task<IActionResult> RevokeMembership(Guid tenantId, Guid userId, CancellationToken ct)
        => (await tenantService.RevokeMembershipAsync(tenantId, userId, ct)).ToActionResult(HttpContext);
}
