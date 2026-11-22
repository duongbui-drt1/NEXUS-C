# NEXUS C 1.0 — Authoritative Platform Contract

**Contract Version:** `1.0.0` (Frozen Foundation)  
**Owner:** NEXUS C — Center  
**Consumers:** NEXUS F (Finance), NEXUS E (Express), NEXUS M (Management), NEXUS C Console, and authorized partner integrations.  
**Base URL Prefix:** `/api/v1`

---

## 1. Contract Governance & Compatibility Rules

1. **URI Versioning:** All stable HTTP endpoints are prefixed with `/api/v1/`. Breaking changes require `/api/v2/`, an ADR, and a migration deprecation window.
2. **Additive Backward Compatibility:** Adding optional request fields, optional response properties, or new event types is backward-compatible. Consumers MUST ignore unknown JSON fields.
3. **Standard Success Envelope (`ApiResponse<T>`):**
   ```json
   {
     "success": true,
     "data": { },
     "meta": {
       "correlationId": "019a2f7b-..."
     }
   }
   ```
4. **Standard Error Envelope (RFC 9457 `application/problem+json`):**
   ```json
   {
     "type": "https://errors.nexus.internal/TENANT_SUSPENDED",
     "title": "Forbidden",
     "status": 403,
     "detail": "This tenant is currently suspended. Business operations are disabled.",
     "errorCode": "TENANT_SUSPENDED",
     "correlationId": "019a2f7b-..."
   }
   ```
5. **Pagination Contract (`PagedResult<T>`):**
   Query parameters: `page` (1-based, default `1`), `pageSize` (default `20`, capped server-side), optional `search`, `sortBy`, `sortDescending`.
   ```json
   {
     "items": [],
     "page": 1,
     "pageSize": 20,
     "totalCount": 42,
     "totalPages": 3,
     "hasNext": true,
     "hasPrevious": false
   }
   ```
6. **Correlation & Distributed Tracing:**
   - Send `X-Correlation-Id` on every request; NEXUS C echoes it in response headers, `meta.correlationId`, audit entries, outbox events, and OpenTelemetry spans.
   - Standard W3C `traceparent` headers are propagated automatically by ASP.NET Core & OpenTelemetry.
7. **Idempotency:**
   - State-changing `POST`/`PUT`/`PATCH` requests may pass `Idempotency-Key` (max 128 chars). Identical replays within 24 hours return the original response with header `X-Idempotent-Replayed: true`; mismatched payloads return HTTP `409 IDEMPOTENCY_PAYLOAD_MISMATCH`.

---

## 2. Human Authentication & Session Lifecycle

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /api/v1/auth/login` | Anonymous (Rate-limited) | Authenticates user, returns short-lived JWT (`accessToken`) + rotating opaque `refreshToken`. Optional header `X-Nexus-Auth-Mode: cookie` sets `HttpOnly; SameSite=Strict` `__nexus_rt` cookie. |
| `POST /api/v1/auth/refresh` | Anonymous (Rate-limited) | Rotates refresh token atomically (`FOR UPDATE`). Re-presenting a rotated/revoked token triggers family-wide & session revocation (`401 AUTH_REFRESH_TOKEN_REUSE`). Cookie refresh requires `X-Nexus-CSRF: 1`. |
| `POST /api/v1/auth/logout` | Bearer | Revokes active session & all session refresh tokens immediately. Cookie logout requires `X-Nexus-CSRF: 1`. |
| `POST /api/v1/auth/switch-tenant` | Bearer | Verifies active membership and non-suspended/non-archived status, revokes prior tenant refresh tokens on the session, and issues tenant-bound JWT + refresh token. |
| `GET /api/v1/auth/me` | Bearer | Returns verified identity claims (`userId`, `email`, `tenantId`, `sessionId`, `scope`, `isPlatformUser`, `isServicePrincipal`, `serviceClientId`). |
| `GET /api/v1/auth/sessions` / `DELETE /api/v1/auth/sessions/{sessionId}` | Bearer | Inspect and revoke own active sessions. |

---

## 3. Service-to-Service Identity (OAuth 2.0 Client Credentials & Scoped API Keys)

Member applications (**NEXUS F, E, M**) MUST NEVER share a superuser token. Each application obtains a dedicated `ServiceClient` with explicit least-privilege scopes (wildcards `*` are rejected with `403 WILDCARD_SCOPE_FORBIDDEN`).

### 3.1 Token Exchange (`POST /api/v1/auth/token`)
**Request:**
```json
{
  "grantType": "client_credentials",
  "clientId": "nexus-f-service",
  "clientSecret": "nxs_...",
  "scope": "platform.organizations.read platform.subscriptions.read",
  "tenantId": "01a1ffb8-4b7d-7bfe-8149-2681b37fa5a7"
}
```
**Response (`200 OK`):**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIs...",
    "tokenType": "Bearer",
    "expiresInSeconds": 600,
    "expiresAt": "2026-11-22T16:40:00Z",
    "clientId": "nexus-f-service",
    "tenantId": "01a1ffb8-4b7d-7bfe-8149-2681b37fa5a7",
    "scopes": ["platform.organizations.read", "platform.subscriptions.read"]
  },
  "meta": { "correlationId": "..." }
}
```
- Tokens carry `scope: "service"`, `client_id`, `app_code`, optional `tid`, and explicit `scp` claims.
- Every request checks live client status; revoking a client (`POST /api/v1/platform/api-clients/{id}/revoke`) invalidates in-flight tokens immediately (`401 AUTH_SERVICE_CLIENT_REVOKED`).

