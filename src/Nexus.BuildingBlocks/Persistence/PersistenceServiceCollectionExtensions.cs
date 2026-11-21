using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Nexus.BuildingBlocks.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddNexusModuleDbContext<TContext>(
        this IServiceCollection services,
        string connectionString,
        string schemaName)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var connInterceptor = sp.GetRequiredService<TenantRlsInterceptor>();
            var txInterceptor = sp.GetRequiredService<TenantRlsTransactionInterceptor>();
            var cmdInterceptor = sp.GetRequiredService<TenantRlsCommandInterceptor>();

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable($"__ef_migrations_history_{schemaName}", schemaName);
                npgsql.CommandTimeout(30);
            });

            options.AddInterceptors(connInterceptor, txInterceptor, cmdInterceptor);
        });

        return services;
    }
}
