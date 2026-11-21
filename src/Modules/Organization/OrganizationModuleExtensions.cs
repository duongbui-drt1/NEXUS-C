using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Organization.Application;
using Nexus.Modules.Organization.Infrastructure;

namespace Nexus.Modules.Organization;

public static class OrganizationModuleExtensions
{
    public static IServiceCollection AddOrganizationModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<OrganizationDbContext>(connectionString, "organization");
        services.AddScoped<IOrganizationService, OrganizationService>();
        return services;
    }
}
