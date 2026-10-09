-- One login per service; passwords are set from env vars by the migrator, and grants come later in grants/.
DO $$
DECLARE
    r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['identity_svc', 'gateway_svc', 'persister_svc', 'relay_svc', 'orchestrator_svc', 'rag_svc']
    LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
            EXECUTE format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT', r);
        END IF;
    END LOOP;
END $$;

COMMENT ON ROLE identity_svc IS 'identity-service: clinicians, credentials, sessions, devices, auth audit';
COMMENT ON ROLE gateway_svc IS 'sync-gateway: push/pull, patient alias, REST baseline';
COMMENT ON ROLE persister_svc IS 'ingest-persister: wound-events -> clinical record';
COMMENT ON ROLE relay_svc IS 'outbox-relay: messaging.outbox -> Kafka';
COMMENT ON ROLE orchestrator_svc IS 'orchestrator: recommendations';
COMMENT ON ROLE rag_svc IS 'Recommendation Service (Member 3): rag schema only';
