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

    [HttpPost("connections/{connectionId:guid}/test")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> TestConnection(Guid connectionId, CancellationToken ct)
        => (await integrationService.TestConnectionAsync(connectionId, ct)).ToActionResult(HttpContext);

    [HttpPost("connections/{connectionId:guid}/disable")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> DisableConnection(Guid connectionId, CancellationToken ct)
        => (await integrationService.SetConnectionActiveAsync(connectionId, false, ct)).ToActionResult(HttpContext);

    [HttpPost("connections/{connectionId:guid}/reconnect")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> ReconnectConnection(Guid connectionId, CancellationToken ct)
        => (await integrationService.SetConnectionActiveAsync(connectionId, true, ct)).ToActionResult(HttpContext);

    [HttpPost("connections/{connectionId:guid}/rotate-credential-ref")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> RotateConnectionCredentialRef(Guid connectionId, [FromBody] RotateConnectionCredentialRefRequest request, CancellationToken ct)
        => (await integrationService.RotateConnectionCredentialRefAsync(connectionId, request, ct)).ToActionResult(HttpContext);

    [HttpDelete("connections/{connectionId:guid}")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> DeleteConnection(Guid connectionId, CancellationToken ct)
        => (await integrationService.DeleteConnectionAsync(connectionId, ct)).ToActionResult(HttpContext);

    [HttpPost("connections/{connectionId:guid}/sync-logs")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> RecordSync(Guid connectionId, [FromBody] RecordSyncRequest request, CancellationToken ct)
        => (await integrationService.RecordSyncAsync(connectionId, request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("connections/{connectionId:guid}/sync-logs")]
    [RequirePermission(Permissions.IntegrationsRead)]
    public async Task<IActionResult> ListSyncLogs(Guid connectionId, CancellationToken ct)
        => (await integrationService.ListSyncLogsAsync(connectionId, ct)).ToActionResult(HttpContext);

    [HttpGet("errors")]
    [RequirePermission(Permissions.IntegrationsRead)]
    public async Task<IActionResult> ListErrors(CancellationToken ct)
        => (await integrationService.ListErrorsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("webhooks")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> RegisterWebhook([FromBody] CreateWebhookRequest request, CancellationToken ct)
        => (await integrationService.RegisterWebhookAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("webhooks")]
    [RequirePermission(Permissions.IntegrationsRead)]
    public async Task<IActionResult> ListWebhooks(CancellationToken ct)
        => (await integrationService.ListWebhooksAsync(ct)).ToActionResult(HttpContext);

    [HttpPut("webhooks/{webhookId:guid}")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> UpdateWebhook(Guid webhookId, [FromBody] UpdateWebhookRequest request, CancellationToken ct)
        => (await integrationService.UpdateWebhookAsync(webhookId, request, ct)).ToActionResult(HttpContext);

    [HttpPost("webhooks/{webhookId:guid}/disable")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> DisableWebhook(Guid webhookId, CancellationToken ct)
        => (await integrationService.SetWebhookActiveAsync(webhookId, false, ct)).ToActionResult(HttpContext);

    [HttpPost("webhooks/{webhookId:guid}/enable")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> EnableWebhook(Guid webhookId, CancellationToken ct)
        => (await integrationService.SetWebhookActiveAsync(webhookId, true, ct)).ToActionResult(HttpContext);

    [HttpDelete("webhooks/{webhookId:guid}")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> DeleteWebhook(Guid webhookId, CancellationToken ct)
        => (await integrationService.DeleteWebhookAsync(webhookId, ct)).ToActionResult(HttpContext);

    [HttpPost("webhooks/{webhookId:guid}/rotate-secret")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> RotateWebhookSecret(Guid webhookId, CancellationToken ct)
        => (await integrationService.RotateWebhookSecretAsync(webhookId, ct)).ToActionResult(HttpContext);

    [HttpPost("webhooks/{webhookId:guid}/dispatch")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> DispatchWebhook(Guid webhookId, [FromBody] DispatchWebhookRequest request, CancellationToken ct)
        => (await integrationService.DispatchWebhookAsync(webhookId, request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("webhooks/{webhookId:guid}/deliveries")]
    [RequirePermission(Permissions.IntegrationsRead)]
    public async Task<IActionResult> ListWebhookDeliveries(Guid webhookId, CancellationToken ct)
        => (await integrationService.ListWebhookDeliveriesAsync(webhookId, ct)).ToActionResult(HttpContext);

    [HttpPost("webhooks/{webhookId:guid}/deliveries")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> RecordWebhookDelivery(Guid webhookId, [FromBody] RecordWebhookDeliveryRequest request, CancellationToken ct)
        => (await integrationService.RecordWebhookDeliveryAsync(webhookId, request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("webhooks/deliveries/{deliveryId:guid}/retry")]
    [RequirePermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> RetryWebhookDelivery(Guid deliveryId, CancellationToken ct)
        => (await integrationService.RetryWebhookDeliveryAsync(deliveryId, ct)).ToActionResult(HttpContext);

    [HttpPost("ai/models")]
    [RequirePermission(Permissions.AiGatewayManage)]
    public async Task<IActionResult> RegisterAiModel([FromBody] RegisterAiModelRequest request, CancellationToken ct)
        => (await integrationService.RegisterAiModelAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("ai/invoke")]
    [RequirePermission(Permissions.AiGatewayInvoke)]
    public async Task<IActionResult> InvokeAi([FromBody] InvokeAiRequest request, CancellationToken ct)
        => (await integrationService.InvokeAiGatewayAsync(request, ct)).ToActionResult(HttpContext);
}
