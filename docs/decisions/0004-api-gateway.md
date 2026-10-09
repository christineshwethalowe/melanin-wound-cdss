# 0004: API gateway (YARP) as the single entry point

- Status: accepted
- Date: 2026-09-30

## Context

With identity split out (0003) and an admin dashboard added (0005), clients would otherwise need to know
two backend URLs. CORS, rate limiting and request IDs would be repeated in each service.

The Sync Gateway in v2.0 is a domain service (push, pull, patients, figures proxy) despite its name. It is
not a generic edge: it has business logic and talks to Kafka and PostgreSQL. Kafka remains the
service-to-service backbone; the API gateway only handles client HTTP traffic.

## Decision

- New deployable unit `backend/apps/api-gateway` using YARP (Microsoft's reverse proxy for .NET), mostly
  configuration.
- It is the only publicly exposed port. Routes:
  - `/v1/auth/*`, `/v1/admin/*`, `/.well-known/*` → identity-service
  - `/v1/sync/*`, `/v1/patients/*`, `/v1/figures/*`, `/v1/baseline/*`, `/health` → sync-gateway
- Edge concerns, handled once: rate limiting (strict on login/refresh; also produces the 429 the device
  already handles, §7.1), CORS for the dashboard origin, request size limits, `traceparent` pass-through
  for §13 tracing. WebSockets are allowed so SignalR nudges (§14.2 stretch) can pass later.
- The API gateway validates the JWT against the cached JWKS for routes that need it and rejects bad
  tokens at the edge. The services still validate the token themselves; the edge check is defence in depth,
  not the only check.
- The name "Sync Gateway" stays for now. Renaming it (e.g. `sync-service`) is a separate, optional change.

## Consequences

- Mobile app, device simulator, integration tests and the dashboard use one base URL.
- identity-service and sync-gateway are reachable only inside the Docker network.
- One extra in-process hop (about 1 ms locally). The REST baseline (§13.1) also goes through the API
  gateway so the comparison stays fair, and the latency method should mention the hop.
