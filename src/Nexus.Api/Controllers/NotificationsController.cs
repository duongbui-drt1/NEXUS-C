using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Notification.Application;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/notifications")]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpPost("templates")]
    [RequirePermission(Permissions.NotificationsManage)]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateTemplateRequest request, CancellationToken ct)
        => (await notificationService.CreateTemplateAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("send")]
    [RequirePermission(Permissions.NotificationsManage)]
    public async Task<IActionResult> Send([FromBody] SendNotificationRequest request, CancellationToken ct)
        => (await notificationService.SendAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet]
    [RequirePermission(Permissions.NotificationsRead)]
    public async Task<IActionResult> ListMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => (await notificationService.ListForCurrentUserAsync(new PagedRequest { Page = page, PageSize = pageSize }, ct)).ToActionResult(HttpContext);

    [HttpPost("{notificationId:guid}/read")]
    [RequirePermission(Permissions.NotificationsRead)]
    public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken ct)
        => (await notificationService.MarkAsReadAsync(notificationId, ct)).ToActionResult(HttpContext);
}
