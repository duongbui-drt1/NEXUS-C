using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Messaging;
using Nexus.Modules.AccessControl.Application;
using Nexus.Modules.AccessControl.Infrastructure;
using Nexus.Modules.Audit.Infrastructure;
using Nexus.Modules.Configuration.Infrastructure;
using Nexus.Modules.FileStorage.Infrastructure;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Identity.Infrastructure;
using Nexus.Modules.Integration.Infrastructure;
using Nexus.Modules.Notification.Infrastructure;
using Nexus.Modules.Organization.Infrastructure;
using Nexus.Modules.Subscription.Infrastructure;
using Nexus.Modules.Tenancy.Infrastructure;
using Nexus.Modules.Workflow.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Security;
using Npgsql;

namespace Nexus.Bootstrap;

public sealed class NexusDatabaseMigrator(IServiceProvider serviceProvider, ILogger<NexusDatabaseMigrator> logger)
{
    public async Task MigrateAndHardeningAsync(string migrationConnectionString, string appRoleName = "nexus_app", CancellationToken ct = default)
    {
        logger.LogInformation("Applying NEXUS C Foundation EF Core migrations across all schemas...");

        // Ensure all module schemas exist before running migrations
        string[] schemas =
        [
            "messaging", "tenancy", "organization", "identity", "access_control",
            "audit", "workflow", "subscription", "configuration", "integration",
            "notification", "file_storage"
        ];

        await using (var conn = new NpgsqlConnection(migrationConnectionString))
        {
            await conn.OpenAsync(ct);
            foreach (var schema in schemas)
            {
                await using var cmd = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS \"{schema}\";", conn);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        // Run EF Core migrations for every DbContext using the migration connection string (owner role)
        await using var scope = serviceProvider.CreateAsyncScope();
        var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var bypass = tenantCtx.BeginScope(null, platformBypass: true, bypassReason: "database-migration");

        await MigrateContextAsync<MessagingDbContext>(migrationConnectionString, "messaging", scope.ServiceProvider, ct);
        await MigrateContextAsync<TenancyDbContext>(migrationConnectionString, "tenancy", scope.ServiceProvider, ct);
        await MigrateContextAsync<OrganizationDbContext>(migrationConnectionString, "organization", scope.ServiceProvider, ct);
        await MigrateContextAsync<IdentityModuleDbContext>(migrationConnectionString, "identity", scope.ServiceProvider, ct);
        await MigrateContextAsync<AccessControlDbContext>(migrationConnectionString, "access_control", scope.ServiceProvider, ct);
        await MigrateContextAsync<AuditDbContext>(migrationConnectionString, "audit", scope.ServiceProvider, ct);
        await MigrateContextAsync<WorkflowDbContext>(migrationConnectionString, "workflow", scope.ServiceProvider, ct);
        await MigrateContextAsync<SubscriptionDbContext>(migrationConnectionString, "subscription", scope.ServiceProvider, ct);
        await MigrateContextAsync<ConfigurationDbContext>(migrationConnectionString, "configuration", scope.ServiceProvider, ct);
        await MigrateContextAsync<IntegrationDbContext>(migrationConnectionString, "integration", scope.ServiceProvider, ct);
        await MigrateContextAsync<NotificationDbContext>(migrationConnectionString, "notification", scope.ServiceProvider, ct);
        await MigrateContextAsync<FileStorageDbContext>(migrationConnectionString, "file_storage", scope.ServiceProvider, ct);

        // Apply PostgreSQL Row-Level Security (FORCE RLS), Audit Immutability Trigger, and least-privilege grants for nexus_app
        await ApplyRlsAndHardeningSqlAsync(migrationConnectionString, appRoleName, ct);

        // Seed default RBAC permissions & roles
        var accessControl = scope.ServiceProvider.GetRequiredService<IAccessControlService>();
        await accessControl.SeedDefaultsAsync(ct);

        logger.LogInformation("NEXUS C Foundation migrations, RLS hardening, and RBAC seeding completed successfully.");
    }

    private static async Task MigrateContextAsync<TContext>(
        string migrationConnectionString,
        string schemaName,
        IServiceProvider sp,
        CancellationToken ct) where TContext : DbContext
    {
        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseNpgsql(migrationConnectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable($"__ef_migrations_history_{schemaName}", schemaName);
        });

        TContext db;
        if (typeof(TContext) == typeof(IdentityModuleDbContext))
        {
            db = (TContext)(object)new IdentityModuleDbContext((DbContextOptions<IdentityModuleDbContext>)(object)optionsBuilder.Options);
        }
        else
        {
            var tenantCtx = sp.GetRequiredService<ITenantContext>();
            var currentUser = sp.GetRequiredService<ICurrentUser>();
            var clock = sp.GetRequiredService<ISystemClock>();
            db = (TContext)Activator.CreateInstance(typeof(TContext), optionsBuilder.Options, tenantCtx, currentUser, clock)!;
        }

        await using (db)
        {
            await db.Database.MigrateAsync(ct);
        }
    }

