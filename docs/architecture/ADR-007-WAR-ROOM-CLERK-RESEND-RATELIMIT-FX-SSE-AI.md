# ADR-007: War-Room Final Platform Pillars (Clerk Auth, Resend Email, Distributed Rate Limiting, Reference Data & FX, Real-Time SSE, and AI Governance)

- **Status:** Accepted & Implemented (`feat/nexus-c-final-platform-pillars`)
- **Date:** 2026-11-23
- **Target Release:** NEXUS C 1.0 — Final Foundation & Platform Freeze

---

## 1. Context

To freeze NEXUS C 1.0 as the authoritative shared platform foundation for **NEXUS F (Finance)**, **NEXUS E (Express)**, and **NEXUS M (Management)**, seven final platform capabilities required production-grade completion:

1. Application-scoped authorization and machine credential classes with rotation overlap grace windows.
2. External human authentication via **Clerk** (`app_3MTU0OPO0cFJboGQYPkeOLjZohj`) without relinquishing NEXUS C's internal authorization authority or breaking local password/JWT fallback.
3. Shared transactional email infrastructure backed by **Resend** (verified domain `duli.studio`) with Svix webhook delivery tracking and bounce suppression.
4. Distributed, policy-driven API rate limiting and bounded, sanitized operational API request telemetry.
5. Database-backed global reference data catalogs (countries, IANA timezones, locales, languages, statuses), ISO 4217 currencies, indicative ECB FX reference rates, and non-destructive subscription plan deletion/archival.
6. Authenticated, tenant-isolated, permission-filtered real-time Server-Sent Events (SSE).
7. Tenant-scoped AI governance policies (kill-switch, monthly token budget, and consequential human approval gates).

---

## 2. Architectural Decisions

### 2.1 Application-Scoped Roles & Machine Credential Governance
- `Role.ApplicationCode` and `Role.IsSharedReusable` isolate application-specific roles so a `NEXUS_F` role never grants authorization when evaluated under `X-Application-Code: NEXUS_M` (`APPLICATION_SCOPE_DENIED`).
- `ServiceClient` and `ApiKeyRecord` carry an explicit `CredentialClass` (`CoreInternal`, `SharedEcosystem`, `Application`, `Partner`). Partner credentials cannot hold platform-only scopes, and application-bound credentials cannot escape their owning `ApplicationCode` (`403 APPLICATION_SCOPE_VIOLATION`).
- Secret and API key rotation supports a configurable grace overlap window (`PreviousSecretHash` / `PreviousSecretValidUntil`, `PreviousKeyHash` / `PreviousKeyValidUntil`), while explicit revocation clears both active and previous hashes immediately.

### 2.2 Clerk Dual-Scheme JWKS Authentication & Svix Webhook Sync
- NEXUS C uses a dynamic policy scheme (`NexusDualAuth`) that inspects the incoming JWT header (`alg: RS256` -> `"ClerkJwt"` OIDC JWKS validation; `alg: HS256` -> `"Bearer"` local NEXUS JWT).
- **Authority Boundary:** Clerk authenticates human identity (`sub`), while NEXUS C remains the sole source of truth for immutable internal `UserId`, tenant memberships, organization scope, RBAC/ABAC roles, and entitlements. Unlinked Clerk subjects fail closed (`403 CLERK_USER_NOT_PROVISIONED`).
- Inbound Clerk webhooks (`POST /api/v1/webhooks/clerk`) verify Svix HMAC-SHA256 signatures over the raw request body, deduplicate via `identity.clerk_webhook_events`, guard against out-of-order overwrites using `LastProviderEventAt`, and never derive roles or memberships from Clerk metadata.

### 2.3 Provider-Neutral Shared Email Service & Resend Adapter
- `ISharedEmailService` (`Notification` module) persists every transactional email in `notification.email_deliveries` (protected by hybrid `FORCE ROW LEVEL SECURITY`), enforces idempotency keys, checks `notification.email_suppressions` before sending, and masks recipient addresses in logs.
- `ResendEmailSender` dispatches over HTTPS to `https://api.resend.com/emails` with bounded exponential-backoff retries on `429`/`5xx` and immediate failure classification on `4xx`.
- Inbound Resend webhooks (`POST /api/v1/webhooks/resend`) verify Svix signatures, update delivery status (`Delivered`, `Bounced`, `Complained`), and automatically insert bounce/complaint recipients into `notification.email_suppressions`.

### 2.4 Distributed Rate Limiting & Bounded Sanitized API Request Logs
- `DistributedRateLimitingMiddleware` evaluates active policies from `configuration.rate_limit_policies` (cached per DI host in `RateLimitPolicyCache` and invalidated immediately on CRUD mutations) using `IDistributedCache` (Redis in multi-instance deployments, distributed memory cache in single-node dev).
- Partition keys isolate traffic by `AnonymousIp`, `HumanSession`, `ServiceClient`, `ApiKey`, and `Expensive` route scopes, emitting `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Policy`, and `Retry-After` on HTTP `429`.
- `ApiRequestLoggingMiddleware` writes bounded operational metadata to `messaging.api_request_logs` (protected by hybrid `FORCE ROW LEVEL SECURITY`) and **never** reads or persists `Authorization`, `Cookie`, `X-Api-Key`, query strings, or request/response bodies.

### 2.5 Reference Data, ISO 4217 Currencies, Indicative FX & Safe Plan Archival
- `configuration.reference_data` and `configuration.currencies` are seeded idempotently by `NexusDatabaseMigrator` and power the Console's `SearchableSelect` comboboxes without hardcoded frontend world lists.
- `configuration.fx_rates` stores `decimal(18,8)` observations fetched via `FrankfurterFxRateProvider` (`https://api.frankfurter.app/latest`, guarded by `SsrfGuard.ValidateUriAndResolveDnsAsync`). All FX responses carry explicit `IsStale`, `ObservedAt`, `FetchedAt`, and the disclaimer: `"Indicative FX reference rate — not a settlement rate."`
- `DELETE /api/v1/platform/subscriptions/plans/{planId}` checks cross-tenant subscription references under platform bypass: unused plans (`0` references) are `HardDeleted`; referenced plans (`>= 1` reference) are safely `Archived` (`IsActive = false`, `ArchivedAt` set) preserving 100% of historical and active tenant subscriptions.

### 2.6 Real-Time SSE Operational Stream & AI Governance Policies
- `PlatformRealtimeHub` uses bounded per-subscriber channels (`Capacity = 128`, `DropOldest`) and filters every event by both tenant scope and live RBAC permissions before enqueueing, supporting `Last-Event-ID` replay from a 100-event ring buffer.
- `integration.ai_capability_policies` (strictly tenant-scoped `FORCE ROW LEVEL SECURITY`) enforces per-capability `KillSwitchActive` (`403 AI_KILL_SWITCH_ACTIVE`), `RequireHumanApproval` (`403 AI_HUMAN_APPROVAL_REQUIRED`), and `MonthlyTokenBudget` (`429 AI_BUDGET_EXCEEDED`) inside `InvokeAiGatewayAsync`.
