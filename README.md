# NEXUS C — Enterprise Platform Core & Control Center

**NEXUS C (Center)** is the authoritative multi-tenant foundation platform and administration control center for the NEXUS Enterprise SaaS ecosystem (**NEXUS F — Finance**, **NEXUS E — Express**, **NEXUS M — Management**).

It combines:
- **NEXUS C Backend (`src/`):** ASP.NET Core 10 Modular Monolith with 11 bounded-context modules, PostgreSQL 17 `FORCE ROW LEVEL SECURITY`, least-privilege runtime database access, dedicated `Nexus.Migrator` job, RBAC, immutable audit trail, and transactional outbox/inbox messaging.
- **NEXUS C Console (`nexus-c-console/`):** Lightweight Enterprise Minimalism control center built with React 19, TypeScript (strict), Vite 8, TanStack Query 5, and fail-closed server-hydrated RBAC.

---

## Architecture & Tech Stack

### Backend (`src/`)
- **Runtime:** .NET 10.0 LTS / ASP.NET Core 10
- **Architecture:** Modular Monolith with 11 isolated domain modules (`src/Modules/*`), compile-time dependency boundaries, and automated `Nexus.ArchitectureTests`
- **Database & Tenant Isolation:** PostgreSQL 17 with per-module schemas, EF Core 10 Code-First migrations (12 migrations), and **`FORCE ROW LEVEL SECURITY` on 27 tables** (including hybrid platform/tenant RLS via `app.is_platform_scope` GUC on `access_control.roles`, `access_control.user_roles`, and `configuration.feature_flags`), enforced against a least-privilege non-superuser role (`nexus_app` with `NOBYPASSRLS`)
- **Dedicated Migration & Bootstrap Runner:** One-shot `src/Nexus.Migrator` (`Dockerfile.migrator`) paired with environment-driven `infrastructure/database/init-roles.sh` (zero hardcoded credentials) and fail-fast production startup secret validation
- **Authentication & Authorization:** ASP.NET Core Identity + short-lived JWT access tokens + hashed opaque refresh tokens with rotation & reuse detection + fail-closed Platform/Tenant RBAC (`GET /api/v1/auth/permissions`, strict cross-tenant membership & user-management guards)
- **Messaging, Integrations & Reliability:** Transactional Outbox (`FOR UPDATE SKIP LOCKED`), Idempotent Inbox, Retry + Dead Letter Queue, RabbitMQ (`RabbitMQ.Client` v7), HTTP `Idempotency-Key` middleware, and webhook SSRF target validation
- **Storage, Cache & Observability:** S3-compatible Object Storage (`AWSSDK.S3` + local dev adapter), Redis distributed cache, Serilog structured logging with secret scrubbing, OpenTelemetry traces & metrics, `/health/live` & `/health/ready`, OpenAPI 3.1 + Scalar UI

### Frontend (`nexus-c-console/`)
- **Stack:** Vite 8 + React 19 + TypeScript (strict) + React Router 7 + TanStack Query 5 + React Hook Form + Zod
- **Design System:** Custom Enterprise Minimalism CSS tokens (Light Mode default + Dark Mode, zero heavyweight UI libraries, ~104 KB gzipped initial JS)
- **Security & RBAC:** In-memory session/token management with single-flight refresh locking ([ADR-002](docs/architecture/ADR-002-CONSOLE-SESSION-STORAGE.md)) and fail-closed navigation & route protection hydrated from `GET /api/v1/auth/permissions`

---

## Repository Structure

```text
NEXUS-C/
├── NEXUS.sln
├── Directory.Build.props
├── Directory.Packages.props
├── Dockerfile                        # NEXUS C API production image
├── Dockerfile.migrator               # Dedicated one-shot Nexus.Migrator image
├── docker-compose.yml                # Full stack (Postgres, Redis, RabbitMQ, MinIO, Migrator, API)
├── .env.example                      # Required environment variables template
├── .github/workflows/ci.yml          # Dual CI pipeline (backend-ci + frontend-ci)
├── infrastructure/
│   └── database/
│       ├── init-roles.sh             # Env-driven least-privilege PostgreSQL role bootstrap
│       └── init-roles.sql            # Parameterized SQL role setup reference
├── src/
│   ├── Nexus.SharedKernel/           # Shared primitives, permissions, cross-module contracts
│   ├── Nexus.BuildingBlocks/         # EF Core RLS interceptors, Outbox/Inbox, Auth, Caching
│   ├── Nexus.Bootstrap/              # Module composition, migrator & seed engine
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
│   ├── docs/                         # Frontend architecture, API gap matrix, performance report
│   └── package.json
├── tests/
│   ├── Nexus.UnitTests/
│   ├── Nexus.ArchitectureTests/
│   ├── Nexus.IntegrationTests/       # Real PostgreSQL 17 + RLS + Outbox + Migrator tests
│   ├── Nexus.ApiTests/
│   └── Nexus.SecurityTests/          # Cross-tenant isolation, RBAC & SSRF regression tests
└── docs/
    ├── architecture/                 # Blueprint, ADR-001, ADR-002, Domain Boundaries
    ├── api/                          # REST & Event Contracts
    ├── database/                     # Per-module schemas, RLS policies, Audit immutability
    ├── security/                     # Threat controls, AuthN/AuthZ, Token lifecycle
    ├── deployment/                   # Local setup, Docker, Env, Migrations, Backup, Prod checklist
    └── hardening/                    # Foundation hardening audit, fixes, DB/deploy reports & test proof
```

