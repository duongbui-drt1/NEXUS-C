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

    [HttpGet("templates")]
    [RequirePermission(Permissions.NotificationsRead)]
    public async Task<IActionResult> ListTemplates(CancellationToken ct)
        => (await notificationService.ListTemplatesAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("send")]
    [RequirePermission(Permissions.NotificationsManage)]
    public async Task<IActionResult> Send([FromBody] SendNotificationRequest request, CancellationToken ct)
        => (await notificationService.SendAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("{notificationId:guid}/retry")]
    [RequirePermission(Permissions.NotificationsManage)]
    public async Task<IActionResult> Retry(Guid notificationId, CancellationToken ct)
        => (await notificationService.RetryAsync(notificationId, ct)).ToActionResult(HttpContext);

    [HttpGet]
    [RequirePermission(Permissions.NotificationsRead)]
    public async Task<IActionResult> ListMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => (await notificationService.ListForCurrentUserAsync(new PagedRequest { Page = page, PageSize = pageSize }, ct)).ToActionResult(HttpContext);

    [HttpPost("{notificationId:guid}/read")]
    [RequirePermission(Permissions.NotificationsRead)]
    public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken ct)
        => (await notificationService.MarkAsReadAsync(notificationId, ct)).ToActionResult(HttpContext);

    [HttpGet("preferences")]
    [RequirePermission(Permissions.NotificationsRead)]
    public async Task<IActionResult> GetMyPreferences(CancellationToken ct)
        => (await notificationService.GetMyPreferenceAsync(ct)).ToActionResult(HttpContext);

    [HttpPut("preferences")]
    [RequirePermission(Permissions.NotificationsRead)]
    public async Task<IActionResult> UpsertMyPreferences([FromBody] UpsertNotificationPreferenceRequest request, CancellationToken ct)
        => (await notificationService.UpsertMyPreferenceAsync(request, ct)).ToActionResult(HttpContext);
}
