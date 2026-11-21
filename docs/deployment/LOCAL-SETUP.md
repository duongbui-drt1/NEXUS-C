# Local Development Setup Guide

## Prerequisites

- **.NET 10.0 LTS SDK** (`10.0.x`)
- **PostgreSQL 17** running locally on `localhost:5432`
- *(Optional)* Redis 7.x, RabbitMQ 4.x, MinIO (the API defaults to in-memory distributed cache, in-memory event bus, and local filesystem object storage in Development when external brokers are not enabled).

### macOS (Homebrew) Example

```bash
brew install dotnet@10 postgresql@17
export PATH="/opt/homebrew/opt/dotnet@10/bin:/opt/homebrew/opt/postgresql@17/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@10/libexec"

LC_ALL="en_US.UTF-8" pg_ctl -D /opt/homebrew/var/postgresql@17 -l /opt/homebrew/var/log/postgresql@17.log start
```

---

## 1. Initialize PostgreSQL Roles & Development Database

```bash
psql -d postgres -c "CREATE DATABASE nexus_dev;"
psql -d nexus_dev -f infrastructure/database/init-roles.sql
psql -d postgres -c "ALTER DATABASE nexus_dev OWNER TO nexus_migrator;"
```

---

## 2. Restore, Build & Verify Tests

```bash
dotnet restore NEXUS.sln
dotnet build NEXUS.sln -c Release
dotnet test NEXUS.sln -c Release --no-build
```

> **Note:** Integration, API, and Security tests automatically provision an isolated ephemeral PostgreSQL 17 database (`nexus_test_*`) using both `nexus_migrator` (owner) and `nexus_app` (`NOSUPERUSER NOBYPASSRLS`), run all 12 EF Core migrations + RLS hardening, execute tests, and drop the temporary database on exit. They never alter `nexus_dev`.

---

## 3. Run the API Host

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Nexus.Api/Nexus.Api.csproj
```

Once running:
- **Liveness:** `GET http://localhost:5000/health/live`
- **Readiness:** `GET http://localhost:5000/health/ready`
- **OpenAPI JSON:** `GET http://localhost:5000/openapi/v1.json`
- **Interactive Scalar UI:** `GET http://localhost:5000/scalar`
