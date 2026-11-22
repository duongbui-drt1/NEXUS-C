# ADR-006: Ecosystem Application Registry & Authoritative Entitlement Evaluation

**Status:** Accepted (NEXUS C 1.0 Foundation Freeze)  
**Date:** 2026-11-22

## Context
NEXUS C must know which products belong to the NEXUS ecosystem (`NEXUS_C`, `NEXUS_F`, `NEXUS_E`, `NEXUS_M`), govern their compatibility and dependencies, and provide a single authoritative entitlement decision so a tenant licensed for NEXUS M cannot access NEXUS F or E, and downgraded/suspended tenants lose access immediately.

## Decision
1. **Cohesive Ownership in `subscription` Schema:**
   - `RegisteredApplication` (`subscription.applications`) stores platform catalog metadata (`Code`, `CurrentVersion`, `ReleaseChannel`, `Status`, `MinPlatformVersion`, `RequiredCapabilities`, `ModuleDependencies`, `CompatibilityMetadataJson`), pre-seeded idempotently with `NEXUS_C`, `NEXUS_F`, `NEXUS_E`, and `NEXUS_M`.
   - `TenantApplicationActivation` (`subscription.tenant_applications`, `FORCE RLS`) records per-tenant activation/deactivation state, enforcing active subscription entitlement and declared module dependencies before activation.
   - Registry presence explicitly represents contract & compatibility metadata — never fake deployment health.
2. **Authoritative Multi-Factor Entitlement Engine (`GET /api/v1/platform/entitlements/{featureCode}`):**
   - Evaluates: (a) active/trial non-expired `TenantSubscription`, (b) enabled non-expired `Entitlement` (stale entitlements from prior plans are explicitly revoked on plan change), (c) `TenantApplicationActivation` not deactivated, (d) tenant/global `FeatureFlag` kill-switch via `IFeatureFlagEvaluator`, and (e) metered `UsageRecords` vs `QuotaLimit`.
   - Returns structured denial reasons (`NO_ACTIVE_SUBSCRIPTION`, `SUBSCRIPTION_SUSPENDED`, `FEATURE_NOT_ENTITLED`, `APPLICATION_DEACTIVATED`, `FEATURE_FLAG_DISABLED`, `QUOTA_EXCEEDED`).
   - Supports idempotent usage metering (`UsageRecord.IdempotencyKey` partial unique index).

## Alternatives Rejected
- **Separate 12th module just for Application Registry:** Rejected because Entitlement evaluation requires atomic consistency with `TenantSubscription`, `Entitlement`, and `UsageRecord` inside one bounded context without cross-module joins.
- **Client-side / frontend entitlement computation:** Rejected because commercial access control MUST be authoritative server-side.
