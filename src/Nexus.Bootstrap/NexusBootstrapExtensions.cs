using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexus.BuildingBlocks.Context;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Observability;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.AccessControl;
using Nexus.Modules.Audit;
using Nexus.Modules.Configuration;
using Nexus.Modules.FileStorage;
using Nexus.Modules.Identity;
using Nexus.Modules.Integration;
using Nexus.Modules.Notification;
using Nexus.Modules.Organization;
using Nexus.Modules.Subscription;
using Nexus.Modules.Tenancy;
using Nexus.Modules.Workflow;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Nexus.Bootstrap;

public static class NexusBootstrapExtensions
{
    public static WebApplicationBuilder AddNexusObservability(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "NEXUS.C.Foundation")
                .WriteTo.Console();
        });

        var otelEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("nexus-c-foundation", serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing.AddSource(NexusPlatformMetrics.ActivitySourceName, "Npgsql");
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();
                if (!string.IsNullOrWhiteSpace(otelEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(NexusPlatformMetrics.MeterName);
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                if (!string.IsNullOrWhiteSpace(otelEndpoint))
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));
                }
            });
        _ = otel;

        return builder;
    }

    public static IServiceCollection AddNexusFoundationCore(this IServiceCollection services, IConfiguration configuration, string environmentName = "Production")
    {
        ValidateRuntimeSecurityConfiguration(configuration, environmentName);

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

        services.AddHttpContextAccessor();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<ITenantContext, AmbientTenantContext>();
        services.AddScoped<ICorrelationContext, AmbientCorrelationContext>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        // PostgreSQL RLS interceptors (scoped to match ITenantContext)
        services.AddScoped<TenantRlsInterceptor>();
        services.AddScoped<TenantRlsTransactionInterceptor>();
        services.AddScoped<TenantRlsCommandInterceptor>();

        // Messaging DbContext & Event Infrastructure
        services.AddNexusModuleDbContext<MessagingDbContext>(connectionString, "messaging");
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        var rabbitEnabled = configuration.GetValue<bool>("RabbitMQ:Enabled");
        if (rabbitEnabled)
        {
            services.AddSingleton<IEventBus, RabbitMqEventBus>();
        }
        else
        {
            services.AddSingleton<InMemoryEventBus>();
            services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<InMemoryEventBus>());
        }

        services.AddSingleton<IBackgroundJobTracker, BackgroundJobTracker>();
        services.AddScoped<IEventOutboxWriter, DatabaseOutboxWriter>();
        services.AddScoped<IPlatformOperationsService, PlatformOperationsService>();
        services.AddScoped<OutboxDispatcher>();
        services.AddScoped<IdempotentEventProcessor>();
        services.AddHostedService<OutboxProcessorBackgroundService>();

        // Caching (Redis when configured, in-memory distributed cache otherwise)
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(opts =>
            {
                opts.Configuration = redisConnection;
                opts.InstanceName = "nexus:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // Object Storage (S3-compatible or Local filesystem)
        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));
        var storageProvider = configuration["ObjectStorage:Provider"] ?? "Local";
        if (string.Equals(storageProvider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        }
        else
        {
            services.AddSingleton<IObjectStorage, LocalFileObjectStorage>();
        }

        // Email & AI Gateway Providers
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddSingleton<IAiProvider, UnconfiguredAiProvider>();

        // Authorization policy provider & handler
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // Global exception handling
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        // Rate Limiting (configurable for strict API & auth throttling)
        var permitLimit = configuration.GetValue<int?>("RateLimiting:PermitLimit") ?? 120;
        var windowSeconds = configuration.GetValue<int?>("RateLimiting:WindowSeconds") ?? 60;
        var authPermitLimit = configuration.GetValue<int?>("RateLimiting:AuthPermitLimit") ?? 30;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.ContentType = "application/problem+json";
                var correlationId = ApiResultMapper.GetCorrelationId(ctx.HttpContext);
                await ctx.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://errors.nexus.internal/rate-limit-exceeded",
                    title = "Too Many Requests",
                    status = 429,
                    detail = "Rate limit exceeded. Please retry later.",
                    errorCode = "RATE_LIMIT_EXCEEDED",
                    correlationId
                }, ct);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var key = httpContext.User?.FindFirst("sub")?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
            });

            options.AddFixedWindowLimiter("auth", opt =>
            {
                opt.PermitLimit = authPermitLimit;
                opt.Window = TimeSpan.FromSeconds(windowSeconds);
                opt.QueueLimit = 0;
            });
        });

        // Strict CORS (never AllowAnyOrigin with AllowCredentials)
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
        {
            options.AddPolicy("NexusStrictCors", policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                }
                else
                {
                    policy.WithOrigins("https://nexus.local")
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                }
            });
        });

        // Health Checks
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("API host is alive."), tags: ["live"])
            .AddNpgSql(connectionString, name: "postgresql", tags: ["ready"]);

        // Register all 11 NEXUS C Foundation Modules
        services.AddTenancyModule(connectionString);
        services.AddOrganizationModule(connectionString);
        services.AddIdentityModule(configuration, connectionString);
        services.AddAccessControlModule(connectionString);
        services.AddAuditModule(connectionString);
        services.AddWorkflowModule(connectionString);
        services.AddSubscriptionModule(connectionString);
        services.AddConfigurationModule(connectionString);
        services.AddIntegrationModule(connectionString);
        services.AddNotificationModule(connectionString);
        services.AddFileStorageModule(connectionString);

        services.AddScoped<NexusDatabaseMigrator>();

        return services;
    }

    public static void ValidateRuntimeSecurityConfiguration(IConfiguration configuration, string environmentName)
    {
        var isDevOrTest = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

        if (isDevOrTest)
        {
            return;
        }

        var jwtKey = configuration["Jwt:SigningKey"] ?? string.Empty;
        if (jwtKey.Length < 32 || ContainsInsecurePlaceholder(jwtKey))
        {
            throw new InvalidOperationException(
                "SECURITY STARTUP FAILURE: Jwt:SigningKey must be configured with at least 32 cryptographically random characters in non-development environments.");
        }

        var storageSecret = configuration["ObjectStorage:SigningSecret"] ?? string.Empty;
        if (storageSecret.Length < 32 || ContainsInsecurePlaceholder(storageSecret))
        {
            throw new InvalidOperationException(
                "SECURITY STARTUP FAILURE: ObjectStorage:SigningSecret must be configured with at least 32 characters in non-development environments.");
        }

        var defaultConn = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        if (defaultConn.Contains("dev_password", StringComparison.OrdinalIgnoreCase) ||
            defaultConn.Contains("Username=postgres", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SECURITY STARTUP FAILURE: Production DefaultConnection cannot use development credentials or the postgres superuser.");
        }

        var corsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (corsOrigins.Any(o => o.Trim() == "*"))
        {
            throw new InvalidOperationException(
                "SECURITY STARTUP FAILURE: Wildcard '*' CORS origin is prohibited in production.");
        }
    }

    private static bool ContainsInsecurePlaceholder(string value)
        => value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
           || value.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase)
           || value.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
           || value.Contains("DEV_ONLY", StringComparison.OrdinalIgnoreCase);
}
