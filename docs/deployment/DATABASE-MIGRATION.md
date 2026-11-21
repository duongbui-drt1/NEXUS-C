# Database Migration Guide

NEXUS C Foundation maintains **12 independent EF Core Code-First migration histories**, one per bounded-context schema, ensuring clean module data ownership.

---

## Migration Ownership Map

| Schema | DbContext | Migration History Table | Project Path |
|---|---|---|---|
| `messaging` | `MessagingDbContext` | `messaging.__ef_migrations_history_messaging` | `src/Nexus.BuildingBlocks/Messaging/Migrations` |
| `tenancy` | `TenancyDbContext` | `tenancy.__ef_migrations_history_tenancy` | `src/Modules/Tenancy/Infrastructure/Migrations` |
| `organization` | `OrganizationDbContext` | `organization.__ef_migrations_history_organization` | `src/Modules/Organization/Infrastructure/Migrations` |
| `identity` | `IdentityModuleDbContext` | `identity.__ef_migrations_history_identity` | `src/Modules/Identity/Infrastructure/Migrations` |
| `access_control` | `AccessControlDbContext` | `access_control.__ef_migrations_history_access_control` | `src/Modules/AccessControl/Infrastructure/Migrations` |
| `audit` | `AuditDbContext` | `audit.__ef_migrations_history_audit` | `src/Modules/Audit/Infrastructure/Migrations` |
| `workflow` | `WorkflowDbContext` | `workflow.__ef_migrations_history_workflow` | `src/Modules/Workflow/Infrastructure/Migrations` |
| `subscription` | `SubscriptionDbContext` | `subscription.__ef_migrations_history_subscription` | `src/Modules/Subscription/Infrastructure/Migrations` |
| `configuration` | `ConfigurationDbContext` | `configuration.__ef_migrations_history_configuration` | `src/Modules/Configuration/Infrastructure/Migrations` |
| `integration` | `IntegrationDbContext` | `integration.__ef_migrations_history_integration` | `src/Modules/Integration/Infrastructure/Migrations` |
| `notification` | `NotificationDbContext` | `notification.__ef_migrations_history_notification` | `src/Modules/Notification/Infrastructure/Migrations` |
| `file_storage` | `FileStorageDbContext` | `file_storage.__ef_migrations_history_file_storage` | `src/Modules/FileStorage/Infrastructure/Migrations` |

---

## Generating a New Migration

```bash
dotnet tool restore
dotnet dotnet-ef migrations add <MigrationName> \
  --context <ModuleDbContext> \
  --project src/Modules/<Module> \
  --startup-project src/Nexus.Api \
  --output-dir Infrastructure/Migrations
```

---

## Applying Migrations & RLS Hardening

1. **Always run migrations using `nexus_migrator`** (schema owner) — NEVER `nexus_app` and NEVER `postgres` superuser in production.
2. Call `NexusDatabaseMigrator.MigrateAndHardeningAsync(migrationConn, "nexus_app")` from a dedicated release/init job before rolling out new API instances. This idempotently:
   - Creates module schemas if missing
   - Executes all pending EF Core migrations across all 12 `DbContext`s
   - Applies `ENABLE ROW LEVEL SECURITY` and `FORCE ROW LEVEL SECURITY` policies to all 24 tenant-scoped tables
   - Installs `audit.trg_audit_events_immutable` (`BEFORE UPDATE OR DELETE`)
   - Grants least-privilege DML permissions to `nexus_app` (and `SELECT, INSERT` only on `audit.audit_events`)
   - Seeds default RBAC permissions and 10 system roles.

---

## Rollback Strategy

- Never execute destructive `DROP TABLE` / `DROP COLUMN` migrations in production without a two-phase expand-and-contract deployment.
- Generate idempotent SQL scripts before production change review:
  ```bash
  dotnet dotnet-ef migrations script --idempotent --context TenancyDbContext --project src/Modules/Tenancy --startup-project src/Nexus.Api
  ```
- Take a verified `pg_dump` snapshot prior to every production schema change.
