# Contracts changelog

Every change to a schema in this folder is a single pull request that all four members review.
Bump `schemaVersion` for breaking changes, and add an example to `examples/` for each new rule.

## auth 1.1 (additive, existing mobile clients unaffected)
- Tokens come from the identity service, signed RS256 (ADR 0003); all calls go through the API gateway (ADR 0004).
- `loginRequest.clientId`: `mobile` (default, `deviceId` required) or `admin-dashboard` (no `deviceId`,
  admins only; ADR 0005).
- New error codes: `CLIENT_NOT_ALLOWED` (401), `DEVICE_NOT_EXPECTED`, `UNKNOWN_CLIENT` (400).
- New definitions: `accessTokenClaims` (adds `client_id`; `device_id` only on mobile tokens; audience
  `melanin-wound-cdss-devices` or `melanin-wound-cdss-admin`), `openidConfiguration`, `jwks`.

## wound-event 1.0 (draft)
- Initial version from architecture §5.
- Open: final tri-state field list (ulcerLocation, probeToBone, infectionGrade, woundBedLabels), see §15.

## sync-changes, auth, rag-request, rag-response 0.1
- Placeholders only.
