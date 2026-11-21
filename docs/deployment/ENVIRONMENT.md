# Environment Configuration Reference

All production secrets MUST be supplied via environment variables or a managed secret store (HashiCorp Vault, AWS Secrets Manager, GCP Secret Manager, Azure Key Vault). Never commit `.env` or production `appsettings.Production.json` to Git.

| Environment Variable | Required | Default | Description |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Yes | `Production` | Runtime mode (`Development`, `Staging`, `Production`) |
| `ConnectionStrings__DefaultConnection` | Yes | — | PostgreSQL connection string using least-privilege **`nexus_app`** role (`NOSUPERUSER NOBYPASSRLS`) |
| `ConnectionStrings__MigrationConnection` | Migration only | — | PostgreSQL connection string using **`nexus_migrator`** schema owner role |
| `ConnectionStrings__Redis` | Production | `""` | StackExchange.Redis connection string (falls back to memory cache only in dev/test) |
| `Jwt__Issuer` | Yes | `https://nexus-c.local` | Expected JWT `iss` claim |
| `Jwt__Audience` | Yes | `nexus-ecosystem` | Expected JWT `aud` claim |
| `Jwt__SigningKey` | Yes | — | Symmetric signing key (minimum 64 random bytes) |
| `Jwt__AccessTokenMinutes` | No | `15` | Short-lived access token TTL in minutes |
| `Jwt__RefreshTokenDays` | No | `14` | Rotating refresh token lifetime in days |
| `RabbitMQ__Enabled` | No | `false` | Set `true` to publish Outbox events to RabbitMQ |
| `RabbitMQ__HostName` / `Port` / `UserName` / `Password` | When enabled | — | RabbitMQ broker connection settings |
| `Outbox__EnableBackgroundProcessor` | No | `true` | Enables `OutboxProcessorBackgroundService` |
| `Outbox__MaxRetries` | No | `5` | Retries with exponential backoff before moving to `messaging.dead_letter_messages` |
| `ObjectStorage__Provider` | No | `Local` | `"S3"` (AWS S3 / MinIO / R2) or `"Local"` |
| `ObjectStorage__BucketName` | Yes | `nexus-files` | Target bucket name |
| `ObjectStorage__ServiceUrl` | MinIO/S3-compat | — | Custom S3 endpoint URL |
| `ObjectStorage__AccessKey` / `SecretKey` | S3 | — | Object storage credentials |
| `ObjectStorage__SigningSecret` | Yes | — | HMAC-SHA256 secret used to sign temporary download URLs |
| `Cors__AllowedOrigins__0` | Production | `[]` | Explicit allowlisted browser origins (wildcard + credentials is prohibited) |
| `RateLimiting__PermitLimit` | No | `120` | Global per-user/IP request quota per window |
| `RateLimiting__AuthPermitLimit` | No | `30` | Stricter auth endpoint quota per window |
| `OpenApi__Enabled` | No | `false` | Keep `false` in public production unless behind internal gateway |
| `OpenTelemetry__OtlpEndpoint` | Optional | `""` | OTLP collector endpoint for traces & metrics |
