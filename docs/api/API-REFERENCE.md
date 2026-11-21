# NEXUS C Foundation — API Reference (`/api/v1`)

All endpoints return:
- **Success:** `{ "success": true, "data": { ... }, "meta": { "correlationId": "..." } }`
- **Error (RFC 9457 `application/problem+json`):** `{ "type": "...", "title": "...", "status": 4xx/5xx, "detail": "...", "errorCode": "STABLE_CODE", "correlationId": "..." }`
- **Headers:** `X-Correlation-Id` (request/response), `Idempotency-Key` (optional on POST/PUT/PATCH mutations).
- **Interactive Spec:** `GET /openapi/v1.json` and `GET /scalar` (when `OpenApi:Enabled=true`).

---

## Endpoints Inventory (Implemented & Tested)

### Health Probes
- `GET /health/live` — Liveness check (anonymous)
- `GET /health/ready` — Readiness check including PostgreSQL connectivity (anonymous)

### Authentication (`/api/v1/auth`)
- `POST /api/v1/auth/login` — Authenticate with email/password (+ optional `tenantId`), returns JWT + rotating opaque refresh token
- `POST /api/v1/auth/refresh` — Rotate refresh token (transactional `FOR UPDATE` lock + family reuse detection)
- `POST /api/v1/auth/logout` — Revoke active session and its refresh tokens
- `POST /api/v1/auth/switch-tenant` — Mint tenant-bound token after verifying active membership
- `POST /api/v1/auth/change-password` — Change authenticated user's password
- `POST /api/v1/auth/reset-password` — Complete password reset and revoke all sessions
- `POST /api/v1/auth/verify-email` — Confirm email verification token
- `GET /api/v1/auth/sessions` — List authenticated user's sessions
- `DELETE /api/v1/auth/sessions/{sessionId}` — Revoke a specific session
- `GET /api/v1/auth/me` — Inspect current authenticated principal claims

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
- `POST/GET /api/v1/platform/organizations/units` & `PUT /{id}` (cycle-checked hierarchy)
- `POST /api/v1/platform/organizations/assignments`

### Users & Roles (`/api/v1/platform/users`, `/api/v1/platform/roles`)
- `POST/GET /api/v1/platform/users`, `POST /invite`, `POST /{userId}/disable`, `POST /{userId}/enable`, `DELETE /{userId}/sessions/{sessionId}`
- `GET /api/v1/platform/roles/permissions`, `GET/POST /api/v1/platform/roles`, `POST /assign`, `DELETE /assignments/{userRoleId}`, `GET /users/{userId}`

### Governance Services
- **Audit (Read-Only):** `GET /api/v1/platform/audit` (`platform.audit.read`)
- **Workflows:** `POST /definitions`, `POST/GET /instances`, `GET /instances/{id}`, `POST /tasks/{taskId}/decide` (Maker–Checker + `FOR UPDATE`), `POST /instances/{id}/cancel`
- **Subscriptions:** `POST/GET /plans`, `POST /assign`, `GET /current`, `GET /entitlements/{featureCode}`
- **Configuration:** `PUT/GET /tenant`, `PUT/GET /global`, `PUT /feature-flags`, `PUT /feature-flags/global`, `GET /feature-flags`, `GET /feature-flags/{key}/evaluate`

### Integration, Notification & File Storage
- **Integrations & AI Gateway:** `POST/GET /providers`, `POST/GET /connections`, `POST/GET /connections/{id}/sync-logs`, `POST /webhooks`, `POST /ai/models`, `POST /ai/invoke`
- **Notifications:** `POST /templates`, `POST /send`, `GET /`, `POST /{notificationId}/read`
- **File Storage:** `POST /` (multipart upload), `GET /`, `GET /{fileId}`, `GET /{fileId}/download`, `POST /{fileId}/temporary-url`, `GET /{fileId}/signed-download`, `DELETE /{fileId}`

---

## Event Contracts (`EventEnvelope`)

Canonical schema published via Transactional Outbox to RabbitMQ topic exchange `nexus.events`:
- `platform.tenant.created.v1`, `platform.tenant.updated.v1`, `platform.tenant.suspended.v1`, `platform.tenant.activated.v1`
- `platform.user.created.v1`, `platform.user.invited.v1`, `platform.user.disabled.v1`
- `platform.subscription.activated.v1`, `platform.subscription.expired.v1`
- `platform.workflow.approved.v1`, `platform.workflow.rejected.v1`
