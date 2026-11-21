using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Integration.Application;
using Nexus.Modules.Integration.Infrastructure;

namespace Nexus.Modules.Integration;

public static class IntegrationModuleExtensions
{
    public static IServiceCollection AddIntegrationModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<IntegrationDbContext>(connectionString, "integration");
        services.AddScoped<IIntegrationService, IntegrationService>();
        return services;
    }
}
