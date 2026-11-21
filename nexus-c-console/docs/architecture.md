# NEXUS C Console — Frontend Architecture

## 1. Architectural Principles

- **Enterprise Minimalism:** High-density, quiet, structured UI built with custom CSS Variables (NEXUS Design Tokens) and zero heavyweight UI frameworks (no MUI, no Ant Design, no animation engines).
- **Feature-Based Modularity:** Each platform domain (`overview`, `tenants`, `organizations`, `users`, `access-control`, `applications`, `api-management`, `integrations`, `operations`, `audit`, `subscriptions`, `settings`) owns its views, hooks, and domain UI while sharing reusable primitives from `src/components/` and centralized API modules from `src/services/api/`.
- **Backend as Single Source of Truth:** No client-side fake data, no optimistic permission grants, and strict server-side pagination/filtering.

---

## 2. State Management Separation

| State Category | Solution | Rationale |
|---|---|---|
| **Server State** | `@tanstack/react-query` | Caching, request deduplication, `AbortSignal` cancellation, controlled retries, zero aggressive polling |
| **Authentication / Session** | React Context (`AuthProvider`) + in-memory token store | Zero tokens in `localStorage`/`sessionStorage`; single-flight refresh lock prevents refresh-token reuse race conditions |
| **Form State** | `react-hook-form` + `zod` | Minimal re-renders, strict client-side schema validation mirroring backend rules |
| **UI State** | Local `useState` + `ThemeContext` | Only non-sensitive preferences (`theme`, `sidebarCollapsed`) stored in `localStorage` |

---

## 3. Security Controls

1. **In-Memory Token Isolation:** Access and refresh tokens live strictly in memory inside `tokenStore.ts` — never persisted to `localStorage`, `sessionStorage`, or URLs.
2. **Single-Flight Refresh Lock:** Concurrent `401` responses share one in-flight `POST /api/v1/auth/refresh` promise so two simultaneous requests never present the same rotated refresh token (which would trigger backend reuse detection).
3. **Permission-Aware UX Layer:** Navigation and action buttons respect verified user scope (`platform` vs `tenant`) and resolved backend permissions (`GET /api/v1/platform/roles/users/{id}`), while backend authorization remains authoritative.
4. **Safe API Explorer:** Parses live `/openapi/v1.json`; non-GET endpoints cannot be executed blindly and always require explicit confirmation and live backend permission checks.
5. **Zero Secret Exposure:** `VITE_*` variables carry only public non-secret configuration (`VITE_APP_NAME`, `VITE_API_BASE_URL`, `VITE_APP_ENV`).
