# NEXUS C 1.0 — Complete Repository Baseline Audit

**Audit Date:** 2026-11-22  
**Repository:** `https://github.com/duongbui-drt1/NEXUS-C`  
**Default Branch:** `main` (`befbeac`)  
**Starting HEAD:** `345ea66` (`fix/nexus-foundation-hardening`)  
**Target Release Branch:** `release/nexus-c-1.0-foundation`  
**Audit Scope:** Full backend (`src/`), frontend (`nexus-c-console/`), database & RLS (`infrastructure/`, `Nexus.Migrator`), tests (`tests/`), CI/CD (`.github/workflows/ci.yml`), and documentation (`docs/`).

---

## 1. Baseline Verification Results (Pre-Modification)

Executed cleanly on branch `release/nexus-c-1.0-foundation` (branched from `345ea66`) prior to any code edits:

| Gate | Command | Result | Details |
|---|---|---|---|
| Backend Build | `dotnet build NEXUS.sln -c Release` | **PASS** | 0 warnings, 0 errors (.NET 10.0.404) |
| Unit Tests | `Nexus.UnitTests` | **PASS** | 30 passed, 0 failed |
| Architecture Tests | `Nexus.ArchitectureTests` | **PASS** | 5 passed, 0 failed |
| Integration / RLS Tests | `Nexus.IntegrationTests` | **PASS** | 11 passed, 0 failed (PostgreSQL 17.12) |
| API Tests | `Nexus.ApiTests` | **PASS** | 5 passed, 0 failed |
| Security Tests | `Nexus.SecurityTests` | **PASS** | 4 passed, 0 failed |
| Frontend Typecheck | `npm run typecheck` | **PASS** | TypeScript strict clean |
| Frontend Lint | `npm run lint` | **PASS** | 0 errors, 0 warnings |
| Frontend Unit/UI Tests | `npm test` (Vitest) | **PASS** | 11 passed across 2 test files |
| Frontend Production Build | `npm run build` | **PASS** | Vite 8.3.8 clean build (~104 KB gz initial JS) |
| Docker Daemon | `docker info` | **UNAVAILABLE** | Docker CLI/daemon is not installed in this local macOS host environment |

---

## 2. Existing Verified Capabilities

1. **Modular Monolith & Compile-Time Isolation:** 11 bounded-context modules (`Tenancy`, `Organization`, `Identity`, `AccessControl`, `Audit`, `Workflow`, `Subscription`, `Configuration`, `Integration`, `Notification`, `FileStorage`) plus `SharedKernel`, `BuildingBlocks`, `Bootstrap`, `Migrator`, `Api`. Enforced by `Nexus.ArchitectureTests`.
2. **PostgreSQL 17 RLS & Least-Privilege Runtime Role:** `FORCE ROW LEVEL SECURITY` active on 24 strictly tenant-scoped tables + 3 hybrid tables (`access_control.roles`, `access_control.user_roles`, `configuration.feature_flags`); runtime connects as `nexus_app` (`NOSUPERUSER`, `NOBYPASSRLS`); `audit.audit_events` protected by append-only trigger + `SELECT, INSERT`-only grant.
3. **Hardened Multi-Tenant AuthN/AuthZ:** ASP.NET Core Identity, short-lived JWT access tokens, SHA-256 hashed rotating opaque refresh tokens with `FOR UPDATE` concurrency lock and family-wide reuse detection, anti-tenant-header-spoofing middleware, cross-tenant membership protection (`platform.memberships.manage`), and fail-closed Console RBAC hydrated from `GET /api/v1/auth/permissions`.
4. **Transactional Outbox / Inbox / Idempotency:** `FOR UPDATE SKIP LOCKED` Outbox dispatcher, unique-constrained Inbox deduplication, RabbitMQ v7 publisher + InMemory fallback, HTTP `Idempotency-Key` middleware.
5. **Deployment & CI:** Dedicated one-shot `src/Nexus.Migrator` (`Dockerfile.migrator`), env-driven `infrastructure/database/init-roles.sh`, production startup secret validation, dual CI (`backend-ci` + `frontend-ci`).

---

## 3. Verified Audit Findings & Remediation Plan

### P0 — Critical Security & Data Integrity Defects

