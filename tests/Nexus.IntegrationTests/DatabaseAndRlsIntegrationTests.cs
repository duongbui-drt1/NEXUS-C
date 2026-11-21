using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.Audit.Infrastructure;
using Nexus.Modules.Organization.Domain;
using Nexus.Modules.Organization.Infrastructure;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Events;
using Nexus.SharedKernel.Results;
using Npgsql;
using Xunit;

namespace Nexus.IntegrationTests;

public sealed class DatabaseAndRlsIntegrationTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    [Fact]
    public async Task AppDatabaseRole_IsNonSuperuserAndNoBypassRls()
    {
        await using var conn = new NpgsqlConnection(fixture.AppConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user;", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.False(reader.GetBoolean(0), "Application role MUST NOT be a PostgreSQL superuser.");
        Assert.False(reader.GetBoolean(1), "Application role MUST NOT have BYPASSRLS.");
    }

    [Fact]
    public async Task PostgreSqlRls_EnforcesFailClosedAndStrictTenantIsolation_AtDatabaseLayer()
    {
        Guid tenantA;
        Guid tenantB;

        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var tenantSvc = scope.ServiceProvider.GetRequiredService<ITenantService>();
            var tA = await tenantSvc.CreateAsync(new CreateTenantRequest("RLSA1", "RLS Tenant A", "rls-a1"));
            var tB = await tenantSvc.CreateAsync(new CreateTenantRequest("RLSB1", "RLS Tenant B", "rls-b1"));
            Assert.True(tA.IsSuccess);
            Assert.True(tB.IsSuccess);
            tenantA = tA.Value.Id;
            tenantB = tB.Value.Id;

            var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var orgDb = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();

            using (tenantCtx.BeginScope(tenantA))
            {
                orgDb.LegalEntities.Add(LegalEntity.Create(tenantA, "LE-A1", "Alpha Legal", "TIN-A", "Addr A").Value);
                await orgDb.SaveChangesAsync();
            }

            using (tenantCtx.BeginScope(tenantB))
            {
                orgDb.LegalEntities.Add(LegalEntity.Create(tenantB, "LE-B1", "Beta Legal", "TIN-B", "Addr B").Value);
                await orgDb.SaveChangesAsync();
            }
        }

        // Now connect DIRECTLY with raw SQL as nexus_app (zero EF Core query filter involvement!) to prove PostgreSQL RLS independently!
        await using var conn = new NpgsqlConnection(fixture.AppConnectionString);
        await conn.OpenAsync();

        // 1. Fail-closed: with NO tenant GUC set, query returns 0 rows!
        await using (var noTenantCmd = new NpgsqlCommand("SELECT COUNT(*) FROM organization.legal_entities;", conn))
        {
            var count = (long)(await noTenantCmd.ExecuteScalarAsync())!;
            Assert.Equal(0L, count);
        }

        // 2. Set Tenant A -> sees ONLY Tenant A rows!
        await using (var txA = await conn.BeginTransactionAsync())
        {
            await using (var setCmd = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', @tid, true);", conn, txA))
            {
                setCmd.Parameters.AddWithValue("@tid", tenantA.ToString("D"));
                await setCmd.ExecuteNonQueryAsync();
            }

            await using var queryA = new NpgsqlCommand("SELECT \"Code\", \"TenantId\" FROM organization.legal_entities;", conn, txA);
            await using var rdr = await queryA.ExecuteReaderAsync();
            var rows = new List<(string Code, Guid TenantId)>();
            while (await rdr.ReadAsync()) rows.Add((rdr.GetString(0), rdr.GetGuid(1)));
            await rdr.CloseAsync();

            Assert.Single(rows);
            Assert.Equal("LE-A1", rows[0].Code);
            Assert.Equal(tenantA, rows[0].TenantId);

            // 3. Attempting cross-tenant INSERT for Tenant B while scoped to Tenant A MUST fail with RLS violation (42501)!
            await using var crossInsert = new NpgsqlCommand(@"
                INSERT INTO organization.legal_entities
                (""Id"", ""TenantId"", ""Code"", ""LegalName"", ""Status"", ""CreatedAt"", ""UpdatedAt"")
                VALUES (@id, @tenantB, 'HACK', 'Cross Tenant', 'Active', now(), now());", conn, txA);
            crossInsert.Parameters.AddWithValue("@id", Guid.CreateVersion7());
            crossInsert.Parameters.AddWithValue("@tenantB", tenantB);

            var pgEx = await Assert.ThrowsAsync<PostgresException>(() => crossInsert.ExecuteNonQueryAsync());
            Assert.Equal("42501", pgEx.SqlState);

            await txA.RollbackAsync();
        }
    }

    [Fact]
    public async Task PooledConnection_MaxPoolSize1_NeverLeaksTenantContextAcrossRequests()
    {
        Guid tenantA;
        Guid tenantB;

        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var tenantSvc = scope.ServiceProvider.GetRequiredService<ITenantService>();
            tenantA = (await tenantSvc.CreateAsync(new CreateTenantRequest("POOLA", "Pool A", "pool-a"))).Value.Id;
            tenantB = (await tenantSvc.CreateAsync(new CreateTenantRequest("POOLB", "Pool B", "pool-b"))).Value.Id;

            var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var orgDb = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();

            using (ctx.BeginScope(tenantA))
            {
                orgDb.LegalEntities.Add(LegalEntity.Create(tenantA, "POOL-LE-A", "Pool Alpha", null, null).Value);
                await orgDb.SaveChangesAsync();
            }

            using (ctx.BeginScope(tenantB))
            {
                orgDb.LegalEntities.Add(LegalEntity.Create(tenantB, "POOL-LE-B", "Pool Beta", null, null).Value);
                await orgDb.SaveChangesAsync();
            }
        }

        // Force Maximum Pool Size=1 so EVERY query physically reuses the EXACT same PostgreSQL backend connection!
        var singlePoolConn = new NpgsqlConnectionStringBuilder(fixture.AppConnectionString)
        {
            MaxPoolSize = 1,
            MinPoolSize = 1,
            Pooling = true
        }.ConnectionString;

        for (var i = 0; i < 12; i++)
        {
            var tenantCtx = new Nexus.BuildingBlocks.Context.AmbientTenantContext();
            var connInt = new TenantRlsInterceptor(tenantCtx);
            var txInt = new TenantRlsTransactionInterceptor(connInt);
            var cmdInt = new TenantRlsCommandInterceptor(connInt);

            var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
                .UseNpgsql(singlePoolConn)
                .AddInterceptors(connInt, txInt, cmdInt)
                .Options;

            var target = i % 3; // 0 = TenantA, 1 = TenantB, 2 = NoTenant (fail-closed)
            if (target == 0) tenantCtx.SetTenant(tenantA);
            else if (target == 1) tenantCtx.SetTenant(tenantB);

            await using var db = new OrganizationDbContext(opts, tenantCtx, new StubCurrentUser(), new SystemClock());
            // Use IgnoreQueryFilters() to prove PostgreSQL RLS ALONE isolates the pooled connection!
            var entities = await db.LegalEntities.IgnoreQueryFilters().AsNoTracking().ToListAsync();

            if (target == 0)
            {
                Assert.Single(entities);
                Assert.Equal(tenantA, entities[0].TenantId);
            }
            else if (target == 1)
            {
                Assert.Single(entities);
                Assert.Equal(tenantB, entities[0].TenantId);
            }
            else
            {
                Assert.Empty(entities);
            }
        }
    }

    [Fact]
    public async Task TransactionRollback_RevertsDomainAndOutboxWritesAtomically()
    {
        Guid tenantId;
        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var tenantSvc = scope.ServiceProvider.GetRequiredService<ITenantService>();
            tenantId = (await tenantSvc.CreateAsync(new CreateTenantRequest("ROLL1", "Rollback Tenant", "roll-1"))).Value.Id;
        }

        var eventId = Guid.CreateVersion7();
        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var orgDb = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();

            using (ctx.BeginScope(tenantId))
            {
                await using var tx = await orgDb.Database.BeginTransactionAsync();
                orgDb.LegalEntities.Add(LegalEntity.Create(tenantId, "ROLL-LE", "Rolled Back Entity", null, null).Value);
                orgDb.StageOutboxEvent(new EventEnvelope(
                    eventId, "test.rolledback.v1", tenantId, DateTimeOffset.UtcNow, "corr-rb", 1,
                    System.Text.Json.JsonSerializer.SerializeToElement(new { ok = false })));
                await orgDb.SaveChangesAsync();

                // Simulate failure before commit
                await tx.RollbackAsync();
            }
        }

        using (var verifyScope = fixture.ApiFactory.Services.CreateScope())
        {
            var ctx = verifyScope.ServiceProvider.GetRequiredService<ITenantContext>();
            var orgDb = verifyScope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            var msgDb = verifyScope.ServiceProvider.GetRequiredService<MessagingDbContext>();

            using (ctx.BeginScope(tenantId))
            {
                Assert.False(await orgDb.LegalEntities.AnyAsync(e => e.Code == "ROLL-LE"));
            }
            Assert.False(await msgDb.OutboxMessages.AnyAsync(m => m.EventId == eventId));
        }
    }

    [Fact]
    public async Task Outbox_RetriesFailedPublications_AndMovesToDeadLetterQueue()
    {
        fixture.EventBus.Clear();
        var envelope = EventEnvelope.Create("test.dlq.event.v1", Guid.NewGuid(), "corr-dlq", new { value = 42 });

        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IEventOutboxWriter>();
            await writer.EnqueueAsync(envelope);
        }

        // Inject broker failure for this specific event
        fixture.EventBus.FailureInjector = ev => ev.EventId == envelope.EventId
            ? new InvalidOperationException("Simulated RabbitMQ broker outage")
            : null;

        try
        {
            // MaxRetries in test config = 3; force NextAttemptAt to past between attempts to test all 3 retries deterministically
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var scope = fixture.ApiFactory.Services.CreateScope();
                var msgDb = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
                if (attempt > 0)
                {
                    await msgDb.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE messaging.outbox_messages SET \"NextAttemptAt\" = now() - interval '1 minute' WHERE \"EventId\" = {envelope.EventId}");
                }

                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
                await dispatcher.DispatchBatchAsync();
            }
        }
        finally
        {
            fixture.EventBus.FailureInjector = null;
        }

        using (var verify = fixture.ApiFactory.Services.CreateScope())
        {
            var msgDb = verify.ServiceProvider.GetRequiredService<MessagingDbContext>();
            var outbox = await msgDb.OutboxMessages.AsNoTracking().FirstAsync(x => x.EventId == envelope.EventId);
            Assert.Equal(OutboxStatus.DeadLettered, outbox.Status);
            Assert.Equal(3, outbox.RetryCount);

            var dlq = await msgDb.DeadLetterMessages.AsNoTracking().FirstOrDefaultAsync(x => x.EventId == envelope.EventId);
            Assert.NotNull(dlq);
            Assert.Contains("Simulated RabbitMQ broker outage", dlq!.Reason);
        }
    }

    [Fact]
    public async Task Inbox_ConcurrentDuplicateEventDelivery_ExecutesSideEffectExactlyOnce()
    {
        var envelope = EventEnvelope.Create("test.concurrent.inbox.v1", Guid.NewGuid(), "corr-idem", new { amount = 100 });
        var executionCount = 0;

        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            using var scope = fixture.ApiFactory.Services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IdempotentEventProcessor>();
            return await processor.ProcessAsync(envelope, "ConcurrentTestConsumer", (_, _) =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.CompletedTask;
            });
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(4, results.Count(r => !r));
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task BackgroundWorker_ProcessesEventStrictlyWithinEventTenantScope()
    {
        Guid tenantA;
        Guid tenantB;

        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var tenantSvc = scope.ServiceProvider.GetRequiredService<ITenantService>();
            tenantA = (await tenantSvc.CreateAsync(new CreateTenantRequest("BGWKA", "Worker Tenant A", "bgwk-a"))).Value.Id;
            tenantB = (await tenantSvc.CreateAsync(new CreateTenantRequest("BGWKB", "Worker Tenant B", "bgwk-b"))).Value.Id;

            var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var orgDb = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            using (ctx.BeginScope(tenantA))
            {
                orgDb.LegalEntities.Add(LegalEntity.Create(tenantA, "BG-A", "Worker Alpha", null, null).Value);
                await orgDb.SaveChangesAsync();
            }
            using (ctx.BeginScope(tenantB))
            {
                orgDb.LegalEntities.Add(LegalEntity.Create(tenantB, "BG-B", "Worker Beta", null, null).Value);
                await orgDb.SaveChangesAsync();
            }
        }

        var evForA = EventEnvelope.Create("test.worker.scope.v1", tenantA, "corr-bg", new { });
        var visibleTenants = new ConcurrentBag<Guid>();

        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IdempotentEventProcessor>();
            var orgDb = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();

            var processed = await processor.ProcessAsync(evForA, "TenantScopedWorker", async (_, ct) =>
            {
                // Even with IgnoreQueryFilters(), PostgreSQL RLS ensures worker ONLY sees Tenant A!
                var rows = await orgDb.LegalEntities.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
                foreach (var r in rows) visibleTenants.Add(r.TenantId);
            });

            Assert.True(processed);
        }

        Assert.Single(visibleTenants);
        Assert.All(visibleTenants, tid => Assert.Equal(tenantA, tid));
    }

    [Fact]
    public async Task AuditEvents_AreImmutableAtBothApplicationAndPostgreSqlLayers()
    {
        Guid auditId;
        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
            await writer.WriteAsync(new AuditEntryRequest("test.audit.immutable", "Test", "1", null, "Success"));

            var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            var ev = await auditDb.AuditEvents.OrderByDescending(x => x.OccurredAt).FirstAsync();
            auditId = ev.Id;

            // Application-layer EF Core guard rejects deletion
            auditDb.AuditEvents.Remove(ev);
            var ex = await Assert.ThrowsAsync<DomainException>(() => auditDb.SaveChangesAsync());
            Assert.Equal("AUDIT_IMMUTABLE", ex.Error.Code);
        }

        // PostgreSQL layer as nexus_app rejects UPDATE and DELETE (permission denied 42501)
        await using (var appConn = new NpgsqlConnection(fixture.AppConnectionString))
        {
            await appConn.OpenAsync();
            await using var upd = new NpgsqlCommand("UPDATE audit.audit_events SET \"Result\" = 'Tampered' WHERE \"Id\" = @id;", appConn);
            upd.Parameters.AddWithValue("@id", auditId);
            await Assert.ThrowsAsync<PostgresException>(() => upd.ExecuteNonQueryAsync());
        }

        // Even table owner (nexus_migrator) is blocked by PostgreSQL immutability trigger!
        await using (var ownerConn = new NpgsqlConnection(fixture.MigratorConnectionString))
        {
            await ownerConn.OpenAsync();
            await using var del = new NpgsqlCommand("DELETE FROM audit.audit_events WHERE \"Id\" = @id;", ownerConn);
            del.Parameters.AddWithValue("@id", auditId);
            var trigEx = await Assert.ThrowsAsync<PostgresException>(() => del.ExecuteNonQueryAsync());
            Assert.Contains("append-only and immutable", trigEx.Message);
        }
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? SessionId => null;
        public Guid? TenantId => null;
        public string? Email => null;
        public string? Scope => null;
        public bool IsAuthenticated => false;
        public bool IsPlatformUser => false;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
