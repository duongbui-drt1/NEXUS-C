using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Configuration.Application;
using Nexus.Modules.Configuration.Infrastructure;

namespace Nexus.Modules.Configuration;

public static class ConfigurationModuleExtensions
{
    public static IServiceCollection AddConfigurationModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<ConfigurationDbContext>(connectionString, "configuration");
        services.AddScoped<IConfigurationService, ConfigurationService>();
        return services;
    }
}
