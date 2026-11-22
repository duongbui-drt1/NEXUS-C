using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Messaging;
using Nexus.Modules.AccessControl.Application;
using Nexus.Modules.AccessControl.Infrastructure;
using Nexus.Modules.Audit.Infrastructure;
using Nexus.Modules.Configuration.Application;
using Nexus.Modules.Configuration.Infrastructure;
using Nexus.Modules.FileStorage.Infrastructure;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Identity.Infrastructure;
using Nexus.Modules.Integration.Application;
using Nexus.Modules.Integration.Infrastructure;
using Nexus.Modules.Notification.Infrastructure;
using Nexus.Modules.Organization.Infrastructure;
using Nexus.Modules.Subscription.Application;
using Nexus.Modules.Subscription.Infrastructure;
using Nexus.Modules.Tenancy.Infrastructure;
using Nexus.Modules.Workflow.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Security;
using Npgsql;

namespace Nexus.Bootstrap;

public sealed class NexusDatabaseMigrator(IServiceProvider serviceProvider, ILogger<NexusDatabaseMigrator> logger)
{
    private const long MigrationAdvisoryLockKey = 74839210L;

    // Serializes concurrent in-process EF Core 10 MigrateAsync() calls (e.g., parallel multi-DB test suites)
    // to prevent EF Core's MigrationsAssembly.ModelSnapshot finalization race on cold startup.
    private static readonly SemaphoreSlim InProcessEfMigrationGate = new(1, 1);