| ID | Area | Verified Defect | Remediation |
|---|---|---|---|
| **F-001** | `SubscriptionService` | `AssignPlanToTenantAsync` calls `tenantContext.BeginScope(request.TenantId)` without a service-level platform permission check. Worse, switching a tenant from a richer plan to a narrower plan leaves previous `Entitlement` rows enabled indefinitely, and `HasFeatureAsync` does not check whether the parent `TenantSubscription` is still `Active`/`Trial`. | Enforce service-level authorization, disable/revoke entitlements removed during plan changes, verify parent subscription validity + feature flags in entitlement evaluation, and add regression tests. |
| **F-002** | `OrganizationService` | `AssignUserAsync` does not validate that `request.UserId` exists or holds an active membership in the current tenant — permitting cross-tenant user assignment into a tenant's organization chart. Missing audit events on branch/unit/assignment mutations. | Validate user existence via `IUserDirectoryReader` and active tenant membership via `ITenantAccessValidator` before assignment; audit all organization mutations. |
| **F-003** | PostgreSQL RLS (`AccessControl` & `Audit`) | `access_control.role_permissions` and `audit.audit_events` currently have no PostgreSQL `FORCE ROW LEVEL SECURITY` policies, relying solely on application-level LINQ checks. | Apply `FORCE ROW LEVEL SECURITY` on `access_control.role_permissions` (linked to role scope/tenant ownership), `audit.audit_events` (hybrid tenant/platform SELECT + append-only INSERT), and all newly added tenant-scoped tables. |
| **F-004** | `AuthService.SwitchTenantAsync` | Switching tenants issues a new refresh token for the target tenant without revoking active refresh tokens bound to the previous tenant in the same session, and does not reject switching into a `Suspended` or `Archived` tenant. | Reject switching into `Suspended`/`Archived` tenants and revoke previous tenant's active refresh tokens for that session upon tenant switch. |
| **F-005** | `FileStorageService` | `ComputeSignature` silently falls back to a hardcoded `"NEXUS_FALLBACK_SIGNING_SECRET_FOR_DEV_ONLY"` if `ObjectStorage:SigningSecret` is blank instead of failing closed outside dev/test. Download & temporary URL creation are not audited. | Fail closed when signing secret is missing outside dev/test; audit file download and temporary URL issuance; add file retention metadata (`RetentionDays`/`ExpiresAt`). |

### P1 — Required Capabilities for NEXUS C 1.0 Foundation Freeze

