# Database Architecture, Schemas & PostgreSQL RLS

## 1. Per-Module Schema Ownership (12 Schemas, 21 EF Core Migrations)

All 12 schemas reside in a single PostgreSQL 17 database (`nexus_*`) with strict compile-time and schema-level table ownership:

- `messaging`: `outbox_messages`, `inbox_messages`, `dead_letter_messages`, `idempotency_records`
- `tenancy`: `tenants`, `memberships`
- `organization`: `legal_entities`, `branches`, `organization_units`, `user_organization_assignments`
- `identity`: `users`, `user_profiles`, `sessions`, `refresh_tokens`, `external_identities`, `login_attempts`, `service_clients`, `api_keys`, plus ASP.NET Core Identity tables
- `access_control`: `roles`, `permissions`, `role_permissions`, `user_roles`
- `audit`: `audit_events`
- `workflow`: `definitions`, `versions`, `instances`, `approval_tasks`, `approval_decisions`, `approval_delegations`, `history`
- `subscription`: `applications`, `tenant_applications`, `plans`, `plan_features`, `subscriptions`, `subscription_items`, `entitlements`, `usage_records`
- `configuration`: `platform_settings`, `tenant_settings`, `feature_flags`, `setting_histories`
- `integration`: `providers`, `connections`, `sync_logs`, `integration_errors`, `webhooks`, `webhook_deliveries`, `ai_model_configs`, `ai_usage_logs`
- `notification`: `templates`, `notifications`, `preferences`
- `file_storage`: `stored_files`

---

## 2. PostgreSQL Row-Level Security (`FORCE ROW LEVEL SECURITY` — 33 Tables)

NEXUS C 1.0 enforces `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` on **33 tables** (`28` strictly tenant-scoped tables + `5` hybrid platform/tenant tables), executed at runtime by the least-privilege `nexus_app` role (`NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`).

### 2.1 Strictly Tenant-Scoped Tables (28 Tables)
`tenancy.memberships`, `organization.{legal_entities, branches, organization_units, user_organization_assignments}`, `workflow.{definitions, versions, instances, approval_tasks, approval_decisions, approval_delegations, history}`, `subscription.{subscriptions, subscription_items, entitlements, usage_records, tenant_applications}`, `configuration.tenant_settings`, `integration.{connections, sync_logs, integration_errors, webhooks, webhook_deliveries, ai_usage_logs}`, `notification.{templates, notifications, preferences}`, and `file_storage.stored_files`.

```sql
ALTER TABLE <schema>.<table> ENABLE ROW LEVEL SECURITY;
ALTER TABLE <schema>.<table> FORCE ROW LEVEL SECURITY;

CREATE POLICY nexus_tenant_isolation ON <schema>.<table>
USING (
    current_setting('app.bypass_rls', true) = 'on'
    OR (
        NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
        AND "TenantId" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    )
)
WITH CHECK (
    current_setting('app.bypass_rls', true) = 'on'
    OR (
        NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
        AND "TenantId" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    )
);
```

### 2.2 Hybrid Platform/Tenant Tables (5 Tables)
See [ADR-003: Hybrid RLS & Policy Engine](../architecture/ADR-003-HYBRID-RLS-AND-POLICY-ENGINE.md) for the exact SQL predicates:
1. `access_control.roles`: tenant callers read system Tenant-scoped template roles (`TenantId IS NULL AND Scope = 'Tenant'`) and own tenant custom roles; writes restricted to own non-system roles.
2. `access_control.role_permissions`: `EXISTS` join against parent `access_control.roles` ownership/visibility rules.
3. `access_control.user_roles`: tenant callers see/modify only own tenant assignments; platform scope (`app.is_platform_scope = 'on'`) sees platform assignments.
4. `configuration.feature_flags`: tenant callers read global flags (`TenantId IS NULL`) and own tenant overrides; writes restricted to own tenant overrides unless platform scope.
5. `audit.audit_events`: tenant callers `SELECT` only own tenant audit rows (`TenantId = current_tenant_id`); platform scope reads all; `INSERT` allowed for own tenant or scoped internal `audit-append` bypass; **no `UPDATE` or `DELETE` policy exists** (default-deny even for table owner).

### 2.3 Connection Pooling Safety & Migration Concurrency
- `TenantRlsInterceptor`, `TenantRlsTransactionInterceptor`, and `TenantRlsCommandInterceptor` set `app.current_tenant_id`, `app.bypass_rls`, and `app.is_platform_scope` on `ConnectionOpened` / `TransactionStarted` and unconditionally reset all three GUCs on `ConnectionClosing` before returning physical sockets to Npgsql's pool (verified with `MaxPoolSize=1`).
- `NexusDatabaseMigrator` acquires PostgreSQL advisory lock `pg_advisory_lock(74839210)` before applying migrations, RLS hardening, RBAC seeding (39 permissions, 10 system roles), and Application Registry seeding (`NEXUS_C`, `NEXUS_F`, `NEXUS_E`, `NEXUS_M`).

---

## 3. Audit Immutability Controls

`audit.audit_events` is hardened at **three independent layers**:
1. **API Layer:** `AuditController` exposes `GET` only (zero PUT/PATCH/DELETE endpoints).
2. **Application/ORM Layer:** `AuditDbContext.SaveChangesAsync` throws `DomainException("AUDIT_IMMUTABLE")` on any `Modified` or `Deleted` `AuditEvent`.
3. **PostgreSQL Layer:**
   - `nexus_app` holds `GRANT SELECT, INSERT` ONLY on `audit.audit_events`.
   - `FORCE ROW LEVEL SECURITY` defines ONLY `FOR SELECT` and `FOR INSERT` policies (default-deny `UPDATE`/`DELETE` even for table owner `nexus_migrator`).
   - Trigger `audit.trg_audit_events_immutable` (`BEFORE UPDATE OR DELETE`) raises an exception even for PostgreSQL superusers.
6. **`notification.email_deliveries` (`nexus_email_deliveries_isolation`)**
   - Platform scope reads/writes all email delivery records; tenant scope reads/writes only rows matching `app.current_tenant_id`.
7. **`messaging.api_request_logs` (`nexus_api_request_logs_isolation`)**
   - Platform scope reads all operational API telemetry; tenant scope reads only rows matching `app.current_tenant_id`.

Plus `integration.ai_capability_policies` added to strict tenant-scoped `FORCE ROW LEVEL SECURITY` (`nexus_tenant_isolation`), bringing the total to **36 `FORCE ROW LEVEL SECURITY` tables** across the 12 PostgreSQL schemas.