### 3.2 Scoped API Keys (`X-Api-Key`)
- Managed via `GET/POST /api/v1/platform/api-keys`, `POST /api/v1/platform/api-keys/{id}/rotate`, `DELETE /api/v1/platform/api-keys/{id}`.
- Stored exclusively as one-way SHA-256 hashes; raw key returned ONCE at creation/rotation.
- Evaluated against explicit `AllowedScopes` and bound `TenantId` — never bypasses RBAC.

---

## 4. Tenant Context, Organization Context & Authorization

### 4.1 Tenant Context Rules
- For human tokens, `tid` claim is authoritative. Sending an `X-Tenant-Id` / `Tenant-ID` header that conflicts with `tid` fails immediately with `403 TENANT_SPOOFING_DETECTED`.
- For tenant-bound service clients, attempting to override the bound tenant returns `403 TENANT_SPOOFING_DETECTED`.
- Suspended (`403 TENANT_SUSPENDED`) and Archived (`403 TENANT_ARCHIVED`) tenants are blocked from all business endpoints.

### 4.2 Effective Permissions & Organizational Scope
- **`GET /api/v1/auth/permissions`** — Returns authoritative roles and effective permission codes for the authenticated principal in the active Platform or Tenant context.
- **`GET /api/v1/auth/organization-scope`** — Returns the user's effective `legalEntityIds`, `branchIds`, and `organizationUnitIds` (including active descendant subtree) in the current tenant.
- **`POST /api/v1/auth/authorize`** — Evaluates RBAC + Explicit Deny + Resource/Organization scope (`Platform`, `Tenant`, `LegalEntity`, `Branch`, `OrganizationUnit`, `Resource`):
  ```json
  // Request
  { "permissionCode": "platform.organizations.read", "scopeType": "Branch", "scopeId": "<branch-guid>" }
  // Response data
  { "allowed": false, "reason": "ORGANIZATION_SCOPE_DENIED", "explicitDeny": false }
  ```

---

## 5. Ecosystem Application Registry & Authoritative Entitlements

| Endpoint | Permission | Purpose |
|---|---|---|
| `GET /api/v1/platform/applications` | `platform.applications.read` | Lists registered ecosystem applications (`NEXUS_C`, `NEXUS_F`, `NEXUS_E`, `NEXUS_M`) with versions, channels, capabilities, and dependencies. |
| `POST /api/v1/platform/applications` / `PUT /api/v1/platform/applications/{code}` | `platform.applications.manage` | Registers or updates application compatibility metadata (Platform-only). |
| `GET /api/v1/platform/applications/tenant` | `platform.applications.read` | Returns activation and entitlement status of all ecosystem apps for the active tenant. |
| `POST /api/v1/platform/applications/{code}/activate` & `/deactivate` | `platform.applications.manage` | Activates/deactivates an application for a tenant after validating subscription entitlement and module dependencies. |
| `GET /api/v1/platform/entitlements/{featureCode}` | `platform.subscriptions.read` | Authoritative entitlement evaluation combining Subscription status, Entitlement expiry, Tenant Application activation, Feature Flag kill-switch, and Usage quota. |
| `GET /api/v1/platform/entitlements` | `platform.subscriptions.read` | Lists all authoritative entitlement evaluations for the active tenant. |
| `POST /api/v1/platform/subscriptions/usage` & `GET /usage` | `platform.subscriptions.read` | Idempotent usage metering (`idempotencyKey`) and summary query. |

