# NEXUS C — Security Fixes & Verification Summary

## 1. F-01 (P0 CRITICAL): Cross-Tenant Membership Privilege Escalation — **FIXED**
- **Root Cause:** `TenantsController.AddMembership` / `RevokeMembership` accepted a route `{tenantId}` and required only `Permissions.UsersManage`; `TenantService` immediately switched ambient tenant context via `tenantContext.BeginScope(tenantId)` without verifying caller authorization on `tenantId`.
- **Code Changes:**
  - Added dedicated platform permission `Permissions.MembershipsManageCrossTenant` (`platform.memberships.manage`, `PermissionScope.Platform`) in `src/Nexus.SharedKernel/Security/Permissions.cs`, granted ONLY to `PlatformSuperAdmin` (never to `TenantAdmin`, `TenantOwner`, or `PlatformSupport`).
  - Added server-side controller scope check in `src/Nexus.Api/Controllers/TenantsController.cs`.
  - Added defense-in-depth application service authorization (`AuthorizeMembershipManagementAsync`) in `src/Modules/Tenancy/Application/TenantService.cs` BEFORE `BeginScope`, validating:
    1. Platform actor holds `platform.memberships.manage`.
    2. Tenant actor's authenticated `tenantContext.TenantId == targetTenantId` AND holds `platform.users.manage`.
    3. Target tenant exists and is not `Archived`; target user exists (`IUserDirectoryReader`).
    4. Cross-tenant platform membership operations are explicitly flagged `IsCrossTenantPlatformAccess = true` in Audit.
- **Regression Evidence:** `Phase02AuthorizationRegressionTests.MembershipAndUserManagement_CrossTenantEscalation_IsStrictlyBlocked`.

## 2. F-02 (P0 CRITICAL): Cross-Tenant User Management & Role Assignment Hardening — **FIXED**
- **Root Cause:** `AuthService.CreateUserAsync`, `DisableUserAsync`, `EnableUserAsync`, `RevokeSessionAsync`, and `ListUsersAsync` lacked tenant-admin target isolation; `AccessControlService.AssignRoleAsync` did not verify target user existence or target tenant membership.
- **Code Changes:**
  - `src/Modules/Identity/Application/AuthService.cs`: Blocks tenant admins from creating platform users, creating users in other tenants, assigning platform roles, listing users outside their tenant, or disabling/enabling/revoking sessions of platform users or users outside their tenant.
  - `src/Modules/AccessControl/Application/AccessControlService.cs`: Verifies target user existence (`404 USER_NOT_FOUND`) and active membership in target tenant (`403 USER_NOT_TENANT_MEMBER`) before creating any tenant-scoped `UserRole`; scopes `ListUserRolesAsync` strictly to current tenant for non-platform callers.
- **Regression Evidence:** `Phase02AuthorizationRegressionTests`.

## 3. F-05 (P1 HIGH): Frontend Fail-Open Permission Fallback & Sensitive Route Protection — **FIXED**
- **Root Cause:** `AuthContext.hasPermission()` returned `true` when `permissions` was empty or failed to load; `/api-management/*` and `/operations` had no permission checks; backend lacked `GET /api/v1/auth/permissions`.
- **Code Changes:**
  - Added `GET /api/v1/auth/permissions` (`[Authorize]`) in `src/Nexus.Api/Controllers/AuthController.cs`, returning strictly the authenticated principal's effective roles & permissions in their current Platform/Tenant scope (never accepting a client `userId`).
  - Added `platform.api.read`, `platform.api.manage`, `platform.operations.read`, `platform.operations.manage` to `Permissions.Catalog`.
  - Refactored `nexus-c-console/src/services/auth/AuthContext.tsx` to explicit `PermissionStatus` (`idle | loading | loaded | failed`) with strict fail-closed `hasPermission()` (`permissionStatus === 'loaded' && permissions.includes(permission)`).
  - Protected `/api-management/*` (`platform.api.read`) and `/operations` (`platform.operations.read`, `platformOnly`) in `router.tsx` and `AppShell.tsx`.
- **Regression Evidence:** `nexus-c-console/tests/console-pages.test.tsx` fail-closed & permission denial tests + `Phase02AuthorizationRegressionTests`.

## 4. F-06 (P1 HIGH): Webhook SSRF Validation — **FIXED**
- **Root Cause:** `WebhookRegistration.Create` checked only `https://` scheme, permitting localhost, loopback, cloud metadata (`169.254.169.254`), and RFC1918 private IPs.
- **Code Changes:** Added server-side `IsUnsafeSsrfTarget(Uri)` in `src/Modules/Integration/Domain/IntegrationEntities.cs` blocking localhost/internal/local DNS names, IPv4 loopback/private/CGNAT/link-local/metadata, and IPv6 loopback/ULA/link-local addresses.
- **Regression Evidence:** `HardeningRegressionUnitTests.WebhookRegistration_RejectsUnsafeSsrfTargets` (11 vectors).
- **Remaining Limitation:** DNS rebinding protection at HTTP delivery time (re-validating resolved IP addresses before opening outbound socket) must also be enforced in the future webhook delivery worker.
