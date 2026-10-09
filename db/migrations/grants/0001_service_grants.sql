-- Least-privilege grants per service; audit tables are insert-only and only identity can read credentials.

-- Everyone: the startup schema-version check (§9.2).
GRANT USAGE ON SCHEMA sync TO identity_svc, gateway_svc, persister_svc, relay_svc, orchestrator_svc, rag_svc;
GRANT SELECT ON sync.schema_migrations TO identity_svc, gateway_svc, persister_svc, relay_svc, orchestrator_svc, rag_svc;

-- identity-service: owns clinician identity (ADR 0003). The only reader of clinician_credential.
GRANT USAGE ON SCHEMA clinical, audit TO identity_svc;
GRANT SELECT, INSERT, UPDATE ON clinical.clinician, clinical.clinician_credential, clinical.clinician_session
    TO identity_svc;
GRANT SELECT, INSERT ON clinical.device TO identity_svc;           -- first login registers the device
GRANT SELECT, INSERT ON audit.auth_audit TO identity_svc;          -- insert-only; read for the admin audit view

-- sync-gateway: push, pull, patient alias, REST baseline.
GRANT USAGE ON SCHEMA clinical, audit, baseline TO gateway_svc;
GRANT SELECT, INSERT ON clinical.patient TO gateway_svc;
GRANT UPDATE (display_alias, updated_at) ON clinical.patient TO gateway_svc;
GRANT SELECT ON clinical.wound, clinical.wound_assessment, clinical.recommendation TO gateway_svc;
GRANT SELECT, INSERT ON audit.provenance TO gateway_svc;          -- GATEWAY_ACCEPTED and DELIVERED
GRANT SELECT ON sync.change_log TO gateway_svc;
GRANT SELECT, INSERT ON sync.device_cursor TO gateway_svc;
GRANT UPDATE (last_seq, updated_at) ON sync.device_cursor TO gateway_svc;
GRANT SELECT, INSERT ON baseline.assessment, baseline.recommendation TO gateway_svc;

-- ingest-persister: wound-events → the clinical record (§9.4).
GRANT USAGE ON SCHEMA clinical, audit, messaging TO persister_svc;
GRANT SELECT, INSERT ON clinical.patient TO persister_svc;
GRANT UPDATE (updated_at) ON clinical.patient TO persister_svc;
GRANT INSERT ON clinical.wound TO persister_svc;
GRANT SELECT, INSERT ON clinical.wound_assessment TO persister_svc;
GRANT INSERT ON audit.provenance, messaging.outbox, sync.change_log TO persister_svc;

-- outbox-relay: publishes and marks outbox rows, sees nothing else.
GRANT USAGE ON SCHEMA messaging TO relay_svc;
GRANT SELECT ON messaging.outbox TO relay_svc;
GRANT UPDATE (published_at) ON messaging.outbox TO relay_svc;

-- orchestrator: recommendations (§10.1).
GRANT USAGE ON SCHEMA clinical, audit, messaging TO orchestrator_svc;
GRANT SELECT ON clinical.wound_assessment, clinical.patient TO orchestrator_svc;
GRANT UPDATE (status) ON clinical.wound_assessment TO orchestrator_svc;   -- SUPERSEDED only
GRANT INSERT ON clinical.recommendation TO orchestrator_svc;
GRANT SELECT, INSERT ON messaging.inbox, sync.change_log TO orchestrator_svc;
GRANT INSERT ON audit.provenance, messaging.outbox TO orchestrator_svc;

-- Recommendation Service (Member 3): its own schema only, including tables its migrations add later (§10.5).
GRANT USAGE ON SCHEMA rag TO rag_svc;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA rag TO rag_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA rag GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO rag_svc;
ALTER DEFAULT PRIVILEGES IN SCHEMA rag GRANT USAGE ON SEQUENCES TO rag_svc;

-- Identity columns draw from sequences: writers need them.
GRANT USAGE ON ALL SEQUENCES IN SCHEMA clinical, audit, messaging, sync, baseline
    TO identity_svc, gateway_svc, persister_svc, orchestrator_svc;
