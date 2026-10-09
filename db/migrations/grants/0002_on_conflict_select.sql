-- ON CONFLICT needs SELECT on the key columns, so grant just those to the two writers that use it.
GRANT SELECT (wound_id) ON clinical.wound TO persister_svc;
GRANT SELECT (assessment_id, revision) ON clinical.recommendation TO orchestrator_svc;
