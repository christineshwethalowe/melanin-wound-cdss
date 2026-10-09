-- Separate login for housekeeping so only it gets the delete rights it needs.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'housekeeping_svc') THEN
        CREATE ROLE housekeeping_svc LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
    END IF;
END $$;

COMMENT ON ROLE housekeeping_svc IS 'housekeeping: outbox cleanup, change-log archival, inbox retention';
