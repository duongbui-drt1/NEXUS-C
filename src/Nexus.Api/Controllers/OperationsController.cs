using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Web;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

public sealed record RetryDeadLetterRequest(string? Note = null);

[ApiController]
[Route("api/v1/platform/operations")]
public sealed class OperationsController(IPlatformOperationsService opsService) : ControllerBase
{
    [HttpGet("outbox")]
    [RequirePermission(Permissions.OperationsRead)]
    public async Task<IActionResult> ListOutbox(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? eventType = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken ct = default)
        => (await opsService.ListOutboxAsync(new PagedRequest { Page = page, PageSize = pageSize }, status, eventType, tenantId, ct))
            .ToActionResult(HttpContext);

    [HttpGet("inbox")]
    [RequirePermission(Permissions.OperationsRead)]
    public async Task<IActionResult> ListInbox(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? consumerName = null,
        [FromQuery] string? eventType = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken ct = default)
        => (await opsService.ListInboxAsync(new PagedRequest { Page = page, PageSize = pageSize }, consumerName, eventType, tenantId, ct))
            .ToActionResult(HttpContext);

    [HttpGet("dead-letters")]
    [RequirePermission(Permissions.OperationsRead)]
    public async Task<IActionResult> ListDeadLetters(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? resolved = null,
        [FromQuery] string? eventType = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken ct = default)
        => (await opsService.ListDeadLettersAsync(new PagedRequest { Page = page, PageSize = pageSize }, resolved, eventType, tenantId, ct))
            .ToActionResult(HttpContext);

    [HttpPost("dead-letters/{id:guid}/retry")]
    [RequirePermission(Permissions.OperationsManage)]
    public async Task<IActionResult> RetryDeadLetter(Guid id, [FromBody] RetryDeadLetterRequest? request, CancellationToken ct)
        => (await opsService.RetryDeadLetterAsync(id, request?.Note, ct)).ToActionResult(HttpContext);

    [HttpGet("jobs")]
    [RequirePermission(Permissions.OperationsRead)]
    public IActionResult ListJobs()
        => opsService.ListBackgroundJobs().ToActionResult(HttpContext);
}
