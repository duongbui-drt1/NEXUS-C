# NEXUS C Console

Enterprise SaaS Platform Control Center for **NEXUS C (Center)** — built with **Vite + React 19 + TypeScript (strict)**, **TanStack Query**, **React Hook Form + Zod**, and custom **NEXUS Design System Tokens** (zero heavyweight UI frameworks).

## Key Engineering Characteristics

- **Enterprise Minimalism:** Quiet, structured, desktop-first administration UI with Light Mode default and optional Dark Mode.
- **Strict Security:** Access and refresh tokens are kept strictly in memory (`tokenStore.ts`) — never persisted to `localStorage`. Single-flight refresh locking prevents concurrent refresh-token reuse triggers.
- **Zero Fake Production Data:** Every supported screen connects directly to live NEXUS C Backend endpoints (`/api/v1/*`, `/health/*`, `/openapi/v1.json`). Unimplemented backend endpoints display honest `Not Available` panels documented in [docs/api-integration.md](docs/api-integration.md).
- **Low Resource Footprint:** Route-level `React.lazy` code splitting, vendor chunk splitting, server-side pagination, and zero aggressive polling.

## Development & Verification Commands

```bash
npm install
npm run typecheck     # TypeScript strict check
npm run lint          # ESLint (zero explicit any)
npm test              # Vitest + React Testing Library suite
npm run build         # Production static build (dist/)
npm run dev           # Local dev server (proxies /api, /health, /openapi to http://localhost:5000)
```

## Documentation

- [Frontend Architecture](docs/architecture.md)
- [Backend API Integration & Gap Matrix](docs/api-integration.md)
- [Performance Budget & Bundle Report](docs/performance.md)
