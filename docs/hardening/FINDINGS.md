# NEXUS C Foundation — Confirmed Security, Functional & Configuration Findings

Every finding below is backed by direct source inspection and concrete exploit/impact path.

---

## F-01: Cross-Tenant Membership Privilege Escalation (P0 — CRITICAL)
- **Classification:** Confirmed Vulnerability
- **Files:** `src/Nexus.Api/Controllers/TenantsController.cs`, `src/Modules/Tenancy/Application/TenantService.cs`
- **Root Cause:** `POST/DELETE /api/v1/platform/tenants/{tenantId}/memberships/{userId}` requires only `Permissions.UsersManage` (which `TenantAdmin` and `TenantOwner` possess inside Tenant A). `TenantService.AddMembershipAsync` and `RevokeMembershipAsync` immediately call `tenantContext.BeginScope(tenantId)` using the client-controlled `{tenantId}` route parameter without verifying that the caller belongs to `tenantId` or holds a cross-tenant platform permission. Furthermore, neither method checks whether `userId` exists or whether the target tenant is `Archived`.
- **Impact:** A `TenantAdmin` of Tenant A can grant themselves or others membership into Tenant B, or revoke Tenant B users' memberships.

## F-02: Cross-Tenant User Creation, Listing, Disable & Session Revocation + Unchecked Role Assignment Target (P0 — CRITICAL)
- **Classification:** Confirmed Vulnerability
- **Files:** `src/Modules/Identity/Application/AuthService.cs`, `src/Modules/AccessControl/Application/AccessControlService.cs`
- **Root Cause:**
  1. `AuthService.CreateUserAsync` accepts `request.TenantId`, `request.IsPlatformUser`, and `request.RoleName` directly from client input without restricting non-platform callers to their own `tenantContext.TenantId` and tenant-scoped roles.
  2. `AuthService.DisableUserAsync`, `EnableUserAsync`, and `RevokeSessionAsync` (when invoked via `UsersController` by a `TenantAdmin`) do not verify that the target user is a member of the caller's tenant (and not a Platform user).
  3. `AuthService.ListUsersAsync` returns users across all tenants even when called by a tenant-scoped admin.
  4. `AccessControlService.AssignRoleAsync` does not verify that `request.UserId` exists or holds an active membership in the target tenant before creating a `UserRole` record.
- **Impact:** Cross-tenant user enumeration, cross-tenant account disablement/DoS, privilege escalation by creating platform users from a tenant admin session, or assigning roles to non-member users.

## F-03: Missing PostgreSQL RLS & SaveChanges Guards on `access_control.roles`, `access_control.user_roles`, and `configuration.feature_flags` (P1 — HIGH)
- **Classification:** Confirmed Vulnerability (Defense-in-Depth Gap)
- **Files:** `src/Nexus.Bootstrap/NexusDatabaseMigrator.cs`, `src/Modules/AccessControl/Infrastructure/AccessControlDbContext.cs`, `src/Nexus.BuildingBlocks/Persistence/TenantRlsInterceptor.cs`
- **Root Cause:** Because `roles`, `user_roles`, and `feature_flags` have nullable `TenantId` (hosting both global/platform rows and tenant-specific rows), they were excluded from `rlsTables` in `NexusDatabaseMigrator.cs` and lacked hybrid RLS policies and `SaveChanges` guards.
- **Impact:** Under `nexus_app`, direct SQL or any application query bug could read/mutate another tenant's custom roles, `user_roles` assignments, or tenant feature flag overrides.

## F-04: Hardcoded DB Passwords & DB Name in `init-roles.sql` + API Self-Migrating Multi-Replica Risk + Missing Startup Secret Validation (P1 — HIGH)
- **Classification:** Confirmed Configuration Defect
- **Files:** `infrastructure/database/init-roles.sql`, `docker-compose.yml`, `src/Nexus.Api/Program.cs`, `src/Nexus.Bootstrap/NexusBootstrapExtensions.cs`
- **Root Cause:**
  1. `init-roles.sql` hardcodes `'nexus_migrator_dev_password'`, `'nexus_app_dev_password'`, and `nexus_dev`, conflicting with Docker Compose variables (`POSTGRES_DB`, `POSTGRES_APP_PASSWORD`, `POSTGRES_MIGRATOR_PASSWORD`) and omitting `ALTER DEFAULT PRIVILEGES`.
  2. Migrations ran inside `Nexus.Api/Program.cs` (`NEXUS_RUN_MIGRATIONS=true`) rather than a dedicated one-shot migrator job, risking race conditions across multiple API replicas.
  3. `Program.cs` / `IdentityModuleExtensions.cs` falls back to a placeholder signing key when `Jwt:SigningKey` is empty instead of failing fast in Production.

## F-05: Frontend Fail-Open Permission Fallback & Unprotected Sensitive Routes (P1 — HIGH)
- **Classification:** Confirmed Vulnerability (Frontend Authorization)
- **Files:** `nexus-c-console/src/services/auth/AuthContext.tsx`, `nexus-c-console/src/app/router.tsx`, `nexus-c-console/src/components/layout/AppShell.tsx`
- **Root Cause:**
  1. `AuthContext.hasPermission()` falls back to `return true;` whenever `permissions.length === 0` (loading, error, or user without `platform.roles.read`), causing fail-open UI visibility.
  2. Backend lacked a dedicated `GET /api/v1/auth/permissions` endpoint for the authenticated principal to retrieve their own effective permissions without holding `platform.roles.read`.
  3. `/api-management/*` and `/operations` routes checked only `RequireAuth` without dedicated permissions.

## F-06: Webhook SSRF Target Validation Gap (P1 — HIGH)
- **Classification:** Confirmed Vulnerability
- **Files:** `src/Modules/Integration/Domain/IntegrationEntities.cs`
- **Root Cause:** `WebhookRegistration.Create` validates only `uri.Scheme == Uri.UriSchemeHttps`, permitting internal/loopback/cloud-metadata URLs (`https://127.0.0.1`, `https://169.254.169.254`, `https://10.x.x.x`, `https://localhost`, `https://[::1]`).

## F-07: Frontend CI Missing from GitHub Actions Workflow (P1 — HIGH)
- **Classification:** Confirmed Configuration Defect
- **Files:** `.github/workflows/ci.yml`
- **Root Cause:** CI validates only `NEXUS.sln` (.NET backend) and never executes `nexus-c-console` (`npm ci`, `typecheck`, `lint`, `test`, `build`).
