using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Notification.Application;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/email")]
public sealed class EmailController(ISharedEmailService emailService) : ControllerBase
{
    [HttpGet("status")]
    [RequirePermission(Permissions.EmailManage)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
        => (await emailService.GetProviderStatusAsync(ct)).ToActionResult(HttpContext);

    [HttpGet("deliveries")]
    [RequirePermission(Permissions.NotificationsRead)]
    public async Task<IActionResult> ListDeliveries(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
        => (await emailService.ListDeliveriesAsync(new PagedRequest { Page = page, PageSize = pageSize }, status, ct)).ToActionResult(HttpContext);

    [HttpPost("send")]
    [RequirePermission(Permissions.EmailManage)]
    public async Task<IActionResult> Send([FromBody] SendTransactionalEmailRequest request, CancellationToken ct)
        => (await emailService.SendTransactionalEmailAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("suppressions")]
    [RequirePermission(Permissions.EmailManage)]
    public async Task<IActionResult> ListSuppressions(CancellationToken ct)
        => (await emailService.ListSuppressionsAsync(ct)).ToActionResult(HttpContext);

    /// <summary>
    /// Inbound Resend Svix webhook endpoint (delivery, bounce, complaint, delay events).
    /// Separate from outbound customer webhooks by design.
    /// </summary>
    [HttpPost("/api/v1/webhooks/resend")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveResendWebhook(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);

        var svixId = Request.Headers[SvixSignatureVerifier.IdHeader].FirstOrDefault();
        var svixTimestamp = Request.Headers[SvixSignatureVerifier.TimestampHeader].FirstOrDefault();
        var svixSignature = Request.Headers[SvixSignatureVerifier.SignatureHeader].FirstOrDefault();

        return (await emailService.ProcessResendWebhookAsync(rawBody, svixId, svixTimestamp, svixSignature, ct))
            .ToActionResult(HttpContext);
    }
}
