using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Web;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/api-logs")]
public sealed class ApiLogsController(IApiRequestLogService service) : ControllerBase
{
    public sealed record PurgeApiLogsRequest(int RetentionDays = 30);

    [HttpGet]
    [RequirePermission(Permissions.ApiLogsRead)]
    public async Task<IActionResult> Query(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? route = null,
        [FromQuery] int? statusCode = null,
        [FromQuery] string? applicationCode = null,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] string? rateLimitDecision = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await service.QueryAsync(new ApiRequestLogQuery(
            from,
            to,
            route,
            statusCode,
            applicationCode,
            tenantId,
            correlationId,
            rateLimitDecision,
            page,
            pageSize), ct);
        return Ok(result);
    }

    [HttpPost("purge")]
    [RequirePermission(Permissions.RateLimitsManage)]
    public async Task<IActionResult> Purge([FromBody] PurgeApiLogsRequest request, CancellationToken ct)
    {
        var res = await service.PurgeExpiredAsync(request.RetentionDays, ct);
        if (res.IsFailure)
        {
            return res.ToActionResult(HttpContext);
        }

        return Ok(new { deletedCount = res.Value, retentionDays = request.RetentionDays });
    }
}
