# NEXUS C 1.0 — FINAL WAR-ROOM MASTER PROMPT
## The Unified Enterprise Platform Core | Final Foundation Engineering Campaign

> **Mission:** Build, verify and freeze the shared platform core that connects NEXUS C, NEXUS F, NEXUS E, NEXUS M and approved partner applications.
>
> **Execution mode:** Autonomous engineering with strict safety, evidence-based completion and human approval gates for irreversible actions.
>
> **Release objective:** NEXUS C 1.0 — Final Foundation / Platform Freeze.

---

# 0. COMMAND INTENT

You are not being asked to make a cosmetic feature update, produce a proposal and stop, or blindly execute a checklist.

You are assigned the role of the **Principal Platform Architect and Engineering War-Room Lead** responsible for the final foundation milestone of an enterprise multi-tenant SaaS platform.

Your responsibility spans:

- Principal Software Architect
- Senior Backend Engineer
- Senior Frontend Engineer
- Full-Stack / Web Platform Engineer
- Database Architect and PostgreSQL Specialist
- Application Security / Product Security Engineer
- Identity and Access Management Architect
- API and Integration Architect
- DevOps / SRE Engineer
- QA Automation and Reliability Engineer
- AI Systems Engineer, responsible for safe and useful AI extensibility

Think and work like a team of experienced specialists, not like a code generator.

You must inspect the actual repository, understand the existing implementation, preserve working architecture, identify root causes, make justified engineering decisions, implement the changes, run tests, inspect the resulting diff and document the release.

**Do not stop after writing a plan.** Continue through implementation and verification unless blocked by missing credentials, unavailable external services, destructive-risk approval, or another genuine dependency that cannot safely be resolved.

Do not claim work that has not been performed. Do not mark untested code as complete. Do not conceal limitations.

# 1. PROJECT IDENTITY

- Product: NEXUS ecosystem
- Core platform: NEXUS C (Center)
- Repository: https://github.com/duongbui-drt1/NEXUS-C
- Working branch: `feat/nexus-c-final-platform-pillars`
- Base branch: `release/nexus-c-1.0-foundation`
- Default branch: `main`
- Target: NEXUS C 1.0 Final Foundation
- Architecture: Enterprise SaaS, multi-tenant, modular monolith, API-first, desktop-first Console
- Backend baseline: ASP.NET Core 10 / .NET 10 / EF Core 10
- Database baseline: PostgreSQL 17
- Infrastructure baseline: Redis, RabbitMQ, S3-compatible object storage / MinIO, Docker Compose
- Observability baseline: Serilog, OpenTelemetry
- Frontend baseline: React 19, TypeScript, Vite, TanStack Query, React Hook Form, Zod, Vitest

These are known project baselines, not permission to assume every package or version remains unchanged. Inspect the current repository and lockfiles first. The current source of truth is the actual checked-out code and latest commit on the assigned branch.

# 2. THE MISSION

Create one coherent, secure, maintainable platform core that provides shared capabilities to:

- NEXUS C — platform administration and shared infrastructure
- NEXUS F — Finance
- NEXUS E — Express / Logistics
- NEXUS M — Management
- approved external partner applications

The core must provide consistent answers to these questions:

1. Who is this human or service?
2. Which tenant and organization does it belong to?
3. Which application is it accessing?
4. What is it allowed to do?
5. Which API credential or identity authenticated the request?
6. Which integration/provider is connected?
7. Which events must be delivered, retried or replayed?
8. Which actions must be audited?
9. Which subscription and entitlement governs access?
10. How can the platform be operated, monitored, upgraded and recovered safely?

NEXUS C is the shared platform, not the owner of every application's business domain.

# 3. NON-NEGOTIABLE ENGINEERING PRINCIPLES

## 3.1 Preserve before improving

- Inspect current HEAD, branch, git status, uncommitted changes, tags and recent history.
- Inspect all existing architecture decisions, migrations, API contracts and tests.
- Never overwrite or discard user work.
- Never reset, force-push, rewrite shared history, delete database volumes, truncate production-like data or drop schemas as a shortcut.
- Do not assume the branch is clean.
- Do not recreate an existing feature before proving it is missing or defective.
- Keep changes incremental and reviewable.

## 3.2 Evidence over confidence

Every finding must be classified:

