# NEXUS C — Database & PostgreSQL RLS Hardening Report (Phase 03)

## 1. Hybrid RLS Coverage Added

In addition to the 24 strictly tenant-scoped tables already protected by `FORCE ROW LEVEL SECURITY`, Phase 03 extends PostgreSQL `FORCE ROW LEVEL SECURITY` to the 3 hybrid (Global + Tenant) governance tables using a new transaction/connection-scoped PostgreSQL GUC `app.is_platform_scope`:

| Table | SELECT Policy (as `nexus_app`) | INSERT/UPDATE/DELETE Policy (as `nexus_app`) |
|---|---|---|
| `access_control.roles` | `bypass_rls='on'` OR `is_platform_scope='on'` OR (`Scope='Tenant'` AND (`TenantId IS NULL` OR `TenantId = current_tenant_id`)) | `bypass_rls='on'` OR `is_platform_scope='on'` OR (`Scope='Tenant'` AND `IsSystemRole=false` AND `TenantId = current_tenant_id`) |
| `access_control.user_roles` | `bypass_rls='on'` OR `is_platform_scope='on'` OR `TenantId = current_tenant_id` | Same (`USING` + `WITH CHECK`) |
| `configuration.feature_flags` | `bypass_rls='on'` OR `is_platform_scope='on'` OR `TenantId IS NULL` OR `TenantId = current_tenant_id` | `bypass_rls='on'` OR `is_platform_scope='on'` OR `TenantId = current_tenant_id` |

All three session GUCs (`app.current_tenant_id`, `app.bypass_rls`, `app.is_platform_scope`) are:
- Applied on `ConnectionOpened`, `TransactionStarted` (`is_local = true`), and mid-connection scope changes (`TenantRlsCommandInterceptor`).
- Unconditionally reset (`''`, `'off'`, `'off'`) on `ConnectionClosing` before returning the socket to Npgsql's pool.

## 2. EF Core Defense-in-Depth (`AccessControlDbContext`)

- Global query filters added to `Role` and `UserRole` mirroring the hybrid PostgreSQL policies.
- `EnforceAccessControlWriteGuards()` added to `SaveChanges` / `SaveChangesAsync`, rejecting cross-tenant custom role writes, system/platform role mutations, or cross-tenant `UserRole` reassignments before SQL execution.

## 3. Audit of All `platformBypass: true` Call Sites

Every occurrence of `platformBypass: true` in `src/` was audited:

| File & Line | Purpose | Caller Control | Risk Assessment |
|---|---|---|---|
| `NexusDatabaseMigrator.cs:51` | Executing EF Core migrations & RBAC seeding | CLI/Migrator only (not HTTP-accessible) | **Acceptable** — scoped & disposed |
| `NexusDatabaseMigrator.cs:229` | Initial bootstrap PlatformSuperAdmin creation | CLI/Migrator only (env-gated, audited) | **Acceptable** — scoped & disposed |
| `TenantService.cs` (`ListAccessibleTenantsAsync`) | Lookup active memberships for authenticated `currentUser.UserId` | Reads strictly filtered by `m.UserId == userId` | **Acceptable** — read-only, scoped & disposed |
| `TenantService.cs` (`GetAccessibleTenantIdsAsync`) | Login default-tenant resolution for verified `userId` | Internal `ITenantAccessValidator` contract only | **Acceptable** — read-only, scoped & disposed |

No HTTP request can arbitrarily enable `platformBypass: true`.
