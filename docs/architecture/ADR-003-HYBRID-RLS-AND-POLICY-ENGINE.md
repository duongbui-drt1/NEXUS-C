# ADR-003: Hybrid PostgreSQL RLS & Organization-Scoped Policy Engine

**Status:** Accepted (NEXUS C 1.0 Foundation Freeze)  
**Date:** 2026-11-22

## Context
NEXUS C stores both global/platform metadata (permission catalog, system tenant roles, global feature flags, platform audit records) and tenant-owned records (custom tenant roles, user-role assignments, tenant overrides, tenant audit records) inside shared PostgreSQL schemas. A naive `TenantId = current_tenant_id` RLS rule either breaks global catalog reads or fails to isolate hybrid tables (`access_control.roles`, `access_control.role_permissions`, `access_control.user_roles`, `configuration.feature_flags`, `audit.audit_events`). Additionally, enterprise applications (F/E/M) need organizational-scope isolation (LegalEntity, Branch, OrganizationUnit hierarchy) and explicit deny semantics without duplicating authorization logic.

## Decision
1. **33 Tables under PostgreSQL `FORCE ROW LEVEL SECURITY`:**
   - 28 strictly tenant-owned tables enforce `"TenantId" = current_setting('app.current_tenant_id')::uuid` (or `app.bypass_rls = 'on'`), failing closed when no tenant context is present.
   - 5 hybrid tables (`access_control.roles`, `access_control.role_permissions`, `access_control.user_roles`, `configuration.feature_flags`, `audit.audit_events`) enforce split SELECT/WRITE policies driven by `app.is_platform_scope` and `app.current_tenant_id` GUCs, while `access_control.permissions` remains a read-only global catalog.
   - `audit.audit_events` enforces `FORCE RLS` (SELECT + INSERT policies only, default-deny UPDATE/DELETE) AND `SELECT, INSERT`-only privileges for `nexus_app` AND a `BEFORE UPDATE OR DELETE` PostgreSQL trigger.
2. **Scoped Policy Evaluation (`POST /api/v1/auth/authorize`) & Permission Cache:**
   - `AccessControlService.EvaluatePolicyAsync` evaluates explicit `Deny` before `Allow`, supports `(ResourceScopeType, ResourceScopeId)` scoped role assignments, and checks `IOrganizationScopeReader` (resolving LegalEntity -> Branch -> OrgUnit active subtrees) so branch-assigned users cannot access other branches.
   - Effective user permissions are cached in `IDistributedCache` (`perm:v1:{scope}:{tenantId}:{userId}`, 2-minute TTL) with immediate invalidation on role assignment/revocation.

## Alternatives Rejected
- **Application-only EF Core query filters without DB RLS:** Rejected because a single `IgnoreQueryFilters()` or raw SQL bug would leak cross-tenant data.
- **Database-per-tenant or schema-per-tenant:** Rejected for 1.0 due to connection-pool explosion, migration fan-out operational risk, and unnecessary complexity when PostgreSQL 17 `FORCE RLS` + `NOBYPASSRLS` provides strong physical enforcement.
- **External OPA / Zanzibar sidecar:** Rejected to avoid introducing extra runtime network hops and infrastructure complexity inside the Modular Monolith core.
