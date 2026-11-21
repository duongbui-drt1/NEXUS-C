# NEXUS C — Foundation Engineering Backend

Enterprise multi-tenant Modular Monolith backend serving as the authoritative platform core (**NEXUS C — Center**) for the NEXUS Ecosystem (**NEXUS F — Finance**, **NEXUS E — Express**, **NEXUS M — Management**).

---

## Architecture & Tech Stack

- **Runtime:** .NET 10.0 LTS / ASP.NET Core 10
- **Architecture:** Modular Monolith with 11 bounded-context modules (`src/Modules/*`), compile-time isolation, and automated `Nexus.ArchitectureTests`
- **Database:** PostgreSQL 17 with per-module schemas, EF Core 10 Code-First migrations, and **`FORCE ROW LEVEL SECURITY`** enforced against a least-privilege non-superuser role (`nexus_app`)
- **Authentication & Authorization:** ASP.NET Core Identity + short-lived JWT access tokens + hashed opaque refresh tokens with rotation & reuse detection + Platform/Tenant RBAC (ABAC-ready)
- **Messaging & Reliability:** Transactional Outbox (`FOR UPDATE SKIP LOCKED`), Idempotent Inbox, Retry + Dead Letter Queue, RabbitMQ (`RabbitMQ.Client` v7), HTTP `Idempotency-Key` middleware
- **Storage, Cache & Observability:** S3-compatible Object Storage (`AWSSDK.S3` + Local dev adapter), Redis distributed cache, Serilog structured logging with secret scrubbing, OpenTelemetry traces & metrics, `/health/live` & `/health/ready`, OpenAPI 3.1 + Scalar UI

---

## Solution Structure

```text
NEXUS PRODUCTION/
├── NEXUS.sln
├── Directory.Build.props
├── Directory.Packages.props
├── Dockerfile
├── docker-compose.yml
├── .env.example
├── .github/workflows/ci.yml
├── infrastructure/database/init-roles.sql
├── docs/
│   ├── architecture/        # Blueprint, ADR-001, Domain Boundaries, Tech Decisions, Plan
│   ├── api/                 # REST & Event Contracts
│   ├── database/            # Per-module schemas, RLS policies, Audit immutability
│   ├── security/            # Threat controls, AuthN/AuthZ, Token lifecycle
│   └── deployment/          # Local setup, Docker, Env, Migrations, Backup, Prod checklist
├── src/
│   ├── Nexus.SharedKernel/
│   ├── Nexus.BuildingBlocks/
│   ├── Nexus.Bootstrap/
│   ├── Nexus.Api/
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
└── tests/
    ├── Nexus.UnitTests/
    ├── Nexus.ArchitectureTests/
    ├── Nexus.IntegrationTests/
    ├── Nexus.ApiTests/
    └── Nexus.SecurityTests/
```

---

## Quick Start

### 1. Run Locally (with PostgreSQL 17)
See [docs/deployment/LOCAL-SETUP.md](docs/deployment/LOCAL-SETUP.md).

```bash
# Restore & Build
dotnet restore NEXUS.sln
dotnet build NEXUS.sln -c Release

# Run full automated test suite (Unit, Architecture, PostgreSQL Integration/RLS, API, Security)
dotnet test NEXUS.sln -c Release --no-build

# Run API locally (Development enables /openapi/v1.json and /scalar)
dotnet run --project src/Nexus.Api/Nexus.Api.csproj
```

### 2. Run Full Stack via Docker Compose
See [docs/deployment/DOCKER.md](docs/deployment/DOCKER.md).

```bash
cp .env.example .env
# Edit .env with strong unique passwords and JWT_SIGNING_KEY
docker compose up -d --build
```

---

## Documentation Index

- [Architecture Blueprint](docs/architecture/ARCHITECTURE.md)
- [ADR-001: Modular Monolith](docs/architecture/ADR-001-MODULAR-MONOLITH.md)
- [Domain Boundaries](docs/architecture/DOMAIN-BOUNDARIES.md)
- [Technology Decisions](docs/architecture/TECHNOLOGY-DECISIONS.md)
- [API Reference](docs/api/API-REFERENCE.md)
- [Database Schema & PostgreSQL RLS](docs/database/SCHEMA-AND-RLS.md)
- [Security Architecture](docs/security/SECURITY-ARCHITECTURE.md)
- [Local Setup Guide](docs/deployment/LOCAL-SETUP.md)
- [Docker Guide](docs/deployment/DOCKER.md)
- [Environment Configuration](docs/deployment/ENVIRONMENT.md)
- [Database Migration Guide](docs/deployment/DATABASE-MIGRATION.md)
- [Backup & Recovery](docs/deployment/BACKUP-RECOVERY.md)
- [Production Readiness Checklist](docs/deployment/PRODUCTION-CHECKLIST.md)
