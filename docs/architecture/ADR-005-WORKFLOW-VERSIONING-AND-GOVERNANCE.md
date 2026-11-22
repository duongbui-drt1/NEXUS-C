# ADR-005: Shared Workflow Engine Versioning & Governance Model

**Status:** Accepted (NEXUS C 1.0 Foundation Freeze)  
**Date:** 2026-11-22

## Context
NEXUS F (journal/invoice approvals), NEXUS E (exception/dispatch approvals), and NEXUS M (budget/policy approvals) all need reliable Maker–Checker approval workflows. Embedding domain-specific approval logic or hardcoding role names like CFO/CEO in NEXUS C violates separation of concerns, while allowing mutable definitions risks silently corrupting in-progress approvals.

## Decision
1. **Immutable Version Pinning:**
   - Every `WorkflowInstance` references a specific `WorkflowVersion.Id` (`workflow.versions`). Governance rules (`RequiredApprovals`, `ApproverRoleName`, `ExpirationHours`, `RequireMakerChecker`, `ApprovalMode`, `ConditionExpressionJson`, `EscalationHours`, `EscalationRoleName`) are stored on `WorkflowVersion`.
   - Publishing a new version (`POST /definitions/{id}/versions`) increments `CurrentVersionNumber` and creates a new `WorkflowVersion` row without modifying any existing `WorkflowInstance`.
2. **Generic Configurable Governance:**
   - Supports both `Sequential` (step N cannot be decided while step < N is pending) and `Parallel` approval modes.
   - Enforces Maker–Checker (`InitiatorUserId != actorId` when `RequireMakerChecker == true`).
   - Enforces role eligibility (`ApproverRoleName` / `EscalatedToRoleName`) combined with standing time-bounded `ApprovalDelegation` (`ValidFrom <= now < ValidTo`) or explicit per-task delegation (`POST /tasks/{id}/delegate`), all strictly RLS-isolated.
   - Concurrency-safe task decisions use PostgreSQL `SELECT ... FOR UPDATE` row locks + unique `ApprovalDecision` constraint on `ApprovalTaskId`.
   - Every state transition writes `WorkflowHistory`, immutable `AuditEvent`, and transactional `OutboxMessage` events (`submitted`, `approved`, `rejected`, `cancelled`, `delegated`, `escalated`).

## Alternatives Rejected
- **External BPMN engine (Camunda/Temporal):** Rejected as over-engineering for 1.0; adds JVM/Go cluster management outside the .NET 10 + PostgreSQL 17 operational stack.
- **Per-application custom approval tables in F/E/M:** Rejected because Maker–Checker, delegation, concurrency locking, and auditability are cross-cutting platform governance responsibilities.