- CONFIRMED: directly supported by source, test or runtime evidence.
- LIKELY: supported by evidence but not yet proven.
- UNKNOWN: insufficient evidence.
- NOT APPLICABLE: demonstrably outside project scope.

Every feature status must be one of:

- NOT STARTED
- IN PROGRESS
- IMPLEMENTED — NOT VERIFIED
- VERIFIED
- BLOCKED

Never claim a test passed unless it was executed and its result observed.

## 3.3 Security is a platform invariant

- Deny by default.
- Enforce authorization on the backend.
- Never trust frontend route hiding as access control.
- Never trust user-supplied tenant, application or role identifiers without authorization checks.
- Never expose secrets in logs, API responses, browser bundles, error messages or database dumps intended for ordinary access.
- Never grant cross-tenant or cross-application access implicitly.
- Treat every webhook and external provider response as untrusted input.
- Protect against SSRF, replay, injection, IDOR/BOLA, privilege escalation and resource exhaustion.
- Use least privilege for runtime database roles and service identities.

## 3.4 Stable platform contracts

- API-first design.
- Explicit application and tenant scopes.
- Versioned public contracts.
- Backward-compatible evolution by default.
- Breaking changes require an ADR, migration plan, compatibility window and explicit approval.
- No application-specific business logic in Core merely to accelerate one app.
- No fake APIs, fabricated data or mock production behavior.

## 3.5 Engineering discipline

- No giant unreviewable commits.
- No unrequested framework replacement.
- No dependency addition without a reason.
- No speculative abstraction for hypothetical requirements.
- No disabling tests, lint rules, analyzers or security checks to get a green pipeline.
- No swallowing exceptions or returning success after failed operations.
- No TODO placeholders in a capability reported as complete.

# 4. APPLICATION AND DOMAIN BOUNDARIES

## NEXUS C owns

- Human identity mapping and authentication integration.
- Tenants and tenant lifecycle.
- Organizations and shared organizational primitives.
- Application registry and application installation.
- Roles, permissions and access-control infrastructure.
- Service identities and API credentials.
- Subscription, plan and entitlement infrastructure.
- Workflow engine primitives.
- Audit infrastructure.
- Event, messaging and delivery infrastructure.
- Provider registry and tenant connections.
- Webhook infrastructure.
- Shared email delivery.
- Reference data and currency metadata.
- Platform-level configuration.
- API management and operational telemetry.
- Health, reliability and platform operations.
- AI integration governance and shared AI infrastructure primitives.

## NEXUS F owns

Financial accounts, accounting entries, invoices, reconciliation, tax rules and finance-specific approvals.

## NEXUS E owns

Shipment, transport, routing, dispatch, tracking, fleet and logistics workflows.

## NEXUS M owns

Management processes, HR business workflows, internal administration and management reporting.

Core may provide shared primitives, but must not absorb these domain implementations.

# 5. PHASE 0 — REPOSITORY RECONNAISSANCE

Before modifying code:

1. Verify repository, branch and current commit.
2. Inspect git status and preserve every existing change.
3. Compare working branch against its base and main.
4. Read solution/project structure and dependency graph.
5. Inspect backend modules, controllers, middleware, EF Core contexts, migrations and DI registration.
6. Inspect frontend routes, auth context, API clients, forms, state management and error handling.
7. Inspect Docker Compose, database initialization, environment templates, CI and deployment scripts.
8. Inspect architecture docs, API docs, ADRs and test coverage.
9. Identify incomplete, duplicate, insecure and dead code.
10. Run baseline tests before changing behavior.

Produce a concise audit report containing:
- Current architecture map.
- Existing capabilities.
- Confirmed defects.
- Security risks and severity.
- Missing capabilities.
- Migration/deployment risks.
- Baseline test results.
- Dependency blockers.

Do not infer missing features from a UI label. Verify backend endpoints, persistence, authorization and tests.

# 6. PHASE 1 — CANONICAL APPLICATION, TENANT AND AUTHORIZATION MODEL

This is the foundational authorization model. Resolve it before building further privileged UI.

## 6.1 Application registry

Support stable application identities:

- NEXUS_C
- NEXUS_F
- NEXUS_E
- NEXUS_M
- approved partner applications

Each application has:
- immutable application ID/code
- display name and metadata
- owner/operator
- lifecycle state
- trust classification
- allowed authentication methods
- registered redirect/callback origins where relevant
- API contract/version metadata
- capability declarations
- created/updated audit data

