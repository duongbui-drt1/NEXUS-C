using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Configuration.Application;
using Nexus.Modules.Configuration.Infrastructure;
using Nexus.SharedKernel.Contracts;

namespace Nexus.Modules.Configuration;

public static class ConfigurationModuleExtensions
{
    public static IServiceCollection AddConfigurationModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<ConfigurationDbContext>(connectionString, "configuration");
        services.AddScoped<ConfigurationService>();
        services.AddScoped<IConfigurationService>(sp => sp.GetRequiredService<ConfigurationService>());
        services.AddScoped<IFeatureFlagEvaluator>(sp => sp.GetRequiredService<ConfigurationService>());
        services.AddSingleton<RateLimitPolicyCache>();
        services.AddScoped<RateLimitPolicyService>();
        services.AddScoped<IRateLimitPolicyService>(sp => sp.GetRequiredService<RateLimitPolicyService>());
        services.AddScoped<IRateLimitPolicyProvider>(sp => sp.GetRequiredService<RateLimitPolicyService>());
        services.AddHttpClient(FrankfurterFxRateProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddSingleton<IFxRateProvider, FrankfurterFxRateProvider>();
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        return services;
    }
}
