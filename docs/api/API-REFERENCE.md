# NEXUS C 1.0 Foundation — API Reference (`/api/v1`)

All endpoints return:
- **Success:** `{ "success": true, "data": { ... }, "meta": { "correlationId": "..." } }`
- **Error (RFC 9457 `application/problem+json`):** `{ "type": "...", "title": "...", "status": 4xx/5xx, "detail": "...", "errorCode": "STABLE_CODE", "correlationId": "..." }`
- **Headers:**
  - `X-Correlation-Id` (propagated request/response trace ID)
  - `Idempotency-Key` (optional on POST/PUT/PATCH mutations; PostgreSQL-backed replay & payload-hash check)
  - `X-Api-Key` (optional scoped API key authentication for machine-to-machine calls)
  - `X-Nexus-Auth-Mode: cookie` + `X-Nexus-CSRF: 1` (optional `HttpOnly; SameSite=Strict` refresh cookie mode)
- **Interactive Spec:** `GET /openapi/v1.json` and `GET /scalar` (when `OpenApi:Enabled=true` or in Development).
- **Frozen Ecosystem Contract:** See [PLATFORM-CONTRACT.md](../architecture/PLATFORM-CONTRACT.md).

---

## Endpoints Inventory (Implemented & Tested)

### Health Probes (Anonymous, Sanitized)
- `GET /health/live` — Process liveness probe
- `GET /health/ready` — Readiness probe including PostgreSQL connectivity
- `GET /health/dependencies` — Sanitized JSON dependency health matrix (zero credentials or connection strings)

### Authentication, Policy & Service Identity (`/api/v1/auth`)
- `POST /api/v1/auth/login` — Authenticate with email/password (+ optional `tenantId`); supports `X-Nexus-Auth-Mode: cookie`
- `POST /api/v1/auth/refresh` — Rotate refresh token (`FOR UPDATE` lock + reuse detection; requires `X-Nexus-CSRF: 1` for cookie mode)
- `POST /api/v1/auth/token` — OAuth 2.0 `client_credentials` token issuance (`scope=service`, 10-min TTL)
- `POST /api/v1/auth/logout` — Revoke active session and refresh tokens (requires `X-Nexus-CSRF: 1` when cookie present)
- `POST /api/v1/auth/switch-tenant` — Switch tenant after verifying active membership & non-suspended/archived lifecycle; revokes prior tenant refresh tokens
- `POST /api/v1/auth/change-password` — Change authenticated user's password
- `POST /api/v1/auth/reset-password` — Complete password reset and revoke all sessions
- `POST /api/v1/auth/verify-email` — Confirm email verification token (rate-limited)
- `GET /api/v1/auth/sessions` — List authenticated user's sessions
- `DELETE /api/v1/auth/sessions/{sessionId}` — Revoke a specific session
- `GET /api/v1/auth/me` — Inspect current authenticated human or service principal claims
- `GET /api/v1/auth/permissions` — Effective roles & permissions (or service scopes) in active Platform or Tenant scope
- `GET /api/v1/auth/organization-scope` — Effective Legal Entity, Branch, and active Organization Unit descendant subtrees in current tenant
- `POST /api/v1/auth/authorize` — Authoritative RBAC/ABAC/Org-scoped policy evaluation (`Allow`/`Deny`, resource scope)

