-- NEXUS C Foundation: Parameterized PostgreSQL Role Initialization
-- Usage (never hardcode passwords in SQL files):
--   psql -v ON_ERROR_STOP=1 -d "$POSTGRES_DB" \
--     -v db_name="$POSTGRES_DB" \
--     -v migrator_password="$POSTGRES_MIGRATOR_PASSWORD" \
--     -v app_password="$POSTGRES_APP_PASSWORD" \
--     -f infrastructure/database/init-roles.sql
--
-- For Docker Compose and automated provisioning, use infrastructure/database/init-roles.sh.

\set ON_ERROR_STOP on

SELECT set_config('nexus.init_db', :'db_name', false);
SELECT set_config('nexus.init_migrator_pw', :'migrator_password', false);
SELECT set_config('nexus.init_app_pw', :'app_password', false);

DO $$
DECLARE
    v_db text := current_setting('nexus.init_db');
    v_mig_pw text := current_setting('nexus.init_migrator_pw');
    v_app_pw text := current_setting('nexus.init_app_pw');
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nexus_migrator') THEN
        EXECUTE format('CREATE ROLE nexus_migrator WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', v_mig_pw);
    ELSE
        EXECUTE format('ALTER ROLE nexus_migrator WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', v_mig_pw);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nexus_app') THEN
        EXECUTE format('CREATE ROLE nexus_app WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS', v_app_pw);
    ELSE
        EXECUTE format('ALTER ROLE nexus_app WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS', v_app_pw);
    END IF;

    EXECUTE format('ALTER DATABASE %I OWNER TO nexus_migrator', v_db);
    EXECUTE format('REVOKE ALL ON DATABASE %I FROM PUBLIC', v_db);
    EXECUTE format('GRANT CONNECT, CREATE ON DATABASE %I TO nexus_migrator', v_db);
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO nexus_app', v_db);
END
$$;
