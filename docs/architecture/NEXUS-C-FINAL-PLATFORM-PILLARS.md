# NEXUS C — Final Platform Pillars
## Engineering scope and implementation contract

**Status:** Proposed implementation baseline  
**Target release:** NEXUS C 1.0 Foundation  
**Working branch:** `feat/nexus-c-final-platform-pillars`  
**Scope:** NEXUS C only. NEXUS F, E and M remain independent applications.

This document turns the final foundation requirements into implementable platform contracts. It is not evidence that the capabilities are already implemented. Every item must be verified against source code, implemented, migrated, tested and documented before being marked complete.

---

## 1. Non-negotiable architecture boundaries

- PostgreSQL remains the source of truth for NEXUS tenant, organization, membership, application installation, authorization and operational metadata.
- Clerk becomes the external identity provider for interactive user authentication. Clerk user IDs are stored as immutable external identity references; do not use email as a primary key.
- NEXUS authorization remains authoritative for platform, tenant, application and resource permissions. A valid Clerk session authenticates a person; it does not grant NEXUS roles or tenant membership.
- Resend is the default outbound email provider behind a provider-neutral email abstraction.
- Provider credentials, Clerk secrets, Resend API keys, API keys and webhook signing secrets must never be returned by ordinary read/list APIs.
- Real-time updates must be event-driven where possible. Do not implement indiscriminate polling.
- Application-specific business logic remains outside NEXUS C.

## 2. Shared and application-scoped identity model

### 2.1 Applications

Use a canonical application registry with stable codes:
- `NEXUS_C`
- `NEXUS_F`
- `NEXUS_E`
- `NEXUS_M`
- Partner applications registered through an explicit onboarding process.

An application registration is metadata, not proof that an application is deployed or healthy.

### 2.2 Tenants

Do not create duplicate real-world tenant identities for each application.

Use one canonical tenant identity in Core, then model application participation separately:
- Core tenant record.
- Tenant application installation/activation.
- Application-specific tenant settings and external connection bindings.
- Tenant subscription and entitlements.
- Application-specific resource scope only where a module genuinely requires it.

This allows a single customer organization to use F, E and M without creating three unrelated customer identities.

### 2.3 Roles

Support distinct role scopes:
- Platform/Core roles: apply to NEXUS C administration.
- Shared ecosystem roles: reusable by registered first-party or partner applications only when explicitly granted.
- Application-scoped roles: bound to one application code and its permission namespace.
- Tenant-scoped roles: bound to a tenant.
- Resource/organization-scoped assignments: bound to the relevant organizational scope.

Role definitions and role assignments are separate concerns. A role's application scope must be checked server-side on every authorization decision. Do not let an F role authorize M operations by matching permission names.

No automatic privilege inheritance between applications. Cross-application grants must be explicit, auditable and tenant-bound.

### 2.4 Service identities and API keys

Model machine identities independently from human users.

An API credential must be bound to:
- Service client identity.
- Issuing application.
- Credential scope: Core/shared, application-specific, or partner integration.
- Tenant restriction where applicable.
- Explicit scopes/permissions.
- Expiration and rotation policy.
- Created-by actor and audit metadata.
- Revocation state and last-used metadata.

Secret handling:
- Generate high-entropy secrets once.
- Store only a cryptographic hash or keyed digest suitable for verification.
- Show the plaintext secret only once at creation/rotation.
- Never store or return plaintext secrets.
- Support rotation with overlap/grace period where required.
- Support immediate revocation.
- Enforce rate limits and scope checks.
- Never issue a universal ecosystem super-key.

Shared Core credentials must be distinguishable from application credentials. Partner credentials must be isolated from first-party application credentials and must not receive implicit platform-admin permissions.

---

## 3. User administration

The Users area must support authorized administrators editing user profile and administrative metadata without changing role assignments.

Editable fields should be limited to explicitly approved fields, for example:
- Display name / first name / last name.
- Preferred display name.
- Phone number, if collected and verified according to policy.
- Locale and timezone.
- Region/country metadata.
- Account status where policy permits.
- Internal notes or administrative metadata, with strict permission and audit controls.

Role assignment, role removal, permission changes and tenant membership must remain in separate workflows with separate permissions. The user profile update endpoint must reject role, permission, tenant membership and privilege fields even if supplied in the request body.

For Clerk-managed identity attributes, use Clerk Backend API to update Clerk, then synchronize the authoritative result back to the local profile. Never allow a local edit to be silently overwritten by a later webhook without a defined field ownership policy.

