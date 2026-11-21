# NEXUS C Console — Backend API Integration & Gap Matrix

**Discovery Date:** 2026-11-22  
**Backend Reference:** `NEXUS C Foundation` (`/api/v1`, ASP.NET Core 10, OpenAPI `/openapi/v1.json`)  
**Envelope Contract:**
- Success: `{ "success": true, "data": T, "meta": { "correlationId": "uuid" } }`
- Error (RFC 9457 `application/problem+json`): `{ "type": "...", "title": "...", "status": 4xx/5xx, "detail": "...", "errorCode": "...", "correlationId": "...", "errors"?: Record<string, string[]> }`
- Pagination: `PagedResult<T>` (`items`, `page`, `pageSize`, `totalCount`, `totalPages`, `hasNext`, `hasPrevious`)

---

## 1. Authentication & Session Contract

| Capability | Backend Endpoint | Auth | Status |
|---|---|---|---|
| Login | `POST /api/v1/auth/login` | Anonymous | Available |
| Refresh Token Rotation | `POST /api/v1/auth/refresh` | Anonymous | Available |
| Logout (Revoke Session) | `POST /api/v1/auth/logout` | Bearer JWT | Available |
| Switch Tenant | `POST /api/v1/auth/switch-tenant` | Bearer JWT | Available |
| Change Password | `POST /api/v1/auth/change-password` | Bearer JWT | Available |
| List My Sessions | `GET /api/v1/auth/sessions` | Bearer JWT | Available |
| Revoke My Session | `DELETE /api/v1/auth/sessions/{sessionId}` | Bearer JWT | Available |
| Current Principal | `GET /api/v1/auth/me` | Bearer JWT | Available |
| HttpOnly Cookie Refresh | — | — | **Missing** (Backend currently returns JSON tokens; Console stores tokens in memory only — never `localStorage`) |

---

## 2. Feature-to-Backend-Endpoint Matrix

