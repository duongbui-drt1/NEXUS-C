using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Nexus.Bootstrap;
using Nexus.BuildingBlocks.Web;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddNexusObservability();
builder.Services.AddNexusFoundationCore(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi("v1");

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();

app.UseCors("NexusStrictCors");
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi("/openapi/{documentName}.json");
    app.MapScalarApiReference("/scalar", options =>
    {
        options.WithTitle("NEXUS C Foundation API v1");
    });
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapControllers();

// Opt-in migration & initial admin bootstrap (only when explicitly enabled via environment variables)
if (string.Equals(Environment.GetEnvironmentVariable("NEXUS_RUN_MIGRATIONS"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var migrator = scope.ServiceProvider.GetRequiredService<NexusDatabaseMigrator>();
    var migrationConn = app.Configuration.GetConnectionString("MigrationConnection")
                        ?? app.Configuration.GetConnectionString("DefaultConnection")
                        ?? throw new InvalidOperationException("MigrationConnection is required when NEXUS_RUN_MIGRATIONS=true.");
    await migrator.MigrateAndHardeningAsync(migrationConn);

    var bootstrapEmail = Environment.GetEnvironmentVariable("NEXUS_BOOTSTRAP_ADMIN_EMAIL");
    var bootstrapPassword = Environment.GetEnvironmentVariable("NEXUS_BOOTSTRAP_ADMIN_PASSWORD");
    if (!string.IsNullOrWhiteSpace(bootstrapEmail) && !string.IsNullOrWhiteSpace(bootstrapPassword))
    {
        await migrator.SeedBootstrapPlatformAdminAsync(bootstrapEmail, bootstrapPassword, "NEXUS Platform SuperAdmin");
    }
}

app.Run();

public partial class Program;