User detail should expose safe internal metadata to authorized admins:
- Clerk user ID (non-secret identifier).
- Internal NEXUS user ID.
- Created/updated timestamps.
- Last sign-in or last activity if reliably available.
- Account status and synchronization status.
- Tenant memberships and application access summaries, subject to permission checks.
- Session summaries and revocation controls, subject to permission checks.

Secrets, recovery tokens, API credentials and authentication material must never be shown. Mask any displayable sensitive identifier and require an explicit privileged reveal action only if there is a legitimate operational need. Prefer never exposing secret values at all.

---

## 4. Clerk authentication and database synchronization

### 4.1 Authentication flow

- Replace the current interactive login UI with Clerk's supported React integration.
- Configure Clerk publishable key in the frontend and secret key only on the server.
- ASP.NET Core must validate Clerk session JWTs using the configured issuer, audience/authorized party as appropriate, and Clerk JWKS/public keys.
- Validate signature, issuer, audience/authorized party, expiry and required subject claims.
- Resolve the Clerk subject to an internal NEXUS user record.
- Resolve active tenant context and NEXUS permissions independently.
- A Clerk-authenticated user without an active NEXUS user/membership must not gain access by default.
- Remove or disable the old password-login path only after a migration and rollback plan exists.
- Do not place Clerk secret keys in Vite variables or browser bundles.

### 4.2 Local identity data

Store the Clerk subject as a unique immutable external identity key. Keep NEXUS-specific profile extensions, memberships, roles, organization assignments, preferences and audit records in NEXUS databases.

Define field ownership:
- Clerk-owned: primary identity attributes managed by Clerk (subject, verified email state, provider identity, profile attributes selected by policy).
- NEXUS-owned: tenant membership, roles, organization assignments, application access, locale/timezone overrides if designated local, internal notes and operational metadata.

Do not let a Clerk webhook overwrite NEXUS-owned authorization fields.

### 4.3 Webhook synchronization

Implement a public, dedicated Clerk webhook endpoint that:
- Verifies Svix signature against the raw request body.
- Validates timestamp tolerance.
- Persistes delivery/event ID before side effects.
- Handles at-least-once delivery idempotently.
- Handles out-of-order events using provider timestamps/version strategy.
- Handles `user.created`, `user.updated`, `user.deleted`, and relevant deprovision/ban/unban events.
- Uses soft deletion/deactivation where records are referenced by audit history.
- Does not create roles, memberships or entitlements from profile webhook data.
- Returns 2xx only after durable acceptance/processing according to the chosen inbox pattern.
- Provides reconciliation/backfill using Clerk Backend API pagination for missed events.

Webhook deliveries are asynchronous and may be duplicated or reordered. Never depend on webhook completion for a synchronous login decision.

---

## 5. Resend email platform

Create a provider-neutral email delivery abstraction and implement Resend as the initial provider.

Required capabilities:
- Central sender configuration and verified sending domain.
- Environment-specific sender identities.
- Template catalog with stable template keys and versions.
- Transactional email sending.
- Delivery attempt records and provider message IDs.
- Retry with bounded exponential backoff and jitter.
- Idempotency/deduplication for business-triggered messages.
- Provider error classification.
- Suppression/bounce/complaint handling where supported.
- Webhook signature verification and event deduplication.
- Redaction of addresses and message content in operational logs as appropriate.

All NEXUS modules must use the shared email abstraction; modules must not instantiate Resend directly.

Secrets are server-only environment/secret-manager values. Never commit Resend API keys.

---

## 6. Rate limiting and API logs

### 6.1 Rate limiting design

Use policy-based rate limiting with layered keys:
- Anonymous traffic: IP/network partition with safe proxy handling.
- Human sessions: user + tenant + route/policy.
- Service clients: client identity + application + tenant.
- API keys: credential identity + scope + tenant.
- Expensive operations: dedicated stricter policy.

Support:
- Per-route policy.
- Per-tenant plan limits.
- Per-application limits.
- Burst and sustained limits.
- Configurable policies with audited changes.
- Distributed coordination for multi-instance deployments.
- Standard 429 response with Retry-After and correlation ID.
- Exemptions only for documented health endpoints and trusted internal traffic.

Never trust arbitrary forwarded IP headers. Configure trusted proxies explicitly. Avoid high-cardinality or attacker-controlled metric labels.

### 6.2 API request logs

Build structured request telemetry with separate retention and access policy from business audit records.

Capture only operationally necessary fields:
- Timestamp, method, normalized route template, status code.
- Duration and response size.
- Correlation/trace ID.
- Application/service client ID.
- Tenant ID where safe.
- User ID where authorized and privacy-compliant.
- Rate-limit decision/policy.
- Error code and sanitized failure category.

