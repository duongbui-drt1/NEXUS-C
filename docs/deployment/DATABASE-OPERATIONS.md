# NEXUS C 1.0 — Database Operations, RLS & Recovery Runbook

**Database Engine:** PostgreSQL 17  
**ORM / Migration Tool:** Entity Framework Core 10 Code-First (`21` migrations across `12` schemas)  
**Tenant Isolation:** `33` tables under `FORCE ROW LEVEL SECURITY` (`28` strict tenant-scoped + `5` hybrid)

---

## 1. Schema Ownership & RLS Matrix

| Schema | Migrations | Tables | RLS Enforcement |
|---|---|---|---|
| `messaging` | 2 | `outbox_messages`, `inbox_messages`, `dead_letter_messages`, `idempotency_records` | Platform infrastructure (scoped by worker/middleware) |
| `tenancy` | 1 | `tenants`, `memberships` | `memberships`: Strict Tenant `FORCE RLS` |
| `organization` | 2 | `legal_entities`, `branches`, `organization_units`, `user_organization_assignments` | All 4 tables: Strict Tenant `FORCE RLS` |
| `identity` | 2 | `users`, `user_profiles`, `sessions`, `refresh_tokens`, `external_identities`, `login_attempts`, `service_clients`, `api_keys`, ASP.NET Identity tables | Global identity tables; service/key scopes verified by middleware & `ServiceClientService` |
| `access_control` | 2 | `permissions`, `roles`, `role_permissions`, `user_roles` | `roles`, `role_permissions`, `user_roles`: Hybrid Platform/Tenant `FORCE RLS` |
| `audit` | 1 | `audit_events` | Hybrid Tenant/Platform `FORCE RLS` (`SELECT`/`INSERT` only) + `trg_audit_events_immutable` + `SELECT, INSERT`-only grant for `nexus_app` |
| `workflow` | 2 | `definitions`, `versions`, `instances`, `approval_tasks`, `approval_decisions`, `approval_delegations`, `history` | All 7 tables: Strict Tenant `FORCE RLS` |
| `subscription` | 2 | `applications`, `plans`, `plan_features`, `subscriptions`, `subscription_items`, `entitlements`, `usage_records`, `tenant_applications` | `subscriptions`, `subscription_items`, `entitlements`, `usage_records`, `tenant_applications`: Strict Tenant `FORCE RLS` |
| `configuration` | 1 | `platform_settings`, `tenant_settings`, `feature_flags`, `setting_histories` | `tenant_settings`: Strict `FORCE RLS`; `feature_flags`: Hybrid `FORCE RLS` |
| `integration` | 2 | `providers`, `connections`, `sync_logs`, `integration_errors`, `webhooks`, `webhook_deliveries`, `ai_model_configs`, `ai_usage_logs` | All 6 tenant tables: Strict Tenant `FORCE RLS` |
| `notification` | 2 | `templates`, `notifications`, `preferences` | All 3 tables: Strict Tenant `FORCE RLS` |
| `file_storage` | 2 | `stored_files` | Strict Tenant `FORCE RLS` |

---

## 2. Repeatable Migration Execution Path

Never run migrations from `Nexus.Api` replicas in production. Execute the dedicated `Nexus.Migrator` job:

```bash
ConnectionStrings__MigrationConnection="Host=<pg-host>;Port=5432;Database=<db>;Username=nexus_migrator;Password=<migrator-pw>" \
ConnectionStrings__DefaultConnection="Host=<pg-host>;Port=5432;Database=<db>;Username=nexus_app;Password=<app-pw>" \
NEXUS_APP_DB_ROLE="nexus_app" \
dotnet Nexus.Migrator.dll
```

- **Concurrency protection:** Serialized via `pg_advisory_lock(74839210)` inside `NexusDatabaseMigrator.MigrateAndHardeningAsync`.
- **Failure semantics:** Non-zero exit code (`1`) halts CI/CD rollout before any API container restarts.
- **Migration Rollback Policy:** Additive schema migrations (new columns/tables) are preferred so prior API builds remain compatible during rolling deploys. Destructive schema rollbacks MUST restore from a verified pre-upgrade logical/PITR backup rather than running blind `DROP COLUMN` downgrades in production.

---

## 3. Backup, Restore & Verification Procedure

### 3.1 Logical Backup (`pg_dump`)
```bash
pg_dump --format=custom --no-owner --no-privileges \
  --host=<pg-host> --port=5432 --username=<admin-or-backup-user> \
  --file="nexus_$(date -u +%Y%m%dT%H%M%SZ).dump" <db-name>
```
*(For production RPO < 5 minutes, pair daily `pg_dump` with continuous WAL archiving / PITR and S3 bucket versioning + cross-region replication for `ObjectStorage`.)*

### 3.2 Clean Restore & Post-Restore Verification
```bash
# 1) Provision fresh target DB & least-privilege roles via init-roles.sh
NEXUS_DB_NAME=<restore-db> \
NEXUS_MIGRATOR_USER=nexus_migrator NEXUS_MIGRATOR_PASSWORD="<migrator-pw>" \
NEXUS_APP_USER=nexus_app NEXUS_APP_PASSWORD="<app-pw>" \
./infrastructure/database/init-roles.sh

# 2) Restore dump as table owner (nexus_migrator)
pg_restore --no-owner --role=nexus_migrator --exit-on-error \
  --host=<pg-host> --port=5432 --username=<admin> \
  --dbname=<restore-db> nexus_backup.dump

# 3) Re-apply deterministic RLS policies, triggers & least-privilege grants via Nexus.Migrator
ConnectionStrings__MigrationConnection="Host=<pg-host>;Port=5432;Database=<restore-db>;Username=nexus_migrator;Password=<migrator-pw>" \
NEXUS_APP_DB_ROLE="nexus_app" \
dotnet Nexus.Migrator.dll
```

### 3.3 Mandatory Verification Queries Post-Restore
1. Confirm `33` tables have `relrowsecurity = true AND relforcerowsecurity = true` in `pg_class`.
2. Confirm `nexus_app` has `rolsuper = false AND rolbypassrls = false` in `pg_roles`.
3. Confirm `trg_audit_events_immutable` exists on `audit.audit_events`.
4. Confirm row counts on `tenancy.tenants`, `identity.users`, `subscription.applications`, `audit.audit_events`.
5. Smoke-test `GET /health/ready` and authenticated `GET /api/v1/auth/permissions`.
