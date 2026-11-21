using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Bootstrap;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.BuildingBlocks.Messaging;
using Nexus.Modules.Identity.Application;
using Nexus.SharedKernel.Security;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Nexus.IntegrationTests.Infrastructure;

public sealed class NexusPostgresFixture : IAsyncLifetime
{
    public const string TestJwtSigningKey = "NEXUS_TEST_ONLY_JWT_SIGNING_KEY_64_BYTES_LONG_FOR_HMAC_SHA256_TEST_OK";
    public const string TestStorageSigningSecret = "NEXUS_TEST_ONLY_STORAGE_HMAC_SIGNING_SECRET_64_BYTES_LONG_OK_1234";

    private PostgreSqlContainer? _container;
    private string _adminConn = string.Empty;
    private string _dbName = string.Empty;
    private string _migratorRole = "nexus_migrator";
    private string _appRole = "nexus_app";
    private string _migratorPass = "nexus_migrator_test_pw";
    private string _appPass = "nexus_app_test_pw";
    private string _tempStorageDir = string.Empty;

    public string MigratorConnectionString { get; private set; } = string.Empty;
    public string AppConnectionString { get; private set; } = string.Empty;
    public string AppRoleName => _appRole;
    public string TempStorageDir => _tempStorageDir;

    public NexusApiFactory ApiFactory { get; private set; } = null!;
    public InMemoryEventBus EventBus => ApiFactory.Services.GetRequiredService<InMemoryEventBus>();
    public InMemoryTestEmailSender EmailSender => ApiFactory.Services.GetRequiredService<InMemoryTestEmailSender>();

    public string SuperAdminEmail { get; } = "superadmin@nexus.test";
    public string SuperAdminPassword { get; } = "SuperAdmin!Nexus2026#";

