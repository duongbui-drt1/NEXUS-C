using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.AccessControl.Application;
using Nexus.Modules.Subscription.Application;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Results;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

public sealed record TenantApplicationToggleRequest(Guid? TenantId = null);

[ApiController]
[Route("api/v1/platform")]
public sealed class ApplicationsController(
    ISubscriptionService subscriptionService,
    IAccessControlService accessControlService,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("applications")]
    [RequirePermission(Permissions.ApplicationsRead)]
    public async Task<IActionResult> ListApplications(CancellationToken ct)
        => (await subscriptionService.ListApplicationsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("applications")]
    [RequirePermission(Permissions.ApplicationsManage)]
    public async Task<IActionResult> RegisterApplication([FromBody] RegisterApplicationRequest request, CancellationToken ct)
        => (await subscriptionService.RegisterApplicationAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPut("applications/{code}")]
    [RequirePermission(Permissions.ApplicationsManage)]
    public async Task<IActionResult> UpdateApplication(string code, [FromBody] UpdateApplicationRequest request, CancellationToken ct)
        => (await subscriptionService.UpdateApplicationAsync(code, request, ct)).ToActionResult(HttpContext);

    [HttpGet("applications/tenant")]
    [RequirePermission(Permissions.ApplicationsRead)]
    public async Task<IActionResult> ListTenantApplications(CancellationToken ct)
        => (await subscriptionService.ListTenantApplicationsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("applications/{code}/activate")]
    [RequirePermission(Permissions.ApplicationsManage)]
    public async Task<IActionResult> ActivateForTenant(string code, [FromBody] TenantApplicationToggleRequest? request, CancellationToken ct)
        => (await subscriptionService.SetTenantApplicationActivationAsync(code, activate: true, request?.TenantId, ct)).ToActionResult(HttpContext);

    [HttpPost("applications/{code}/deactivate")]
    [RequirePermission(Permissions.ApplicationsManage)]
    public async Task<IActionResult> DeactivateForTenant(string code, [FromBody] TenantApplicationToggleRequest? request, CancellationToken ct)
        => (await subscriptionService.SetTenantApplicationActivationAsync(code, activate: false, request?.TenantId, ct)).ToActionResult(HttpContext);

    /// <summary>
    /// Idempotently registers or updates application-scoped domain permissions (e.g., NEXUS_M m.* manifest)
    /// into NEXUS C's authoritative RBAC permission catalog (ADR-001).
    /// </summary>
    [HttpPost("applications/{code}/permissions/sync")]
    [RequirePermission(Permissions.ApplicationsManage)]
    public async Task<IActionResult> SyncApplicationPermissions(
        string code,
        [FromBody] SyncApplicationPermissionsRequest request,
        CancellationToken ct)
        => (await accessControlService.SyncApplicationPermissionsAsync(code, request, ct)).ToActionResult(HttpContext);

    /// <summary>
    /// Authoritative tenant entitlement evaluation endpoint for NEXUS F, E, M and Console.
    /// </summary>
    [HttpGet("entitlements/{featureCode}")]
    [RequirePermission(Permissions.SubscriptionsRead)]
    public async Task<IActionResult> EvaluateEntitlement(string featureCode, CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
        {
            return ApiResultMapper.ToProblemResult(
                Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required to evaluate entitlements."),
                HttpContext);
        }

        return (await subscriptionService.EvaluateEntitlementAsync(tenantContext.TenantId!.Value, featureCode, ct)).ToActionResult(HttpContext);
    }

    [HttpGet("entitlements")]
    [RequirePermission(Permissions.SubscriptionsRead)]
    public async Task<IActionResult> ListEntitlements(CancellationToken ct)
        => (await subscriptionService.ListCurrentTenantEntitlementsAsync(ct)).ToActionResult(HttpContext);
}
