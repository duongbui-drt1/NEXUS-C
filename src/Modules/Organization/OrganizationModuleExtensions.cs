using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Organization.Application;
using Nexus.Modules.Organization.Infrastructure;
using Nexus.SharedKernel.Contracts;

namespace Nexus.Modules.Organization;

public static class OrganizationModuleExtensions
{
    public static IServiceCollection AddOrganizationModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<OrganizationDbContext>(connectionString, "organization");
        services.AddScoped<OrganizationService>();
        services.AddScoped<IOrganizationService>(sp => sp.GetRequiredService<OrganizationService>());
        services.AddScoped<IOrganizationScopeReader>(sp => sp.GetRequiredService<OrganizationService>());
        return services;
    }
}
