using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Identity.Application;

namespace Nexus.Api.Controllers;

/// <summary>
/// Public inbound Clerk webhook endpoint protected strictly by Svix HMAC-SHA256 signature verification on the raw request body.
/// Separate from outbound customer webhooks by design.
/// </summary>
[ApiController]
[Route("api/v1/webhooks/clerk")]
public sealed class ClerkWebhooksController(IClerkSyncService clerkSyncService) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);

        var svixId = Request.Headers[SvixSignatureVerifier.IdHeader].FirstOrDefault();
        var svixTimestamp = Request.Headers[SvixSignatureVerifier.TimestampHeader].FirstOrDefault();
        var svixSignature = Request.Headers[SvixSignatureVerifier.SignatureHeader].FirstOrDefault();

        var result = await clerkSyncService.ProcessWebhookAsync(rawBody, svixId, svixTimestamp, svixSignature, ct);
        return result.ToActionResult(HttpContext);
    }
}
