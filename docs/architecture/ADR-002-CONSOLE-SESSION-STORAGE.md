# ADR-002: NEXUS C Console Session Storage & Cookie/BFF Strategy

- **Status:** Accepted (Foundation Phase)
- **Date:** 2026-11-22
- **Deciders:** Security & Frontend Architecture

---

## 1. Context

NEXUS C Backend currently exposes a JSON token-based authentication contract (`POST /api/v1/auth/login` and `POST /api/v1/auth/refresh` returning `{ accessToken, refreshToken }` in JSON) suitable for multi-client federation (Web Console, Electron Desktop, CLI, future NEXUS F/E/M services).

Browser clients face XSS exfiltration risk if refresh tokens are persisted in `localStorage` or `sessionStorage`.

---

## 2. Decision

1. **Current Hardened Baseline (Retained):** NEXUS C Console stores `accessToken` and `refreshToken` **strictly in JavaScript memory** (`src/services/auth/tokenStore.ts`) with a single-flight refresh lock preventing concurrent refresh reuse detection. Tokens are NEVER written to `localStorage`, `sessionStorage`, or URLs. Page refresh cleanly requires re-authentication rather than weakening XSS defenses.
2. **Deferred Future Upgrade (HttpOnly Cookie / BFF Mode):** Moving to silent page-reload persistence requires a dedicated backend cookie contract (`Set-Cookie: nexus_refresh=...; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth/refresh`), strict `Origin` + double-submit CSRF token validation, and explicit cookie domain/TLS configuration. Per RULE 04, we intentionally do not half-implement cookie mode without full backend CSRF/domain parity; this is tracked as a dedicated post-foundation hardening task.