    private static async Task ApplyRlsAndHardeningSqlAsync(string connectionString, string appRoleName, CancellationToken ct)
    {
        // All strictly tenant-scoped tables enforcing PostgreSQL FORCE ROW LEVEL SECURITY
        (string Schema, string Table)[] rlsTables =
        [
            ("tenancy", "memberships"),
            ("organization", "legal_entities"),
            ("organization", "branches"),
            ("organization", "organization_units"),
            ("organization", "user_organization_assignments"),
            ("workflow", "definitions"),
            ("workflow", "versions"),
            ("workflow", "instances"),
            ("workflow", "approval_tasks"),
            ("workflow", "approval_decisions"),
            ("workflow", "history"),
            ("subscription", "subscriptions"),
            ("subscription", "subscription_items"),
            ("subscription", "entitlements"),
            ("subscription", "usage_records"),
            ("configuration", "tenant_settings"),
            ("integration", "connections"),
            ("integration", "sync_logs"),
            ("integration", "integration_errors"),
            ("integration", "webhooks"),
            ("integration", "ai_usage_logs"),
            ("notification", "templates"),
            ("notification", "notifications"),
            ("file_storage", "stored_files")
        ];

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        foreach (var (schema, table) in rlsTables)
        {
            var sql = $@"
                ALTER TABLE ""{schema}"".""{table}"" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ""{schema}"".""{table}"" FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS nexus_tenant_isolation ON ""{schema}"".""{table}"";
                CREATE POLICY nexus_tenant_isolation ON ""{schema}"".""{table}""
                USING (
                    current_setting('app.bypass_rls', true) = 'on'
                    OR (
                        NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                        AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                    )
                )
                WITH CHECK (
                    current_setting('app.bypass_rls', true) = 'on'
                    OR (
                        NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                        AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                    )
                );";

            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Audit immutability trigger (append-only at PostgreSQL level!)
        const string auditImmutabilitySql = @"
            CREATE OR REPLACE FUNCTION audit.prevent_audit_mutation()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'Audit events are strictly append-only and immutable.';
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS trg_audit_events_immutable ON audit.audit_events;
            CREATE TRIGGER trg_audit_events_immutable
                BEFORE UPDATE OR DELETE ON audit.audit_events
                FOR EACH ROW EXECUTE FUNCTION audit.prevent_audit_mutation();";

        await using (var auditCmd = new NpgsqlCommand(auditImmutabilitySql, conn))
        {
            await auditCmd.ExecuteNonQueryAsync(ct);
        }

        // Least-privilege grants for the application runtime role (if it exists in PostgreSQL)
        var roleExistsSql = "SELECT 1 FROM pg_roles WHERE rolname = @role";
        bool roleExists;
        await using (var checkCmd = new NpgsqlCommand(roleExistsSql, conn))
        {
            checkCmd.Parameters.AddWithValue("@role", appRoleName);
            roleExists = await checkCmd.ExecuteScalarAsync(ct) is not null;
        }

        if (roleExists)
        {
            string[] rwSchemas =
            [
                "messaging", "tenancy", "organization", "identity", "access_control",
                "workflow", "subscription", "configuration", "integration",
                "notification", "file_storage"
            ];

            foreach (var schema in rwSchemas)
            {
                var grantSql = $@"
                    GRANT USAGE ON SCHEMA ""{schema}"" TO ""{appRoleName}"";
                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA ""{schema}"" TO ""{appRoleName}"";
                    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA ""{schema}"" TO ""{appRoleName}"";";
                await using var gCmd = new NpgsqlCommand(grantSql, conn);
                await gCmd.ExecuteNonQueryAsync(ct);
            }

            // Audit schema: strictly INSERT and SELECT ONLY for nexus_app (NO UPDATE, NO DELETE!)
            var auditGrantSql = $@"
                GRANT USAGE ON SCHEMA audit TO ""{appRoleName}"";
                REVOKE ALL ON TABLE audit.audit_events FROM ""{appRoleName}"";
                GRANT SELECT, INSERT ON TABLE audit.audit_events TO ""{appRoleName}"";";
            await using var agCmd = new NpgsqlCommand(auditGrantSql, conn);
            await agCmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task SeedBootstrapPlatformAdminAsync(string email, string password, string fullName, CancellationToken ct = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var bypass = tenantCtx.BeginScope(null, platformBypass: true, bypassReason: "bootstrap-superadmin");

        var existing = await scope.ServiceProvider.GetRequiredService<Nexus.SharedKernel.Contracts.IUserDirectoryReader>()
            .GetByEmailAsync(email, ct);
        if (existing is not null)
        {
            return;
        }

        var result = await authService.CreateUserAsync(new CreateUserRequest(
            Email: email,
            Password: password,
            FullName: fullName,
            TenantId: null,
            RoleName: SystemRoles.PlatformSuperAdmin,
            IsPlatformUser: true), ct);

        if (result.IsFailure)
        {
            logger.LogWarning("Bootstrap platform admin creation skipped or failed: {Error}", result.Error.Message);
        }
    }
}
