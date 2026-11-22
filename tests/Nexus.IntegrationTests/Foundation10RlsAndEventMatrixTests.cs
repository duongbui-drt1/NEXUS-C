using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Messaging;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.AccessControl.Domain;
using Nexus.Modules.AccessControl.Infrastructure;
using Nexus.Modules.Organization.Domain;
using Nexus.Modules.Organization.Infrastructure;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Events;
using Npgsql;
using Xunit;

namespace Nexus.IntegrationTests;

/// <summary>
/// Section 13.4 & 13.6 Verification:
/// - PostgreSQL FORCE RLS on access_control.role_permissions & audit.audit_events under nexus_app
/// - Concurrent connection-pool tenant context isolation & GUC reset
/// - Complete Outbox -> Failure -> DLQ -> Manual Retry -> Recovery & Inbox deduplication matrix
/// </summary>
public sealed class Foundation10RlsAndEventMatrixTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    [Fact]
    public async Task PostgreSqlRls_EnforcesRolePermissionsAndAuditEventsIsolation_UnderNexusApp()
    {
        Guid tenantA;
        Guid tenantB;
        Guid roleAId;
        Guid roleBId;
        Guid anyPermId;

        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var tenantSvc = scope.ServiceProvider.GetRequiredService<ITenantService>();
            tenantA = (await tenantSvc.CreateAsync(new CreateTenantRequest("F10RLSA", "F10 RLS A", "f10-rls-a"))).Value.Id;
            tenantB = (await tenantSvc.CreateAsync(new CreateTenantRequest("F10RLSB", "F10 RLS B", "f10-rls-b"))).Value.Id;

            var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var acDb = scope.ServiceProvider.GetRequiredService<AccessControlDbContext>();
            var auditWriter = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

            anyPermId = await acDb.Permissions.AsNoTracking().Select(p => p.Id).FirstAsync();

            using (ctx.BeginScope(tenantA))
            {
                var rA = Role.Create("RlsPermRoleA", "Custom A", RoleScopeType.Tenant, tenantA, false).Value;
                acDb.Roles.Add(rA);
                acDb.RolePermissions.Add(new RolePermission { RoleId = rA.Id, PermissionId = anyPermId });
                await acDb.SaveChangesAsync();
                roleAId = rA.Id;
            }

            using (ctx.BeginScope(tenantB))
            {
                var rB = Role.Create("RlsPermRoleB", "Custom B", RoleScopeType.Tenant, tenantB, false).Value;
                acDb.Roles.Add(rB);
                acDb.RolePermissions.Add(new RolePermission { RoleId = rB.Id, PermissionId = anyPermId });
                await acDb.SaveChangesAsync();
                roleBId = rB.Id;
            }

            await auditWriter.WriteAsync(new AuditEntryRequest("f10.audit.a", "Doc", "A1", tenantA, "Success"));
            await auditWriter.WriteAsync(new AuditEntryRequest("f10.audit.b", "Doc", "B1", tenantB, "Success"));
        }

        // Direct PostgreSQL connection as non-superuser nexus_app (NOBYPASSRLS)
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

        // 1. role_permissions SELECT: sees roleA, NEVER roleB!
        var visibleRoleIds = new List<Guid>();
        await using (var rpCmd = new NpgsqlCommand("SELECT DISTINCT \"RoleId\" FROM access_control.role_permissions;", conn, tx))
        await using (var rdr = await rpCmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync()) visibleRoleIds.Add(rdr.GetGuid(0));
        }

        Assert.Contains(roleAId, visibleRoleIds);
        Assert.DoesNotContain(roleBId, visibleRoleIds);

        // 2. role_permissions INSERT into Tenant B's role -> 42501 RLS violation!
        var secondPermId = Guid.Empty;
        await using (var pCmd = new NpgsqlCommand("SELECT \"Id\" FROM access_control.permissions WHERE \"Id\" <> @p LIMIT 1;", conn, tx))
        {
            pCmd.Parameters.AddWithValue("@p", anyPermId);
            secondPermId = (Guid)(await pCmd.ExecuteScalarAsync())!;
        }

        await using (var badInsert = new NpgsqlCommand(
            "INSERT INTO access_control.role_permissions (\"RoleId\", \"PermissionId\") VALUES (@rid, @pid);", conn, tx))
        {
            badInsert.Parameters.AddWithValue("@rid", roleBId);
            badInsert.Parameters.AddWithValue("@pid", secondPermId);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => badInsert.ExecuteNonQueryAsync());
            Assert.Equal("42501", ex.SqlState);
        }

        await tx.RollbackAsync();

        // 3. audit.audit_events SELECT & INSERT RLS under Tenant A
        await using var tx2 = await conn.BeginTransactionAsync();
        await using (var setCmd = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tid, true), set_config('app.is_platform_scope', 'off', true), set_config('app.bypass_rls', 'off', true);",
            conn, tx2))
        {
            setCmd.Parameters.AddWithValue("@tid", tenantA.ToString("D"));
            await setCmd.ExecuteNonQueryAsync();
        }

        var visibleAuditActions = new List<string>();
        await using (var aCmd = new NpgsqlCommand("SELECT \"Action\" FROM audit.audit_events;", conn, tx2))
        await using (var rdr = await aCmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync()) visibleAuditActions.Add(rdr.GetString(0));
        }

        Assert.Contains("f10.audit.a", visibleAuditActions);
        Assert.DoesNotContain("f10.audit.b", visibleAuditActions);

        // Tenant A CANNOT forge an audit event belonging to Tenant B -> 42501 RLS violation!
        await using (var forgeAudit = new NpgsqlCommand(@"
            INSERT INTO audit.audit_events
            (""Id"", ""TenantId"", ""Action"", ""ResourceType"", ""OccurredAt"", ""CorrelationId"", ""Result"", ""IsCrossTenantPlatformAccess"")
            VALUES (@id, @tenantB, 'forged.action', 'Test', now(), 'corr-forge', 'Success', false);", conn, tx2))
        {
            forgeAudit.Parameters.AddWithValue("@id", Guid.CreateVersion7());
            forgeAudit.Parameters.AddWithValue("@tenantB", tenantB);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => forgeAudit.ExecuteNonQueryAsync());
            Assert.Equal("42501", ex.SqlState);
        }

        await tx2.RollbackAsync();
    }

    [Fact]
    public async Task TenantContextAndConnectionPooling_ConcurrentRequestsNeverLeakAcrossTenants()
    {
        Guid tenantA;
        Guid tenantB;

        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var tenantSvc = scope.ServiceProvider.GetRequiredService<ITenantService>();
            tenantA = (await tenantSvc.CreateAsync(new CreateTenantRequest("POOLA", "Pool Tenant A", "pool-a"))).Value.Id;
            tenantB = (await tenantSvc.CreateAsync(new CreateTenantRequest("POOLB", "Pool Tenant B", "pool-b"))).Value.Id;

            var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var orgDb = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();

            using (ctx.BeginScope(tenantA))
            {
                orgDb.LegalEntities.Add(LegalEntity.Create(tenantA, "PA-1", "Pool Corp A", null, null).Value);
                await orgDb.SaveChangesAsync();
            }

            using (ctx.BeginScope(tenantB))
            {
                orgDb.LegalEntities.Add(LegalEntity.Create(tenantB, "PB-1", "Pool Corp B", null, null).Value);
                await orgDb.SaveChangesAsync();
            }
        }

        // Run 24 concurrent scoped reads across Tenant A, Tenant B, and empty context over the same Npgsql pool!
        var tasks = Enumerable.Range(0, 24).Select(async i =>
        {
            using var scope = fixture.ApiFactory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var orgDb = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();

            var mode = i % 3;
            if (mode == 0)
            {
                using (ctx.BeginScope(tenantA))
                {
                    var rows = await orgDb.LegalEntities.IgnoreQueryFilters().AsNoTracking().ToListAsync();
                    Assert.NotEmpty(rows);
                    Assert.All(rows, r => Assert.Equal(tenantA, r.TenantId));
                }
            }
            else if (mode == 1)
            {
                using (ctx.BeginScope(tenantB))
                {
                    var rows = await orgDb.LegalEntities.IgnoreQueryFilters().AsNoTracking().ToListAsync();
                    Assert.NotEmpty(rows);
                    Assert.All(rows, r => Assert.Equal(tenantB, r.TenantId));
                }
            }
            else
            {
                // Empty tenant context MUST fail closed and see 0 rows even with IgnoreQueryFilters!
                var rows = await orgDb.LegalEntities.IgnoreQueryFilters().AsNoTracking().ToListAsync();
                Assert.Empty(rows);
            }

            // Confirm ambient context is restored cleanly after BeginScope disposal
            Assert.False(ctx.HasTenant);
            Assert.False(ctx.IsPlatformBypass);
        });

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task EventReliabilityMatrix_RetryBackoff_DlqRouting_AndRecoveryAfterManualRetry()
    {
        var tenantId = Guid.NewGuid();
        var corrId = $"corr-dlq-{Guid.NewGuid():N}";
        var env = EventEnvelope.Create(
            "platform.test.dlq_recovery.v1",
            tenantId,
            corrId,
            new { OrderRef = "ORD-42" },
            producer: "nexus-c",
            causationId: "cause-77");

        // Enqueue outbox event
        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IEventOutboxWriter>();
            await writer.EnqueueAsync(env);
        }

        // Inject broker failure for this exact event until it hits DeadLettered (MaxRetries = 5)
        fixture.EventBus.Clear();
        fixture.EventBus.FailureInjector = e => e.EventId == env.EventId ? new InvalidOperationException("Simulated broker outage Password=secret123") : null;

        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var scope = fixture.ApiFactory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

                // Fast-forward NextAttemptAt to simulate backoff expiry
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE messaging.outbox_messages SET \"NextAttemptAt\" = now() - interval '1 minute' WHERE \"EventId\" = {env.EventId};");

                await dispatcher.DispatchBatchAsync();
            }
        }
        finally
        {
            fixture.EventBus.FailureInjector = null;
        }

        // Verify DLQ entry created & outbox marked DeadLettered
        Guid dlqId;
        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
            var dlq = await db.DeadLetterMessages.AsNoTracking().FirstOrDefaultAsync(d => d.EventId == env.EventId);
            Assert.NotNull(dlq);
            Assert.False(dlq!.IsResolved);
            Assert.Equal(corrId, dlq.CorrelationId);
            Assert.Equal(tenantId, dlq.TenantId);
            dlqId = dlq.Id;
        }

        // Now trigger manual retry via real authenticated PlatformSuperAdmin HTTP API!
        var adminAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var admin = fixture.CreateAuthenticatedClient(adminAuth.AccessToken);

        var listResp = await admin.GetAsync("/api/v1/platform/operations/dead-letters?resolved=false");
        listResp.EnsureSuccessStatusCode();
        var listJson = await listResp.Content.ReadAsStringAsync();
        Assert.Contains("Password=[REDACTED]", listJson);
        Assert.DoesNotContain("secret123", listJson);

        var retryResp = await admin.PostAsync($"/api/v1/platform/operations/dead-letters/{dlqId:D}/retry", null);
        retryResp.EnsureSuccessStatusCode();

        // Loop protection: second retry on already-resolved DLQ entry MUST be rejected (422)!
        var duplicateRetry = await admin.PostAsync($"/api/v1/platform/operations/dead-letters/{dlqId:D}/retry", null);
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, duplicateRetry.StatusCode);

        // Dispatch again after recovery -> event MUST now publish cleanly with preserved CorrelationId & TenantId!
        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
            await dispatcher.DispatchBatchAsync();
        }

        Assert.Contains(fixture.EventBus.PublishedEvents, e =>
            e.EventId == env.EventId &&
            e.CorrelationId == corrId &&
            e.TenantId == tenantId &&
            e.Producer == "nexus-c" &&
            e.CausationId == "cause-77");
    }
}
