using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Subscription.Application;
using Nexus.Modules.Subscription.Infrastructure;
using Nexus.SharedKernel.Contracts;

namespace Nexus.Modules.Subscription;

public static class SubscriptionModuleExtensions
{
    public static IServiceCollection AddSubscriptionModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<SubscriptionDbContext>(connectionString, "subscription");
        services.AddScoped<SubscriptionService>();
        services.AddScoped<ISubscriptionService>(sp => sp.GetRequiredService<SubscriptionService>());
        services.AddScoped<IEntitlementChecker>(sp => sp.GetRequiredService<SubscriptionService>());
        return services;
    }
}