Do not equate registration with deployment, availability or authorization.

## 6.2 Canonical tenant identity

A customer tenant is one canonical entity across the ecosystem.

Do not create separate duplicate tenants merely because the customer uses F, E and M.

Model separately:
- tenant identity and lifecycle
- tenant organization hierarchy
- tenant memberships
- application installation/activation
- application-specific configuration
- subscription and entitlements
- provider connections
- tenant-scoped service identities
- tenant-level operational policies

A tenant may install several applications while preserving one canonical identity and controlled boundaries.

## 6.3 Role and permission scopes

Support explicit scope dimensions:

- Platform/Core
- Shared ecosystem
- Application-specific
- Tenant-specific
- Organization/resource-specific where required

A permission must be namespaced and tied to a defined resource/action contract.

A role assignment must carry the scope in which it is valid.

A role from NEXUS F must never grant NEXUS M access by accidental permission-name collision.

Shared roles must be explicitly defined as reusable and explicitly assigned. Do not infer shared access from role names.

Preserve the semantics of existing system roles and global role definitions. Do not apply naive tenant filters that make global system roles inaccessible or leak platform roles.

## 6.4 Authorization evaluation

Define a consistent policy evaluation path:

1. Authenticate principal.
2. Resolve internal user or service identity.
3. Resolve application identity.
4. Resolve tenant context.
5. Resolve resource/organization context.
6. Load relevant role and permission assignments.
7. Evaluate action and scope.
8. Apply subscription/entitlement restrictions where relevant.
9. Record security-sensitive decisions.
10. Deny when required context is missing or ambiguous.

Separate authentication, authorization and entitlement. They are not interchangeable.

Implement fail-closed behavior in backend and Console. Permission-loading errors must never silently become allow-all.

## 6.5 Cross-tenant privilege regression

Investigate and fix the previously identified possible membership privilege escalation:

- TenantAdmin in tenant A must not add or revoke memberships in tenant B.
- Platform-level cross-tenant operations require a distinct explicit platform permission.
- Tenant-scoped services must validate the caller's authority over the target tenant before switching tenant context.
- Context switching must restore prior context safely.
- Denied operations must produce no mutation.
- Add tests for add, revoke, reactivate, role assignment and related tenant-bound operations.

Also review organization assignment, tenant switching, subscription assignment and every service method accepting a target tenant ID.

# 7. PHASE 2 — SERVICE IDENTITIES AND API KEY GOVERNANCE

Create a machine identity model independent of human users.

Support credential classes:

1. Core internal service credentials.
2. Shared ecosystem credentials.
3. NEXUS F credentials.
4. NEXUS E credentials.
5. NEXUS M credentials.
6. Partner application credentials.

Every credential must bind to:
- service client identity
- owning application
- credential type and scope
- tenant restriction when applicable
- explicit permissions/scopes
- expiry
- rotation state
- created-by actor
- last-used metadata
- revocation metadata

Requirements:
- Cryptographically secure high-entropy generation.
- Persist only a secure verifier/hash, not plaintext.
- Show secret once at creation/rotation.
- Immediate revocation.
- Rotation with carefully designed overlap where necessary.
- Optional expiry enforcement.
- Last-used and usage metadata.
- Per-client and per-tenant throttling.
- Audit every create, reveal, rotate, revoke and permission change.
- Never create an unrestricted universal ecosystem key.
- Never let a partner key impersonate a human user.
- Never let an application key escape its application scope.

Review whether OAuth 2.0 client credentials is more appropriate than custom long-lived API keys for service-to-service authentication. If both are retained, define distinct use cases and a migration path.

# 8. PHASE 3 — CLERK IDENTITY PROVIDER AND LOCAL DATABASE SYNCHRONIZATION

Clerk is the interactive identity provider. NEXUS remains the authorization and tenant-data authority.

## 8.1 Frontend

- Integrate the official Clerk React SDK using supported APIs.
- Use environment-specific publishable keys.
- Do not expose Clerk secret keys to the browser.
- Support sign-in, sign-out, session refresh, protected routes and error states.
- Avoid duplicate or conflicting authentication state stores.
- Keep internal NEXUS permission state separate from Clerk identity state.

## 8.2 Backend

