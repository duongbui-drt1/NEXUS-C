# NEXUS C — 7-Step Foundation Implementation Plan

This implementation plan tracks the deterministic execution of all 7 mandatory engineering steps for NEXUS Foundation Engineering.

---

## STEP 01 — Repository Audit & Architecture Blueprint
- Audit host environment, workspace, tools, SDK, PostgreSQL, Docker availability.
- Provision `.NET 10.0 LTS` and `PostgreSQL 17` locally for authentic compilation and database verification.
- Author architecture docs: `ARCHITECTURE.md`, `ADR-001-MODULAR-MONOLITH.md`, `DOMAIN-BOUNDARIES.md`, `TECHNOLOGY-DECISIONS.md`, `IMPLEMENTATION-PLAN.md`.

## STEP 02 — Solution Scaffolding & Core Architecture
- Create `NEXUS.sln`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`, `.editorconfig`.
- Create `Nexus.SharedKernel`, `Nexus.BuildingBlocks`, all 11 Module projects under `src/Modules/`, `Nexus.Bootstrap`, `Nexus.Api`, and all 5 test projects under `tests/`.
- Wire DI composition, centralized configuration, Serilog structured logging, OpenTelemetry, global RFC 9457 Problem Details exception handler, Correlation ID middleware, Security Headers, OpenAPI (`/openapi/v1.json` + `/scalar/v1`), and `/health/live` + `/health/ready`.
- Verify `dotnet build` and baseline architecture/smoke tests.

## STEP 03 — Tenancy & Organization Foundation
- Implement `Tenancy` (`Tenant`, `UserTenantMembership`) and `Organization` (`LegalEntity`, `Branch`, `OrganizationUnit`, `UserOrganizationAssignment`) domains, services, EF Core configurations, and PostgreSQL migrations.
- Implement `TenantContextMiddleware`, EF Core global tenant query filters, and PostgreSQL `FORCE ROW LEVEL SECURITY` policies + `set_config('app.current_tenant_id', ..., true)` transaction-scoped interceptor.
- Create least-privilege `nexus_app` database role and verify RLS isolation and connection-pool non-leakage on real PostgreSQL 17.
- Expose REST endpoints under `/api/v1/platform/tenants` and `/api/v1/platform/organizations`.

## STEP 04 — Identity & Access Control
- Implement `Identity` using ASP.NET Core Identity (`User`, `UserProfile`, `RefreshToken`, `UserSession`, `ExternalIdentity`, `LoginAttempt`) with short-lived JWTs, refresh token rotation, family reuse detection, lockout, password change/reset, email verification tokens, and session revocation.
- Implement `AccessControl` (`Role`, `Permission`, `RolePermission`, `UserRole`, `RoleScope`) with Platform vs. Tenant scopes, default role catalog (`PlatformSuperAdmin`, `PlatformSupport`, `TenantOwner`, `TenantAdmin`, `CFO`, `Accountant`, `LogisticsDirector`, `WarehouseManager`, `Employee`, `Auditor`), custom `IAuthorizationPolicyProvider` + `PermissionAuthorizationHandler`, and Separation of Duties enforcement.
- Expose `/api/v1/auth/*`, `/api/v1/platform/users/*`, and `/api/v1/platform/roles/*`.

## STEP 05 — Platform Governance Services
- Implement `Audit` (append-only `AuditEvent`, sensitive-field scrubber, cross-tenant elevated audit, DB immutability trigger & grants).
- Implement `Workflow` (definitions, versions, instances, approval tasks, decisions, history, Maker–Checker rule, optimistic concurrency).
- Implement `Subscription` (plans, plan features, tenant subscriptions, items, entitlements, usage records, `IEntitlementChecker`).
- Implement `Configuration` (platform settings, tenant settings, feature flags, localization, version history, plaintext-secret rejection).
- Implement full User Management endpoints (invite, revoke membership, disable user, manage sessions, assign role, org assignment).

## STEP 06 — Integration & Event Foundation
- Implement API standards across all controllers: `/api/v1`, unified `ApiResponse<T>`, RFC 9457 `ProblemDetails`, pagination, filtering, sorting, rate limiting, `Idempotency-Key` middleware, Correlation ID.
- Implement `Messaging`: `EventEnvelope`, Transactional Outbox, Inbox idempotency, retry with exponential backoff, Dead Letter Queue, and `RabbitMqEventBus` (with RabbitMQ v7 client + graceful in-memory/loopback test bus for CI without RabbitMQ broker).
- Implement `Integration` Hub (providers, connections with credential references only, sync logs, errors, retry policy, webhook registrations) + AI Gateway Foundation (`IAiProvider`, model config, sanitized request logging, usage tracking, timeout, rate limit, entitlement gate).
- Implement `Notification` Foundation (templates, in-app notifications, `IEmailSender` abstraction + test adapter, delivery status & retry).
- Implement `FileStorage` Foundation (`IObjectStorage` S3-compatible + local dev adapter, tenant-isolated object metadata, MIME/size validation, SHA-256 hash, signed temporary download URLs).

## STEP 07 — Automated Testing, Security Hardening, DevOps & Release
- Execute full test suites:
  - `Nexus.UnitTests`
  - `Nexus.ArchitectureTests`
  - `Nexus.IntegrationTests` (real PostgreSQL 17: migrations, constraints, rollback, RLS with `nexus_app`, pooled connection isolation, Outbox/Inbox, concurrency)
  - `Nexus.ApiTests` (HTTP 200/201/204/400/401/403/404/409/429/500, Problem Details, OpenAPI, pagination, idempotency)
  - `Nexus.SecurityTests` (all 15 mandatory security test cases + concurrency race tests)
- Author DevOps artifacts: `Dockerfile`, `docker-compose.yml`, `.env.example`, `.github/workflows/ci.yml`, `infrastructure/database/init-roles.sql`, and all deployment/API/DB/security docs (`README.md`, `docs/deployment/*`, `docs/api/*`, `docs/database/*`, `docs/security/*`).
- Run `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release`, `dotnet format --verify-no-changes`, `dotnet list package --vulnerable`, fix any failures, and produce an honest final engineering report.
