# NEXUS C 1.0 — Test, Drill & Verification Report

**Release:** `NEXUS C 1.0` (`release/nexus-c-1.0-foundation`)  
**Verification Environments:**
- **Local Workstation:** macOS (Apple Silicon) · .NET 10.0 LTS · PostgreSQL 17.12 · Node 24 LTS (all unit, architecture, RLS integration, security, API, frontend tests, and live isolated DB drills).
- **GitHub Actions Linux CI (`ubuntu-latest` + `postgres:17.4-alpine`):** Run [`45046431690`](https://github.com/duongbui-drt1/NEXUS-C/actions/runs/45046431690) — **ALL 3 JOBS PASSED** (`backend-ci`, `frontend-ci`, and `docker-build-verify` for `Dockerfile`, `Dockerfile.migrator`, and `nexus-c-console/Dockerfile`). Note: Docker CLI is not installed on the local macOS workstation, so container builds were executed and verified in GitHub Actions Linux CI (`docker-build-verify`).

---

## 1. Automated Verification Summary

| Suite / Gate | Command | Result | Passed / Total | Failed | Skipped |
|---|---|---|---:|---:|---:|
| Code Formatting | `dotnet format NEXUS.sln --verify-no-changes` | **PASS** | — | 0 | 0 |
| Solution Restore & Build | `dotnet restore NEXUS.sln && dotnet build NEXUS.sln -c Release --no-restore` | **PASS** (0 Warnings, 0 Errors) | 21/21 projects | 0 | 0 |
| Architecture Tests | `Nexus.ArchitectureTests.dll` | **PASS** | **5 / 5** | 0 | 0 |
| Unit Tests | `Nexus.UnitTests.dll` | **PASS** | **35 / 35** | 0 | 0 |
| PostgreSQL 17 RLS & Integration Tests | `Nexus.IntegrationTests.dll` | **PASS** | **14 / 14** | 0 | 0 |
| Security Matrix Tests | `Nexus.SecurityTests.dll` | **PASS** | **8 / 8** | 0 | 0 |
| API Contract Tests | `Nexus.ApiTests.dll` | **PASS** | **7 / 7** | 0 | 0 |
| **Backend Total** | `dotnet test NEXUS.sln -c Release --no-build` | **PASS** | **69 / 69** | **0** | **0** |
| Frontend Typecheck | `cd nexus-c-console && npm run typecheck` | **PASS** | — | 0 | 0 |
| Frontend Lint | `cd nexus-c-console && npm run lint` | **PASS** | — | 0 | 0 |
| Frontend Unit & UI Tests | `cd nexus-c-console && npm test` | **PASS** | **13 / 13** (2 files) | 0 | 0 |
| Frontend Production Build | `cd nexus-c-console && npm run build` | **PASS** (334 ms, ~103.8 KB gzipped initial JS) | — | 0 | 0 |
| .NET NuGet Vulnerability Scan | `dotnet list NEXUS.sln package --vulnerable --include-transitive` | **PASS** (0 vulnerable packages across 21 projects) | — | 0 | 0 |
| Frontend Production Audit | `cd nexus-c-console && npm audit --omit=dev` | **PASS** (0 vulnerabilities) | — | 0 | 0 |
| Docker Image Builds (`Dockerfile`, `Dockerfile.migrator`, `nexus-c-console/Dockerfile`) | `docker build` (GitHub Actions `docker-build-verify`, Run `45046431690`) | **PASS** (1m 10s on `ubuntu-latest`; local host lacks Docker CLI) | **3 / 3 images** | 0 | 0 |
| Full GitHub Actions CI (`NEXUS Foundation CI`) | Run [`45046431690`](https://github.com/duongbui-drt1/NEXUS-C/actions/runs/45046431690) (`backend-ci`, `frontend-ci`, `docker-build-verify`) | **PASS** (all 3 jobs green on `155bb00`) | **3 / 3 jobs** | 0 | 0 |

---

## 2. Backend Test Coverage Matrix (69 Tests)

### 2.1 `Nexus.ArchitectureTests` (5 / 5 Passed)
- Enforces compile-time module isolation (zero direct cross-module project references across all 11 modules).
- Enforces controllers never reference `DbContext` directly.
- Verifies shared primitives remain inside `Nexus.SharedKernel` and `Nexus.BuildingBlocks`.

### 2.2 `Nexus.UnitTests` (35 / 35 Passed)
- Domain invariant tests across Tenancy, Organization (cycle detection), Identity, AccessControl, Workflow, Subscription, Configuration (secret-key rejection), Integration, Notification, and FileStorage.
- New Foundation 1.0 unit coverage (`Foundation10UnitTests`):
  - `SsrfGuard` blocking non-HTTPS, loopback, RFC1918, cloud metadata (`169.254.169.254`), `.local`, `.internal`, IPv6 link-local/ULA while allowing public HTTPS URLs.
  - `WebhookSignatureHelper` HMAC-SHA256 signing, valid timestamp verification, tampered payload rejection, and stale-timestamp replay rejection (> 300s).
  - `DeadLetterMessage` manual retry counter and loop cap (`MaxManualRetries = 3`).
  - `StoredFileObject` retention days validation and expiration boundary checks.
  - `EventEnvelope` backward-compatible `Producer` / `CausationId` metadata fields.

### 2.3 `Nexus.IntegrationTests` (14 / 14 Passed — Real PostgreSQL 17.12)
- `DatabaseAndRlsIntegrationTests`: `FORCE ROW LEVEL SECURITY` verification, fail-closed zero-row visibility without tenant scope, `MaxPoolSize=1` connection pooling GUC reset verification, `audit.audit_events` 3-layer immutability (EF Core `AUDIT_IMMUTABLE`, `nexus_migrator` `FORCE RLS` 0-row DELETE, superuser `trg_audit_events_immutable` trigger exception), Outbox/Inbox idempotency, and Migrator idempotency.
- New Foundation 1.0 integration coverage (`Foundation10RlsAndEventMatrixTests`):
  - All **33 tables** verified with `relrowsecurity = true AND relforcerowsecurity = true`.
  - Cross-tenant SQL isolation verified on newly protected `audit.audit_events` and `access_control.role_permissions` using least-privilege `nexus_app` (`NOBYPASSRLS`).
  - Outbox -> Dead Letter Queue -> `PlatformOperationsService.RetryDeadLetterAsync` transactional requeue + retry limit cap + audit verification.

### 2.4 `Nexus.SecurityTests` (8 / 8 Passed)
- Cross-tenant isolation, `X-Tenant-Id` spoofing (`403 TENANT_SPOOFING_DETECTED`), RBAC privilege escalation prevention (`403 ROLE_PRIVILEGE_ESCALATION`), refresh token reuse detection revoking token family & session, Maker–Checker workflow enforcement, SSRF webhook blocking.
- New Foundation 1.0 security coverage (`Foundation10SecurityMatrixTests`):
  - **F-002:** Organization assignment rejects non-member users from another tenant (`403`).
  - **F-004:** `switch-tenant` rejects suspended tenants (`403`) and revokes prior tenant refresh tokens (replaying prior token triggers reuse detection & revokes session -> `401`).
  - **Cookie Refresh CSRF:** Cookie-mode `/refresh` without `X-Nexus-CSRF: 1` returns `403 CSRF_HEADER_REQUIRED`; with `X-Nexus-CSRF: 1` succeeds and rotates cookie.
  - **OAuth 2.0 Service Clients & Scoped API Keys:** Wildcard scope rejection (`403 WILDCARD_SCOPE_FORBIDDEN`), tenant-bound client barred from platform scopes, `POST /api/v1/auth/token` issuance, tenant anti-spoofing, immediate rejection upon revocation (`401 AUTH_SERVICE_CLIENT_REVOKED`), and `X-Api-Key` scoped authentication.

### 2.5 `Nexus.ApiTests` (7 / 7 Passed)
- End-to-end HTTP pipeline, RFC 9457 ProblemDetails, `X-Correlation-Id`, `Idempotency-Key` replay & payload mismatch (`409`), `/health/live`, `/health/ready`, sanitized `/health/dependencies`, secret config rejection (`422 CONFIG_SECRET_FORBIDDEN`).
- New Foundation 1.0 platform contract tests (`Foundation10PlatformContractTests`):
  - Full ecosystem flow: Application Registry (`NEXUS_C`/`NEXUS_F`/`NEXUS_E`/`NEXUS_M`) -> Plan assignment -> Entitlement evaluation -> Plan downgrade revoking stale entitlements (**F-001**) -> Tenant application deactivate/reactivate -> Idempotent usage recording & quota enforcement (`QUOTA_EXCEEDED`).
  - Full governance flow: Organization scope resolution (`GET /api/v1/auth/organization-scope`), ABAC explicit `Deny` overriding `Allow` (`POST /api/v1/auth/authorize`), Workflow version pinning, sequential step enforcement, task delegation, standing delegation, escalation, history, and Operations API (`/operations/outbox`, `/operations/jobs`).

---

## 3. Live Isolated PostgreSQL 17 Operational Drills

Executed against isolated temporary databases (`nexus_rc10_fresh_tmp`, `nexus_rc10_upgrade_tmp`, `nexus_rc10_restore_tmp`; cleaned up immediately after verification without touching `nexus_dev` or `nexus_local_sandbox`).

### Drill 1 — Fresh Database Provisioning & Idempotent Re-Run
- Provisioned least-privilege roles via `sh ./infrastructure/database/init-roles.sh`.
- Executed `Nexus.Migrator.dll` **twice consecutively** (advisory lock `74839210`): both passes exited `0`.
- Verified database invariants:
  - `rls_forced_tables` = **`33`**
  - `app_role_flags` (`rolsuper,rolbypassrls,rolcreatedb,rolcreaterole`) = **`false,false,false,false`**
  - `audit_immutable_triggers` = **`1`** (`trg_audit_events_immutable`)
  - `seeded_applications` = **`NEXUS_C,NEXUS_E,NEXUS_F,NEXUS_M`**
  - `seeded_permissions` = **`39`**, `seeded_system_roles` = **`10`**

### Drill 2 — Zero-Data-Loss Upgrade from Pre-1.0 Schema
- Cloned existing pre-1.0 database snapshot (12 migrations, `27` RLS tables, `1` tenant, `4` users, `20` audit events, `37` permissions) into isolated `nexus_rc10_upgrade_tmp`.
- Ran NEXUS C 1.0 `Nexus.Migrator.dll` (applied all 9 new `Foundation10_*` migrations).
- Verified post-upgrade state:
  - `post_rls_forced_tables`: `27` -> **`33`**
  - `post_tenants`: **`1`** (preserved)
  - `post_users`: **`4`** (preserved)
  - `post_audit_events`: **`20`** (preserved)
  - `post_applications`: **`NEXUS_C,NEXUS_E,NEXUS_F,NEXUS_M`** (seeded)
  - `post_permissions`: `37` -> **`39`** (upgraded cleanly)

### Drill 3 — Backup & Restore Verification (`pg_dump` + `pg_restore`)
- Dumped upgraded database via `pg_dump --format=custom --no-owner --no-privileges`.
- Restored into fresh `nexus_rc10_restore_tmp` via `pg_restore --no-owner --no-privileges --exit-on-error` + re-ran `Nexus.Migrator.dll` to re-assert grants and RLS policies.
- Verified identical counts (`33` RLS tables, `1` tenant, `4` users, `20` audit events, `4` apps, `1` immutability trigger).
- Verified least-privilege `app_role` unscoped fail-closed reads:
  - `audit.audit_events` visible rows without tenant/platform scope: **`0`**
  - `organization.legal_entities` visible rows without tenant scope: **`0`**
  - `access_control.role_permissions` visible rows without tenant scope: **`94`** (strictly system Tenant-template roles where `Scope = 'Tenant' AND TenantId IS NULL`; zero custom tenant roles or platform roles leaked).

---

## 4. NEXUS C Console Verification (`nexus-c-console/`)

- **TypeScript (`tsc --noEmit`):** 0 errors (strict mode).
- **ESLint:** 0 errors, 0 warnings.
- **Vitest + Testing Library:** **13 / 13 tests passed** across `tests/api-client-and-auth.test.ts` (4 tests) and `tests/console-pages.test.tsx` (9 tests), verifying:
  - Auth login failure handling, in-memory token management, single-flight refresh lock, tenant switch, logout.
  - Fail-closed RBAC navigation hydration from `/api/v1/auth/permissions`.
  - Live Tenants management, strictly read-only Audit Center, live Operations Console (Jobs + DLQ retry confirmation), live Application Registry (`NEXUS_C`/`F`/`E`/`M`), and live API Management (OpenAPI, API Clients, Scoped API Keys, Webhooks, honest `Not Available` panels for deferred dynamic rate-limiting & HTTP logs).
- **Production Bundle (`vite build`):** Completed in `334 ms`; core initial JS (`index` + `vendor-react` + `vendor-router` + `vendor-query` + `AuthContext`) = **103.82 KB gzipped**; all domain pages code-split into lazy chunks (1.4–6.5 KB gzipped).
