# NEXUS C 1.0 — Enterprise Platform Core & Control Center

**NEXUS C (Center)** is the authoritative multi-tenant foundation platform and administration control center for the NEXUS Enterprise SaaS ecosystem (**NEXUS F — Finance**, **NEXUS E — Express**, **NEXUS M — Management**).

It combines:
- **NEXUS C Backend (`src/`):** ASP.NET Core 10 Modular Monolith with 11 bounded-context modules, **33 PostgreSQL 17 `FORCE ROW LEVEL SECURITY` tables**, least-privilege runtime database access (`nexus_app` with `NOBYPASSRLS`), advisory-locked `Nexus.Migrator`, RBAC/ABAC/Org-scope policy engine, OAuth 2.0 `client_credentials` & scoped API keys, versioned workflow governance, Ecosystem Application Registry & authoritative entitlements, immutable 3-layer audit trail, and transactional outbox/inbox/DLQ operations.
- **NEXUS C Console (`nexus-c-console/`):** Lightweight Enterprise Minimalism control center built with React 19, TypeScript (strict), Vite 8, TanStack Query 5, and fail-closed server-hydrated RBAC.

---

## Architecture & Tech Stack

### Backend (`src/`)
- **Runtime:** .NET 10.0 LTS / ASP.NET Core 10
- **Architecture:** Modular Monolith with 11 isolated domain modules (`src/Modules/*`), compile-time dependency boundaries, and automated `Nexus.ArchitectureTests`
- **Database & Tenant Isolation:** PostgreSQL 17 with 12 module schemas, 21 EF Core 10 Code-First migrations, and **`FORCE ROW LEVEL SECURITY` on 33 tables** (28 strictly tenant-scoped + 5 hybrid platform/tenant tables including `audit.audit_events` and `access_control.role_permissions`), enforced against least-privilege `nexus_app` (`NOSUPERUSER NOBYPASSRLS`)
- **Dedicated Migration & Bootstrap Runner:** Advisory-locked (`pg_advisory_lock(74839210)`) one-shot `src/Nexus.Migrator` (`Dockerfile.migrator`) paired with environment-driven `infrastructure/database/init-roles.sh` (zero hardcoded credentials)
- **Authentication, Service Identity & Policy:** ASP.NET Core Identity + short-lived JWT access tokens + rotating SHA-256 refresh tokens with reuse detection & tenant-switch revocation + optional `HttpOnly; SameSite=Strict` refresh cookie mode (`X-Nexus-CSRF: 1`) + OAuth 2.0 `client_credentials` (`POST /api/v1/auth/token`) + scoped `X-Api-Key` + RBAC/ABAC (`Allow`/`Deny`, resource/org scope via `POST /api/v1/auth/authorize`)
- **Governance, Registry & Reliability:** Version-pinned Workflow Engine (sequential/parallel, Maker–Checker, delegation, escalation), Ecosystem Application Registry (`NEXUS_C`, `NEXUS_F`, `NEXUS_E`, `NEXUS_M`), Authoritative Entitlements & idempotent usage quotas, Transactional Outbox (`FOR UPDATE SKIP LOCKED`), Inbox, Operations API (`/api/v1/platform/operations/*`), RabbitMQ (`RabbitMQ.Client` v7), HTTP `Idempotency-Key` middleware, full webhook lifecycle with `SsrfGuard` & HMAC-SHA256 replay-protected signatures
- **Storage, Cache & Observability:** S3-compatible Object Storage (`AWSSDK.S3` + local dev adapter) with fail-closed HMAC signing & retention expiry, Redis distributed cache with permission/entitlement/config invalidation, Serilog structured logging with secret scrubbing, OpenTelemetry `Nexus.Platform` metrics & traces, `/health/live`, `/health/ready`, sanitized `/health/dependencies`, OpenAPI 3.1 + Scalar UI

### Frontend (`nexus-c-console/`)
- **Stack:** Vite 8 + React 19 + TypeScript (strict) + React Router 7 + TanStack Query 5 + React Hook Form + Zod
- **Design System:** Custom Enterprise Minimalism CSS tokens (Light Mode default + Dark Mode, zero heavyweight UI libraries, ~103.8 KB gzipped initial JS)
- **Security & RBAC:** In-memory session/token management with single-flight refresh locking ([ADR-002](docs/architecture/ADR-002-CONSOLE-SESSION-STORAGE.md)), hardened Nginx CSP/security headers, and fail-closed navigation & route protection hydrated from `GET /api/v1/auth/permissions`

---

## Repository Structure

