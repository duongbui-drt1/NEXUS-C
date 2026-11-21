# Docker & Docker Compose Deployment Guide

## Components

`docker-compose.yml` provisions an isolated internal bridge network (`nexus_internal`) with pinned image tags (never `:latest`) and health checks:

| Service | Pinned Image | Purpose |
|---|---|---|
| `postgres` | `postgres:17.4-alpine` | Primary relational store with `init-roles.sql` role provisioning |
| `redis` | `redis:7.4.2-alpine` | Distributed cache with password authentication & AOF persistence |
| `rabbitmq` | `rabbitmq:4.0.7-management-alpine` | AMQP topic exchange (`nexus.events`) & DLX (`nexus.events.dlx`) |
| `minio` | `minio/minio:RELEASE.2025-02-28T09-55-16Z` | S3-compatible object storage for `FileStorage` |
| `api` | Built from `Dockerfile` (`dotnet/aspnet:10.0`) | Non-root (`USER app`) ASP.NET Core 10 host |

---

## Running with Docker Compose

1. Create `.env` from `.env.example` and replace all `REPLACE_WITH_*` placeholders with strong secrets:
   ```bash
   cp .env.example .env
   ```
2. Validate compose configuration:
   ```bash
   docker compose config
   ```
3. Start infrastructure & API:
   ```bash
   docker compose up -d --build
   ```
4. Check health status:
   ```bash
   docker compose ps
   curl -fsS http://127.0.0.1:8080/health/live
   curl -fsS http://127.0.0.1:8080/health/ready
   ```

---

## Container Security Hardening

- Multi-stage build separates SDK (`mcr.microsoft.com/dotnet/sdk:10.0`) from minimal ASP.NET runtime (`mcr.microsoft.com/dotnet/aspnet:10.0`).
- Container executes as non-root `USER app` (UID 1654).
- All host ports bind to `127.0.0.1` by default in local Compose.
- `HEALTHCHECK` probes `/health/live` and `/health/ready`.
- No secrets are baked into layers; all credentials are injected via environment variables or orchestrator secret stores.