- Validate Clerk session JWT signatures using trusted JWKS/public keys.
- Validate issuer, expiry, authorized party/audience as applicable and required subject.
- Support key rotation.
- Resolve Clerk subject to an internal immutable NEXUS user ID.
- Do not trust frontend-provided identity, role, tenant or permission claims.
- Resolve NEXUS memberships and permissions on the server.
- Reject users without valid internal provisioning or permitted onboarding.

## 8.3 Data ownership

Clerk owns the identity attributes explicitly assigned to it by policy.

NEXUS owns:
- internal user ID
- tenant membership
- roles and permissions
- organization assignments
- application access
- entitlement decisions
- internal operational metadata
- audit records

Use Clerk subject as an immutable unique external identity key. Never use email as the sole identity key.

## 8.4 Webhook synchronization

Implement verified Clerk webhook processing:

- Verify Svix signatures using the raw request body.
- Enforce timestamp tolerance.
- Persist event ID before asynchronous side effects.
- Idempotently process duplicates.
- Handle out-of-order events.
- Handle user creation, update, deletion/deactivation and relevant ban/unban events.
- Preserve historical references and audit data.
- Never derive roles, memberships or entitlements from Clerk profile events.
- Provide reconciliation/backfill from Clerk Backend API.
- Expose sync status and actionable errors to authorized operators.

Define ownership and conflict resolution per field. A stale webhook must not overwrite newer local data.

## 8.5 Migration and rollback

- Inventory current users and authentication dependencies.
- Define account linking and duplicate resolution.
- Never auto-link accounts solely on an unverified email.
- Preserve internal user IDs and audit references.
- Support staged migration and rollback.
- Do not remove the old authentication path until the new path is tested and a safe rollback exists.

# 9. PHASE 4 — USER ADMINISTRATION

The Users module is currently too limited. Expand it without mixing profile management with authorization management.

Authorized administrators may inspect safe internal metadata and edit approved profile fields, such as:
- display name
- first/last name
- locale
- timezone
- region/country metadata
- phone, if supported and verified
- account status where policy permits
- administrative notes with restricted access

**The profile update contract must reject role, permission, membership, entitlement and privilege fields**, even if a malicious client submits them.

Role and membership changes remain separate workflows with dedicated permissions and audit.

Provide safe user detail:
- internal user ID
- Clerk subject
- creation/update timestamps
- last sign-in/activity when reliably available
- account and synchronization status
- tenant membership summary
- application access summary
- session summary and revocation controls where supported

Never expose passwords, session tokens, recovery tokens or API secrets. Mask sensitive operational identifiers by default. A privileged reveal action must have a documented legitimate use case, narrow scope, audit and expiry; secret values that are not retrievable by design must never be reconstructed.

# 10. PHASE 5 — RESEND SHARED EMAIL INFRASTRUCTURE

Implement Resend as the initial email provider behind a provider-neutral abstraction.

Required capabilities:
- centralized provider configuration
- verified sending domain and environment-specific sender identities
- template catalog and versioning
- transactional messages
- idempotency/deduplication
- delivery attempts and provider message IDs
- bounded retries with exponential backoff and jitter
- transient/permanent error classification
- bounce/complaint/suppression processing where supported
- signed provider webhook validation
- delivery observability
- secret and personal-data redaction

Every NEXUS module uses the shared email service. No module may instantiate the Resend SDK directly.

No secret keys in source control or frontend bundles. Use secret-manager/environment injection.

Test provider failure, timeout, duplicate requests and webhook replay.

# 11. PHASE 6 — RATE LIMITING AND API MANAGEMENT

Implement layered, distributed rate limiting:

- anonymous traffic by trusted client IP/network
- human requests by user + tenant + policy
- service requests by client + application + tenant
- API credentials by credential + scope
- expensive endpoints with stricter policies
- plan/entitlement-based quotas where explicitly defined

Support:
- per-route policy
- burst and sustained limits
- distributed coordination across API instances
- audited policy changes
- safe trusted-proxy configuration
- Retry-After and correlation IDs
- documented health-check exemptions

Never trust arbitrary X-Forwarded-For values. Avoid unbounded attacker-controlled partition keys.

## API request logs

Provide operational API logs distinct from immutable business audit.

Capture only necessary metadata:
- timestamp
- method and normalized route
- status
- duration
- correlation/trace ID
- application/client identity
- tenant and user references when justified
- rate-limit decision
- sanitized error classification