    public async Task InitializeAsync()
    {
        _dbName = $"nexus_test_{Guid.NewGuid():N}"[..26];
        _tempStorageDir = Path.Combine(Path.GetTempPath(), $"nexus_storage_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempStorageDir);

        var useContainers = string.Equals(Environment.GetEnvironmentVariable("NEXUS_USE_TESTCONTAINERS"), "true", StringComparison.OrdinalIgnoreCase);
        if (useContainers)
        {
            _container = new PostgreSqlBuilder("postgres:17.4-alpine")
                .WithDatabase("postgres")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _container.StartAsync();
            _adminConn = _container.GetConnectionString();
        }
        else
        {
            var currentOsUser = Environment.UserName;
            _adminConn = Environment.GetEnvironmentVariable("NEXUS_TEST_PG_ADMIN_CONN")
                ?? $"Host=localhost;Port=5432;Database=postgres;Username={currentOsUser}";
        }

        await ProvisionIsolatedDatabaseAndRolesAsync();

        ApiFactory = new NexusApiFactory(AppConnectionString, MigratorConnectionString, _tempStorageDir);

        // Execute all 12 EF Core migrations + FORCE RLS + Audit immutability trigger + RBAC seeding!
        using var scope = ApiFactory.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<NexusDatabaseMigrator>();
        await migrator.MigrateAndHardeningAsync(MigratorConnectionString, _appRole);
        await migrator.SeedBootstrapPlatformAdminAsync(SuperAdminEmail, SuperAdminPassword, "NEXUS Super Admin");
    }

    private async Task ProvisionIsolatedDatabaseAndRolesAsync()
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(_adminConn);

        await using var conn = new NpgsqlConnection(_adminConn);
        await conn.OpenAsync();

        var roleSql = $@"
            SELECT pg_advisory_lock(74839201);
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{_migratorRole}') THEN
                    CREATE ROLE {_migratorRole} WITH LOGIN PASSWORD '{_migratorPass}' NOSUPERUSER NOCREATEDB NOCREATEROLE;
                ELSE
                    ALTER ROLE {_migratorRole} WITH LOGIN PASSWORD '{_migratorPass}' NOSUPERUSER NOCREATEDB NOCREATEROLE;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{_appRole}') THEN
                    CREATE ROLE {_appRole} WITH LOGIN PASSWORD '{_appPass}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                ELSE
                    ALTER ROLE {_appRole} WITH LOGIN PASSWORD '{_appPass}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                END IF;
            END $$;
            SELECT pg_advisory_unlock(74839201);";

        await using (var cmd = new NpgsqlCommand(roleSql, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var createDb = new NpgsqlCommand($"CREATE DATABASE \"{_dbName}\" OWNER \"{_migratorRole}\";", conn))
        {
            await createDb.ExecuteNonQueryAsync();
        }

        await using (var grantConnect = new NpgsqlCommand($"GRANT CONNECT ON DATABASE \"{_dbName}\" TO \"{_appRole}\";", conn))
        {
            await grantConnect.ExecuteNonQueryAsync();
        }

        MigratorConnectionString = new NpgsqlConnectionStringBuilder
        {
            Host = adminBuilder.Host,
            Port = adminBuilder.Port,
            Database = _dbName,
            Username = _migratorRole,
            Password = _migratorPass,
            Pooling = true
        }.ConnectionString;

        AppConnectionString = new NpgsqlConnectionStringBuilder
        {
            Host = adminBuilder.Host,
            Port = adminBuilder.Port,
            Database = _dbName,
            Username = _appRole,
            Password = _appPass,
            Pooling = true
        }.ConnectionString;
    }

    public HttpClient CreateClient() => ApiFactory.CreateClient();

    public async Task<AuthTokenResponse> LoginAsync(string email, string password, Guid? tenantId = null)
    {
        using var client = CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password, tenantId));
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return JsonSerializer.Deserialize<AuthTokenResponse>(
            doc.GetProperty("data").GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public async Task DisposeAsync()
    {
        if (ApiFactory is not null)
        {
            await ApiFactory.DisposeAsync();
        }

        NpgsqlConnection.ClearAllPools();

        if (!string.IsNullOrWhiteSpace(_dbName) && !string.IsNullOrWhiteSpace(_adminConn) && _container is null)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_adminConn);
                await conn.OpenAsync();
                await using var terminate = new NpgsqlCommand(
                    $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_dbName}' AND pid <> pg_backend_pid();", conn);
                await terminate.ExecuteNonQueryAsync();

                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\";", conn);
                await drop.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        if (Directory.Exists(_tempStorageDir))
        {
            try { Directory.Delete(_tempStorageDir, recursive: true); } catch { }
        }
    }
}

public sealed class NexusApiFactory(
    string appConnectionString,
    string migrationConnectionString,
    string tempStorageDir,
    int? overrideRateLimit = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = appConnectionString,
            ["ConnectionStrings:MigrationConnection"] = migrationConnectionString,
            ["ConnectionStrings:Redis"] = "",
            ["Jwt:Issuer"] = "https://nexus-c.test",
            ["Jwt:Audience"] = "nexus-ecosystem-test",
            ["Jwt:SigningKey"] = NexusPostgresFixture.TestJwtSigningKey,
            ["Jwt:AccessTokenMinutes"] = "15",
            ["Jwt:RefreshTokenDays"] = "14",
            ["RabbitMQ:Enabled"] = "false",
            ["Outbox:EnableBackgroundProcessor"] = "false",
            ["Outbox:MaxRetries"] = "3",
            ["ObjectStorage:Provider"] = "Local",
            ["ObjectStorage:LocalBasePath"] = tempStorageDir,
            ["ObjectStorage:SigningSecret"] = NexusPostgresFixture.TestStorageSigningSecret,
            ["OpenApi:Enabled"] = "true",
            ["RateLimiting:PermitLimit"] = (overrideRateLimit ?? 5000).ToString(),
            ["RateLimiting:AuthPermitLimit"] = (overrideRateLimit ?? 5000).ToString(),
            ["RateLimiting:WindowSeconds"] = "60"
        };

        foreach (var kv in settings)
        {
            builder.UseSetting(kv.Key, kv.Value);
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<InMemoryTestEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<InMemoryTestEmailSender>());
        });
    }
}
