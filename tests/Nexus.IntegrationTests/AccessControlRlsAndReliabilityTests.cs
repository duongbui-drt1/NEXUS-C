using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.AccessControl.Domain;
using Nexus.Modules.AccessControl.Infrastructure;
using Nexus.Modules.Configuration.Domain;
using Nexus.Modules.Configuration.Infrastructure;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Results;
using Npgsql;
using Xunit;

namespace Nexus.IntegrationTests;

public sealed class AccessControlRlsAndReliabilityTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    [Fact]
    public async Task PostgreSqlRls_IsolatesAccessControlRolesAndUserRoles_AndFeatureFlags_UnderNexusApp()
    {
        Guid tenantA;
        Guid tenantB;
        Guid customRoleAId;
        Guid customRoleBId;

        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var tenantSvc = scope.ServiceProvider.GetRequiredService<ITenantService>();
            tenantA = (await tenantSvc.CreateAsync(new CreateTenantRequest("ACRLSA", "AC RLS A", "ac-rls-a"))).Value.Id;
            tenantB = (await tenantSvc.CreateAsync(new CreateTenantRequest("ACRLSB", "AC RLS B", "ac-rls-b"))).Value.Id;

            var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var acDb = scope.ServiceProvider.GetRequiredService<AccessControlDbContext>();
            var cfgDb = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();

            // Seed Tenant A custom role + user role + feature flag
            using (ctx.BeginScope(tenantA))
            {
                var roleA = Role.Create("CustomRoleA", "Only for A", RoleScopeType.Tenant, tenantA, isSystemRole: false).Value;
                acDb.Roles.Add(roleA);
                acDb.UserRoles.Add(UserRole.Assign(Guid.CreateVersion7(), roleA.Id, tenantA));
                await acDb.SaveChangesAsync();
                customRoleAId = roleA.Id;

                cfgDb.FeatureFlags.Add(FeatureFlag.Create(tenantA, "flag_only_a", true, "A override").Value);
                await cfgDb.SaveChangesAsync();
            }

            // Seed Tenant B custom role + user role + feature flag
            using (ctx.BeginScope(tenantB))
            {
                var roleB = Role.Create("CustomRoleB", "Only for B", RoleScopeType.Tenant, tenantB, isSystemRole: false).Value;
                acDb.Roles.Add(roleB);
                acDb.UserRoles.Add(UserRole.Assign(Guid.CreateVersion7(), roleB.Id, tenantB));
                await acDb.SaveChangesAsync();
                customRoleBId = roleB.Id;

                cfgDb.FeatureFlags.Add(FeatureFlag.Create(tenantB, "flag_only_b", true, "B override").Value);
                await cfgDb.SaveChangesAsync();
            }

            // Seed global platform feature flag
            using (ctx.BeginScope(null, platformBypass: true, bypassReason: "test-global-flag"))
            {
                cfgDb.FeatureFlags.Add(FeatureFlag.Create(null, "flag_global_default", true, "Global").Value);
                await cfgDb.SaveChangesAsync();
            }
        }

        // Now connect DIRECTLY as nexus_app via raw SQL (bypassing EF Core completely!) to verify PostgreSQL RLS!
        await using var conn = new NpgsqlConnection(fixture.AppConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var setCmd = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tid, true), set_config('app.is_platform_scope', 'off', true), set_config('app.bypass_rls', 'off', true);",
            conn, tx))
        {
            setCmd.Parameters.AddWithValue("@tid", tenantA.ToString("D"));
            await setCmd.ExecuteNonQueryAsync();
        }

        // 1. Tenant A sees global Tenant system roles + CustomRoleA, NEVER Platform roles or CustomRoleB!
        var visibleRoles = new List<(Guid Id, string Name, string Scope)>();
        await using (var roleQuery = new NpgsqlCommand("SELECT \"Id\", \"Name\", \"Scope\" FROM access_control.roles;", conn, tx))
        await using (var rdr = await roleQuery.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync()) visibleRoles.Add((rdr.GetGuid(0), rdr.GetString(1), rdr.GetString(2)));
        }

        Assert.Contains(visibleRoles, r => r.Id == customRoleAId);
        Assert.Contains(visibleRoles, r => r.Name == "TenantAdmin" && r.Scope == "Tenant");
        Assert.DoesNotContain(visibleRoles, r => r.Id == customRoleBId);
        Assert.DoesNotContain(visibleRoles, r => r.Scope == "Platform");

        // 2. Tenant A sees ONLY Tenant A user_roles (NEVER Platform user_roles or Tenant B user_roles!)
        var visibleUserRoleTenants = new List<Guid?>();
        await using (var urQuery = new NpgsqlCommand("SELECT \"TenantId\" FROM access_control.user_roles;", conn, tx))
        await using (var rdr = await urQuery.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync()) visibleUserRoleTenants.Add(rdr.IsDBNull(0) ? null : rdr.GetGuid(0));
        }

        Assert.NotEmpty(visibleUserRoleTenants);
        Assert.All(visibleUserRoleTenants, tid => Assert.Equal(tenantA, tid));

        // 3. Tenant A CANNOT insert a custom role for Tenant B (42501 RLS violation!)
        await using (var badRoleInsert = new NpgsqlCommand(@"
            INSERT INTO access_control.roles
            (""Id"", ""TenantId"", ""Name"", ""NormalizedName"", ""Description"", ""Scope"", ""IsSystemRole"", ""CreatedAt"", ""UpdatedAt"")
            VALUES (@id, @tenantB, 'InjectedB', 'INJECTEDB', 'Hack', 'Tenant', false, now(), now());", conn, tx))
        {
            badRoleInsert.Parameters.AddWithValue("@id", Guid.CreateVersion7());
            badRoleInsert.Parameters.AddWithValue("@tenantB", tenantB);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => badRoleInsert.ExecuteNonQueryAsync());
            Assert.Equal("42501", ex.SqlState);
        }

        await tx.RollbackAsync();

        // 4. Verify feature_flags hybrid RLS in a fresh transaction
        await using var tx2 = await conn.BeginTransactionAsync();
        await using (var setCmd = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tid, true), set_config('app.is_platform_scope', 'off', true);",
            conn, tx2))
        {
            setCmd.Parameters.AddWithValue("@tid", tenantA.ToString("D"));
            await setCmd.ExecuteNonQueryAsync();
        }

        var visibleFlags = new List<string>();
        await using (var fQuery = new NpgsqlCommand("SELECT \"Key\" FROM configuration.feature_flags;", conn, tx2))
        await using (var rdr = await fQuery.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync()) visibleFlags.Add(rdr.GetString(0));
        }

        Assert.Contains("flag_only_a", visibleFlags);
        Assert.Contains("flag_global_default", visibleFlags);
        Assert.DoesNotContain("flag_only_b", visibleFlags);

        // Tenant A CANNOT mutate global platform feature flag
        await using (var mutateGlobalFlag = new NpgsqlCommand(
            "UPDATE configuration.feature_flags SET \"IsEnabled\" = false WHERE \"Key\" = 'flag_global_default';", conn, tx2))
        {
            var rowsAffected = await mutateGlobalFlag.ExecuteNonQueryAsync();
            Assert.Equal(0, rowsAffected);
        }

        await tx2.RollbackAsync();
    }

    [Fact]
    public async Task AccessControlDbContext_SaveChangesGuard_RejectsCrossTenantRoleWriteInApplicationLayer()
    {
        Guid tenantA;
        Guid tenantB;

        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var tenantSvc = scope.ServiceProvider.GetRequiredService<ITenantService>();
            tenantA = (await tenantSvc.CreateAsync(new CreateTenantRequest("ACGRDA", "AC Guard A", "ac-grd-a"))).Value.Id;
            tenantB = (await tenantSvc.CreateAsync(new CreateTenantRequest("ACGRDB", "AC Guard B", "ac-grd-b"))).Value.Id;

            var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var acDb = scope.ServiceProvider.GetRequiredService<AccessControlDbContext>();

            using (ctx.BeginScope(tenantA))
            {
                acDb.Roles.Add(Role.Create("HackB", "Cross tenant", RoleScopeType.Tenant, tenantB, false).Value);
                var ex = await Assert.ThrowsAsync<DomainException>(() => acDb.SaveChangesAsync());
                Assert.Equal("ACCESS_CONTROL_CROSS_TENANT_WRITE_DENIED", ex.Error.Code);
            }
        }
    }

    [Fact]
    public async Task Reliability_ReadinessReturns503WhenPostgresUnreachable_WhileLivenessRemains200()
    {
        var brokenConn = "Host=127.0.0.1;Port=54399;Database=unreachable;Username=nexus_app;Password=bad;Timeout=2";
        using var degradedFactory = new NexusApiFactory(brokenConn, brokenConn, fixture.TempStorageDir);
        using var client = degradedFactory.CreateClient();

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }
}
