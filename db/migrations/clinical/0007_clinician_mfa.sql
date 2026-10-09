-- TOTP MFA: the secret is stored encrypted and trusted only once mfa_enabled is true.
ALTER TABLE clinical.clinician_credential
    ADD COLUMN mfa_enabled        boolean NOT NULL DEFAULT false,
    ADD COLUMN mfa_last_used_step bigint;   -- last accepted 30-second step; stops a code being replayed
