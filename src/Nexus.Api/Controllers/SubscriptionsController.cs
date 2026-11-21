using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Subscription.Application;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/subscriptions")]
public sealed class SubscriptionsController(ISubscriptionService subscriptionService, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost("plans")]
    [RequirePermission(Permissions.SubscriptionsManage)]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequest request, CancellationToken ct)
        => (await subscriptionService.CreatePlanAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("plans")]
    [RequirePermission(Permissions.SubscriptionsRead)]
    public async Task<IActionResult> ListPlans(CancellationToken ct)
        => (await subscriptionService.ListPlansAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("assign")]
    [RequirePermission(Permissions.SubscriptionsManage)]
    public async Task<IActionResult> AssignPlan([FromBody] AssignSubscriptionRequest request, CancellationToken ct)
        => (await subscriptionService.AssignPlanToTenantAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("current")]
    [RequirePermission(Permissions.SubscriptionsRead)]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
        => (await subscriptionService.GetCurrentTenantSubscriptionAsync(ct)).ToActionResult(HttpContext);

    [HttpGet("entitlements/{featureCode}")]
    [RequirePermission(Permissions.SubscriptionsRead)]
    public async Task<IActionResult> CheckEntitlement(string featureCode, CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
        {
            return ApiResultMapper.ToProblemResult(
                Nexus.SharedKernel.Results.Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required."),
                HttpContext);
        }

        return (await subscriptionService.CheckEntitlementAsync(tenantContext.TenantId!.Value, featureCode, ct)).ToActionResult(HttpContext);
    }
}
