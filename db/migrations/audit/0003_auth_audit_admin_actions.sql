-- Adds clinician-management and MFA events to the auth audit, with the acting admin recorded.
ALTER TABLE audit.auth_audit DROP CONSTRAINT auth_audit_action_check;
ALTER TABLE audit.auth_audit ADD CONSTRAINT auth_audit_action_check CHECK (action IN (
    'LOGIN', 'REFRESH', 'LOGOUT', 'LOCKOUT',
    'REGISTER', 'UNLOCK', 'DEACTIVATE',
    'MFA_ENROLL', 'MFA_CONFIRM', 'MFA_RESET'));

ALTER TABLE audit.auth_audit ADD COLUMN actor_clinician_id uuid;

CREATE INDEX ix_auth_audit_recorded ON audit.auth_audit (recorded_at);