```text
NEXUS-C/
├── NEXUS.sln
├── Directory.Build.props
├── Directory.Packages.props
├── Dockerfile                        # NEXUS C API production image (non-root + curl healthcheck)
├── Dockerfile.migrator               # Dedicated one-shot Nexus.Migrator image
├── docker-compose.yml                # Full stack (Postgres, Redis, RabbitMQ, MinIO, Migrator, API)
├── .env.example                      # Required environment variables template
├── .github/workflows/ci.yml          # CI pipeline (backend-ci + frontend-ci + docker-build-verify)
├── infrastructure/
│   └── database/
│       ├── init-roles.sh             # Env-driven least-privilege PostgreSQL role bootstrap
│       └── init-roles.sql            # Parameterized SQL role setup reference
├── src/
│   ├── Nexus.SharedKernel/           # Shared primitives, permissions (39), cross-module contracts
│   ├── Nexus.BuildingBlocks/         # EF Core RLS interceptors, Outbox/Inbox/Ops, Auth, SSRF/HMAC
│   ├── Nexus.Bootstrap/              # Module composition, advisory-locked migrator & seed engine
│   ├── Nexus.Migrator/               # Dedicated one-shot migration, RLS & seed console runner
│   ├── Nexus.Api/                    # ASP.NET Core 10 HTTP host, controllers, OpenAPI
│   └── Modules/
│       ├── Tenancy/
│       ├── Organization/
│       ├── Identity/
│       ├── AccessControl/
│       ├── Audit/
│       ├── Workflow/
│       ├── Subscription/
│       ├── Configuration/
│       ├── Integration/
│       ├── Notification/
│       └── FileStorage/
├── nexus-c-console/                  # React 19 + TypeScript + Vite Enterprise Control Center
│   ├── src/
│   ├── nginx.conf                    # Hardened non-root Nginx SPA config with CSP & security headers
│   ├── docs/                         # Frontend architecture, API integration & performance docs
│   └── package.json
├── tests/
│   ├── Nexus.UnitTests/              # 35 unit & domain invariant tests
│   ├── Nexus.ArchitectureTests/      # 5 compile-time & architectural boundary tests
│   ├── Nexus.IntegrationTests/       # 14 real PostgreSQL 17 + 33-table RLS + Outbox/DLQ tests
│   ├── Nexus.ApiTests/               # 7 end-to-end API & ecosystem platform contract tests
│   └── Nexus.SecurityTests/          # 8 security matrix tests (RLS, CSRF, OAuth2, API keys, SSRF)
└── docs/
    ├── architecture/                 # Blueprint, Platform Contract, Ecosystem Integration, ADR-001..006
    ├── api/                          # REST & Event Contracts
    ├── database/                     # Per-module schemas, 33-table RLS policies, Audit immutability
    ├── security/                     # Threat controls, AuthN/AuthZ, Service Identity, Token lifecycle
    ├── deployment/                   # Local setup, Docker, Env, Migrations, DB Ops, Prod Deployment
    ├── hardening/                    # Pre-1.0 hardening audit & verification archive
    └── release/                      # NEXUS C 1.0 audit, platform freeze, security, test & release notes
```

---

## Quick Start

### 1. Run Full Stack via Docker Compose (Recommended)
See [docs/deployment/DOCKER.md](docs/deployment/DOCKER.md) and [docs/deployment/PRODUCTION-DEPLOYMENT.md](docs/deployment/PRODUCTION-DEPLOYMENT.md).

```bash
cp .env.example .env
# Edit .env with strong unique passwords, JWT_SIGNING_KEY (>= 64 chars),
# OBJECT_STORAGE_SIGNING_SECRET (>= 64 chars), and NEXUS_BOOTSTRAP_ADMIN_PASSWORD
docker compose up -d --build
```

Docker Compose initializes least-privilege PostgreSQL roles via `init-roles.sh`, runs `migrator` to completion (21 migrations + 33 `FORCE RLS` tables + RBAC & Application Registry seed), and starts `api` using `nexus_app`.

### 2. Run Locally (PostgreSQL 17 + .NET 10 + Node 24)
See [docs/deployment/LOCAL-SETUP.md](docs/deployment/LOCAL-SETUP.md).

```bash
# 1) Build backend solution
dotnet restore NEXUS.sln
dotnet build NEXUS.sln -c Release

# 2) Provision least-privilege PostgreSQL roles & run migrations/seed (one-shot)
createdb -h 127.0.0.1 -p 5432 nexus_dev
PGHOST=127.0.0.1 PGPORT=5432 POSTGRES_DB=nexus_dev POSTGRES_USER="$(whoami)" \
  POSTGRES_MIGRATOR_PASSWORD="<strong-migrator-password>" \
  POSTGRES_APP_PASSWORD="<strong-app-password>" \
  sh ./infrastructure/database/init-roles.sh

ConnectionStrings__MigrationConnection="Host=127.0.0.1;Port=5432;Database=nexus_dev;Username=nexus_migrator;Password=<strong-migrator-password>" \
ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=nexus_dev;Username=nexus_app;Password=<strong-app-password>" \
NEXUS_APP_DB_ROLE="nexus_app" \
NEXUS_BOOTSTRAP_ADMIN_EMAIL="admin@nexus.local" \
NEXUS_BOOTSTRAP_ADMIN_PASSWORD="<strong-admin-password>" \
dotnet run -c Release --no-build --project src/Nexus.Migrator/Nexus.Migrator.csproj

# 3) Start Backend API (bind 127.0.0.1:5000 to avoid macOS AirPlay on ::1:5000)
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5000 \
ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=nexus_dev;Username=nexus_app;Password=<strong-app-password>" \
ObjectStorage__SigningSecret="<strong-64-char-hmac-secret>" \
dotnet run -c Release --no-build --project src/Nexus.Api/Nexus.Api.csproj

# 4) Start NEXUS C Console (in a second terminal)
cd nexus-c-console
npm ci
npm run dev -- --host 127.0.0.1 --port 5173
```

