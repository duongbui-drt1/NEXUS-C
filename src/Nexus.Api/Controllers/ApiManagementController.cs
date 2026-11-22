using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Identity.Application;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

public sealed record RotateSecretRequest(int OverlapMinutes = 0);
public sealed record RotateApiKeyRequest(DateTimeOffset? NewExpiresAt = null, int OverlapMinutes = 0);

[ApiController]
[Route("api/v1/platform")]
public sealed class ApiManagementController(IServiceClientService serviceClientService) : ControllerBase
{
    [HttpGet("api-clients")]
    [RequirePermission(Permissions.ApiRead)]
    public async Task<IActionResult> ListServiceClients(CancellationToken ct)
        => (await serviceClientService.ListServiceClientsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("api-clients")]
    [RequirePermission(Permissions.ApiManage)]
    public async Task<IActionResult> CreateServiceClient([FromBody] CreateServiceClientRequest request, CancellationToken ct)
        => (await serviceClientService.CreateServiceClientAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("api-clients/{id:guid}/rotate-secret")]
    [RequirePermission(Permissions.ApiManage)]
    public async Task<IActionResult> RotateServiceClientSecret(Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RotateSecretRequest? request, CancellationToken ct)
        => (await serviceClientService.RotateServiceClientSecretAsync(id, request?.OverlapMinutes ?? 0, ct)).ToActionResult(HttpContext);

    [HttpPost("api-clients/{id:guid}/revoke")]
    [RequirePermission(Permissions.ApiManage)]
    public async Task<IActionResult> RevokeServiceClient(Guid id, CancellationToken ct)
        => (await serviceClientService.RevokeServiceClientAsync(id, ct)).ToActionResult(HttpContext);

    [HttpGet("api-keys")]
    [RequirePermission(Permissions.ApiRead)]
    public async Task<IActionResult> ListApiKeys(CancellationToken ct)
        => (await serviceClientService.ListApiKeysAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("api-keys")]
    [RequirePermission(Permissions.ApiManage)]
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request, CancellationToken ct)
        => (await serviceClientService.CreateApiKeyAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("api-keys/{id:guid}/rotate")]
    [RequirePermission(Permissions.ApiManage)]
    public async Task<IActionResult> RotateApiKey(Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RotateApiKeyRequest? request, CancellationToken ct)
        => (await serviceClientService.RotateApiKeyAsync(id, request?.NewExpiresAt, request?.OverlapMinutes ?? 0, ct)).ToActionResult(HttpContext);

    [HttpDelete("api-keys/{id:guid}")]
    [RequirePermission(Permissions.ApiManage)]
    public async Task<IActionResult> RevokeApiKey(Guid id, CancellationToken ct)
        => (await serviceClientService.RevokeApiKeyAsync(id, ct)).ToActionResult(HttpContext);
}
