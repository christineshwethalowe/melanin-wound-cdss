-- family_id links a login and all its refreshes into one session; existing rows get their own family.
ALTER TABLE clinical.clinician_session ADD COLUMN family_id uuid NOT NULL DEFAULT gen_random_uuid();
UPDATE clinical.clinician_session SET family_id = session_id;

CREATE INDEX ix_clinician_session_family ON clinical.clinician_session (family_id, issued_at);
