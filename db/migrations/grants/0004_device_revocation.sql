-- Revocation grants: identity can set revoked_at, the gateway can read it.
GRANT UPDATE (revoked_at) ON clinical.device TO identity_svc;
GRANT SELECT (device_id, revoked_at) ON clinical.device TO gateway_svc;