---

## 6. Shared Workflow Engine (`/api/v1/platform/workflows`)

- `POST/GET /definitions`, `POST /definitions/{id}/versions` (immutable versioning — in-progress instances stay pinned to their original `VersionId`), `POST /definitions/{id}/activate|deactivate`.
- `POST/GET /instances`, `GET /instances/{id}`, `GET /instances/{id}/history`, `POST /instances/{id}/escalate`, `POST /instances/{id}/cancel`.
- `POST /tasks/{taskId}/decide` (`FOR UPDATE` row lock, Maker–Checker enforcement, Sequential order check, Role + active unexpired Delegation eligibility check), `POST /tasks/{taskId}/delegate`.
- `POST/GET /delegations`, `DELETE /delegations/{id}` (explicit validity window `validFrom`..`validTo`).

---

## 7. Event Contracts & Platform Operations

### 7.1 Event Envelope (`EventEnvelope`)
```json
{
  "eventId": "01a1ff...",
  "eventType": "platform.workflow.approved.v1",
  "tenantId": "01a1ff...",
  "occurredAt": "2026-11-22T16:30:00Z",
  "correlationId": "corr-123",
  "schemaVersion": 1,
  "producer": "nexus-c",
  "causationId": "cmd-456",
  "payload": { }
}
```
Published to RabbitMQ topic exchange `nexus.events` (routing key = `eventType`, DLX = `nexus.events.dlx`) via Transactional Outbox (`FOR UPDATE SKIP LOCKED`) with exponential backoff and DLQ routing.

**Frozen Platform Event Types (v1):**
- Tenancy: `platform.tenant.created.v1`, `platform.tenant.updated.v1`, `platform.tenant.suspended.v1`, `platform.tenant.activated.v1`
- Identity: `platform.user.created.v1`, `platform.user.invited.v1`, `platform.user.disabled.v1`, `platform.service_client.created.v1`, `platform.service_client.revoked.v1`
- Subscription & Applications: `platform.subscription.activated.v1`, `platform.subscription.cancelled.v1`, `platform.subscription.expired.v1`, `platform.application.activated.v1`, `platform.application.deactivated.v1`
- Workflow: `platform.workflow.submitted.v1`, `platform.workflow.approved.v1`, `platform.workflow.rejected.v1`, `platform.workflow.cancelled.v1`, `platform.workflow.delegated.v1`, `platform.workflow.escalated.v1`

### 7.2 Operations Endpoints (`/api/v1/platform/operations`)
- `GET /outbox`, `GET /inbox`, `GET /dead-letters`, `GET /jobs` (`platform.operations.read`)
- `POST /dead-letters/{id}/retry` (`platform.operations.manage` — audited, loop-protected max 3 manual retries)

---

## 8. Audit, Configuration, Notifications, Files & Integrations

- **Audit (`/api/v1/platform/audit`):** Strictly read-only HTTP (`GET` only). Append-only PostgreSQL trigger + `SELECT, INSERT`-only grant + `FORCE ROW LEVEL SECURITY`.
- **Configuration (`/api/v1/platform/configuration`):** Global (`global`), Tenant (`tenant`), History (`history`), and Feature Flags (`feature-flags`). Rejects plaintext secret-like keys (`422 CONFIG_SECRET_FORBIDDEN`).
- **Notifications (`/api/v1/platform/notifications`):** Templates (`POST/GET /templates`), Send (`POST /send` — `InApp`, `Email`, `Webhook` with SSRF guard), Retry (`POST /{id}/retry`), Inbox (`GET /`, `POST /{id}/read`), User Preferences (`GET/PUT /preferences`).
- **File Storage (`/api/v1/platform/files`):** Tenant-scoped upload (allowlisted MIME types, SHA-256 hash, optional `retentionDays`), metadata, download, HMAC-signed expiring temporary download URL, soft delete (all audited).
- **Integrations & Webhooks (`/api/v1/platform/integrations`):** Providers, Connections (external secret references `vault://`/`kms://`/`env://` only), Sync Logs, Errors, Full Webhook Lifecycle (`POST/GET/PUT/DELETE /webhooks`, `/enable`, `/disable`, `/rotate-secret`, `/deliveries`, `/deliveries/{id}/retry`, HMAC-SHA256 `X-Nexus-Signature` + `X-Nexus-Timestamp` replay protection + SSRF blocking), AI Gateway (`POST /ai/invoke` entitlement-gated).
