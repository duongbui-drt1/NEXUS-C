# NEXUS C — Domain Boundaries & Module Ownership

This document defines the explicit ownership, PostgreSQL schemas, public contracts, and integration boundaries for every bounded context in NEXUS C Foundation.

---

## 1. Bounded Context Matrix

| Module | Project | PostgreSQL Schema | Core Aggregates / Entities | Owns Business Rules For |
|---|---|---|---|---|
| **Tenancy** | `Nexus.Modules.Tenancy` | `tenancy` | `Tenant`, `UserTenantMembership` | Tenant lifecycle (Active/Suspended/Archived), slugs/codes, timezone/currency/country, user-to-tenant membership lifecycle |
| **Organization** | `Nexus.Modules.Organization` | `organization` | `LegalEntity`, `Branch`, `OrganizationUnit`, `UserOrganizationAssignment` | Legal entities (TIN), branches under legal entities, hierarchical org units (cycle detection), tenant-scoped structural validation |
| **Identity** | `Nexus.Modules.Identity` | `identity` | `User` (ASP.NET Core Identity), `UserProfile`, `RefreshToken`, `UserSession`, `ExternalIdentity`, `LoginAttempt` | Credential verification, Argon2/PBKDF2 via ASP.NET Core Identity, JWT issuance, refresh token rotation & reuse detection, lockout, session revocation, MFA/OIDC hooks |
| **AccessControl** | `Nexus.Modules.AccessControl` | `access_control` | `Role`, `Permission`, `RolePermission`, `UserRole`, `RoleScope` | Platform vs. Tenant RBAC, ABAC-ready scope attributes, permission catalog, default role seeding, Separation of Duties checks |
| **Audit** | `Nexus.Modules.Audit` | `audit` | `AuditEvent` | Append-only audit trail, sensitive field redaction, platform cross-tenant access logging, tamper resistance (DB role `INSERT/SELECT` only + immutability trigger) |
| **Workflow** | `Nexus.Modules.Workflow` | `workflow` | `WorkflowDefinition`, `WorkflowVersion`, `WorkflowInstance`, `ApprovalTask`, `ApprovalDecision`, `WorkflowHistory` | State machine (`Draft -> Pending -> Approved/Rejected/Cancelled/Expired`), Maker–Checker separation (creator cannot self-approve), optimistic concurrency on approval tasks |
| **Subscription** | `Nexus.Modules.Subscription` | `subscription` | `Plan`, `PlanFeature`, `TenantSubscription`, `SubscriptionItem`, `Entitlement`, `UsageRecord` | SaaS plans, tenant plan assignment, product enablement (`NEXUS_F`, `NEXUS_E`, `NEXUS_M`, `AI_GATEWAY`), feature quota & entitlement checks |
| **Configuration** | `Nexus.Modules.Configuration` | `configuration` | `PlatformSetting`, `TenantSetting`, `FeatureFlag`, `SettingHistory` | Versioned platform/tenant configuration, localization settings, feature flags; explicitly forbids plaintext secret storage |
| **Integration** | `Nexus.Modules.Integration` | `integration` | `ProviderDefinition`, `IntegrationConnection`, `SyncLog`, `IntegrationError`, `WebhookRegistration`, `AiModelConfig`, `AiUsageLog` | Connector registry, credential-reference metadata (never raw secrets), retry policy, sync history, webhook registry, AI Gateway provider orchestration & sanitized telemetry |
| **Notification** | `Nexus.Modules.Notification` | `notification` | `NotificationTemplate`, `NotificationMessage`, `DeliveryAttempt` | In-app notifications, templated rendering, email provider abstraction (`IEmailSender`), delivery tracking & retry |
| **FileStorage** | `Nexus.Modules.FileStorage` | `file_storage` | `StoredFileObject` | Tenant-isolated file metadata, SHA-256 hash, MIME/size validation, S3-compatible storage abstraction, signed temporary download tokens |
| **Messaging** | `Nexus.BuildingBlocks` | `messaging` | `OutboxMessage`, `InboxMessage`, `DeadLetterMessage`, `IdempotencyRecord` | Transactional Outbox, idempotent Inbox, dead-letter persistence, HTTP idempotency tracking |

---

## 2. Cross-Module Communication Rules

1. **Reference by Identifier Only:** Modules store foreign IDs (`Guid TenantId`, `Guid UserId`, `Guid LegalEntityId`) without cross-schema ORM navigation properties.
2. **Synchronous Read Contracts:** Allowed strictly through narrow interfaces in `Nexus.SharedKernel` or module `Contracts/` (for example, `ITenantAccessValidator`, `IPermissionEvaluator`, `IAuditWriter`, `IEntitlementChecker`).
3. **Asynchronous Write Propagation:** State transitions publish versioned events via the Transactional Outbox (`platform.tenant.created.v1`, `platform.user.created.v1`, `platform.subscription.activated.v1`, `platform.workflow.approved.v1`, etc.).
4. **No Circular Module Dependencies:** Dependency graph is strictly acyclic, rooted at `Nexus.SharedKernel` -> `Nexus.BuildingBlocks` -> Modules -> `Nexus.Bootstrap` -> `Nexus.Api`.

---

## 3. Extension Boundaries for NEXUS F, NEXUS E, and NEXUS M

Future business modules will plug into NEXUS C without modifying Foundation internals:
- **Authentication & Context:** Validate NEXUS C JWTs or inherit `ITenantContext` / `ICurrentUser`.
- **Authorization:** Register product-scoped permissions (e.g., `finance.journals.post`, `express.shipments.dispatch`, `management.kpi.read`) into the `AccessControl` permission catalog.
- **Entitlements:** Gate module APIs via `IEntitlementChecker.HasFeatureAsync(tenantId, "NEXUS_F")`.
- **Approvals:** Instantiate Maker–Checker workflows via `Workflow` contracts (`finance.journal.approval`).
- **Events:** Consume `platform.*` events and publish domain events (`finance.invoice.issued.v1`, `warehouse.stock.issued.v1`, `shipment.delivered.v1`) using the shared `EventEnvelope`.
- **Audit & Files:** Emit audit events via `IAuditWriter` and store invoices/PODs via `FileStorage` tenant-scoped buckets/prefixes.
