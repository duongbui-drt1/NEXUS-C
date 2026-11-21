using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Organization.Application;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/organizations")]
public sealed class OrganizationsController(IOrganizationService orgService) : ControllerBase
{
    [HttpPost("legal-entities")]
    [RequirePermission(Permissions.OrganizationsManage)]
    public async Task<IActionResult> CreateLegalEntity([FromBody] CreateLegalEntityRequest request, CancellationToken ct)
        => (await orgService.CreateLegalEntityAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("legal-entities")]
    [RequirePermission(Permissions.OrganizationsRead)]
    public async Task<IActionResult> ListLegalEntities([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken ct = default)
        => (await orgService.ListLegalEntitiesAsync(new PagedRequest { Page = page, PageSize = pageSize, Search = search }, ct)).ToActionResult(HttpContext);

    [HttpGet("legal-entities/{id:guid}")]
    [RequirePermission(Permissions.OrganizationsRead)]
    public async Task<IActionResult> GetLegalEntity(Guid id, CancellationToken ct)
        => (await orgService.GetLegalEntityAsync(id, ct)).ToActionResult(HttpContext);

    [HttpPut("legal-entities/{id:guid}")]
    [RequirePermission(Permissions.OrganizationsManage)]
    public async Task<IActionResult> UpdateLegalEntity(Guid id, [FromBody] UpdateLegalEntityRequest request, CancellationToken ct)
        => (await orgService.UpdateLegalEntityAsync(id, request, ct)).ToActionResult(HttpContext);

    [HttpDelete("legal-entities/{id:guid}")]
    [RequirePermission(Permissions.OrganizationsManage)]
    public async Task<IActionResult> DeleteLegalEntity(Guid id, CancellationToken ct)
        => (await orgService.DeleteLegalEntityAsync(id, ct)).ToActionResult(HttpContext);

    [HttpPost("branches")]
    [RequirePermission(Permissions.OrganizationsManage)]
    public async Task<IActionResult> CreateBranch([FromBody] CreateBranchRequest request, CancellationToken ct)
        => (await orgService.CreateBranchAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("branches")]
    [RequirePermission(Permissions.OrganizationsRead)]
    public async Task<IActionResult> ListBranches([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? legalEntityId = null, CancellationToken ct = default)
        => (await orgService.ListBranchesAsync(new PagedRequest { Page = page, PageSize = pageSize }, legalEntityId, ct)).ToActionResult(HttpContext);

    [HttpGet("branches/{id:guid}")]
    [RequirePermission(Permissions.OrganizationsRead)]
    public async Task<IActionResult> GetBranch(Guid id, CancellationToken ct)
        => (await orgService.GetBranchAsync(id, ct)).ToActionResult(HttpContext);

    [HttpPut("branches/{id:guid}")]
    [RequirePermission(Permissions.OrganizationsManage)]
    public async Task<IActionResult> UpdateBranch(Guid id, [FromBody] UpdateBranchRequest request, CancellationToken ct)
        => (await orgService.UpdateBranchAsync(id, request, ct)).ToActionResult(HttpContext);

    [HttpDelete("branches/{id:guid}")]
    [RequirePermission(Permissions.OrganizationsManage)]
    public async Task<IActionResult> DeleteBranch(Guid id, CancellationToken ct)
        => (await orgService.DeleteBranchAsync(id, ct)).ToActionResult(HttpContext);

    [HttpPost("units")]
    [RequirePermission(Permissions.OrganizationsManage)]
    public async Task<IActionResult> CreateUnit([FromBody] CreateOrganizationUnitRequest request, CancellationToken ct)
        => (await orgService.CreateUnitAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPut("units/{id:guid}")]
    [RequirePermission(Permissions.OrganizationsManage)]
    public async Task<IActionResult> UpdateUnit(Guid id, [FromBody] UpdateOrganizationUnitRequest request, CancellationToken ct)
        => (await orgService.UpdateUnitAsync(id, request, ct)).ToActionResult(HttpContext);

    [HttpGet("units")]
    [RequirePermission(Permissions.OrganizationsRead)]
    public async Task<IActionResult> ListUnits(CancellationToken ct)
        => (await orgService.ListUnitsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("assignments")]
    [RequirePermission(Permissions.OrganizationsManage)]
    public async Task<IActionResult> AssignUser([FromBody] AssignUserOrganizationRequest request, CancellationToken ct)
        => (await orgService.AssignUserAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);
}