---

## Quick Start

### 1. Run Full Stack via Docker Compose (Recommended)
See [docs/deployment/DOCKER.md](docs/deployment/DOCKER.md) and [docs/deployment/ENVIRONMENT.md](docs/deployment/ENVIRONMENT.md).

```bash
cp .env.example .env
# Edit .env with strong unique passwords, JWT_SIGNING_KEY (>= 32 chars),
# OBJECT_STORAGE_SIGNING_SECRET, and NEXUS_BOOTSTRAP_ADMIN_PASSWORD
docker compose up -d --build
```

Docker Compose initializes PostgreSQL roles via `init-roles.sh`, runs `nexus-migrator` to completion (migrations + RLS + initial seed), and starts `nexus-api` on the least-privilege `nexus_app` database role.

### 2. Run Locally (PostgreSQL 17 + .NET 10 + Node 24)
See [docs/deployment/LOCAL-SETUP.md](docs/deployment/LOCAL-SETUP.md).

```bash
# 1) Build backend solution
dotnet restore NEXUS.sln
dotnet build NEXUS.sln -c Release

# 2) Provision least-privilege PostgreSQL roles & run migrations/seed (one-shot)
NEXUS_DB_NAME=nexus_dev \
NEXUS_MIGRATOR_USER=nexus_migrator NEXUS_MIGRATOR_PASSWORD="<strong-migrator-password>" \
NEXUS_APP_USER=nexus_app NEXUS_APP_PASSWORD="<strong-app-password>" \
./infrastructure/database/init-roles.sh

ConnectionStrings__MigrationConnection="Host=127.0.0.1;Port=5432;Database=nexus_dev;Username=nexus_migrator;Password=<strong-migrator-password>" \
Nexus__Database__AppRoleName="nexus_app" \
Nexus__BootstrapAdmin__Email="admin@nexus.local" \
Nexus__BootstrapAdmin__Password="<strong-admin-password>" \
dotnet run -c Release --no-build --project src/Nexus.Migrator/Nexus.Migrator.csproj

# 3) Start Backend API (bind 127.0.0.1:5000 to avoid macOS AirPlay on ::1:5000)
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5000 \
ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=nexus_dev;Username=nexus_app;Password=<strong-app-password>" \
dotnet run -c Release --no-build --project src/Nexus.Api/Nexus.Api.csproj

# 4) Start NEXUS C Console (in a second terminal)
cd nexus-c-console
npm install
npm run dev -- --host 127.0.0.1 --port 5173
```

Open **http://127.0.0.1:5173** for NEXUS C Console, **http://127.0.0.1:5000/openapi/v1.json** for OpenAPI, and **http://127.0.0.1:5000/scalar** for interactive API docs (Development mode).

---

## Automated Verification & CI

Both backend and frontend quality gates run automatically in [`.github/workflows/ci.yml`](.github/workflows/ci.yml):

```bash
# Backend (55 tests: Unit, Architecture, PostgreSQL 17 RLS/Integration, API, Security)
dotnet test NEXUS.sln -c Release

# Frontend (TypeScript strict, ESLint, Vitest + Testing Library, Production bundle)
cd nexus-c-console
npm run typecheck
npm run lint
npm test
npm run build
```

---

## Documentation Index

### Architecture & Decisions
- [Architecture Blueprint](docs/architecture/ARCHITECTURE.md)
- [ADR-001: Modular Monolith](docs/architecture/ADR-001-MODULAR-MONOLITH.md)
- [ADR-002: Console Session Storage & Refresh Strategy](docs/architecture/ADR-002-CONSOLE-SESSION-STORAGE.md)
- [Domain Boundaries](docs/architecture/DOMAIN-BOUNDARIES.md)
- [Technology Decisions](docs/architecture/TECHNOLOGY-DECISIONS.md)
- [NEXUS C Console Architecture](nexus-c-console/docs/architecture.md)

### API, Database & Security
- [API Reference](docs/api/API-REFERENCE.md)
- [Frontend API Integration & Gap Matrix](nexus-c-console/docs/api-integration.md)
- [Database Schema & PostgreSQL RLS](docs/database/SCHEMA-AND-RLS.md)
- [Security Architecture](docs/security/SECURITY-ARCHITECTURE.md)

### Deployment & Operations
- [Local Setup Guide](docs/deployment/LOCAL-SETUP.md)
- [Docker Guide](docs/deployment/DOCKER.md)
- [Environment Configuration](docs/deployment/ENVIRONMENT.md)
- [Database Migration Guide](docs/deployment/DATABASE-MIGRATION.md)
- [Backup & Recovery](docs/deployment/BACKUP-RECOVERY.md)
- [Production Readiness Checklist](docs/deployment/PRODUCTION-CHECKLIST.md)

### Security Hardening & Verification Reports
- [Hardening Findings](docs/hardening/FINDINGS.md)
- [Security Fixes Report](docs/hardening/SECURITY-FIXES.md)
- [Database Hardening Report](docs/hardening/DATABASE-HARDENING.md)
- [Deployment Hardening Report](docs/hardening/DEPLOYMENT-HARDENING.md)
- [Test Verification Results](docs/hardening/TEST-RESULTS.md)
- [Production Readiness Assessment](docs/hardening/PRODUCTION-READINESS.md)
