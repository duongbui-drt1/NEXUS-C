using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Tenancy.Application;
using Nexus.Modules.Tenancy.Infrastructure;
using Nexus.SharedKernel.Contracts;

namespace Nexus.Modules.Tenancy;

public static class TenancyModuleExtensions
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<TenancyDbContext>(connectionString, "tenancy");
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<ITenantAccessValidator, TenantAccessValidator>();
        return services;
    }
}
