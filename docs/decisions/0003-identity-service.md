# 0003: Separate identity service with basic OpenID Connect (RS256 + JWKS)

- Status: accepted
- Date: 2026-09-30

## Context

Architecture v2.0 (§3, §7.3, §12) has the Sync Gateway issue and verify its own JWTs, signed with a shared
HS256 key. Login, refresh, logout, MFA and clinician administration therefore live in the same process as
push and pull. Senior developer review asked for identity to be a separate service, with basic OpenID and
JWT, and with low latency on the request path.

A shared HS256 secret means every service that checks a token must also hold the key that can mint one.
Adding services (api-gateway, dashboard backends, the REST baseline) would spread that secret further.

## Decision

- New deployable unit `backend/apps/identity-service` (ASP.NET Core, .NET 10). It owns `/v1/auth/*`
  (login, refresh, logout, MFA) and `/v1/admin/*` (clinician registration, unlock, deactivate, reset MFA,
  auth audit), and the `create-clinician` bootstrap command. The existing code moves over unchanged in
  behaviour: Argon2id, 5-attempt lockout, TOTP, device binding, `audit.auth_audit`.
- Tokens are signed with **RS256**. The private key is held only by identity-service (in a secrets store
  outside local dev, like `Secrets__EncryptionKey`).
- Identity-service publishes basic OpenID Connect metadata: `/.well-known/openid-configuration` and
  `/.well-known/jwks.json`.
- Every other service validates tokens **locally** with the public key it fetches from the JWKS once and
  caches (refreshed on key rotation). There is no call to identity-service and no database lookup per request.
- The device-facing API from §7.3 does not change: same paths, same request and response bodies, same
  15-minute access token with claims `sub`, `device_id`, `facility_id`, `role`, same rotating opaque refresh
  token stored only as a SHA-256 hash.
- Full OIDC flows (authorization code + PKCE via OpenIddict) and external providers (Keycloak) were
  considered and not chosen: they add work to fit MFA and device binding, and the panel still needs a
  concrete, inspectable auth story (§3). This is still an in-house identity provider, not an external one.
- Identity-service keeps using the `clinical.clinician*` tables and `audit.auth_audit`. Moving them to a
  separate schema is not part of this change.

## Consequences

- Sync Gateway becomes a pure resource server and loses its auth code.
- Only identity-service's database role may read `clinical.clinician_credential` (Phase 9, §9.4, §12).
- A signing-key rotation needs no redeploy of other services: publish the new key in the JWKS first,
  then switch signing.
- One more container. Token checks stay in-process, so request latency does not increase.
