using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Workflow.Application;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

public sealed record CancelWorkflowRequest(string? Reason);

[ApiController]
[Route("api/v1/platform/workflows")]
public sealed class WorkflowsController(IWorkflowService workflowService) : ControllerBase
{
    [HttpPost("definitions")]
    [RequirePermission(Permissions.WorkflowsManage)]
    public async Task<IActionResult> CreateDefinition([FromBody] CreateWorkflowDefinitionRequest request, CancellationToken ct)
        => (await workflowService.CreateDefinitionAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("definitions")]
    [RequirePermission(Permissions.WorkflowsRead)]
    public async Task<IActionResult> ListDefinitions(CancellationToken ct)
        => (await workflowService.ListDefinitionsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("definitions/{definitionId:guid}/versions")]
    [RequirePermission(Permissions.WorkflowsManage)]
    public async Task<IActionResult> PublishVersion(Guid definitionId, [FromBody] PublishWorkflowVersionRequest request, CancellationToken ct)
        => (await workflowService.PublishVersionAsync(definitionId, request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("definitions/{definitionId:guid}/activate")]
    [RequirePermission(Permissions.WorkflowsManage)]
    public async Task<IActionResult> ActivateDefinition(Guid definitionId, CancellationToken ct)
        => (await workflowService.SetDefinitionActiveAsync(definitionId, true, ct)).ToActionResult(HttpContext);

    [HttpPost("definitions/{definitionId:guid}/deactivate")]
    [RequirePermission(Permissions.WorkflowsManage)]
    public async Task<IActionResult> DeactivateDefinition(Guid definitionId, CancellationToken ct)
        => (await workflowService.SetDefinitionActiveAsync(definitionId, false, ct)).ToActionResult(HttpContext);

    [HttpPost("instances")]
    [RequirePermission(Permissions.WorkflowsCreate)]
    public async Task<IActionResult> CreateInstance([FromBody] CreateWorkflowInstanceRequest request, CancellationToken ct)
        => (await workflowService.CreateInstanceAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("instances")]
    [RequirePermission(Permissions.WorkflowsRead)]
    public async Task<IActionResult> ListInstances([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, CancellationToken ct = default)
        => (await workflowService.ListInstancesAsync(new PagedRequest { Page = page, PageSize = pageSize }, status, ct)).ToActionResult(HttpContext);

    [HttpGet("instances/{instanceId:guid}")]
    [RequirePermission(Permissions.WorkflowsRead)]
    public async Task<IActionResult> GetInstance(Guid instanceId, CancellationToken ct)
        => (await workflowService.GetInstanceAsync(instanceId, ct)).ToActionResult(HttpContext);

    [HttpGet("instances/{instanceId:guid}/history")]
    [RequirePermission(Permissions.WorkflowsRead)]
    public async Task<IActionResult> GetHistory(Guid instanceId, CancellationToken ct)
        => (await workflowService.GetHistoryAsync(instanceId, ct)).ToActionResult(HttpContext);

    [HttpPost("tasks/{taskId:guid}/decide")]
    [RequirePermission(Permissions.WorkflowsApprove)]
    public async Task<IActionResult> DecideTask(Guid taskId, [FromBody] DecideApprovalTaskRequest request, CancellationToken ct)
        => (await workflowService.DecideTaskAsync(taskId, request, ct)).ToActionResult(HttpContext);

    [HttpPost("tasks/{taskId:guid}/delegate")]
    [RequirePermission(Permissions.WorkflowsApprove)]
    public async Task<IActionResult> DelegateTask(Guid taskId, [FromBody] DelegateApprovalTaskRequest request, CancellationToken ct)
        => (await workflowService.DelegateTaskAsync(taskId, request, ct)).ToActionResult(HttpContext);

    [HttpPost("instances/{instanceId:guid}/escalate")]
    [RequirePermission(Permissions.WorkflowsManage)]
    public async Task<IActionResult> EscalateInstance(Guid instanceId, CancellationToken ct)
        => (await workflowService.EscalateInstanceAsync(instanceId, ct)).ToActionResult(HttpContext);

    [HttpPost("instances/{instanceId:guid}/cancel")]
    [RequirePermission(Permissions.WorkflowsManage)]
    public async Task<IActionResult> CancelInstance(Guid instanceId, [FromBody] CancelWorkflowRequest? request, CancellationToken ct)
        => (await workflowService.CancelInstanceAsync(instanceId, request?.Reason, ct)).ToActionResult(HttpContext);

    [HttpPost("delegations")]
    [RequirePermission(Permissions.WorkflowsApprove)]
    public async Task<IActionResult> CreateDelegation([FromBody] CreateApprovalDelegationRequest request, CancellationToken ct)
        => (await workflowService.CreateDelegationAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("delegations")]
    [RequirePermission(Permissions.WorkflowsRead)]
    public async Task<IActionResult> ListDelegations(CancellationToken ct)
        => (await workflowService.ListDelegationsAsync(ct)).ToActionResult(HttpContext);

    [HttpDelete("delegations/{delegationId:guid}")]
    [RequirePermission(Permissions.WorkflowsApprove)]
    public async Task<IActionResult> RevokeDelegation(Guid delegationId, CancellationToken ct)
        => (await workflowService.RevokeDelegationAsync(delegationId, ct)).ToActionResult(HttpContext);
}
