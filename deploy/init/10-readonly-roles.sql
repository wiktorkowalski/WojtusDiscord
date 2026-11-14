-- Read-only login roles. Idempotent: runs on an empty data volume, and by hand on a live one:
--   docker exec -i wojtus-postgres psql -U postgres -d discord_event_service -v ON_ERROR_STOP=1 < init/10-readonly-roles.sql
-- No passwords here. Set the exporter password by hand (it lives in .env as POSTGRES_MONITOR_PASSWORD):
--   ALTER ROLE wojtus_monitor PASSWORD '...';
-- Cluster roles are not in pg_dump: re-run this file after a restore into a new cluster.
DO $$
BEGIN
    -- postgres-exporter. pg_monitor reads pg_stat_* and pg_stat_statements text, no table data.
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_monitor') THEN
        CREATE ROLE wojtus_monitor LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
    GRANT pg_monitor TO wojtus_monitor;

    -- The agent (#340, #496): SELECT on every table over `docker exec psql -U wojtus_agent`.
    -- No password: the image trusts only the local socket, so TCP logins fail.
    -- BYPASSRLS: the RLS policies serve the bot's wojtus_query sandbox only; without it the agent reads 0 rows there.
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_agent') THEN
        CREATE ROLE wojtus_agent LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE BYPASSRLS;
    END IF;
    ALTER ROLE wojtus_agent BYPASSRLS;
    ALTER ROLE wojtus_agent SET default_transaction_read_only = on;
    GRANT pg_read_all_data TO wojtus_agent;
END
$$;
