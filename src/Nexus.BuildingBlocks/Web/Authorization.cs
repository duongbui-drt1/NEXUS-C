using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;

namespace Nexus.BuildingBlocks.Web;

public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "PERM:";

    public RequirePermissionAttribute(string permission)
        : base(PolicyPrefix + permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return base.GetPolicyAsync(policyName);
    }
}

public sealed class PermissionAuthorizationHandler(
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IPermissionEvaluator permissionEvaluator) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
        {
            return;
        }

        var hasPermission = await permissionEvaluator.HasPermissionAsync(
            currentUser.UserId.Value,
            tenantContext.TenantId ?? currentUser.TenantId,
            requirement.Permission,
            currentUser.IsPlatformUser);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
