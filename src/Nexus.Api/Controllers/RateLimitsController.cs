using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Configuration.Application;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/rate-limits")]
public sealed class RateLimitsController(IRateLimitPolicyService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.ApiRead)]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await service.ListPoliciesAsync(ct));

    [HttpPost]
    [RequirePermission(Permissions.RateLimitsManage)]
    public async Task<IActionResult> Create([FromBody] CreateRateLimitPolicyCommand cmd, CancellationToken ct)
        => (await service.CreatePolicyAsync(cmd, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.RateLimitsManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRateLimitPolicyCommand cmd, CancellationToken ct)
        => (await service.UpdatePolicyAsync(id, cmd, ct)).ToActionResult(HttpContext);

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.RateLimitsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => (await service.DeletePolicyAsync(id, ct)).ToActionResult(HttpContext, StatusCodes.Status204NoContent);
}
