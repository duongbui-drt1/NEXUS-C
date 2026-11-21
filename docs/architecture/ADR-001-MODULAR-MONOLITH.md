# ADR-001: Modular Monolith Architecture for NEXUS C Foundation

- **Status:** Accepted
- **Date:** 2026-11-22
- **Deciders:** Principal Software Architecture (NEXUS Foundation Engineering)

---

## 1. Context

NEXUS is a multi-product enterprise SaaS ecosystem consisting of:
- **NEXUS C (Center):** Platform core & governance services.
- **NEXUS F (Finance):** Accounting, invoicing, treasury (future phase).
- **NEXUS E (Express):** Warehouse & logistics (future phase).
- **NEXUS M (Management):** Executive analytics & KPIs (future phase).

During the Foundation stage, the platform must deliver 10+ tightly coordinated core capabilities (Tenancy, Organization, Identity, Access Control, Audit, Workflow, Subscription, Configuration, Integration Hub, Notification, File Storage) with strong transactional consistency, strict multi-tenant security, and low operational overhead while preserving clean extraction boundaries for future scale.

---

## 2. Decision

We adopt a **Modular Monolith** built on **ASP.NET Core (.NET 10 LTS)** and **PostgreSQL 17** with:
1. **Explicit Module Projects** (`src/Modules/Nexus.Modules.<Name>`) with compile-time and automated architecture-test boundary enforcement.
2. **Schema-per-Module Ownership** in PostgreSQL (`tenancy`, `organization`, `identity`, `access_control`, `audit`, `workflow`, `subscription`, `configuration`, `integration`, `notification`, `file_storage`, `messaging`).
3. **Separate Module `DbContext`s** inheriting from `ModuleDbContextBase` so no module can accidentally query or mutate another module's tables.
4. **Asynchronous Integration via Transactional Outbox + RabbitMQ** for cross-domain and future F/E/M communication, alongside narrow synchronous read contracts only where strictly needed (e.g., AuthN/AuthZ validation).

---

## 3. Alternatives Considered

### Option A: Microservices from Day 1
- **Rejected:** Introduces distributed transactions, network latency across core auth/tenant/audit checks, complex local dev setup, and high SRE burden before domain boundaries have stabilized across F/E/M.

### Option B: Traditional Layered Monolith (Single DbContext, Shared Entities)
- **Rejected:** Leads to a "big ball of mud" where Identity, Tenancy, Workflow, and future Finance tables become tightly coupled via foreign keys and shared services, making independent evolution impossible.

---

## 4. Consequences

### Positive
- Single deployable host (`Nexus.Api`) with simple debugging, atomic local transactions, and zero distributed-trace hop overhead on core platform checks.
- Strict schema and assembly boundaries allow extracting any module (e.g., Notification, FileStorage, or future NEXUS F/E/M services) into an independent service later with minimal refactoring because events already flow through the Outbox/RabbitMQ contract.
- Automated `Nexus.ArchitectureTests` continuously verify that module boundaries never erode.

### Negative / Trade-offs
- Requires discipline around cross-module contracts (no direct table joins across module schemas; use IDs and contracts).
- Multiple `DbContext` migrations must be coordinated by a deterministic migration runner in `Nexus.Bootstrap`.