### Tenants (`/api/v1/platform/tenants`)
- `POST /api/v1/platform/tenants` (`platform.tenants.create`)
- `GET /api/v1/platform/tenants` (`platform.tenants.read` — pagination, search, status filter, sort)
- `GET /api/v1/platform/tenants/accessible` (authenticated user's active tenants)
- `GET /api/v1/platform/tenants/{tenantId}` (`platform.tenants.read`)
- `PUT /api/v1/platform/tenants/{tenantId}` (`platform.tenants.update` — optimistic concurrency via `expectedRowVersion`)
- `POST /api/v1/platform/tenants/{tenantId}/suspend` (`platform.tenants.suspend`)
- `POST /api/v1/platform/tenants/{tenantId}/activate` (`platform.tenants.suspend`)
- `POST /api/v1/platform/tenants/{tenantId}/memberships/{userId}` (`platform.users.manage`)
- `DELETE /api/v1/platform/tenants/{tenantId}/memberships/{userId}` (`platform.users.manage`)

### Organizations (`/api/v1/platform/organizations`)
- `POST/GET /api/v1/platform/organizations/legal-entities` & `GET/PUT/DELETE /{id}`
- `POST/GET /api/v1/platform/organizations/branches` & `GET/PUT/DELETE /{id}`
- `POST/GET /api/v1/platform/organizations/units` & `PUT /{id}` (cycle-checked hierarchy + status)
- `POST/GET /api/v1/platform/organizations/assignments` (enforces active tenant membership) & `DELETE /assignments/{assignmentId}`

### Users, Roles & Service Identity (`/api/v1/platform/users`, `/roles`, `/api-clients`, `/api-keys`)
- **Users:** `POST/GET /api/v1/platform/users`, `POST /invite`, `POST /{userId}/disable`, `POST /{userId}/enable`, `DELETE /{userId}/sessions/{sessionId}`
- **Roles & Policy:** `GET /api/v1/platform/roles/permissions`, `GET/POST /api/v1/platform/roles`, `POST /assign` (supports `Effect: Allow|Deny` & `ResourceScopeType`/`ResourceScopeId`), `DELETE /assignments/{userRoleId}`, `GET /users/{userId}`
- **OAuth 2.0 Service Clients:** `GET/POST /api/v1/platform/api-clients`, `POST /api-clients/{id}/rotate-secret`, `POST /api-clients/{id}/revoke`
- **Scoped API Keys:** `GET/POST /api/v1/platform/api-keys`, `POST /api-keys/{id}/rotate`, `DELETE /api-keys/{id}`

### Ecosystem Application Registry, Subscriptions & Entitlements
- **Application Registry (`/api/v1/platform/applications`):**
  - `GET /api/v1/platform/applications` (`platform.applications.read`) — Pre-seeded with `NEXUS_C`, `NEXUS_F`, `NEXUS_E`, `NEXUS_M`
  - `POST /api/v1/platform/applications`, `PUT /api/v1/platform/applications/{code}` (`platform.applications.manage`)
  - `GET /api/v1/platform/applications/tenant` (`platform.applications.read`)
  - `POST /api/v1/platform/applications/{code}/activate` & `/deactivate` (`platform.applications.manage`)
- **Entitlements (`/api/v1/platform/entitlements`):**
  - `GET /api/v1/platform/entitlements` & `GET /api/v1/platform/entitlements/{featureCode}` (`platform.subscriptions.read` — evaluates subscription validity + entitlement + tenant activation + feature flag kill-switch + usage quota)
- **Subscriptions (`/api/v1/platform/subscriptions`):**
  - `POST/GET /plans`, `POST /assign`, `POST /{tenantId}/status`, `GET /current`, `POST/GET /usage` (idempotent usage recording)

### Governance, Operations & Platform Services
- **Audit (Strictly Read-Only):** `GET /api/v1/platform/audit` (`platform.audit.read`)
- **Platform Operations (`/api/v1/platform/operations`):**
  - `GET /outbox`, `GET /inbox`, `GET /dead-letters`, `GET /jobs` (`platform.operations.read`)
  - `POST /dead-letters/{id}/retry` (`platform.operations.manage` — loop-capped & audited)
- **Workflows (`/api/v1/platform/workflows`):**
  - `POST/GET /definitions`, `POST /definitions/{id}/versions`, `POST /definitions/{id}/activate`, `POST /definitions/{id}/deactivate`
  - `POST/GET /instances`, `GET /instances/{id}`, `GET /instances/{id}/history`, `POST /instances/{id}/escalate`, `POST /instances/{id}/cancel`
  - `POST /tasks/{taskId}/decide` (Maker–Checker + sequential ordering + role/delegation eligibility + `FOR UPDATE`), `POST /tasks/{taskId}/delegate`
  - `POST/GET /delegations`, `DELETE /delegations/{delegationId}`
- **Configuration (`/api/v1/platform/configuration`):** `PUT/GET /tenant`, `PUT/GET /global`, `GET /history`, `PUT /feature-flags`, `PUT /feature-flags/global`, `GET /feature-flags`, `GET /feature-flags/{key}/evaluate`
- **Integrations, Webhooks & AI Gateway (`/api/v1/platform/integrations`):**
  - `POST/GET /providers`, `POST/GET /connections`, `POST/GET /connections/{id}/sync-logs`, `GET /errors`
  - `POST/GET /webhooks`, `PUT/DELETE /webhooks/{id}`, `POST /webhooks/{id}/enable`, `POST /webhooks/{id}/disable`, `POST /webhooks/{id}/rotate-secret`, `POST/GET /webhooks/{id}/deliveries`, `POST /webhooks/deliveries/{deliveryId}/retry`
  - `POST /ai/models`, `POST /ai/invoke`
- **Notifications (`/api/v1/platform/notifications`):** `POST/GET /templates`, `POST /send`, `GET /`, `POST /{notificationId}/read`, `POST /{notificationId}/retry`, `GET/PUT /preferences`
- **File Storage (`/api/v1/platform/files`):** `POST /` (multipart + optional `retentionDays`), `GET /`, `GET /{fileId}`, `GET /{fileId}/download`, `POST /{fileId}/temporary-url`, `GET /{fileId}/signed-download`, `DELETE /{fileId}`

---

## Event Contracts (`EventEnvelope` v1)

Canonical envelope published via Transactional Outbox to RabbitMQ topic exchange `nexus.events` (`EventId`, `EventType`, `Producer`, `TenantId`, `OccurredAt`, `CorrelationId`, `CausationId`, `SchemaVersion`, `Payload`):
- **Tenancy:** `platform.tenant.created.v1`, `platform.tenant.updated.v1`, `platform.tenant.suspended.v1`, `platform.tenant.activated.v1`
- **Identity & Service Clients:** `platform.user.created.v1`, `platform.user.invited.v1`, `platform.user.disabled.v1`, `platform.service_client.created.v1`, `platform.service_client.revoked.v1`
- **Subscriptions & Applications:** `platform.subscription.activated.v1`, `platform.subscription.cancelled.v1`, `platform.subscription.expired.v1`, `platform.application.activated.v1`, `platform.application.deactivated.v1`
- **Workflows:** `platform.workflow.submitted.v1`, `platform.workflow.approved.v1`, `platform.workflow.rejected.v1`, `platform.workflow.cancelled.v1`, `platform.workflow.delegated.v1`, `platform.workflow.escalated.v1`
