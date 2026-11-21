# NEXUS C Foundation — Security Architecture

## 1. Defense-in-Depth Multi-Tenant Isolation

1. **Zero Trust in Client Tenant Inputs:** Request headers (`X-Tenant-Id`, `Tenant-ID`) are NEVER accepted as authorization proof. If a tenant-scoped caller sends a mismatched tenant header, `TenantContextMiddleware` immediately rejects the request with `403 TENANT_SPOOFING_DETECTED`.
2. **Active Membership & Lifecycle Verification:** Every authenticated request validates user active/lockout status, active session status, active tenant membership, and tenant lifecycle (`Suspended` tenants receive `403 TENANT_SUSPENDED` on business endpoints).
3. **EF Core Global Query Filters + SaveChanges Tenant Guard:** Prevents accidental cross-tenant reads or writes inside application code.
4. **PostgreSQL `FORCE ROW LEVEL SECURITY`:** Enforced independently inside PostgreSQL against the non-superuser `nexus_app` (`NOBYPASSRLS`) role with fail-closed semantics.

---

## 2. Authentication & Token Lifecycle

- **Password Storage:** Managed exclusively by ASP.NET Core Identity (`UserManager<ApplicationUser>` PBKDF2 hashing + lockout after 5 failed attempts).
- **Access Tokens (JWT):** Short-lived (15 min default), validated with `ClockSkew = TimeSpan.Zero`, carrying `sub`, `sid`, `tid`, `scope`, and `jti`.
- **Refresh Tokens:** 64-byte CSPRNG opaque strings stored **only as SHA-256 hashes** (`identity.refresh_tokens`), bound to `TokenFamilyId` and `SessionId`.
  - **Rotation + Concurrency Lock:** `RefreshAsync` locks the row with PostgreSQL `SELECT *, xmin ... FOR UPDATE` inside an explicit ACID transaction, preventing double-rotation race conditions.
  - **Reuse Detection:** Presenting an already-rotated or revoked refresh token immediately revokes **every token in the family** and **revokes the `UserSession`**, invalidating both access and refresh tokens and emitting a `security.refresh_token.reuse_detected` audit event.

---

## 3. Authorization (RBAC + Separation of Duties)

- Strict separation between **Platform Scope** (`PlatformSuperAdmin`, `PlatformSupport`) and **Tenant Scope** (`TenantOwner`, `TenantAdmin`, `CFO`, `Accountant`, `LogisticsDirector`, `WarehouseManager`, `Employee`, `Auditor`).
- Tenant administrators cannot create roles containing platform-only permissions or assign platform-scoped roles (`403 ROLE_PRIVILEGE_ESCALATION` / `ROLE_SCOPE_VIOLATION`).
- Workflow engine enforces **Maker–Checker Separation of Duties**: initiators cannot approve their own workflow requests (`403 WORKFLOW_MAKER_CHECKER_VIOLATION`).

---

## 4. Secret Hygiene & File Security

- `SensitiveDataRedactor` recursively redacts keys matching `password`, `secret`, `token`, `apikey`, `credential`, `privatekey`, etc., from audit metadata and AI Gateway logs.
- Configuration service rejects storing plaintext secret keys (`CONFIG_SECRET_FORBIDDEN`).
- Integration connectors accept only external secret references (`vault://`, `env://`, `kms://`).
- File uploads enforce a strict MIME allowlist, configurable max byte limit, SHA-256 checksums, tenant-prefixed object keys (`tenants/{tenantId}/{id}/{fileName}`), and HMAC-SHA256 tenant-bound expiring download signatures.

---

## 5. Known Remaining Hardening Items for Future Production Rollout

1. **RS256 / JWKS Asymmetric Key Rotation:** Current JWT configuration uses HS256 symmetric signing for local/foundation portability; production multi-service F/E/M federation should migrate `JwtTokenGenerator` to RS256/ES256 with rotating JWKS key IDs (`kid`).
2. **MFA Enrollment & OIDC Provider Callbacks:** Schema fields (`MfaEnforced`, `ExternalIdentity`) and ASP.NET Core Identity 2FA primitives are in place; concrete TOTP QR endpoints and live OIDC IdP client registration require IdP credentials.
3. **Distributed Rate Limiting:** Built-in ASP.NET Core `RateLimiter` is active per-node; multi-replica horizontal scaling should back rate limiting with Redis or an ingress API Gateway (Envoy/Kong/YARP).
