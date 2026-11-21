using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.Bootstrap;

var builder = Host.CreateApplicationBuilder(args);

var migrationConnection = builder.Configuration.GetConnectionString("MigrationConnection")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:MigrationConnection is required for Nexus.Migrator.");

var appConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? migrationConnection;

var appRoleName = Environment.GetEnvironmentVariable("NEXUS_APP_DB_ROLE") ?? "nexus_app";

// Register services in Testing/Migrator mode without requiring runtime API CORS/web settings
var configOverride = new Dictionary<string, string?>
{
    ["ConnectionStrings:DefaultConnection"] = appConnection,
    ["ConnectionStrings:MigrationConnection"] = migrationConnection,
    ["Outbox:EnableBackgroundProcessor"] = "false",
    ["RabbitMQ:Enabled"] = "false"
};
builder.Configuration.AddInMemoryCollection(configOverride);

builder.Services.AddNexusFoundationCore(builder.Configuration, "Testing");

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Nexus.Migrator");

try
{
    using var scope = host.Services.CreateScope();
    var migrator = scope.ServiceProvider.GetRequiredService<NexusDatabaseMigrator>();

    logger.LogInformation("Starting NEXUS C one-shot database migration & RLS hardening...");
    await migrator.MigrateAndHardeningAsync(migrationConnection, appRoleName);

    var bootstrapEmail = Environment.GetEnvironmentVariable("NEXUS_BOOTSTRAP_ADMIN_EMAIL");
    var bootstrapPassword = Environment.GetEnvironmentVariable("NEXUS_BOOTSTRAP_ADMIN_PASSWORD");
    if (!string.IsNullOrWhiteSpace(bootstrapEmail) && !string.IsNullOrWhiteSpace(bootstrapPassword))
    {
        logger.LogInformation("Bootstrap platform administrator env vars detected; ensuring initial admin exists...");
        await migrator.SeedBootstrapPlatformAdminAsync(bootstrapEmail, bootstrapPassword, "NEXUS Platform SuperAdmin");
    }

    logger.LogInformation("NEXUS.Migrator completed successfully.");
    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "NEXUS.Migrator failed. Aborting deployment.");
    return 1;
}