| ID | Phase | Verified Gap | Remediation |
|---|---|---|---|
| **F-006** | Phase 01 (Org & ABAC) | No effective organizational scope resolution (`LegalEntity`/`Branch`/`OrgUnit` hierarchy) and no organization/resource-scoped policy evaluation or permission caching. `AssignRoleAsync` deduplication ignores `(ResourceScopeType, ResourceScopeId)`. | Add `OrganizationUnit.Status`, assignment effective dates (`EffectiveFrom`/`EffectiveTo`), `IOrganizationScopeReader`, `GET /api/v1/auth/organization-scope`, `POST /api/v1/auth/authorize`, ABAC scope + explicit deny evaluation, and `IDistributedCache` permission cache with invalidation. |
| **F-007** | Phase 01 (Auth Cookie & IdP) | Refresh token is JSON-body-only; no optional HttpOnly/Secure/SameSite cookie refresh flow with CSRF protection, and no documented external IdP contract. | Add opt-in HttpOnly/SameSite cookie refresh mode with CSRF header/origin check (100% backward-compatible with JSON clients) and `IExternalIdentityProvider` contract + ADR. |
| **F-008** | Phase 02 (Workflow) | Workflow module lacks version publishing/activation/deactivation, Sequential vs Parallel approval enforcement, role/delegation approver eligibility enforcement, `ApprovalDelegation` with validity period, SLA/escalation rules, and history query API. | Extend `WorkflowVersion` (pinning `RequireMakerChecker`, `ApprovalMode`, `ConditionExpressionJson`, `EscalationHours`, `EscalationRoleName`), add `ApprovalDelegation` (RLS), enforce role/delegation & sequential order in `DecideTaskAsync`, and expose full definition/version/delegation/escalation/history APIs. |
| **F-009** | Phase 03 (Operations & Events) | `EventEnvelope` lacks `Producer` and `CausationId`. No Operations REST API exists for Outbox, Inbox, Dead Letters, DLQ manual retry (with loop guard & audit), or live background job status. | Extend `EventEnvelope` backward-compatibly, add DLQ retry history/loop-protection columns, add `IBackgroundJobTracker`, and implement `OperationsController` (`/api/v1/platform/operations/*`). |
| **F-010** | Phase 03 (Webhooks) | Webhook module only supports `POST /webhooks`; missing list, update, disable/enable, delete, secret rotation, delivery history (`WebhookDelivery` with RLS), delivery retry, and timestamped HMAC-SHA256 replay-protected signature helper. | Complete full webhook lifecycle & delivery tracking with SSRF protection and non-plaintext secret handling. |
| **F-011** | Phase 03 (Service Identity & API Keys) | No machine-to-machine OAuth 2.0 Client Credentials flow (`POST /api/v1/auth/token`) or API Client / Scoped API Key management for NEXUS F, E, M. | Add `ServiceClient` & `ApiKeyRecord` in `Identity`, implement OAuth 2.0 `client_credentials` token issuance & scoped API key auth (strictly scoped, revocable, audited, no superuser bypass), and expose `/api/v1/platform/api-clients` & `/api/v1/platform/api-keys`. |
| **F-012** | Phase 04 (Application Registry & Entitlements) | No Application Registry (`NEXUS_C`, `NEXUS_F`, `NEXUS_E`, `NEXUS_M`), no tenant application activation/deactivation with dependency checks, no rich authoritative `GET /api/v1/platform/entitlements/{featureCode}` endpoint, and no idempotent usage recording. | Add `RegisteredApplication` & `TenantApplicationActivation` (RLS), seed F/E/M/C metadata, enforce plan/subscription/activation/feature-flag entitlement evaluation, add idempotent usage ingestion, and expose `/api/v1/platform/applications` & `/api/v1/platform/entitlements`. |
| **F-013** | Phase 05 (Platform Services) | Configuration lacks secret-key guard, application-specific config scope, caching/invalidation, and history API. Notification lacks template listing, webhook channel delivery, retry, and user preferences. | Complete Configuration, Notification, and FileStorage hardening and endpoints. |
| **F-014** | Phase 06 (Console) | `OperationsPage`, `ApiManagementPage` (`clients`/`keys`/`webhooks` tabs), and `ApplicationsPage` display `Not Available` placeholders for the newly required platform endpoints. | Connect all Console screens to live backend APIs while preserving fail-closed RBAC, in-memory token security, and honest status indicators for intentionally deferred items (e.g., dynamic per-client rate-limit policies & centralized REST HTTP log query). |

### P2 — Operational, Deployment & Observability Hardening

| ID | Phase | Verified Gap | Remediation |
|---|---|---|---|
| **F-015** | Phase 07 (Migrator Lock) | `NexusDatabaseMigrator.MigrateAndHardeningAsync` does not acquire a PostgreSQL advisory lock to prevent concurrent migration races across simultaneous deployments. | Wrap migration & hardening in a session-level `pg_advisory_lock` with guaranteed `pg_advisory_unlock` in `finally`. |
| **F-016** | Phase 08 (Observability) | OpenTelemetry traces lack Npgsql/custom activity sources and custom platform metrics (`Nexus.Platform` meter); health check returns plain text rather than safe structured JSON without secrets; reverse-proxy `ForwardedHeaders` not wired. | Add `NexusPlatformMetrics`, Npgsql/custom tracing sources, safe JSON health check writer (no secrets/connection strings), and `ForwardedHeaders` middleware. |
| **F-017** | Phase 07 (Backup/Restore Drill) | Backup/restore procedure is documented but not verified via an executed restore test in this release milestone. | Execute live `pg_dump` -> isolated restore database -> row/RLS/trigger verification drill and record evidence in release reports. |

### Deferred (Explicitly Outside NEXUS C 1.0 Foundation Freeze)

- Full interactive OIDC/SAML SSO federation broker (contract `IExternalIdentityProvider` + ADR delivered; provider adapters deferred post-1.0).
- Dynamic per-client runtime rate-limit policy editor UI & distributed Redis rate-limiting store (multi-instance limitation explicitly documented in deployment docs; global/auth rate limiting active).
- Centralized REST query API for raw HTTP access logs (structured logs exported via Serilog + OpenTelemetry OTLP to external log aggregators).
- Application-specific business domains (NEXUS F ledgers/invoicing, NEXUS E routing/fleet, NEXUS M executive KPIs).