Never capture authorization headers, cookies, API key values, passwords, refresh tokens, request bodies or response bodies by default. Add explicit allowlisted body logging only for narrowly justified diagnostics with redaction.

Provide query APIs with pagination, time range, status, route, application, tenant and correlation filters. Enforce permission scopes and tenant isolation. Define retention, indexing, partitioning and export strategy before enabling high-volume persistence. Prefer OpenTelemetry/log aggregation for full-fidelity observability and a bounded operational log store for Console search.

---

## 7. Tenant Connections and provider catalog

Create a provider catalog plus tenant connection instances.

Provider catalog describes supported integrations; tenant connection stores a tenant's configured binding.

Candidate provider categories:
- Email: Resend and future alternatives.
- Cloud storage: S3-compatible providers.
- Identity/SSO: Clerk and future OIDC/SAML providers.
- Messaging: RabbitMQ-compatible infrastructure or managed brokers.
- Accounting/ERP integrations where supported by explicit connector modules.
- Payment providers only when a product requirement exists.
- Currency/FX data providers.
- Other third-party APIs registered through a controlled provider adapter.

A connection must include:
- Provider code and version.
- Tenant and optionally application scope.
- Display name and status.
- Non-secret configuration.
- Secret references stored in a secure secret store, never plain JSON in ordinary tables.
- Health/validation status.
- Last successful sync/check.
- Created/updated audit metadata.
- Permission-controlled test, rotate, disable and delete actions.

Use provider adapters and typed configuration schemas. Do not create a generic arbitrary-URL connector that creates SSRF risk. Enforce outbound host allowlists, DNS/IP validation, private-network blocking, timeouts, TLS validation and secret redaction.

---

## 8. Webhook platform

Build webhook management as a first-class integration capability.

Required lifecycle:
- Register endpoint.
- Select event subscriptions.
- Generate signing secret.
- Show secret once.
- Verify HMAC signature and timestamp.
- Deliver with bounded retries and exponential backoff plus jitter.
- Record delivery attempts and sanitized response metadata.
- Disable, rotate secret, update subscriptions and revoke endpoint.
- Manual retry/replay with permission and audit.
- Idempotency and event schema versioning.
- Tenant/application isolation.
- Delivery history and filtering.

Prevent SSRF:
- HTTPS required in production unless an explicit local-development exception.
- Block loopback, link-local, private and metadata-service addresses.
- Resolve and validate DNS at connection time; protect against DNS rebinding.
- Restrict redirects or revalidate every redirect target.
- Apply connection/read timeouts and response-size limits.

Do not confuse inbound Clerk/Resend provider webhooks with outbound NEXUS customer webhooks. They must have separate endpoint registrations, signing secrets, event models and authorization.

---

## 9. Metadata-driven selectable fields

Optional fields that currently require free-text input should become controlled selectors where the value belongs to a known vocabulary.

Create a shared reference-data/catalog capability for:
- Country and region.
- Locale and timezone.
- Currency codes.
- Language.
- Common status/type/category codes.
- Other stable enumerations discovered in the existing forms.

Rules:
- Use stable ISO/provider codes as persisted values.
- Display localized human-readable labels.
- Support search and keyboard navigation.
- Support inactive/deprecated values without breaking historical records.
- Seed stable reference data through versioned migrations or controlled seed jobs.
- Allow dynamic provider-backed catalogs only when freshness is required.
- Preserve free text for genuinely open-ended values.
- Optional fields remain optional; do not force a selection.

Do not hard-code large world datasets in React components.

---

## 10. Currency and FX rates

Separate currency metadata from exchange-rate observations.

Currency catalog:
- ISO 4217 code.
- Display name.
- Symbol.
- Decimal precision.
- Active status.

FX rate record:
- Base currency.
- Quote currency.
- Rate.
- Provider.
- Observed timestamp.
- Retrieved timestamp.
- Validity/staleness status.
- Source reference.

Requirements:
- Provider adapter so FX source can be changed.
- Scheduled refresh plus on-demand refresh with throttling.
- Cache with explicit freshness.
- Display source and last-updated time.
- Handle provider outage and stale rates honestly.
- Never silently substitute a stale rate as current.
- Store rates as decimal/numeric, never binary floating point.
- Support base/quote conversion consistently.
- Do not treat FX reference rates as transaction settlement rates.
- Do not claim real-time FX unless the provider contract actually offers it.

Currency selection must be searchable and show code, name and symbol. When an amount is entered, the selected currency controls formatting and precision.

---

## 11. Subscription plan deletion

Allow authorized administrators to delete subscription plans, but protect historical integrity.