| Console Area | Frontend Feature | Actual Backend Endpoint(s) | Required Permission | Status |
|---|---|---|---|---|
| **Overview** | Liveness / Readiness Health | `GET /health/live`, `GET /health/ready` | Anonymous | **Available** |
| **Overview** | Tenant & User Counts, Recent Audit, Feature Flags | `GET /api/v1/platform/tenants`, `GET /api/v1/platform/users`, `GET /api/v1/platform/audit`, `GET /api/v1/platform/configuration/feature-flags` | Read permissions | **Available** |
| **Overview** | Event Bus / Background Job / Queue Backlog / Alerts | — | — | **Missing** (UI renders honest "Not Available" state) |
| **Core — Tenants** | List, Search, Filter, Sort, Detail, Create, Update (`expectedRowVersion`), Suspend, Activate, Memberships, Accessible | `/api/v1/platform/tenants`, `/{tenantId}`, `/{tenantId}/suspend`, `/{tenantId}/activate`, `/{tenantId}/memberships/{userId}`, `/accessible` | `platform.tenants.*`, `platform.users.manage` | **Available** |
| **Core — Organizations** | Legal Entities, Branches, Organization Units, User Assignments | `/api/v1/platform/organizations/legal-entities`, `/branches`, `/units`, `/assignments` | `platform.organizations.*` (Tenant scope) | **Available** |
| **Core — Users** | List, Search, Create, Invite, Disable, Enable, Revoke Session | `/api/v1/platform/users`, `/invite`, `/{userId}/disable`, `/{userId}/enable`, `/{userId}/sessions/{sessionId}` | `platform.users.*` | **Available** |
| **Core — Access Control** | Permissions Catalog, Roles List/Create, Assign/Revoke/List User Roles | `/api/v1/platform/roles`, `/permissions`, `/assign`, `/assignments/{userRoleId}`, `/users/{userId}` | `platform.roles.*` | **Available** |
| **Applications** | NEXUS F / E / M Entitlements & Feature Flags | `/api/v1/platform/subscriptions/plans`, `/current`, `/entitlements/{featureCode}`, `/assign`, `/api/v1/platform/configuration/feature-flags` | `platform.subscriptions.*`, `platform.configuration.*` | **Partially Available** |
| **Applications** | Application Registry / Service Version Catalog | — | — | **Missing** |
| **API Management — Overview** | API Health + OpenAPI Endpoint Count & Version | `GET /health/ready`, `GET /openapi/v1.json` | OpenAPI enabled | **Partially Available** |
| **API Management — Overview** | Request Volume, Error Rate, p95 Latency Telemetry | — | — | **Missing** |
| **API Management — Explorer** | OpenAPI 3.x Spec Browser (Endpoints, Schemas, Params, Safe GET Execution) | `GET /openapi/v1.json` | OpenAPI enabled | **Available** |
| **API Management — Clients** | OAuth / Machine API Clients CRUD | — | — | **Missing** |
| **API Management — API Keys** | Create / One-Time Secret / Rotate / Revoke API Keys | — | — | **Missing** |
| **API Management — Rate Limits** | Dynamic Rate-Limit Policy CRUD & History | — | — | **Missing** |
| **API Management — API Logs** | Queryable HTTP Access & Error Logs | — | — | **Missing** |
| **API Management — Webhooks** | Register Webhook | `POST /api/v1/platform/integrations/webhooks` | `platform.integrations.manage` (Tenant scope) | **Partially Available** (List & delivery history endpoints **Missing**) |
| **Integration Hub** | Providers, Connections, Sync Logs, AI Model Registry | `/api/v1/platform/integrations/providers`, `/connections`, `/connections/{id}/sync-logs`, `/ai/models` | `platform.integrations.*`, `platform.ai.manage` | **Available** (Retry Failed Job endpoint **Missing**) |
| **Operations (Events & Jobs)** | Outbox / Inbox / DLQ Inspection & Manual Retry | — | — | **Missing** |
| **Audit Center** | Server-Paginated Append-Only Audit Query | `GET /api/v1/platform/audit` | `platform.audit.read` | **Available** |
| **Subscriptions** | Plans CRUD, Assign Subscription, Current Subscription, Entitlement Check | `/api/v1/platform/subscriptions/plans`, `/assign`, `/current`, `/entitlements/{featureCode}` | `platform.subscriptions.*` | **Available** (Usage Records query endpoint **Missing**) |
| **System Settings** | Platform Settings, Tenant Settings, Feature Flags, Notifications | `/api/v1/platform/configuration/global`, `/tenant`, `/feature-flags`, `/api/v1/platform/notifications` | `platform.configuration.*`, `platform.notifications.*` | **Available** |

---

## 3. Proposed Backend Follow-Up Tasks (Out of Scope for Frontend Phase)

To unlock the currently unavailable C Console sections without altering backend contracts during this frontend milestone:
1. **API Management Module:** Add `/api/v1/platform/api-clients`, `/api/v1/platform/api-keys` (with hashed key storage and one-time secret return), `/api/v1/platform/rate-limits`, and `/api/v1/platform/api-logs`.
2. **Operations / Messaging Admin API:** Add `/api/v1/platform/operations/outbox`, `/inbox`, `/dead-letters` (with audited manual retry endpoint), and `/jobs`.
3. **Webhooks Query & Delivery History:** Add `GET /api/v1/platform/integrations/webhooks` and `GET /api/v1/platform/integrations/webhooks/{id}/deliveries`.
4. **Application Registry & Usage Read API:** Add `/api/v1/platform/applications` (NEXUS F/E/M release metadata) and `GET /api/v1/platform/subscriptions/usage`.
5. **HttpOnly Cookie Session Mode (Optional):** Support `Set-Cookie: nexus_refresh=...; HttpOnly; Secure; SameSite=Strict` on `/api/v1/auth/login` and `/refresh` for seamless page-reload session persistence without exposing refresh tokens to JavaScript.
