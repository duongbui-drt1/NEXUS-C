# Environment Configuration Reference

All production secrets MUST be supplied via environment variables or a managed secret store (HashiCorp Vault, AWS Secrets Manager, GCP Secret Manager, Azure Key Vault). Never commit `.env` or production `appsettings.Production.json` to Git.

## 1. API Runtime (`Nexus.Api`)

| Environment Variable | Required | Default | Description |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Yes | `Production` | Runtime mode (`Development`, `Staging`, `Production`) |
| `ConnectionStrings__DefaultConnection` | Yes | — | PostgreSQL connection string using least-privilege **`nexus_app`** role (`NOSUPERUSER NOBYPASSRLS`) |
| `ConnectionStrings__Redis` | Production | `""` | StackExchange.Redis connection string (falls back to in-memory cache only in dev/test; required in multi-replica production for permission/entitlement cache invalidation) |
| `Jwt__Issuer` | Yes | `https://nexus-c.local` | Expected JWT `iss` claim |
| `Jwt__Audience` | Yes | `nexus-ecosystem` | Expected JWT `aud` claim |
| `Jwt__SigningKey` | Yes | — | Symmetric signing key (minimum 64 random bytes in production; fail-fast validated at startup) |
| `Jwt__AccessTokenMinutes` | No | `15` | Short-lived human access token TTL in minutes (service tokens fixed to 10 min) |
| `Jwt__RefreshTokenDays` | No | `14` | Rotating refresh token lifetime in days |
| `RabbitMQ__Enabled` | No | `false` | Set `true` to publish Outbox events to RabbitMQ |
| `RabbitMQ__HostName` / `Port` / `UserName` / `Password` | When enabled | — | RabbitMQ broker connection settings |
| `Outbox__EnableBackgroundProcessor` | No | `true` | Enables `OutboxProcessorBackgroundService` (`FOR UPDATE SKIP LOCKED`) |
| `Outbox__MaxRetries` | No | `5` | Retries with exponential backoff before moving to `messaging.dead_letter_messages` |
| `ObjectStorage__Provider` | No | `Local` | `"S3"` (AWS S3 / MinIO / R2) or `"Local"` |
| `ObjectStorage__BucketName` | Yes | `nexus-files` | Target bucket name |
| `ObjectStorage__ServiceUrl` | MinIO/S3-compat | — | Custom S3 endpoint URL |
| `ObjectStorage__AccessKey` / `SecretKey` | S3 | — | Object storage credentials |
| `ObjectStorage__SigningSecret` | Yes | — | Mandatory HMAC-SHA256 secret (>= 16 chars; >= 64 chars recommended) used to sign temporary download URLs (fails closed if missing) |
| `Cors__AllowedOrigins__0` | Production | `[]` | Explicit allowlisted browser origins (wildcard + credentials is prohibited) |
| `RateLimiting__PermitLimit` | No | `120` | Global per-user/IP request quota per window |
| `RateLimiting__AuthPermitLimit` | No | `30` | Stricter auth endpoint quota per window |
| `OpenApi__Enabled` | No | `false` | Keep `false` in public production unless behind internal gateway |
| `OpenTelemetry__OtlpEndpoint` | Optional | `""` | OTLP collector endpoint for traces & `Nexus.Platform` metrics |

## 2. One-Shot Migrator (`Nexus.Migrator`) & Role Bootstrap (`init-roles.sh`)

| Environment Variable | Required | Default | Description |
|---|---|---|---|
| `ConnectionStrings__MigrationConnection` | Yes (Migrator) | — | PostgreSQL connection string using **`nexus_migrator`** schema owner role |
| `NEXUS_APP_DB_ROLE` | No (Migrator) | `nexus_app` | Least-privilege application role receiving post-migration schema/table grants |
| `NEXUS_BOOTSTRAP_ADMIN_EMAIL` | Initial only | `""` | Optional initial Platform SuperAdmin email (set only during first bootstrap) |
| `NEXUS_BOOTSTRAP_ADMIN_PASSWORD` | Initial only | `""` | Complex password for initial Platform SuperAdmin (never pass to `Nexus.Api`) |
| `POSTGRES_DB` / `POSTGRES_USER` | `init-roles.sh` | — | Target PostgreSQL database and superuser executing `infrastructure/database/init-roles.sh` |
| `POSTGRES_MIGRATOR_PASSWORD` / `POSTGRES_APP_PASSWORD` | `init-roles.sh` | — | Passwords for created/updated `NEXUS_MIGRATOR_ROLE` (`nexus_migrator`) and `NEXUS_APP_ROLE` (`nexus_app`) |
