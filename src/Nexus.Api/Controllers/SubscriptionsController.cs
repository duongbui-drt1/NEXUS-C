using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Subscription.Application;
using Nexus.Modules.Subscription.Domain;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

public sealed record UpdateSubscriptionStatusRequest(SubscriptionStatus Status, string? Reason = null);

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

    [HttpDelete("plans/{planId:guid}")]
    [RequirePermission(Permissions.SubscriptionsDelete)]
    public async Task<IActionResult> DeleteOrArchivePlan(Guid planId, [FromQuery] string? reason = null, CancellationToken ct = default)
        => (await subscriptionService.DeleteOrArchivePlanAsync(planId, reason, ct)).ToActionResult(HttpContext);

    [HttpPost("assign")]
    [RequirePermission(Permissions.SubscriptionsManage)]
    public async Task<IActionResult> AssignPlan([FromBody] AssignSubscriptionRequest request, CancellationToken ct)
        => (await subscriptionService.AssignPlanToTenantAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("{tenantId:guid}/status")]
    [RequirePermission(Permissions.SubscriptionsManage)]
    public async Task<IActionResult> UpdateStatus(Guid tenantId, [FromBody] UpdateSubscriptionStatusRequest request, CancellationToken ct)
        => (await subscriptionService.UpdateSubscriptionStatusAsync(tenantId, request.Status, request.Reason, ct)).ToActionResult(HttpContext);

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

    [HttpPost("usage")]
    [RequirePermission(Permissions.SubscriptionsRead)]
    public async Task<IActionResult> RecordUsage([FromBody] RecordUsageRequest request, CancellationToken ct)
        => (await subscriptionService.RecordUsageIdempotentAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("usage")]
    [RequirePermission(Permissions.SubscriptionsRead)]
    public async Task<IActionResult> ListUsage(CancellationToken ct)
        => (await subscriptionService.ListUsageSummaryAsync(ct)).ToActionResult(HttpContext);
}
