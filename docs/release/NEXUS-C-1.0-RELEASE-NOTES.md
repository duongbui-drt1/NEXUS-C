# NEXUS C 1.0 — Release Notes

**Release:** `NEXUS C 1.0` — Final Foundation & Platform Freeze  
**Branch:** `release/nexus-c-1.0-foundation`  
**Platform Stack:** .NET 10 LTS · ASP.NET Core 10 · EF Core 10 · PostgreSQL 17 · Redis 7.4 · RabbitMQ 4.0 · React 19 · TypeScript (strict) · Vite 8

---

## Highlights

NEXUS C 1.0 completes and freezes the shared enterprise platform foundation for the NEXUS ecosystem (**NEXUS F — Finance**, **NEXUS E — Express**, **NEXUS M — Management**). Every platform contract, security control, database schema, and console workspace is backed by real server-side persistence and verified automated tests.

### 1. Security, Tenancy & PostgreSQL RLS Hardening
- **33 PostgreSQL `FORCE ROW LEVEL SECURITY` Tables:** Expanded from 27 to 33 tables by adding `FORCE RLS` to `audit.audit_events`, `access_control.role_permissions`, and 4 new tenant-scoped tables (`workflow.approval_delegations`, `integration.webhook_deliveries`, `subscription.tenant_applications`, `notification.preferences`).
- **Session & Tenant-Switch Hardening:** `POST /api/v1/auth/switch-tenant` now blocks switching into `Suspended` or `Archived` tenants and immediately revokes prior tenant refresh tokens on the session (replaying an old token triggers reuse detection and revokes the full session).
- **Opt-In HttpOnly Cookie Refresh Mode:** `AuthController` supports `X-Nexus-Auth-Mode: cookie` (`HttpOnly; SameSite=Strict` refresh cookie) with mandatory `X-Nexus-CSRF: 1` protection on cookie-based refresh and logout.
- **File Storage Fail-Closed Signing:** Removed hardcoded fallback HMAC secret in `FileStorageService`; added file `RetentionDays` / `RetentionExpiresAt` enforcement and full download/temporary-URL audit logging.
- **Concurrent Migration Safety:** Wrapped `NexusDatabaseMigrator` in PostgreSQL session advisory lock `pg_advisory_lock(74839210)` for safe multi-replica startup orchestration.

### 2. Organization Scope & RBAC/ABAC Policy Engine
- **Active Membership Enforcement:** `OrganizationService.AssignUserAsync` validates target user existence and active tenant membership before creating organizational assignments, with temporal history (`EffectiveFrom`, `EffectiveTo`, `IsActive`) and full revocation support.
- **Organizational Scope Resolution:** Added `GET /api/v1/auth/organization-scope` returning effective Legal Entity, Branch, and full active Organization Unit descendant subtrees.
- **Policy Evaluation Engine:** Added `AuthorizationEffect` (`Allow` / `Deny`) on `UserRole` (explicit Deny overrides Allow), resource/org-scoped roles, `IDistributedCache` permission caching with instant invalidation, and authoritative `POST /api/v1/auth/authorize`.

### 3. Service-to-Service Identity (OAuth 2.0 `client_credentials` & Scoped API Keys)
- Added `identity.service_clients` and `identity.api_keys` with salted SHA-256 secret hashing, one-time secret display on creation/rotation, and strict prohibition of wildcard scopes (`*`) and platform scopes on tenant-bound credentials.
- Added `POST /api/v1/auth/token` (`grant_type=client_credentials`), `X-Api-Key` header authentication, live revocation/scope-intersection verification in `TenantContextMiddleware`, and `/api/v1/platform/api-clients` + `/api/v1/platform/api-keys` management APIs.

### 4. Shared Workflow Governance Engine
- Added workflow versioning (`POST /definitions/{id}/versions`, activate/deactivate) with live instances strictly pinned to their creation `VersionId`.
- Added `ApprovalMode` (`Sequential` / `Parallel`), sequential step ordering, approver role & active standing delegation eligibility (`workflow.approval_delegations`), task delegation (`POST /tasks/{id}/delegate`), SLA escalation (`POST /instances/{id}/escalate`), and history endpoint (`GET /instances/{id}/history`).
- Enforced Maker–Checker on both decisions and delegation targets, plus BOLA guards on task delegation and delegation revocation.

