using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Integration.Application;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/integrations")]
public sealed class IntegrationsController(IIntegrationService integrationService) : ControllerBase
{
    [HttpPost("providers")]
    [RequirePermission(Permissions.PlatformConfigurationManage)]
    public async Task<IActionResult> RegisterProvider([FromBody] CreateProviderRequest request, CancellationToken ct)
        => (await integrationService.RegisterProviderAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("providers")]
    [RequirePermission(Permissions.IntegrationsRead)]
    public async Task<IActionResult> ListProviders(CancellationToken ct)
        => (await integrationService.ListProvidersAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("connections")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> CreateConnection([FromBody] CreateConnectionRequest request, CancellationToken ct)
        => (await integrationService.CreateConnectionAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("connections")]
    [RequirePermission(Permissions.IntegrationsRead)]
    public async Task<IActionResult> ListConnections(CancellationToken ct)
        => (await integrationService.ListConnectionsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("connections/{connectionId:guid}/sync-logs")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> RecordSync(Guid connectionId, [FromBody] RecordSyncRequest request, CancellationToken ct)
        => (await integrationService.RecordSyncAsync(connectionId, request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("connections/{connectionId:guid}/sync-logs")]
    [RequirePermission(Permissions.IntegrationsRead)]
    public async Task<IActionResult> ListSyncLogs(Guid connectionId, CancellationToken ct)
        => (await integrationService.ListSyncLogsAsync(connectionId, ct)).ToActionResult(HttpContext);

    [HttpPost("webhooks")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> RegisterWebhook([FromBody] CreateWebhookRequest request, CancellationToken ct)
        => (await integrationService.RegisterWebhookAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("ai/models")]
    [RequirePermission(Permissions.AiGatewayManage)]
    public async Task<IActionResult> RegisterAiModel([FromBody] RegisterAiModelRequest request, CancellationToken ct)
        => (await integrationService.RegisterAiModelAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("ai/invoke")]
    [RequirePermission(Permissions.AiGatewayInvoke)]
    public async Task<IActionResult> InvokeAi([FromBody] InvokeAiRequest request, CancellationToken ct)
        => (await integrationService.InvokeAiGatewayAsync(request, ct)).ToActionResult(HttpContext);
}
