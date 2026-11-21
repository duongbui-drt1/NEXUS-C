# NEXUS C — Technology Decisions & Environment Audit

## 1. Environment Audit (Executed 2026-11-22)

- **Host OS:** macOS 27.3 (Darwin 27.3.0, Apple Silicon `arm64`)
- **Workspace:** `/Users/linhhhuce/Documents/NEXUS PRODUCTION` (clean directory; no pre-existing solution or conflicting source files)
- **Git:** Installed via Homebrew (`git`); repository not yet initialized (no auto-commit performed unless requested)
- **Available Package Manager:** Homebrew 7.0.13 (`/opt/homebrew/bin/brew`)
- **Runtime / Database Provisioning:**
  - `.NET 10.0 LTS` (`dotnet@10`, SDK `10.0.404`) — installed via Homebrew. Chosen over `.NET 11` (STS) because `.NET 10` is the current official Long-Term Support (LTS) release explicitly preferred by NEXUS technology baseline.
  - `PostgreSQL 17` (`postgresql@17`, version `17.12`) — installed via Homebrew to execute genuine PostgreSQL migrations, constraints, concurrency tests, and Row-Level Security verification with a real non-superuser application database role (`nexus_app`).
  - **Docker Engine:** Not installed on the local host (and disk headroom is ~17 GB). Therefore:
    - Complete production-grade `Dockerfile`, `docker-compose.yml`, `.env.example`, and CI workflows are authored and validated.
    - Testcontainers support is wired via an environment-aware test fixture: when Docker is available (e.g., CI / developer workstation with Docker), `Testcontainers.PostgreSql` spins up an ephemeral PostgreSQL 17 container; when Docker is absent locally, tests run against a real isolated PostgreSQL 17 test database on localhost (NEVER falling back to EF Core InMemory!).

---

## 2. Backend Technology Stack (Pinned Stable Versions)

All NuGet versions are centrally managed in `Directory.Packages.props`. Zero preview packages are used.

| Category | Selection | Rationale |
|---|---|---|
| **Runtime & Framework** | .NET 10.0 LTS / ASP.NET Core 10 | Official LTS release; native OpenAPI, built-in Rate Limiting, modern minimal & controller pipeline, high throughput |
| **Database** | PostgreSQL 17 + `Npgsql.EntityFrameworkCore.PostgreSQL` (10.x) | Native Row-Level Security (`FORCE RLS`), `FOR UPDATE SKIP LOCKED`, strong ACID guarantees, `numeric` financial precision, rich indexing |
| **ORM & Migrations** | Entity Framework Core 10 (Code-First) | Strongly-typed queries, global tenant filters, interceptors for RLS session setup, deterministic migration history |
| **Identity & Auth** | `Microsoft.AspNetCore.Identity.EntityFrameworkCore` + `Microsoft.AspNetCore.Authentication.JwtBearer` | Industry-standard PBKDF2 password hashing, lockout, security stamps, OIDC/MFA extensibility, short-lived signed JWT access tokens |
| **Validation** | `FluentValidation` | Declarative, composable, testable request validation returning RFC 9457 Problem Details |
| **API Documentation** | `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` | Native .NET 10 OpenAPI 3.1 document generation with clean interactive explorer |
| **Messaging** | `RabbitMQ.Client` (v7 stable async API) | Native async AMQP client for topic exchanges, publisher confirms, retry queues, and dead-letter exchanges |
| **Caching** | `Microsoft.Extensions.Caching.StackExchangeRedis` | Distributed cache backing for feature flags, session revocation checks, and future multi-instance rate limiting |
| **Object Storage** | `AWSSDK.S3` (S3-compatible) + Local Encrypted-Path Dev/Test Adapter | Works with MinIO locally and AWS S3 / Cloudflare R2 / GCS S3-interop in production via a single `IObjectStorage` interface |
| **Logging & Telemetry** | `Serilog.AspNetCore` + `OpenTelemetry.Extensions.Hosting` / `Instrumentation.AspNetCore` | Structured JSON logging with sensitive-data scrubbing + vendor-neutral traces & metrics |
| **Testing** | `xUnit`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`, `FluentAssertions` (v7 MIT) / xUnit Assert | Real PostgreSQL integration tests, full HTTP pipeline tests, architecture boundary tests, security & concurrency tests |

---

## 3. Key Security Decisions

1. **Two PostgreSQL Roles:**
   - `nexus_migrator`: Owns schemas/tables; executes EF Core migrations and RLS DDL only.
   - `nexus_app`: `NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`; runtime API role with least-privilege table grants and `INSERT/SELECT`-only rights on `audit.audit_events`.
2. **JWT + Opaque Refresh Token Rotation:**
   - Access tokens: HS256 (dev/configurable) or RS256/OIDC-ready, 15-minute default lifetime, includes `sub`, `jti`, `sid`, `tid`, `scope`, and security stamp validation.
   - Refresh tokens: 64-byte CSPRNG opaque token, stored **only as SHA-256 hash**, bound to `TokenFamilyId`. Presenting a previously rotated/revoked token triggers **reuse detection** that immediately revokes the entire token family and session.
3. **Audit Immutability:**
   - Append-only service API (`IAuditWriter`) + PostgreSQL `GRANT INSERT, SELECT` only for `nexus_app` + PostgreSQL `BEFORE UPDATE OR DELETE` trigger blocking mutations on `audit.audit_events`.
4. **No Plaintext Secrets in Configuration or DB:**
   - Configuration service rejects keys marked sensitive or matching secret patterns (`password`, `secret`, `apikey`, `token`, `privatekey`); integration connectors store only external `CredentialReference` URIs (e.g., `vault://...`, `env://...`).