Default behavior:
- Plans referenced by subscriptions, invoices, usage records or entitlements are archived/deactivated, not physically deleted.
- Hard deletion is allowed only when the plan has never been referenced and no dependent records exist.
- Require confirmation and a permission distinct from ordinary plan editing.
- Audit actor, timestamp, reason and affected references.
- Existing tenants retain their historical plan snapshot and entitlements according to the defined migration policy.

Never cascade-delete subscription history to satisfy a UI delete button.

---

## 12. Real-time platform updates

Classify update needs before selecting transport.

- Server-Sent Events or SignalR/WebSockets: live operational status, job state, API activity summaries, integration health and delivery progress.
- Webhooks: outbound notifications to partner applications.
- RabbitMQ/domain events: internal module-to-module propagation.
- Query invalidation/refetch: Console data refresh after mutations.
- Scheduled refresh: FX rates, external provider metadata and reference catalogs where appropriate.

Requirements:
- Tenant and permission-aware subscriptions.
- Reconnect and resume strategy.
- Backpressure and bounded buffers.
- No sensitive payload broadcast to unauthorized clients.
- Eventual consistency clearly represented in UI.
- Polling only as a documented fallback with sensible intervals.

---

## 13. User interface requirements

- Convert optional known-value fields to searchable select/combobox controls.
- Provide loading, empty, error, stale and permission-denied states.
- Mask secret material by default.
- Provide one-time reveal for newly generated API/webhook secrets only.
- Require elevated permission and explicit confirmation for destructive actions.
- Separate profile editing from role/permission administration.
- Show sync status for Clerk-linked users.
- Show provider health and last verification time for tenant connections.
- Show FX source and timestamp.
- Make real-time freshness visible rather than implying data is live when it is cached.

---

## 14. Implementation sequence

Implement in dependency order, not UI order:

1. Audit existing branch and current main/release diffs; do not overwrite work already present.
2. Finalize canonical application/tenant/role/service-client scope model.
3. Implement API credential scopes and rotation/revocation.
4. Implement user profile editing with role fields explicitly excluded.
5. Implement Clerk JWT validation and internal identity mapping.
6. Implement Clerk webhook inbox, idempotency and reconciliation.
7. Implement shared Resend email provider abstraction and delivery records.
8. Implement rate-limit policy model and distributed enforcement.
9. Implement API operational logging with redaction, retention and query APIs.
10. Implement provider catalog and secure tenant connection lifecycle.
11. Implement outbound webhook lifecycle and SSRF defenses.
12. Implement reference-data catalogs and searchable selectors.
13. Implement FX provider adapter, freshness and currency UI.
14. Implement subscription plan archive/delete safeguards.
15. Implement real-time transport for justified live views.
16. Add Console screens, API contracts, migrations, tests and documentation.
17. Run full security, tenant isolation, integration, frontend and deployment test suites.

Do not implement all features in one oversized commit. Use small, reviewable commits grouped by architectural capability.

---

## 15. Mandatory acceptance criteria

- No application can use another application's API key or role unless explicitly granted.
- Shared Core/partner access is explicit, scoped and revocable.
- One canonical tenant can install multiple NEXUS applications without duplicate tenant identity.
- User profile updates cannot change roles, permissions or memberships.
- Clerk session JWTs are validated by ASP.NET Core; browser identity claims are never trusted without verification.
- Clerk webhook duplicates and out-of-order events do not corrupt user data.
- NEXUS-specific roles and memberships are never derived from Clerk profile fields.
- All system email is sent through the shared email abstraction using Resend as the configured provider.
- Rate limits work across API instances when deployed in multi-instance mode.
- API logs never persist credentials or sensitive request bodies by default.
- Tenant connections keep secrets outside ordinary database configuration and defend against SSRF.
- Outbound webhooks are signed, retryable, auditable and tenant/application scoped.
- Selectable metadata uses stable codes and remains backward-compatible.
- FX values expose source and freshness and never silently masquerade as live.
- Subscription history cannot be destroyed by deleting a plan.
- Real-time subscriptions enforce tenant and permission boundaries.
- Backend and frontend tests pass; no security test is disabled to obtain a green build.

---

## 16. Required implementation report

For each capability, report:
- Existing implementation found.
- Gaps confirmed.
- Architecture chosen.
- Database migrations.
- API endpoints.
- UI screens.
- Security controls.
- Tests executed and exact results.
- Remaining limitations.

Use status values:
- NOT STARTED
- IN PROGRESS
- IMPLEMENTED — NOT VERIFIED
- VERIFIED
- BLOCKED

Do not mark a capability VERIFIED without executable test evidence.
