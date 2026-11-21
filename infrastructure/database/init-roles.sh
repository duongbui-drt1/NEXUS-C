#!/usr/bin/env sh
# NEXUS C Foundation — Environment-Driven Least-Privilege PostgreSQL Role Initializer
# Safely binds environment variables using psql variables + format('%I' / '%L').
# ZERO hardcoded passwords or database names.

set -eu

: "${POSTGRES_DB:?POSTGRES_DB must be set}"
: "${POSTGRES_USER:?POSTGRES_USER must be set}"
: "${POSTGRES_MIGRATOR_PASSWORD:?POSTGRES_MIGRATOR_PASSWORD must be set}"
: "${POSTGRES_APP_PASSWORD:?POSTGRES_APP_PASSWORD must be set}"

NEXUS_MIGRATOR_ROLE="${NEXUS_MIGRATOR_ROLE:-nexus_migrator}"
NEXUS_APP_ROLE="${NEXUS_APP_ROLE:-nexus_app}"

psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  -v db_name="$POSTGRES_DB" \
  -v migrator_role="$NEXUS_MIGRATOR_ROLE" \
  -v migrator_password="$POSTGRES_MIGRATOR_PASSWORD" \
  -v app_role="$NEXUS_APP_ROLE" \
  -v app_password="$POSTGRES_APP_PASSWORD" <<'EOSQL'
SELECT set_config('nexus.init_db', :'db_name', false);
SELECT set_config('nexus.init_migrator_role', :'migrator_role', false);
SELECT set_config('nexus.init_migrator_pw', :'migrator_password', false);
SELECT set_config('nexus.init_app_role', :'app_role', false);
SELECT set_config('nexus.init_app_pw', :'app_password', false);

DO $$
DECLARE
    v_db text := current_setting('nexus.init_db');
    v_mig_role text := current_setting('nexus.init_migrator_role');
    v_mig_pw text := current_setting('nexus.init_migrator_pw');
    v_app_role text := current_setting('nexus.init_app_role');
    v_app_pw text := current_setting('nexus.init_app_pw');
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = v_mig_role) THEN
        EXECUTE format('CREATE ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', v_mig_role, v_mig_pw);
    ELSE
        EXECUTE format('ALTER ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', v_mig_role, v_mig_pw);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = v_app_role) THEN
        EXECUTE format('CREATE ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS', v_app_role, v_app_pw);
    ELSE
        EXECUTE format('ALTER ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS', v_app_role, v_app_pw);
    END IF;

    EXECUTE format('ALTER DATABASE %I OWNER TO %I', v_db, v_mig_role);
    EXECUTE format('REVOKE ALL ON DATABASE %I FROM PUBLIC', v_db);
    EXECUTE format('GRANT CONNECT, CREATE ON DATABASE %I TO %I', v_db, v_mig_role);
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', v_db, v_app_role);
END
$$;
EOSQL