Open **http://127.0.0.1:5173** for NEXUS C Console, **http://127.0.0.1:5000/openapi/v1.json** for OpenAPI, and **http://127.0.0.1:5000/scalar** for interactive API docs (Development mode).

---

## Automated Verification & CI

Backend, frontend, and Docker build gates run automatically in [`.github/workflows/ci.yml`](.github/workflows/ci.yml):

```bash
# Backend (82 tests: Unit, Architecture, PostgreSQL 17 RLS/Integration, Security, API)
dotnet format NEXUS.sln --verify-no-changes
dotnet build NEXUS.sln -c Release
dotnet test NEXUS.sln -c Release --no-build

# Frontend (TypeScript strict, ESLint, 14 Vitest + Testing Library tests, Production bundle)
cd nexus-c-console
npm run typecheck
npm run lint
npm test
npm run build
```

---

## Documentation Index

### NEXUS C 1.0 Release & Platform Freeze
- [NEXUS C 1.0 War-Room Final Freeze Report](docs/release/NEXUS-C-1.0-WAR-ROOM-FREEZE-REPORT.md)
- [NEXUS C 1.0 Release Notes](docs/release/NEXUS-C-1.0-RELEASE-NOTES.md)
- [NEXUS C 1.0 Platform Freeze Specification](docs/release/NEXUS-C-1.0-PLATFORM-FREEZE.md)
- [NEXUS C 1.0 Baseline Audit](docs/release/NEXUS-C-1.0-BASELINE-AUDIT.md)
- [NEXUS C 1.0 Security Report](docs/release/NEXUS-C-1.0-SECURITY-REPORT.md)
- [NEXUS C 1.0 Test & Drill Report](docs/release/NEXUS-C-1.0-TEST-REPORT.md)

### Architecture, Contracts & ADRs
- [Platform Contract (Frozen v1.0)](docs/architecture/PLATFORM-CONTRACT.md)
- [Ecosystem Integration Guide (NEXUS F / E / M)](docs/architecture/ECOSYSTEM-INTEGRATION.md)
- [Architecture Blueprint](docs/architecture/ARCHITECTURE.md)
- [Domain Boundaries](docs/architecture/DOMAIN-BOUNDARIES.md)
- [Technology Decisions](docs/architecture/TECHNOLOGY-DECISIONS.md)
- [ADR-001: Modular Monolith](docs/architecture/ADR-001-MODULAR-MONOLITH.md)
- [ADR-002: Console Session Storage & Refresh Strategy](docs/architecture/ADR-002-CONSOLE-SESSION-STORAGE.md)
- [ADR-003: Hybrid RLS & Policy Engine](docs/architecture/ADR-003-HYBRID-RLS-AND-POLICY-ENGINE.md)
- [ADR-004: Service-to-Service OAuth 2.0 & API Keys](docs/architecture/ADR-004-SERVICE-TO-SERVICE-OAUTH2-AND-API-KEYS.md)
- [ADR-005: Workflow Versioning & Governance](docs/architecture/ADR-005-WORKFLOW-VERSIONING-AND-GOVERNANCE.md)
- [ADR-006: Application Registry & Authoritative Entitlements](docs/architecture/ADR-006-APPLICATION-REGISTRY-AND-ENTITLEMENTS.md)
- [ADR-007: War-Room Final Platform Pillars (Clerk, Resend, Rate Limits, FX, SSE, AI Governance)](docs/architecture/ADR-007-WAR-ROOM-CLERK-RESEND-RATELIMIT-FX-SSE-AI.md)
- [NEXUS C Console Architecture](nexus-c-console/docs/architecture.md)

### API, Database & Security
- [API Reference](docs/api/API-REFERENCE.md)
- [Frontend API Integration & Gap Matrix](nexus-c-console/docs/api-integration.md)
- [Database Schema & 36-Table PostgreSQL RLS](docs/database/SCHEMA-AND-RLS.md)
- [Security Architecture](docs/security/SECURITY-ARCHITECTURE.md)

### Deployment & Operations
- [Production Deployment Guide](docs/deployment/PRODUCTION-DEPLOYMENT.md)
- [Operational Runbook: Clerk, Resend, Rate Limiting, FX & AI Governance](docs/operations/RUNBOOK-CLERK-RESEND-FX-RATELIMIT.md)
- [Database Operations Runbook](docs/deployment/DATABASE-OPERATIONS.md)
- [Local Setup Guide](docs/deployment/LOCAL-SETUP.md)
- [Docker Guide](docs/deployment/DOCKER.md)
- [Environment Configuration](docs/deployment/ENVIRONMENT.md)
- [Database Migration Guide](docs/deployment/DATABASE-MIGRATION.md)
- [Backup & Recovery](docs/deployment/BACKUP-RECOVERY.md)
- [Production Readiness Checklist](docs/deployment/PRODUCTION-CHECKLIST.md)
