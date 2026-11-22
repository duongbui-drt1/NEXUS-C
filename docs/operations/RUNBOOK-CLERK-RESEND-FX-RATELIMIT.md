# NEXUS C 1.0 — Operational Runbook: Clerk Sync, Resend Email, Distributed Rate Limiting, FX & AI Governance

---

## 1. Clerk Identity Sync & Reconciliation (`app_3MTU0OPO0cFJboGQYPkeOLjZohj`)

### 1.1 Configuration
Set in environment or secret manager (never commit raw secrets):
```bash
Clerk__Enabled=true
Clerk__AppId=app_3MTU0OPO0cFJboGQYPkeOLjZohj
Clerk__Authority=https://<your-clerk-frontend-api>.clerk.accounts.dev
Clerk__SecretKey=sk_live_...
Clerk__WebhookSigningSecret=whsec_...
```

### 1.2 Host CLI Authentication & Live Reconciliation
When reconciling users from Clerk Backend API or restoring keys via Clerk CLI on a developer/ops host:
```bash
npx -y clerk@latest auth login
npx -y clerk@latest apps list
```
Once `Clerk__SecretKey` is configured, trigger on-demand reconciliation:
- **Endpoint:** `POST /api/v1/platform/users/clerk/reconcile?limit=100`
- **Permission:** `platform.users.manage` (Platform scope)
- **Status Check:** `GET /api/v1/platform/users/clerk/sync-status`

### 1.3 Staged Rollback to Local NEXUS Auth
Because NEXUS C uses dual-scheme JWT selection (`NexusDualAuth`), setting `Clerk__Enabled=false` immediately disables Clerk JWKS authentication while keeping local NEXUS HS256 JWT + Argon2id password login (`POST /api/v1/auth/login`) 100% operational with zero user ID or role changes.

---

## 2. Resend Shared Email Platform (`duli.studio`)

### 2.1 Configuration
```bash
Email__Provider=Resend
Email__DefaultFromAddress=no-reply@duli.studio
Email__DefaultFromName="NEXUS C Platform"
Email__Resend__ApiKey=re_...
Email__Resend__WebhookSecret=whsec_...
```

### 2.2 Operational Checks & Bounce Suppressions
- **Provider Health:** `GET /api/v1/platform/email/status` (`platform.email.manage`)
- **Delivery Ledger:** `GET /api/v1/platform/email/deliveries?page=1&pageSize=20&status=Bounced`
- **Suppression List:** `GET /api/v1/platform/email/suppressions`
- **Inbound Svix Webhook:** Configure Resend Dashboard to send `email.sent`, `email.delivered`, `email.bounced`, and `email.complained` to `POST https://<api-host>/api/v1/webhooks/resend`.

---

## 3. Distributed Rate Limiting & Operational API Request Logs

- **List Active Policies:** `GET /api/v1/platform/rate-limits`
- **Emergency Throttle Rule:** `POST /api/v1/platform/rate-limits` (`platform.rate-limits.manage`) with a lower `priority` (e.g., `1`) and target `routePattern` — takes effect immediately across all replicas via `RateLimitPolicyCache` invalidation and Redis counters.
- **Inspect 429 Throttled Requests:** `GET /api/v1/platform/api-logs?rateLimitDecision=Throttled` (`platform.api-logs.read`).
- **Retention Purge:** `POST /api/v1/platform/api-logs/purge` with `{ "retentionDays": 30 }`.

---

## 4. Reference Data & Indicative FX Reference Rates

- **Refresh ECB Reference Rates:** `POST /api/v1/platform/fx/refresh?baseCurrency=USD` (`platform.reference-data.manage`).
- **Manual / Treasury Fallback Observation:** `POST /api/v1/platform/fx/observations` with `{ "baseCurrency": "USD", "quoteCurrency": "VND", "rate": 25450.50, "provider": "CorporateTreasury", "ttlHours": 24 }`.
- **Important Notice:** All FX rates served by NEXUS C are indicative reference rates (`"Indicative FX reference rate — not a settlement rate."`). Accounting ledger settlement rates remain owned by NEXUS F.

---

## 5. Emergency AI Capability Kill-Switch

To immediately halt AI Gateway invocations for a specific capability within a tenant:
- **Endpoint:** `PUT /api/v1/platform/integrations/ai/policies` (`platform.integrations.manage`)
- **Payload:**
  ```json
  {
    "capabilityCode": "DEFAULT",
    "isEnabled": false,
    "killSwitchActive": true,
    "requireHumanApproval": true,
    "monthlyTokenBudget": 0
  }
  ```
- **Effect:** Subsequent calls to `POST /api/v1/platform/integrations/ai/invoke` immediately fail closed with HTTP `403 AI_KILL_SWITCH_ACTIVE` and record a `KillSwitchBlocked` audit trail in `integration.ai_usage_logs`.
