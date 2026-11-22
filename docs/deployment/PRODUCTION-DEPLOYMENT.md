# NEXUS C 1.0 — Production Deployment Guide

**Target Platform:** NEXUS C 1.0 Foundation (`ASP.NET Core 10` + `PostgreSQL 17` + `Redis 7.4` + `RabbitMQ 4.0` + `S3/MinIO` + `NEXUS C Console`)

---

## 1. Production Architecture & Role Separation

1. **Bootstrap Provisioning (`infrastructure/database/init-roles.sh`):**
   - Executed once on a fresh PostgreSQL instance (automatically via `/docker-entrypoint-initdb.d/01-init-roles.sh` in Docker Compose).
   - Creates `nexus_migrator` (database owner, `NOSUPERUSER`, DDL/migration privileges) and `nexus_app` (`NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`, DML only, `SELECT + INSERT` only on `audit.audit_events`).
   - Supports custom `POSTGRES_DB` names and environment-supplied passwords with zero hardcoded credentials.
2. **One-Shot Migration Job (`src/Nexus.Migrator` / `Dockerfile.migrator`):**
   - Runs to completion **before** any API replica starts (`depends_on: migrator: condition: service_completed_successfully`).
   - Acquires PostgreSQL session advisory lock (`pg_advisory_lock(74839210)`) to serialize concurrent release pipelines.
   - Applies all 21 EF Core Code-First migrations across 12 schemas, enforces `FORCE ROW LEVEL SECURITY` on 33 tables, installs `trg_audit_events_immutable`, grants least-privilege privileges to `nexus_app`, and seeds default RBAC catalog + Ecosystem Application Registry (`NEXUS_C`, `NEXUS_F`, `NEXUS_E`, `NEXUS_M`).
   - Optionally provisions the initial `PlatformSuperAdmin` once when `NEXUS_BOOTSTRAP_ADMIN_EMAIL` and `NEXUS_BOOTSTRAP_ADMIN_PASSWORD` are supplied (remove both secrets after initial bootstrap).
3. **Runtime API Replicas (`src/Nexus.Api` / `Dockerfile`):**
   - Connects **exclusively** as `nexus_app` (`NOBYPASSRLS`) — never receives `MigrationConnection`, superuser credentials, or bootstrap admin passwords.
   - `ValidateRuntimeSecurityConfiguration` fails fast on startup in non-Development/Testing environments if `Jwt:SigningKey` (< 32 chars or placeholder), `ObjectStorage:SigningSecret` (< 32 chars or placeholder), dev/superuser DB credentials, or wildcard `*` CORS origin are detected.

---

## 2. Reverse Proxy, HTTPS, Cookies & CORS

- `Program.cs` enables `ForwardedHeadersMiddleware` (`X-Forwarded-For`, `X-Forwarded-Proto`, `ForwardLimit = 2`). Terminate TLS 1.3 at your reverse proxy (Nginx / Envoy / ALB) and forward `X-Forwarded-Proto: https`.
- Configure explicit origins via `Cors__AllowedOrigins__0=https://console.your-domain.com`. Wildcard `*` is rejected at startup.
- When browser clients enable cookie refresh (`X-Nexus-Auth-Mode: cookie`), `__nexus_rt` is set `HttpOnly; Secure` (over HTTPS), `SameSite=Strict`, `Path=/api/v1/auth`, and cookie-based `POST /refresh` and `POST /logout` require `X-Nexus-CSRF: 1`.
- OpenAPI (`/openapi/v1.json`) and Scalar (`/scalar`) are **disabled by default in Production** (`OpenApi:Enabled = false`). Enable only on internal admin networks if required.

---

## 3. Health Checks & Observability

| Endpoint | Purpose | Safe for Public LB? |
|---|---|---|
| `GET /health/live` | Process liveness (`self` tag only). Dependency outages NEVER fail liveness. | Yes |
| `GET /health/ready` | Readiness (`postgresql` check). Returns `503` when DB is unreachable. | Internal LB |
| `GET /health/dependencies` | Sanitized JSON dependency report (status + latency only; zero connection strings/secrets). | Internal Ops |

- **Tracing & Metrics:** Set `OpenTelemetry__OtlpEndpoint=http://otel-collector:4317` to export ASP.NET Core, HttpClient, Npgsql, and `Nexus.Platform` custom spans & low-cardinality metrics (`nexus.auth.attempts`, `nexus.messaging.outbox.*`, `nexus.messaging.dlq.*`, `nexus.workflow.decisions`, `nexus.integration.webhook.deliveries`, `nexus.jobs.*`).
- **Structured Logging:** Serilog writes JSON/structured logs enriched with `CorrelationId`, `TenantId` (UUID only), `ActorType`, `UserId`/`ServiceClientId`, and `Application`. Sensitive fields are redacted by `SensitiveDataRedactor`.

---

## 4. Multi-Instance Deployment Assessment & Explicit Limitations

| Capability | Multi-Instance Safe? | Notes |
|---|---|---|
| PostgreSQL 17 RLS & Connection Pooling | **Yes** | Per-connection & transaction-local GUCs reset on connection close; verified under 24-way concurrent pool test. |
| Database Migrations (`Nexus.Migrator`) | **Yes** | Dedicated one-shot job serialized by `pg_advisory_lock(74839210)`. |
| Transactional Outbox Dispatcher | **Yes** | Uses `SELECT ... FOR UPDATE SKIP LOCKED` so multiple replicas never double-dispatch the same row. |
| Inbox Deduplication & HTTP Idempotency | **Yes** | Backed by PostgreSQL unique constraints (`inbox_messages`, `idempotency_records`). |
| Permission Cache & Feature Flags | **Yes (with Redis)** | Configure `ConnectionStrings__Redis` in multi-instance deployments; without Redis, in-memory cache is node-local (2-minute TTL). |
| Session & Service Client Revocation | **Yes** | Verified synchronously against PostgreSQL in `TenantContextMiddleware`. |
| Object Storage | **Yes (with S3/MinIO)** | Set `ObjectStorage__Provider=S3` in multi-instance deployments (`Local` filesystem provider is single-node dev only). |
| **HTTP Rate Limiting** | **Node-Local Limitation** | Built-in ASP.NET Core `FixedWindowLimiter` stores counters in process memory (per replica). In multi-instance deployments, enforce global rate limits at the edge gateway / ingress or upgrade to a Redis-backed rate limiter post-1.0. |
| **Background Job Tracker (`/operations/jobs`)** | **Node-Local Limitation** | `BackgroundJobTracker` reports in-memory metrics for the serving API replica. Aggregate across replicas via OpenTelemetry `nexus.jobs.*` metrics. |
