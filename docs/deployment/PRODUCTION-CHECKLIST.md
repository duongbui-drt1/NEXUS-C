# Production Readiness Checklist

Complete every item before deploying NEXUS C Foundation to production:

## Security & Secrets
- [ ] `ASPNETCORE_ENVIRONMENT=Production` (never `Development`).
- [ ] `Jwt__SigningKey` is ≥ 64 cryptographically random bytes injected from Secret Manager.
- [ ] `ObjectStorage__SigningSecret` is ≥ 64 random bytes injected from Secret Manager.
- [ ] Application connects strictly as PostgreSQL role **`nexus_app`** (`rolsuper = false`, `rolbypassrls = false`).
- [ ] `Cors__AllowedOrigins` contains only explicit HTTPS production origins (no wildcards).
- [ ] `OpenApi__Enabled=false` on public internet ingress (expose only internally if needed).
- [ ] TLS 1.3 terminated at reverse proxy / ingress with HSTS enabled.

## Data & Infrastructure
- [ ] All 12 EF Core module migrations executed via **`nexus_migrator`**.
- [ ] `FORCE ROW LEVEL SECURITY` verified active on all 24 tenant-scoped tables.
- [ ] `audit.audit_events` immutability trigger and `INSERT/SELECT`-only grant for `nexus_app` verified.
- [ ] Redis (`ConnectionStrings__Redis`) enabled with authentication & TLS.
- [ ] RabbitMQ (`RabbitMQ__Enabled=true`) connected with TLS, durable exchanges, and DLQ monitoring.
- [ ] S3 Object Storage (`ObjectStorage__Provider=S3`) bucket has Block Public Access enabled and server-side encryption (SSE-KMS/SSE-S3).
- [ ] PostgreSQL connection pool sizing tuned (`MaxPoolSize` matched to PgBouncer / DB `max_connections`).

## Observability & Resilience
- [ ] `OpenTelemetry__OtlpEndpoint` connected to tracing/metrics collector.
- [ ] Serilog JSON logs forwarded to centralized SIEM/log aggregation (verify zero passwords/tokens logged).
- [ ] Orchestrator probes configured: liveness -> `/health/live`, readiness -> `/health/ready`.
- [ ] Graceful shutdown timeout (`terminationGracePeriodSeconds >= 30`) configured.
- [ ] Alerts configured for `messaging.dead_letter_messages` growth, 5xx spikes, and `security.refresh_token.reuse_detected` audit events.
- [ ] Automated backups and monthly restore drill scheduled.
