# NEXUS C Foundation — Security Architecture

## 1. Defense-in-Depth Multi-Tenant Isolation

1. **Zero Trust in Client Tenant Inputs:** Request headers (`X-Tenant-Id`, `Tenant-ID`) are NEVER accepted as authorization proof for human tenant users or tenant-bound service principals. Mismatched tenant headers immediately fail with `403 TENANT_SPOOFING_DETECTED`.
2. **Active Membership, Session & Lifecycle Verification:** Every authenticated request validates user active/lockout status, active session status, active tenant membership, and tenant lifecycle (`Suspended` / `Archived` tenants receive `403` on business endpoints and cannot be targeted via `switch-tenant`).
3. **EF Core Global Query Filters + SaveChanges Tenant Guard:** Prevents accidental cross-tenant reads or writes inside application code.
4. **PostgreSQL `FORCE ROW LEVEL SECURITY`:** Enforced independently across **33 tables** (`28` strictly tenant-scoped + `5` hybrid including `audit.audit_events` and `access_control.role_permissions`) against the non-superuser `nexus_app` (`NOBYPASSRLS`) role with fail-closed semantics.

---

## 2. Authentication, Token Lifecycle & Service Identity

### Human Identity
- **Password Storage:** Managed exclusively by ASP.NET Core Identity (`UserManager<ApplicationUser>` PBKDF2 hashing + lockout after 5 failed attempts).
- **Access Tokens (JWT):** Short-lived (15 min default), validated with `ClockSkew = TimeSpan.Zero`, carrying `sub`, `sid`, `tid`, `scope`, and `jti`.
- **Refresh Tokens:** 64-byte CSPRNG opaque strings stored **only as SHA-256 hashes** (`identity.refresh_tokens`), bound to `TokenFamilyId` and `SessionId`.
  - **Rotation + Concurrency Lock:** `RefreshAsync` locks the row with PostgreSQL `SELECT *, xmin ... FOR UPDATE` inside an ACID transaction.
  - **Tenant-Switch Revocation:** `SwitchTenantAsync` revokes prior active refresh tokens on that session so stale-tenant tokens cannot be replayed.
  - **Reuse Detection:** Presenting an already-rotated or revoked refresh token revokes **every token in the family** and **revokes the `UserSession`**, emitting a `security.refresh_token.reuse_detected` audit event.
  - **Opt-In Cookie Mode:** Clients passing `X-Nexus-Auth-Mode: cookie` receive an `HttpOnly; SameSite=Strict` refresh cookie (`__nexus_rt`) protected by mandatory `X-Nexus-CSRF: 1` validation on cookie-based `/refresh` and `/logout`.

### Machine-to-Machine Identity (NEXUS F, E, M & Integrations)
See [ADR-004: Service-to-Service OAuth 2.0 & API Keys](../architecture/ADR-004-SERVICE-TO-SERVICE-OAUTH2-AND-API-KEYS.md).
- **OAuth 2.0 `client_credentials` (`POST /api/v1/auth/token`):** Issues 10-minute `scope=service` Bearer JWTs after constant-time salted SHA-256 verification. Revoked clients are rejected immediately on subsequent API calls via live validation in `TenantContextMiddleware`, which also intersects token scopes with current `AllowedScopes`.
- **Scoped API Keys (`X-Api-Key`):** SHA-256 hashed at rest, prefix-only listing, one-time reveal on creation/rotation, explicit least-privilege scopes, and automatic expiry/revocation enforcement.
- **Zero Wildcards / Zero Superuser Bypass:** Wildcard scopes (`*`) are forbidden (`403 WILDCARD_SCOPE_FORBIDDEN`), tenant-bound clients/keys cannot receive platform-only scopes, and `PermissionAuthorizationHandler` evaluates explicit granted scopes only.

---

## 3. Authorization (RBAC, ABAC, Org Scope & Separation of Duties)

- **Scope Separation:** Strict boundary between Platform permissions (`PermissionScope.Platform`) and Tenant permissions (`PermissionScope.Tenant`/`Both`). Tenant admins cannot create roles with platform permissions (`403 ROLE_PRIVILEGE_ESCALATION`) or assign platform roles (`403 ROLE_SCOPE_VIOLATION`).
- **Explicit Deny & Resource/Org Scoping:** `UserRole.Effect` supports `Allow` and `Deny` (explicit `Deny` always overrides `Allow`) with optional `ResourceScopeType` (`Platform`, `Tenant`, `LegalEntity`, `Branch`, `OrganizationUnit`, `Resource`) and `ResourceScopeId`, evaluated authoritatively via `POST /api/v1/auth/authorize` and cached in `IDistributedCache` with instant invalidation.
- **Organization Scope Isolation:** `OrganizationService.AssignUserAsync` enforces active tenant membership; `GET /api/v1/auth/organization-scope` resolves active Legal Entity, Branch, and descendant Organization Unit subtrees.
- **Workflow Governance:** Enforces version pinning, sequential ordering, **Maker–Checker Separation of Duties** (on decisions and delegation targets), role + unexpired standing delegation eligibility, and BOLA ownership checks on task delegation and delegation revocation.

---

## 4. Secret Hygiene, SSRF Protection & File Security

- `SensitiveDataRedactor` recursively redacts sensitive keys (`password`, `secret`, `token`, `apikey`, `credential`, `privatekey`, etc.) across audit entries, AI Gateway logs, and Operations API error strings.
- Configuration service rejects storing plaintext secret keys (`422 CONFIG_SECRET_FORBIDDEN`).
- Shared `SsrfGuard` enforces HTTPS-only public targets across webhooks and notification webhooks, blocking loopback, RFC1918 private networks, link-local/cloud metadata (`169.254.169.254`), IPv6 ULA/link-local, and `.local`/`.internal` hostnames.
- `WebhookSignatureHelper` signs webhook payloads with HMAC-SHA256 (`{timestamp}.{payload}` -> `v1={digest}`) and enforces a 300-second anti-replay window.
- `FileStorageService` enforces MIME allowlist, max upload size, SHA-256 checksums, tenant-prefixed object keys, retention expiry (`RetentionDays` / `RetentionExpiresAt`), fail-closed `ObjectStorage:SigningSecret` HMAC-SHA256 expiring download links, and full download audit logging.
- `nexus-c-console/nginx.conf` emits strict security headers (`CSP`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Permissions-Policy`) on all SPA and static asset responses.

---

## 5. Known Remaining Hardening Items (Post-1.0 Roadmap)

1. **RS256 / JWKS Asymmetric Key Rotation:** Current JWT signing uses HS256 for foundation portability; multi-service production federation should upgrade to RS256/ES256 with rotating JWKS `kid` keys.
2. **MFA Enrollment & OIDC/SAML Provider Callbacks:** `MfaEnforced`, `ExternalIdentity`, and `IExternalIdentityProvider` contracts are frozen in 1.0; live IdP integrations require enterprise IdP configuration.
3. **Distributed Rate Limiting:** Built-in ASP.NET Core `RateLimiter` is node-local; multi-replica deployments should back global rate limits with Redis or an ingress API Gateway (Envoy/Kong/YARP).
