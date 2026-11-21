# NEXUS C Console

Enterprise SaaS Platform Control Center for **NEXUS C (Center)** — built with **Vite 8 + React 19 + TypeScript (strict)**, **TanStack Query 5**, **React Router 7**, **React Hook Form + Zod**, and custom **NEXUS Design System Tokens** (zero heavyweight UI frameworks).

## Key Engineering Characteristics

- **Enterprise Minimalism:** Quiet, structured, desktop-first administration UI with Light Mode default and optional Dark Mode.
- **Strict Session & RBAC Security:** Access and refresh tokens are kept strictly in memory (`tokenStore.ts`) — never persisted to `localStorage` ([ADR-002](../docs/architecture/ADR-002-CONSOLE-SESSION-STORAGE.md)). Single-flight refresh locking prevents concurrent refresh-token reuse triggers, and navigation/routes enforce fail-closed RBAC hydrated from `GET /api/v1/auth/permissions`.
- **Zero Fake Production Data:** Every supported screen connects directly to live NEXUS C Backend endpoints (`/api/v1/*`, `/health/*`, `/openapi/v1.json`). Unimplemented backend endpoints display honest `Not Available` panels documented in [docs/api-integration.md](docs/api-integration.md).
- **Low Resource Footprint:** Route-level `React.lazy` code splitting, vendor chunk splitting, server-side pagination, and zero aggressive polling.

## Development & Verification Commands

```bash
npm install
npm run typecheck     # TypeScript strict check
npm run lint          # ESLint (zero explicit any)
npm test              # Vitest + React Testing Library suite
npm run build         # Production static build (dist/)
npm run dev           # Local dev server (proxies /api, /health, /openapi to http://127.0.0.1:5000)
```

## Documentation

- [Frontend Architecture](docs/architecture.md)
- [Backend API Integration & Gap Matrix](docs/api-integration.md)
- [Performance Budget & Bundle Report](docs/performance.md)
- [ADR-002: Console Session Storage & Refresh Strategy](../docs/architecture/ADR-002-CONSOLE-SESSION-STORAGE.md)