### 5. Ecosystem Application Registry & Authoritative Entitlements
- Added `subscription.applications` (pre-seeded with `NEXUS_C`, `NEXUS_F`, `NEXUS_E`, `NEXUS_M`) and `subscription.tenant_applications` (RLS-enforced tenant activation state + dependency verification).
- Fixed plan downgrade integrity: `AssignPlanToTenantAsync` enforces platform authorization, revokes stale entitlements from prior plans, and invalidates caches.
- `GET /api/v1/platform/entitlements/{featureCode}` evaluates subscription status/expiry, entitlement state, tenant application activation, feature-flag kill-switches, and usage quota (`POST/GET /subscriptions/usage` with idempotency key).

### 6. Operations Observability, Webhook Lifecycle & Platform Services
- **Operations API:** Live `/api/v1/platform/operations/{outbox,inbox,dead-letters,dead-letters/{id}/retry,jobs}` with error secret redaction, manual DLQ retry loop protection (`MaxManualRetries = 3`), and audit logging.
- **Telemetry:** Added OpenTelemetry `Meter("Nexus.Platform")` + `ActivitySource("Nexus.Platform")` with low-cardinality labels (zero tenant IDs), sanitized JSON `/health/dependencies`, and `ForwardedHeaders` support.
- **Webhooks & Notifications:** Complete webhook CRUD, enable/disable, one-time secret rotation, HMAC-SHA256 replay-protected signatures (`WebhookSignatureHelper`), shared `SsrfGuard`, delivery tracking (`integration.webhook_deliveries`), notification `Webhook` channel, `notification.preferences`, template listing, and delivery retry.
- **Configuration:** Added `GET /api/v1/platform/configuration/history`, distributed cache invalidation, and `IFeatureFlagEvaluator` integration.

### 7. NEXUS C Console Upgrades (`nexus-c-console/`)
- Connected **Operations** (`OperationsPage`), **Applications & Entitlements** (`ApplicationsPage`), and **API Management & Webhooks** (`ApiManagementPage`) directly to live NEXUS C 1.0 APIs with one-time secret reveal modal and explicit `Not Available` honesty for deferred dynamic rate-limiting / REST HTTP request logs.
- Hardened `nexus-c-console/nginx.conf` so all responses emit CSP, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, and `Permissions-Policy`.

---

## Database Upgrade & Migration Notes

- **Total EF Core Migrations:** **21** across 12 schemas (9 new `Foundation10_*` migrations added in 1.0).
- **Zero-Data-Loss Upgrade Verified:** Live upgrade drill from the pre-1.0 schema (12 migrations, 27 RLS tables) to NEXUS C 1.0 (21 migrations, 33 RLS tables) preserved 100% of existing tenants, users, and audit events while adding the 2 new application permissions (`37 -> 39`) and seeding `NEXUS_C`, `NEXUS_F`, `NEXUS_E`, and `NEXUS_M`.
- **Mandatory Deployment Step:** Always run the dedicated one-shot `Nexus.Migrator` container/binary using `nexus_migrator` BEFORE starting `Nexus.Api` replicas (which run as least-privilege `nexus_app` with `NOBYPASSRLS`).

---

## Known Documented Limitations

1. **Multi-Instance Node-Local Components:** Rate limiting (`ASP.NET Core RateLimiter`) and background job heartbeats (`BackgroundJobTracker`) are in-process per replica; multi-replica production deployments should enforce global rate limits at the API Gateway/Redis layer.
2. **Docker Compose End-to-End Runtime Verification:** All three container images (`Dockerfile`, `Dockerfile.migrator`, `nexus-c-console/Dockerfile`) were built and verified passing in GitHub Actions Linux CI (`docker-build-verify`, Run `45046431690`). Full multi-container `docker compose up` runtime smoke testing (including Redis, RabbitMQ, and MinIO containers together) could not be executed on the local macOS workstation (Docker CLI absent) and should be run in staging before production cutover.
3. **Symmetric JWT Signing:** HS256 is active in 1.0; RS256/ES256 + JWKS key rotation is planned for 1.1 multi-service federation.
4. **Deferred Features:** Live OIDC/SAML callbacks, TOTP MFA enrollment UI, dynamic Console rate-limit policy editor, REST HTTP gateway request logs, and outbound HTTP webhook dispatcher worker are explicitly deferred past 1.0.