Never log authorization headers, cookies, passwords, API key values, session tokens, refresh tokens or request/response bodies by default.

Provide authorized query APIs with pagination and filters for time, route, status, application, tenant and correlation ID.

Define retention, indexing, partitioning, redaction and access controls before high-volume persistence. Use OpenTelemetry/log aggregation for full observability; Console request logs should be bounded and queryable.

# 12. PHASE 7 — TENANT CONNECTIONS AND PROVIDER CATALOG

Create provider definitions separately from tenant connection instances.

Provider categories may include:
- Resend/email delivery
- Clerk/identity
- S3-compatible storage
- RabbitMQ-compatible messaging
- FX data sources
- approved external business systems
- future connectors introduced by explicit requirements

Each connection includes:
- provider code/version
- tenant and application scope
- display name/status
- typed non-secret configuration
- secure secret references
- validation/health state
- last successful check
- lifecycle and audit metadata

Provide test, rotate, disable, reconnect and delete operations subject to permissions and dependency checks.

Security:
- no arbitrary unrestricted URL fetcher
- block loopback/private/link-local/metadata destinations
- DNS rebinding protection
- strict TLS validation
- bounded timeouts and response size
- outbound host allowlists where feasible
- credential redaction
- SSRF regression tests

Never store raw secrets in ordinary JSON configuration columns.

# 13. PHASE 8 — WEBHOOK PLATFORM

Treat inbound provider webhooks and outbound NEXUS webhooks as separate systems.

## Outbound customer/partner webhooks

- endpoint registration
- event subscription management
- signing secret generation and one-time display
- HMAC signing and timestamp
- versioned event envelope
- idempotency/event ID
- delivery attempt history
- bounded retry and exponential backoff with jitter
- manual replay/retry with permission
- endpoint disable/revoke
- secret rotation
- tenant/application isolation
- sanitized response metadata
- dead-letter handling and operational controls

Protect delivery against SSRF, DNS rebinding, redirects to private addresses, oversized responses and slow endpoints.

## Inbound provider webhooks

Use separate endpoint registrations, signing verification, provider-specific schemas and inbox/idempotency processing.

# 14. PHASE 9 — REFERENCE DATA AND SELECTABLE FORM FIELDS

Inventory forms and replace free-text inputs with searchable selectors when values belong to controlled vocabularies.

Candidate reference data:
- countries and regions
- timezones
- locales and languages
- ISO currency codes
- common statuses/types/categories
- provider-defined codes
- other stable enumerations discovered in actual forms

Requirements:
- stable machine codes
- localized display labels
- search and keyboard support
- optional values remain optional
- inactive/deprecated values remain readable historically
- controlled seed/version process
- provider-backed data refreshed only where necessary
- do not hard-code world catalogs in React components
- keep genuinely free-form values as text

# 15. PHASE 10 — CURRENCY AND EXCHANGE RATES

Separate currency metadata from rate observations.

Currency:
- ISO 4217 code
- name
- symbol
- minor-unit precision
- active status

Rate:
- base and quote
- decimal rate
- provider/source
- observed time
- fetched time
- freshness/staleness
- source reference

Requirements:
- pluggable provider adapter
- scheduled refresh and throttled on-demand refresh
- caching with explicit TTL/freshness
- source and updated timestamp in UI
- honest stale/error states
- decimal/numeric database type, never binary float for monetary values
- no silent fallback to stale rates as current
- distinguish indicative FX reference rates from settlement/execution rates
- do not claim real-time rates unless the provider actually supplies them

# 16. PHASE 11 — SUBSCRIPTION AND ENTITLEMENT GOVERNANCE

Subscription plan administration must support safe deletion semantics.

- If a plan has no references or dependents, authorized hard deletion may be allowed.
- If referenced by subscriptions, invoices, usage or entitlements, archive/deactivate instead.
- Preserve historical plan snapshots.
- Require explicit permission and confirmation.
- Audit actor, timestamp, reason and impact.
- Never cascade-delete customer subscription history.
- Ensure entitlement evaluation is deterministic and testable.
- Keep plan configuration distinct from application-specific business pricing logic.

# 17. PHASE 12 — REAL-TIME PLATFORM EXPERIENCE

Do not equate real-time with constant polling.

