using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.AccessControl.Application;
using Nexus.Modules.AccessControl.Infrastructure;
using Nexus.SharedKernel.Contracts;

namespace Nexus.Modules.AccessControl;

public static class AccessControlModuleExtensions
{
    public static IServiceCollection AddAccessControlModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<AccessControlDbContext>(connectionString, "access_control");
        services.AddScoped<AccessControlService>();
        services.AddScoped<IAccessControlService>(sp => sp.GetRequiredService<AccessControlService>());
        services.AddScoped<IPermissionEvaluator>(sp => sp.GetRequiredService<AccessControlService>());
        return services;
    }
}