    public async Task MigrateAndHardeningAsync(string migrationConnectionString, string appRoleName = "nexus_app", CancellationToken ct = default)
    {
        logger.LogInformation("Acquiring PostgreSQL advisory lock ({LockKey}) for NEXUS C Foundation migration...", MigrationAdvisoryLockKey);

        await using var lockConn = new NpgsqlConnection(migrationConnectionString);
        await lockConn.OpenAsync(ct);
        await using (var lockCmd = new NpgsqlCommand($"SELECT pg_advisory_lock({MigrationAdvisoryLockKey});", lockConn))
        {
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            logger.LogInformation("Applying NEXUS C Foundation EF Core migrations across all schemas...");

            string[] schemas =
            [
                "messaging", "tenancy", "organization", "identity", "access_control",
                "audit", "workflow", "subscription", "configuration", "integration",
                "notification", "file_storage"
            ];

            foreach (var schema in schemas)
            {
                await using var cmd = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS \"{schema}\";", lockConn);
                await cmd.ExecuteNonQueryAsync(ct);
            }

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

            await ApplyRlsAndHardeningSqlAsync(migrationConnectionString, appRoleName, ct);

            // Seed default RBAC permissions/roles and NEXUS ecosystem Application Registry
            var accessControl = scope.ServiceProvider.GetRequiredService<IAccessControlService>();
            await accessControl.SeedDefaultsAsync(ct);

            var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
            await subscriptionService.SeedApplicationsAsync(ct);

            var rateLimitService = scope.ServiceProvider.GetRequiredService<IRateLimitPolicyService>();
            await rateLimitService.SeedDefaultPoliciesAsync(ct);

            var integrationService = scope.ServiceProvider.GetRequiredService<IIntegrationService>();
            await integrationService.SeedCanonicalProvidersAsync(ct);

            logger.LogInformation("NEXUS C Foundation migrations, RLS hardening, RBAC, Application Registry & Rate Limit seeding completed successfully.");
        }
        finally
        {
            try
            {
                await using var unlockCmd = new NpgsqlCommand($"SELECT pg_advisory_unlock({MigrationAdvisoryLockKey});", lockConn);
                await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to explicitly release migration advisory lock; session close will release it.");
            }
        }
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
            await InProcessEfMigrationGate.WaitAsync(ct);
            try
            {
                await db.Database.MigrateAsync(ct);
            }
            finally
            {
                InProcessEfMigrationGate.Release();
            }
        }
    }

    private static async Task ApplyRlsAndHardeningSqlAsync(string connectionString, string appRoleName, CancellationToken ct)
    {
        // All 28 strictly tenant-scoped tables enforcing PostgreSQL FORCE ROW LEVEL SECURITY
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
            ("workflow", "approval_delegations"),
            ("workflow", "history"),
            ("subscription", "subscriptions"),
            ("subscription", "subscription_items"),
            ("subscription", "entitlements"),
            ("subscription", "usage_records"),
            ("subscription", "tenant_applications"),
            ("configuration", "tenant_settings"),
            ("integration", "connections"),
            ("integration", "sync_logs"),
            ("integration", "integration_errors"),
            ("integration", "webhooks"),
            ("integration", "webhook_deliveries"),
            ("integration", "ai_usage_logs"),
            ("notification", "templates"),
            ("notification", "notifications"),
            ("notification", "preferences"),
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

        // Hybrid RLS policies for tables holding both Global/Platform rows and Tenant-specific rows (5 tables -> 33 total FORCE RLS tables!)
        const string hybridRlsSql = @"
            -- 1. access_control.roles
            ALTER TABLE access_control.roles ENABLE ROW LEVEL SECURITY;
            ALTER TABLE access_control.roles FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS nexus_roles_select ON access_control.roles;
            DROP POLICY IF EXISTS nexus_roles_write ON access_control.roles;
            CREATE POLICY nexus_roles_select ON access_control.roles
            FOR SELECT USING (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    ""Scope"" = 'Tenant'
                    AND (
                        ""TenantId"" IS NULL
                        OR (
                            NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                            AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                        )
                    )
                )
            );
            CREATE POLICY nexus_roles_write ON access_control.roles
            FOR ALL USING (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    ""Scope"" = 'Tenant'
                    AND ""IsSystemRole"" = false
                    AND NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            )
            WITH CHECK (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    ""Scope"" = 'Tenant'
                    AND ""IsSystemRole"" = false
                    AND NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            );

            -- 2. access_control.role_permissions: follows parent role visibility and write ownership!
            ALTER TABLE access_control.role_permissions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE access_control.role_permissions FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS nexus_role_permissions_select ON access_control.role_permissions;
            DROP POLICY IF EXISTS nexus_role_permissions_write ON access_control.role_permissions;
            CREATE POLICY nexus_role_permissions_select ON access_control.role_permissions
            FOR SELECT USING (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR EXISTS (
                    SELECT 1 FROM access_control.roles r
                    WHERE r.""Id"" = ""RoleId""
                      AND r.""Scope"" = 'Tenant'
                      AND (
                          r.""TenantId"" IS NULL
                          OR (
                              NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                              AND r.""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                          )
                      )
                )
            );
            CREATE POLICY nexus_role_permissions_write ON access_control.role_permissions
            FOR ALL USING (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR EXISTS (
                    SELECT 1 FROM access_control.roles r
                    WHERE r.""Id"" = ""RoleId""
                      AND r.""Scope"" = 'Tenant'
                      AND r.""IsSystemRole"" = false
                      AND NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                      AND r.""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            )
            WITH CHECK (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR EXISTS (
                    SELECT 1 FROM access_control.roles r
                    WHERE r.""Id"" = ""RoleId""
                      AND r.""Scope"" = 'Tenant'
                      AND r.""IsSystemRole"" = false
                      AND NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                      AND r.""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            );

            -- 3. access_control.user_roles
            ALTER TABLE access_control.user_roles ENABLE ROW LEVEL SECURITY;
            ALTER TABLE access_control.user_roles FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS nexus_user_roles_isolation ON access_control.user_roles;
            CREATE POLICY nexus_user_roles_isolation ON access_control.user_roles
            USING (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            )
            WITH CHECK (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            );

            -- 4. configuration.feature_flags
            ALTER TABLE configuration.feature_flags ENABLE ROW LEVEL SECURITY;
            ALTER TABLE configuration.feature_flags FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS nexus_feature_flags_select ON configuration.feature_flags;
            DROP POLICY IF EXISTS nexus_feature_flags_write ON configuration.feature_flags;
            CREATE POLICY nexus_feature_flags_select ON configuration.feature_flags
            FOR SELECT USING (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR ""TenantId"" IS NULL
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            );
            CREATE POLICY nexus_feature_flags_write ON configuration.feature_flags
            FOR ALL USING (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            )
            WITH CHECK (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            );

            -- 5. audit.audit_events: Tenant scope reads ONLY own tenant audit logs; Platform scope reads all; append-only INSERT enforced!
            ALTER TABLE audit.audit_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE audit.audit_events FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS nexus_audit_select ON audit.audit_events;
            DROP POLICY IF EXISTS nexus_audit_insert ON audit.audit_events;
            CREATE POLICY nexus_audit_select ON audit.audit_events
            FOR SELECT USING (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            );
            CREATE POLICY nexus_audit_insert ON audit.audit_events
            FOR INSERT WITH CHECK (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NULL
                )
            );

            -- 6. notification.email_deliveries: Platform scope reads/writes all; Tenant scope reads/writes ONLY own tenant deliveries!
            ALTER TABLE notification.email_deliveries ENABLE ROW LEVEL SECURITY;
            ALTER TABLE notification.email_deliveries FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS nexus_email_deliveries_isolation ON notification.email_deliveries;
            CREATE POLICY nexus_email_deliveries_isolation ON notification.email_deliveries
            USING (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            )
            WITH CHECK (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            );

            -- 7. messaging.api_request_logs: Platform scope reads all; Tenant scope reads ONLY own tenant API telemetry!
            ALTER TABLE messaging.api_request_logs ENABLE ROW LEVEL SECURITY;
            ALTER TABLE messaging.api_request_logs FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS nexus_api_request_logs_isolation ON messaging.api_request_logs;
            CREATE POLICY nexus_api_request_logs_isolation ON messaging.api_request_logs
            USING (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            )
            WITH CHECK (
                current_setting('app.bypass_rls', true) = 'on'
                OR current_setting('app.is_platform_scope', true) = 'on'
                OR (
                    NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    AND ""TenantId"" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                )
            );";

        await using (var hybridCmd = new NpgsqlCommand(hybridRlsSql, conn))
        {
            await hybridCmd.ExecuteNonQueryAsync(ct);
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
                    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA ""{schema}"" TO ""{appRoleName}"";
                    ALTER DEFAULT PRIVILEGES IN SCHEMA ""{schema}"" GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ""{appRoleName}"";
                    ALTER DEFAULT PRIVILEGES IN SCHEMA ""{schema}"" GRANT USAGE, SELECT ON SEQUENCES TO ""{appRoleName}"";";
                await using var gCmd = new NpgsqlCommand(grantSql, conn);
                await gCmd.ExecuteNonQueryAsync(ct);
            }

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
            return;
        }

        var auditWriter = scope.ServiceProvider.GetRequiredService<Nexus.SharedKernel.Contracts.IAuditWriter>();
        await auditWriter.WriteAsync(new Nexus.SharedKernel.Contracts.AuditEntryRequest(
            Action: "platform.bootstrap.admin_created",
            ResourceType: "User",
            ResourceId: result.Value.Id.ToString("D"),
            TenantId: null,
            Result: "Success",
            Reason: "Initial platform administrator provisioned via explicit bootstrap configuration.",
            AfterState: new { result.Value.Id, result.Value.Email, Role = SystemRoles.PlatformSuperAdmin },
            IsCrossTenantPlatformAccess: true), ct);
    }
}
