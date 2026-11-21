using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Audit.Application;
using Nexus.Modules.Audit.Infrastructure;
using Nexus.SharedKernel.Contracts;

namespace Nexus.Modules.Audit;

public static class AuditModuleExtensions
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<AuditDbContext>(connectionString, "audit");
        services.AddScoped<AuditService>();
        services.AddScoped<IAuditWriter>(sp => sp.GetRequiredService<AuditService>());
        services.AddScoped<IAuditQueryService>(sp => sp.GetRequiredService<AuditService>());
        return services;
    }
}