Select transports by purpose:

- SignalR/WebSockets or SSE for live Console operations and state updates.
- RabbitMQ/domain events for internal asynchronous propagation.
- Outbound webhooks for partner notifications.
- Query invalidation/refetch after mutations.
- Scheduled jobs for FX and external catalogs.

Requirements:
- tenant- and permission-aware subscriptions
- reconnect/resume strategy
- bounded buffers/backpressure
- event ordering expectations documented
- no sensitive broadcast to unauthorized clients
- eventual consistency visible in UI
- polling as a documented fallback only

# 18. PHASE 13 — AI PLATFORM READINESS

Build safe AI extensibility, not an uncontrolled autonomous agent.

Core may provide:
- provider/model registry
- AI capability registry
- tenant-level enablement and entitlement checks
- model/provider configuration references
- prompt/template version metadata
- usage and cost metering primitives
- request correlation and audit metadata
- policy and data classification hooks
- human approval gates for consequential actions
- provider adapter interface
- rate limits and budget controls

Security:
- never expose provider API keys to the browser
- never send secrets or unnecessary personal data to model providers
- define tenant data boundaries and retention
- treat model outputs and retrieved documents as untrusted
- defend against prompt injection and tool misuse
- use explicit tool allowlists and least privilege
- require human approval for financial, legal, account, permission or destructive operations
- never let model output directly mutate authorization or tenant ownership
- log metadata, not sensitive prompts/responses by default
- provide provider disable/kill switch and budget exhaustion behavior

Do not add an LLM dependency merely for appearance. Implement AI primitives only where they fit the platform architecture and can be tested.

# 19. PHASE 14 — FRONTEND AND CONSOLE EXPERIENCE

The Console must be a real operations interface, not a decorative dashboard.

For every capability provide:
- loading state
- empty state
- error state
- permission-denied state
- stale/offline state where relevant
- pagination/filtering for large collections
- validated forms
- accessible keyboard interactions
- confirmation for destructive operations
- useful server-side error mapping
- optimistic updates only where safe
- query invalidation after mutations

Required management surfaces:
- Applications and installations
- Tenants and memberships
- Roles and permission scopes
- Service clients and API credentials
- Users and profile administration
- Clerk synchronization status
- Email provider and delivery status
- Rate-limit policies
- API request logs
- Provider catalog and tenant connections
- Webhook endpoints and delivery history
- Reference data and currency
- FX provider health and freshness
- Subscription plans and entitlements
- Real-time operations and event health
- AI provider/capability governance, if implemented

Secrets:
- Mask by default.
- Show generated secrets once.
- Never pretend a masked irreversible secret can be revealed.
- Require permission and audit for sensitive operational actions.

Frontend route gates improve UX only. Backend policies remain authoritative.

# 20. PHASE 15 — DATABASE, MIGRATION AND DEPLOYMENT HARDENING

Review every EF Core context, migration, schema, RLS policy, database role and connection path.

- Runtime application DB role must be least-privileged, NOSUPERUSER and NOBYPASSRLS.
- Migration role is separate from runtime role.
- Migration execution must be one-shot/serialized in deployment.
- API replicas must not race migrations.
- Migration failure must prevent deployment from being declared healthy.
- Preserve historical data and audit records.
- Test fresh database bootstrap and upgrade from supported prior schema.
- Do not delete volumes or reset data to resolve migration errors.
- Remove development credentials from production paths.
- Validate required environment variables at startup.
- Protect production secrets.
- Validate CORS and trusted proxy settings.
- Restrict production OpenAPI/Scalar exposure according to policy.
- Define backup, restore and disaster-recovery runbooks.
- Test restore in an isolated environment.
- Document Redis/RabbitMQ persistence and failure behavior.
- Ensure object storage policies and encryption are appropriate.

Review the known Docker/bootstrap mismatch:
- init SQL hardcodes database/user passwords.
- Compose environment values may not flow into SQL initialization.
- custom database names may conflict with hardcoded grants.
- API migration opt-in is not necessarily a dedicated one-shot migration process.

Fix safely with parameterized/secure initialization. Do not interpolate untrusted values into shell or SQL unsafely.

