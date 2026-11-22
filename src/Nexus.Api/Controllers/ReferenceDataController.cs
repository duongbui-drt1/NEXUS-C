using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Configuration.Application;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/reference-data")]
public sealed class ReferenceDataController(IReferenceDataService service) : ControllerBase
{
    [HttpGet("categories")]
    [Authorize]
    public async Task<IActionResult> ListCategories(CancellationToken ct)
        => Ok(await service.ListCategoriesAsync(ct));

    [HttpGet("{category}")]
    [Authorize]
    public async Task<IActionResult> ListByCategory(
        string category,
        [FromQuery] string? search = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
        => Ok(await service.ListByCategoryAsync(category, search, includeInactive, ct));

    [HttpPost]
    [RequirePermission(Permissions.ReferenceDataManage)]
    public async Task<IActionResult> Create([FromBody] CreateReferenceDataRequest request, CancellationToken ct)
        => (await service.CreateItemAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.ReferenceDataManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateReferenceDataRequest request, CancellationToken ct)
        => (await service.UpdateItemAsync(id, request, ct)).ToActionResult(HttpContext);
}
