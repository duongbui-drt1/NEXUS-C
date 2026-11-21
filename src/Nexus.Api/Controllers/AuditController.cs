using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Audit.Application;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

/// <summary>
/// Read-only audit log query controller. Audit events are strictly append-only (no PUT/PATCH/DELETE endpoints).
/// </summary>
[ApiController]
[Route("api/v1/platform/audit")]
public sealed class AuditController(IAuditQueryService auditQuery) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.AuditRead)]
    public async Task<IActionResult> Query(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? action = null,
        [FromQuery] string? resourceType = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken ct = default)
        => (await auditQuery.QueryAsync(new PagedRequest { Page = page, PageSize = pageSize }, action, resourceType, tenantId, ct))
            .ToActionResult(HttpContext);
}