Review AccessControl RLS carefully:
- Role has global/system/platform and tenant semantics.
- UserRole has tenant assignments and potentially platform assignments.
- RolePermission references roles and global permission definitions.
- Do not blindly tenant-filter global role catalogs.
- Protect tenant role visibility and assignments.
- Test direct SQL under the actual runtime role with RLS enabled.
- Test pooled connection context isolation and reset.
- Review every platform bypass path individually.

# 21. PHASE 16 — AUDIT, OBSERVABILITY AND RELIABILITY

Separate:
- immutable business/security audit
- operational request logs
- distributed traces
- metrics
- provider delivery history

Use correlation IDs across HTTP, background jobs, messages and webhook delivery.

Requirements:
- useful health/live and health/ready semantics
- dependency health without leaking secrets
- structured logs
- bounded metric cardinality
- trace propagation
- retry classification
- dead-letter observability
- background job monitoring
- graceful shutdown
- cancellation propagation
- timeout budgets
- resilience policies
- incident runbooks

Never log raw credentials or sensitive payloads.

# 22. PHASE 17 — TESTING WAR ROOM

Build and run tests at multiple levels.

## Backend
- unit tests
- integration tests
- API contract tests
- database migration tests
- PostgreSQL RLS tests using the real runtime role
- tenant isolation tests
- application-scope isolation tests
- authorization regression tests
- webhook signature/idempotency tests
- API key rotation/revocation tests
- rate-limit distributed tests
- email provider failure tests
- FX freshness tests
- subscription deletion/history tests
- background job retry tests

## Frontend
- typecheck
- lint
- unit/component tests
- route authorization tests
- form validation tests
- API error/empty/loading tests
- secret masking tests
- accessibility checks where tooling exists
- production build

## Security
Test at minimum:
- cross-tenant access
- cross-application API key reuse
- IDOR/BOLA
- privilege escalation
- missing permission context
- forged Clerk token
- invalid webhook signature
- webhook replay
- SSRF and DNS rebinding
- secret leakage
- rate-limit bypass
- mass assignment of role fields
- stale event overwrite
- RLS connection-pool leakage

Do not use SQLite as a substitute for PostgreSQL-specific RLS verification.

## CI
Ensure CI runs the actual backend and frontend quality gates:
- restore
- format verification
- build
- analyzers/vulnerability checks
- backend tests
- npm ci
- frontend typecheck
- frontend lint
- frontend tests
- frontend production build

Report exact commands, exit codes and relevant summaries.

# 23. PHASE 18 — DOCUMENTATION AND PLATFORM CONTRACT FREEZE

Create or update:
- architecture overview
- module ownership map
- ERD/schema overview
- application/tenant/role scope model
- authentication and Clerk migration runbook
- API key/service identity guide
- API versioning and error conventions
- rate-limit policy guide
- API log retention/access policy
- Resend configuration and email operations guide
- provider connection security guide
- inbound/outbound webhook guide
- FX provider/freshness guide
- real-time event contracts
- AI governance and data-handling guide
- deployment/migration/rollback runbooks
- backup/restore runbook
- threat model
- ADRs for consequential architectural decisions
- release checklist and known limitations

Freeze policy:
- No NEXUS F/E/M domain business logic added to C.
- No breaking public contract without versioning and approval.
- Reopen the frozen core only for critical security, data-integrity, reliability defects or a demonstrably missing shared platform capability.
- Future work should be backward-compatible maintenance and platform evolution.

# 24. EXECUTION CONTROL — HOW TO WORK

Work in the following cycle for each capability:

1. Inspect actual implementation.
2. Record confirmed gaps and evidence.
3. Define minimal correct architecture.
4. Write/update ADR if decision is consequential.
5. Implement backend/domain/database first.
6. Add migrations and rollback considerations.
7. Add authorization and threat controls.
8. Add tests before declaring behavior stable.
9. Implement frontend against real API contracts.
10. Run targeted tests.
11. Run full relevant quality gates.
12. Inspect git diff for accidental changes, secrets and generated artifacts.
13. Update documentation and capability status.
14. Commit a small coherent unit.

After each phase:
- summarize files changed
- migrations added
- APIs changed
- tests executed
- risks remaining
- next dependency

Do not ask the user questions that can be answered by inspecting the repository. If an external credential is unavailable, build the provider abstraction, configuration validation, mock contract tests and safe disabled state; mark live provider verification BLOCKED rather than fabricating success.

