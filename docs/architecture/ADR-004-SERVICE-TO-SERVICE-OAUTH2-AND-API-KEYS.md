# ADR-004: Service-to-Service OAuth 2.0 Client Credentials & Scoped API Keys

**Status:** Accepted (NEXUS C 1.0 Foundation Freeze)  
**Date:** 2026-11-22

## Context
NEXUS F (Finance), NEXUS E (Express), NEXUS M (Management), and enterprise partner integrations must authenticate machine-to-machine calls against NEXUS C without sharing a PlatformSuperAdmin account or relying on unscoped wildcard API keys.

## Decision
1. **OAuth 2.0 Client Credentials (`POST /api/v1/auth/token`):**
   - Registered `ServiceClient` records (`identity.service_clients`) store a per-client salt and SHA-256 secret hash (`SecretSalt`, `ClientSecretHash`); plaintext secrets are returned strictly ONCE at creation or rotation (`POST /api/v1/platform/api-clients/{id}/rotate-secret`).
   - Wildcard scopes (`*`) are explicitly forbidden (`403 WILDCARD_SCOPE_FORBIDDEN`). Every client declares explicit least-privilege permission codes from `Permissions.Catalog`.
   - Issued service JWTs have a short 10-minute lifetime, `scope: "service"`, `client_id`, optional `app_code`, optional `tid`, and explicit `scp` claims.
   - `TenantContextMiddleware` verifies live `ServiceClient.IsActive` status and intersects JWT `scp` claims with live `AllowedScopes` on every request, ensuring revocation takes effect immediately without waiting for token expiry.
2. **Scoped API Keys (`X-Api-Key`):**
   - `ApiKeyRecord` (`identity.api_keys`) stores strictly one-way SHA-256 hashes and a short prefix (`nxk_...`), binds optional tenant context and expiration, and passes through the exact same `PermissionAuthorizationHandler` scope check — never bypassing RBAC.
3. **Human Refresh Token Cookie Option & Future SSO Contract:**
   - `AuthController` preserves backward-compatible JSON refresh flows while supporting opt-in `HttpOnly; SameSite=Strict` refresh cookies (`X-Nexus-Auth-Mode: cookie`) protected by a mandatory custom CSRF header (`X-Nexus-CSRF: 1`).
   - External enterprise SSO federation (OIDC/SAML 2.0) is standardized via `IExternalIdentityProvider` in `Nexus.SharedKernel.Contracts` without shipping an incomplete SSO broker in 1.0.

## Alternatives Rejected
- **Shared internal static API secret between F/E/M and C:** Rejected due to blast radius, lack of attribution, inability to rotate independently, and violation of Zero Trust.
- **Standalone IdentityServer / Keycloak container:** Rejected to avoid redundant identity infrastructure and operational overhead inside the NEXUS C 1.0 foundation.
