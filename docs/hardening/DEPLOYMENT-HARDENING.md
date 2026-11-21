# NEXUS C — Docker, Bootstrap & Migration Hardening Report (Phase 04)

## 1. Eliminated Hardcoded Database Credentials & Database Name
- Added [infrastructure/database/init-roles.sh](file:///Users/linhhhuce/Documents/NEXUS%20PRODUCTION/infrastructure/database/init-roles.sh): reads `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_MIGRATOR_PASSWORD`, and `POSTGRES_APP_PASSWORD` from environment variables and safely binds them via `psql -v` (`:'var'` + `format('%I' / '%L')`) with zero shell-to-SQL string interpolation.
- Updated [infrastructure/database/init-roles.sql](file:///Users/linhhhuce/Documents/NEXUS%20PRODUCTION/infrastructure/database/init-roles.sql) to parameterized `psql -v` variables (zero hardcoded passwords or database names).
- Added `ALTER DEFAULT PRIVILEGES IN SCHEMA ...` for future tables and sequences in `NexusDatabaseMigrator.cs`.
- Removed dev connection strings from base `src/Nexus.Api/appsettings.json` (dev defaults live exclusively in `appsettings.Development.json`).

## 2. Dedicated One-Shot Migration Job (`Nexus.Migrator`)
- Created dedicated console project [src/Nexus.Migrator/Nexus.Migrator.csproj](file:///Users/linhhhuce/Documents/NEXUS%20PRODUCTION/src/Nexus.Migrator/Nexus.Migrator.csproj) and [Dockerfile.migrator](file:///Users/linhhhuce/Documents/NEXUS%20PRODUCTION/Dockerfile.migrator) (non-root `USER app`).
- Updated [docker-compose.yml](file:///Users/linhhhuce/Documents/NEXUS%20PRODUCTION/docker-compose.yml) startup pipeline:
  `postgres (healthy)` → `migrator (restart: "no", service_completed_successfully)` → `api (least-privilege nexus_app role ONLY; never receives migrator or bootstrap admin credentials)`.
- Restricted `NEXUS_RUN_MIGRATIONS=true` in `Nexus.Api/Program.cs` strictly to `Development` environment so multi-replica production API deployments can never race on migrations.

## 3. Startup Fail-Fast Security Validation
- `NexusBootstrapExtensions.ValidateRuntimeSecurityConfiguration` fails fast on startup in non-Development/Testing environments if:
  - `Jwt:SigningKey` is missing, `< 32` characters, or contains insecure placeholder strings (`CHANGE_ME`, `REPLACE_WITH`, `PLACEHOLDER`, `DEV_ONLY`).
  - `ObjectStorage:SigningSecret` is missing, `< 32` characters, or contains placeholders.
  - `ConnectionStrings:DefaultConnection` uses `dev_password` or `Username=postgres`.
  - `Cors:AllowedOrigins` contains wildcard `*`.

## 4. Bootstrap SuperAdmin Decommissioning Checklist
After initial installation completes via `Nexus.Migrator`:
1. Unset `NEXUS_BOOTSTRAP_ADMIN_EMAIL` and `NEXUS_BOOTSTRAP_ADMIN_PASSWORD` from deployment secrets/env.
2. Rotate the initial bootstrap SuperAdmin password immediately on first login.
3. Verify `platform.bootstrap.admin_created` audit record exists in `audit.audit_events`.
