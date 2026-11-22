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
        services.AddHttpClient(SafeWebhookHttpDispatcher.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddSingleton<IWebhookHttpDispatcher, SafeWebhookHttpDispatcher>();
        services.AddScoped<IIntegrationService, IntegrationService>();
        return services;
    }
}
