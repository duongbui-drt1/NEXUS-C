# NEXUS C 1.0 — Security Assessment & Hardening Report

**Release:** `NEXUS C 1.0` (`release/nexus-c-1.0-foundation`)  
**Assessment Scope:** Full-stack backend (.NET 10 Modular Monolith, 11 modules, PostgreSQL 17 RLS), API contracts, service-to-service identity, NEXUS C Console (React 19 + Nginx), and deployment artifacts.

---

## 1. Executive Security Summary

NEXUS C 1.0 eliminates all **5 verified P0 security defects** identified in the pre-release baseline audit ([NEXUS-C-1.0-BASELINE-AUDIT.md](NEXUS-C-1.0-BASELINE-AUDIT.md)) plus **4 additional security hardening findings** discovered during the independent Phase 11 final security review:

| Finding ID | Severity | Vulnerability / Gap | Resolution in NEXUS C 1.0 | Verification |
|---|---|---|---|---|
| **F-001** | **P0** | Plan downgrade left stale `subscription.entitlements` active; `AssignPlanToTenantAsync` lacked service-level platform guard | Enforced `IsPlatformUser` guard; revoked stale entitlements on plan change; invalidated entitlement cache; `EvaluateEntitlementAsync` enforces subscription validity + activation + feature flag kill-switch + usage quota | `Foundation10PlatformContractTests`, `Foundation10UnitTests` |
| **F-002** | **P0** | `OrganizationService.AssignUserAsync` did not verify target user existence or active tenant membership | Added cross-module `IUserDirectoryReader` + `ITenantAccessValidator` checks before assigning users to org units | `Foundation10SecurityMatrixTests` |
| **F-003** | **P0** | `audit.audit_events` and `access_control.role_permissions` lacked PostgreSQL `FORCE ROW LEVEL SECURITY` | Enabled `FORCE ROW LEVEL SECURITY` on **all 33 tables** (28 strict tenant + 5 hybrid including `audit.audit_events` and `access_control.role_permissions`); audit writes use scoped internal `audit-append` context | `Foundation10RlsAndEventMatrixTests`, `DatabaseAndRlsIntegrationTests`, Live DB Drills |
| **F-004** | **P0** | `AuthService.SwitchTenantAsync` allowed switching into suspended/archived tenants and left prior tenant refresh tokens live | Rejects `Suspended`/`Archived` tenants (`403`); revokes prior active refresh tokens on session switch (replaying old-tenant token triggers reuse detection & full session revocation) | `Foundation10SecurityMatrixTests` |
| **F-005** | **P0** | `FileStorageService` fell back to hardcoded signing secret if `ObjectStorage:SigningSecret` was unset | Removed fallback secret completely; fails closed if missing or `< 16` chars; audited downloads and signed-URL generation | `FileStorageService.cs`, startup secret validation |
| **P11-01** | **P1** | Workflow `DelegateTaskAsync` / `RevokeDelegationAsync` allowed any tenant approver to delegate unassigned role tasks or revoke another user's delegation (same-tenant BOLA) | Enforced task eligibility or `platform.workflows.manage` on `DelegateTaskAsync`; enforced delegator ownership or `platform.workflows.manage` on `RevokeDelegationAsync` | `WorkflowService.cs`, `Foundation10PlatformContractTests` |
| **P11-02** | **P1** | `nexus-c-console/nginx.conf` `add_header Cache-Control` inside `location` blocks dropped server-level CSP, `X-Frame-Options`, and `X-Content-Type-Options` headers | Re-applied full security header set (`CSP` with `frame-ancestors 'none'; base-uri 'self'; form-action 'self'`, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy`, `Permissions-Policy`) directly inside `/`, `/index.html`, and `/assets/` | `nexus-c-console/nginx.conf` |
| **P11-03** | **P2** | `POST /api/v1/auth/verify-email` lacked `[EnableRateLimiting("auth")]` | Added `[EnableRateLimiting("auth")]` across all anonymous auth endpoints | `AuthController.cs` |
| **P11-04** | **P2** | `Dockerfile` runtime stage (`mcr.microsoft.com/dotnet/aspnet:10.0`) referenced `curl` in `HEALTHCHECK` without ensuring `curl` was installed | Installed `curl` cleanly (`--no-install-recommends`, apt cache purged) before switching to non-root `USER app` | `Dockerfile` |

---

## 2. Multi-Tenant & PostgreSQL RLS Matrix (33 `FORCE RLS` Tables)

| Isolation Layer | Mechanism | Enforcement Scope | Verified Behavior |
|---|---|---|---|
| **HTTP Middleware** | `TenantContextMiddleware` | Every authenticated request | Rejects mismatched `X-Tenant-Id` / `Tenant-ID` (`403 TENANT_SPOOFING_DETECTED`), revoked sessions/clients/keys (`401`), locked users (`401`), inactive memberships (`403`), and suspended/archived tenants (`403`) |
| **Application / EF Core** | `ModuleDbContextBase` global query filters + `SaveChanges` tenant guard | All 11 domain modules | Blocks cross-tenant reads/writes before SQL reaches PostgreSQL |
| **Database Engine** | PostgreSQL 17 `ENABLE` + `FORCE ROW LEVEL SECURITY` on **33 tables** (`28` strict + `5` hybrid) executed as `nexus_app` (`NOSUPERUSER NOBYPASSRLS`) | Every SQL query & direct connection | Unscoped `nexus_app` sees `0` tenant rows across strict & audit tables; pooled connections (`MaxPoolSize=1`) unconditionally reset GUCs on `ConnectionClosing` |
| **Audit Immutability** | 3-layer defense: GET-only API + EF `AUDIT_IMMUTABLE` + `SELECT, INSERT`-only `nexus_app` grant + `FORCE RLS` (no UPDATE/DELETE policies) + `trg_audit_events_immutable` trigger | `audit.audit_events` | Even table owner `nexus_migrator` (`FORCE RLS` 0 rows affected) and PostgreSQL superuser (`trg_audit_events_immutable` raises exception) cannot UPDATE or DELETE audit rows |

---

## 3. Authentication, Service Identity & Authorization Matrix

| Control Area | Verification Matrix | Status |
|---|---|---|
| **Human Auth & Refresh Lifecycle** | Short-lived JWT (`ClockSkew = 0`), rotating SHA-256 refresh tokens (`SELECT ... FOR UPDATE`), reuse detection revoking full token family + session, tenant-switch old-token revocation, optional `HttpOnly; SameSite=Strict` cookie mode requiring `X-Nexus-CSRF: 1` | **PASS** |
| **OAuth 2.0 Service Clients** | `POST /api/v1/auth/token` (`grant_type=client_credentials`), salted SHA-256 secret hashing, one-time secret reveal on create/rotate, wildcard scope rejection (`403 WILDCARD_SCOPE_FORBIDDEN`), tenant-bound clients barred from platform scopes, live revocation check in `TenantContextMiddleware` | **PASS** |
| **Scoped API Keys** | `X-Api-Key` header, SHA-256 hashed key storage, prefix display only, tenant binding anti-spoofing, explicit scope evaluation in `PermissionAuthorizationHandler` (zero superuser bypass), expiry & revocation enforced | **PASS** |
| **RBAC / ABAC / Org Scope** | Explicit `Deny` overrides `Allow`; resource-scoped roles (`LegalEntity`, `Branch`, `OrganizationUnit`, `Resource`); descendant subtree calculation via `GET /api/v1/auth/organization-scope`; authoritative `POST /api/v1/auth/authorize`; distributed cache invalidation on role/assignment mutations | **PASS** |
| **Workflow Governance** | Version pinning on live instances; sequential step ordering; Maker–Checker enforced on direct decision AND delegation target; role + unexpired standing delegation eligibility; BOLA guards on task delegation & delegation revocation | **PASS** |
| **SSRF & Secret Hygiene** | Shared `SsrfGuard` blocks non-HTTPS, `localhost`, `.local`, `.internal`, RFC1918, loopback, link-local, metadata (`169.254.169.254`), and IPv6 ULA/link-local targets on webhooks & notification webhooks; HMAC-SHA256 replay-protected signatures (`X-Nexus-Signature` + `X-Nexus-Timestamp`, 300s window); `SensitiveDataRedactor` + error redaction in Operations API; plaintext secrets rejected in Configuration (`422 CONFIG_SECRET_FORBIDDEN`) | **PASS** |

---

## 4. Static Code & Dependency Vulnerability Audit

| Audit Check | Result |
|---|---|
| `dotnet list NEXUS.sln package --vulnerable --include-transitive` (21 projects) | **0 vulnerable packages** |
| `cd nexus-c-console && npm audit --omit=dev` | **0 vulnerabilities** |
| Raw SQL injection scan (`FromSqlRaw` / `ExecuteSqlRaw` across `src/`) | **0 occurrences** (1 parameterized `FromSqlInterpolated` for `FOR UPDATE` task locking inside RLS transaction) |
| Frontend XSS / token persistence scan (`dangerouslySetInnerHTML`, `eval`, `innerHTML`, token in `localStorage`/`sessionStorage`) | **0 occurrences** (tokens stored exclusively in memory) |
| Anonymous endpoint inventory (`[AllowAnonymous]`) | Strictly 5 rate-limited auth endpoints (`/login`, `/refresh`, `/token`, `/reset-password`, `/verify-email`) + 3 sanitized health probes (`/health/live`, `/health/ready`, `/health/dependencies`) |

---

## 5. Residual Risks & Recommended Post-1.0 Follow-Ups

1. **Asymmetric JWT (RS256/ES256) + JWKS Endpoint:** NEXUS C 1.0 uses HS256 symmetric signing for portable single-foundation deployment. Before exposing multi-team production F/E/M services over untrusted networks, upgrade `JwtTokenGenerator` to RS256/ES256 with rotating `kid` keys and a public `/.well-known/jwks.json` endpoint.
2. **Distributed Rate Limiting:** Built-in ASP.NET Core rate limiting is currently node-local. Multi-replica deployments must enforce global rate limits at the ingress API Gateway (Envoy/Kong/YARP) or via Redis-backed rate limiting.
3. **Outbound Webhook Delivery Worker:** NEXUS C 1.0 owns webhook lifecycle, SSRF validation, secret rotation, HMAC signing/verification helpers, and delivery/retry tracking; an asynchronous HTTP dispatch worker with circuit breaker & DNS-rebinding egress proxy should be added when external tenant webhooks are activated in production.
4. **Live OIDC / SAML & TOTP MFA Enrollment:** Schema fields (`MfaEnforced`, `ExternalIdentity`) and `IExternalIdentityProvider` contract are frozen; concrete IdP callbacks and TOTP QR enrollment are scheduled for NEXUS C 1.1 once enterprise IdP credentials are provisioned.
