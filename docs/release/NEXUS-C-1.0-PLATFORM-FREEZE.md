# NEXUS C 1.0 — Platform Freeze & Governance Policy

**Status:** Release Candidate Ready for Freeze (`release/nexus-c-1.0-foundation`)  
**Effective Version:** `1.0.0`  
**Date:** 2026-11-22

---

## 1. Frozen Core Scope

NEXUS C 1.0 freezes the shared multi-tenant platform foundation for the NEXUS Enterprise SaaS ecosystem (**NEXUS F**, **NEXUS E**, **NEXUS M**):

1. **Identity & Sessions:** ASP.NET Core Identity, JWT access tokens, SHA-256 rotating refresh tokens with reuse detection, opt-in HttpOnly/SameSite cookie refresh with CSRF guard, OAuth 2.0 Client Credentials (`POST /api/v1/auth/token`), Scoped API Keys (`X-Api-Key`), and `IExternalIdentityProvider` contract.
2. **Multi-Tenancy & Isolation:** Tenant lifecycle (`Active`/`Suspended`/`Archived`), cross-tenant membership controls, anti-spoofing middleware, and **33 PostgreSQL 17 `FORCE ROW LEVEL SECURITY` tables** enforced under `nexus_app` (`NOSUPERUSER`, `NOBYPASSRLS`).
3. **Organization & Policy Authorization:** LegalEntity / Branch / OrganizationUnit hierarchy (with cycle detection & active/inactive lifecycle), effective organizational scope (`GET /api/v1/auth/organization-scope`), RBAC + ABAC + Explicit Deny (`POST /api/v1/auth/authorize`), effective permissions (`GET /api/v1/auth/permissions`), and distributed permission cache invalidation.
4. **Shared Workflow Engine:** Immutable versioned workflow definitions, Sequential/Parallel approvals, Maker–Checker enforcement, role eligibility, time-bounded `ApprovalDelegation`, per-task delegation, SLA/escalation, `FOR UPDATE` concurrency protection, and complete execution history.
5. **Events, Webhooks & Operations:** Versioned `EventEnvelope` (`Producer`, `CausationId`, `CorrelationId`), Transactional Outbox (`FOR UPDATE SKIP LOCKED`), Idempotent Inbox, RabbitMQ v7 + DLQ, Platform Operations API (`/api/v1/platform/operations/*`), and complete SSRF-protected Webhook lifecycle with HMAC-SHA256 replay-safe signatures.
6. **Ecosystem Application Registry & Authoritative Entitlements:** Pre-seeded `NEXUS_C`/`NEXUS_F`/`NEXUS_E`/`NEXUS_M` registry, tenant activation/deactivation with dependency validation, authoritative entitlement evaluation (`/api/v1/platform/entitlements/{featureCode}`), plan downgrade revocation, feature flag kill-switch integration, and idempotent usage ingestion.
7. **Shared Platform Services & Console:** Hardened Configuration (secret-key rejection + history), Notifications (`InApp`/`Email`/`Webhook` + preferences + retry), File Storage (MIME allowlist + retention expiry + signed URLs + download audit), Immutable Audit Center, and NEXUS C Console with fail-closed RBAC.

---

## 2. Post-Freeze Change Governance

### Permitted Under Controlled 1.0.x Maintenance
- Security patches and vulnerability remediation.
- Critical data-integrity, reliability, and performance fixes.
- Additive, backward-compatible contract extensions and dependency updates.
- Required non-breaking database migration fixes.

### Changes Requiring Formal Architectural Review & ADR
- Any breaking `/api/v1/` contract change (requires `/api/v2/`).
- Changes to PostgreSQL RLS policies, tenant isolation semantics, or `nexus_app` privileges.
- Changes to `Permissions.Catalog`, authorization policy evaluation semantics, or authentication protocols.
- Changes to `EventEnvelope`, frozen v1 event payload schemas, Application Registry semantics, or Entitlement evaluation rules.

### Explicitly Prohibited Inside NEXUS C
- Accounting ledgers, tax calculations, invoicing, or financial reporting (**NEXUS F**).
- Shipment routing, warehouse execution, fleet dispatch, or carrier rate engines (**NEXUS E**).
- Executive KPI scorecards, OKRs, business planning, or management BI (**NEXUS M**).
- Experimental unapproved shared abstractions or competing duplicate infrastructure.
