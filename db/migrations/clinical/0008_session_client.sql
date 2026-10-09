-- Sessions now record their client; dashboard sessions have no device, existing rows become 'mobile'.
ALTER TABLE clinical.clinician_session
    ADD COLUMN client_id text NOT NULL DEFAULT 'mobile'
        CONSTRAINT ck_clinician_session_client CHECK (client_id IN ('mobile', 'admin-dashboard')),
    ALTER COLUMN device_id DROP NOT NULL,
    ADD CONSTRAINT ck_clinician_session_device
        CHECK ((client_id = 'mobile') = (device_id IS NOT NULL));