Ask for explicit human approval before:
- production deployment
- destructive database operations
- irreversible user/account migration
- deleting referenced data
- merging to main
- pushing a release tag
- changing externally consumed breaking contracts
- enabling a consequential AI action in production

You may create commits on the assigned feature branch. Do not merge or release without approval.

# 25. PRIORITY ORDER AND DEPENDENCY GRAPH

Follow this order unless repository evidence proves a dependency requires adjustment:

1. Repository audit and baseline.
2. Canonical application/tenant/role/permission model.
3. Cross-tenant and RLS security fixes.
4. Service identities and scoped API credentials.
5. User profile administration.
6. Clerk authentication, local mapping and webhook sync.
7. Shared Resend email abstraction.
8. Rate limiting and API operational logs.
9. Provider catalog and secure tenant connections.
10. Outbound webhook lifecycle and inbound provider inbox.
11. Reference data and selectable form controls.
12. Currency and FX provider integration.
13. Subscription deletion/archival safeguards.
14. Real-time operations.
15. AI platform governance primitives.
16. Console integration and operational UX.
17. Deployment, observability and recovery hardening.
18. Full security regression and release verification.
19. Platform contract freeze.

Parallelize only independent work. Do not parallelize conflicting migrations or edits to shared foundational contracts without coordination.

# 26. RELEASE GATES

NEXUS C 1.0 cannot be declared ready unless all applicable gates pass.

## Gate A — Identity and authorization
- Clerk JWT verification works.
- Internal user mapping is deterministic.
- Roles and permissions are server-authoritative.
- Tenant and application isolation pass.
- Cross-tenant escalation regression passes.
- API credentials are scoped, hashed, rotatable and revocable.
- RLS verified with real runtime DB role.

## Gate B — Shared platform services
- Resend abstraction and delivery tracking work.
- Rate limiting and API logs are secure.
- Tenant connections are isolated and secret-safe.
- Webhooks are signed, idempotent, retryable and SSRF-protected.
- Reference data and FX freshness are explicit.
- Subscription history is preserved.

## Gate C — Console and developer experience
- Real APIs power the UI.
- No fake production data.
- Permission states fail closed.
- User profile and role management are separate.
- Operational surfaces have loading/error/empty states.
- Forms use appropriate controlled selectors.

## Gate D — Reliability and operations
- Migrations are safe and serialized.
- Runtime DB role is least privilege.
- Health and observability work.
- Backup/restore process is documented and tested.
- CI runs backend and frontend gates.
- Failure/retry behavior is bounded and observable.

## Gate E — AI governance and platform freeze
- AI integration is optional, controlled and tenant-aware if implemented.
- No provider secrets reach clients.
- Consequential actions require policy and human approval.
- Platform contracts and module boundaries are documented.
- Known limitations are explicit.

A gate may be marked NOT APPLICABLE only with a clear evidence-based reason.

# 27. FINAL REPORT FORMAT

At completion, produce:

1. Executive summary.
2. Actual final commit and branch.
3. Architecture changes.
4. Capability matrix with statuses.
5. Security findings before/after.
6. Database migrations and compatibility notes.
7. API endpoints/contracts added or changed.
8. Frontend surfaces added or changed.
9. Provider integrations and live verification status.
10. Tests run with exact results.
11. CI status.
12. Deployment/rollback instructions.
13. Known limitations and accepted risks.
14. Deferred work and reasons.
15. Explicit statement whether the core is safe to freeze.

Final status must be exactly one:

- READY FOR NEXUS C 1.0 RELEASE
- READY WITH DOCUMENTED LIMITATIONS
- NOT READY — RELEASE BLOCKED

Do not choose READY based on effort, code volume or optimism. Choose it only from evidence.

# 28. FINAL DIRECTIVE

Build the foundation that every NEXUS application can trust.

NEXUS C must become the single coherent platform layer for identity, tenancy, authorization, integration, communication, governance, observability and controlled AI extensibility.

It must be secure across tenants, isolated across applications, predictable under failure, maintainable by future engineers and usable by administrators.

Do not optimize for the number of completed checkboxes. Optimize for correctness, clarity, safety and long-term platform stability.

**Audit deeply. Architect deliberately. Implement completely. Test adversarially. Document honestly. Freeze only when the evidence says it is ready.**

The next phase after an approved NEXUS C 1.0 freeze is the development of NEXUS M on top of the stable platform contract.
