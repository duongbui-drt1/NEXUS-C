using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Notification.Application;
using Nexus.Modules.Notification.Infrastructure;

namespace Nexus.Modules.Notification;

public static class NotificationModuleExtensions
{
    public static IServiceCollection AddNotificationModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<NotificationDbContext>(connectionString, "notification");
        services.AddScoped<INotificationService, NotificationService>();
        return services;
    }
}
