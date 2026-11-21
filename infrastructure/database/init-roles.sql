-- NEXUS C Foundation: Least-Privilege PostgreSQL Role Initialization
-- Executed once by database administrator / Docker entrypoint.
-- NEVER grant SUPERUSER or BYPASSRLS to nexus_app!

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nexus_migrator') THEN
        CREATE ROLE nexus_migrator WITH LOGIN PASSWORD 'nexus_migrator_dev_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nexus_app') THEN
        CREATE ROLE nexus_app WITH LOGIN PASSWORD 'nexus_app_dev_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
END
$$;

-- Create development database if not already created (when run against postgres DB)
-- Note: In Docker Compose POSTGRES_DB creates the DB; locally we grant ownership to nexus_migrator.
GRANT CONNECT ON DATABASE nexus_dev TO nexus_migrator;
GRANT CREATE ON DATABASE nexus_dev TO nexus_migrator;
GRANT CONNECT ON DATABASE nexus_dev TO nexus_app;
