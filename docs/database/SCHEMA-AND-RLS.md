# Database Architecture, Schemas & PostgreSQL RLS

## 1. Per-Module Schema Ownership

All 12 schemas reside in a single PostgreSQL 17 database (`nexus_*`) with strict table ownership:

- `messaging`: `outbox_messages`, `inbox_messages`, `dead_letter_messages`, `idempotency_records`
- `tenancy`: `tenants`, `memberships`
- `organization`: `legal_entities`, `branches`, `organization_units`, `user_organization_assignments`
- `identity`: `users`, `user_profiles`, `sessions`, `refresh_tokens`, `external_identities`, `login_attempts`, plus ASP.NET Core Identity tables
- `access_control`: `roles`, `permissions`, `role_permissions`, `user_roles`
- `audit`: `audit_events`
- `workflow`: `definitions`, `versions`, `instances`, `approval_tasks`, `approval_decisions`, `history`
- `subscription`: `plans`, `plan_features`, `subscriptions`, `subscription_items`, `entitlements`, `usage_records`
- `configuration`: `platform_settings`, `tenant_settings`, `feature_flags`, `setting_histories`
- `integration`: `providers`, `connections`, `sync_logs`, `integration_errors`, `webhooks`, `ai_model_configs`, `ai_usage_logs`
- `notification`: `templates`, `notifications`
- `file_storage`: `stored_files`

---

## 2. PostgreSQL Row-Level Security (`FORCE ROW LEVEL SECURITY`)

Every tenant-scoped table (24 tables total) enforces:

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

### Connection Pooling Safety
`TenantRlsInterceptor`, `TenantRlsTransactionInterceptor`, and `TenantRlsCommandInterceptor` ensure:
1. `ConnectionOpened` sets `app.current_tenant_id` and `app.bypass_rls` from the request's scoped `ITenantContext`.
2. `TransactionStarted` sets them transaction-locally (`is_local = true`).
3. `ConnectionClosing` unconditionally resets `app.current_tenant_id = ''` and `app.bypass_rls = 'off'` before returning the physical socket to Npgsql's pool.
4. Verified by `PooledConnection_MaxPoolSize1_NeverLeaksTenantContextAcrossRequests` in `Nexus.IntegrationTests`.

---

## 3. Audit Immutability Controls

`audit.audit_events` is hardened at **three layers**:
1. **API Layer:** `AuditController` exposes `GET` only (zero PUT/PATCH/DELETE endpoints).
2. **Application/ORM Layer:** `AuditDbContext.SaveChangesAsync` throws `DomainException("AUDIT_IMMUTABLE")` on any `Modified` or `Deleted` `AuditEvent`.
3. **PostgreSQL Layer:** `nexus_app` has `GRANT SELECT, INSERT` ONLY on `audit.audit_events`, AND trigger `audit.trg_audit_events_immutable` blocks `UPDATE` and `DELETE` even for the table owner.
