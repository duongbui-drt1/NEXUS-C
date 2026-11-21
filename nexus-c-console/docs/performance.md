# NEXUS C Console — Performance Budget & Measured Production Bundle Report

## 1. Performance Budget vs. Measured Production Build (`vite build`)

| Metric | Target Budget | Measured Result | Status |
|---|---|---|---|
| **Initial JS (gzip)** | ≤ 250 KB | **~103.95 KB** (`vendor-react` 65.70 KB + `vendor-router` 14.07 KB + `vendor-query` 8.96 KB + `index` 11.84 KB + `client` 3.02 KB + runtime 0.36 KB) | **PASS (< 42% of budget)** |
| **Initial CSS (gzip)** | ≤ 60 KB | **2.62 KB** (`10.47 KB` uncompressed) | **PASS (< 5% of budget)** |
| **Lazy Route Chunks (gzip)** | Required | 12 separate lazy page chunks (`1.25 KB` – `4.46 KB` gzip each) | **PASS** |
| **Form Validation Chunk** | On-demand | `vendor-forms` (`37.46 KB` gzip) loaded only on routes with Zod/RHF forms | **PASS** |
| **Polling Policy** | Zero 1s polling | `staleTime: 30s–300s`, `refetchOnWindowFocus: false`, manual refresh buttons | **PASS** |
| **Server Pagination** | Required | Implemented across Tenants, Users, Organizations, Audit, Notifications | **PASS** |
| **Heavy Libraries** | Prohibited | Zero MUI/AntD, zero chart libraries, zero animation engines, tree-shaken `lucide-react` | **PASS** |

---

## 2. Production Chunk Breakdown (`dist/assets/`)

- `index.css`: `10.47 kB` (**`2.62 kB` gzip**)
- `vendor-react.js`: `210.53 kB` (**`65.70 kB` gzip**)
- `vendor-router.js`: `39.24 kB` (**`14.07 kB` gzip**)
- `vendor-query.js`: `28.91 kB` (**`8.96 kB` gzip**)
- `index.js`: `36.99 kB` (**`11.84 kB` gzip**)
- `vendor-forms.js` (lazy): `121.42 kB` (**`37.46 kB` gzip**)
- Lazy route chunks: `ApiManagementPage` (`4.46 kB` gz), `TenantsPage` (`3.19 kB` gz), `SettingsPage` (`2.64 kB` gz), `OrganizationsPage` (`2.47 kB` gz), `UsersPage` (`2.44 kB` gz), `IntegrationsPage` (`2.44 kB` gz), `SubscriptionsPage` (`2.42 kB` gz), `ProfileSessionsPage` (`1.90 kB` gz), `OverviewPage` (`1.88 kB` gz), `AccessControlPage` (`1.83 kB` gz), `AuditPage` (`1.66 kB` gz), `LoginPage` (`1.44 kB` gz), `ApplicationsPage` (`1.38 kB` gz), `OperationsPage` (`1.25 kB` gz).
