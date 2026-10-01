# Member 4 backend plan (IT23227354)

Plan for finishing the backend from the architecture document (v2.0, with the
[v2.1 changes](architecture/v2.1-changes.md)), following the build order in §14.1.
Each phase ends with a **done when** check that can be demonstrated on the local Docker stack.

Status key: ✅ done · 🧩 architecture skeleton in place (structure, types, wiring; logic TODO) · 🔨 in progress · ⬜ not started

**Run everything in Docker:** `docker compose up -d --build` (see README). Both integration suites pass against
the containers.

**Verify phases 1–5:** `python tests/integration/e2e_smoke.py` (18 checks) with the stack, gateway, persister and
relay running. Also verified by hand on 2026-09-28: replaying `wound-events` from offset 0 left the row count
unchanged and wrote a DEDUPLICATED provenance row; stopping Kafka made push return 503 with `Retry-After`,
and the pipeline recovered when Kafka came back.

| Phase | What | Architecture | Status |
|-------|------|--------------|--------|
| 0 | Repo, Docker stack, migrations, shared library | §4, §9.2, App. A | ✅ |
| 1 | Clinician auth in the gateway | §7.3, §9.1, §12 | ✅ |
| 1b | Admin registration, TOTP MFA, patient alias, audit trail | §7.3, §9.1, §12, §14.1 step 2 | ✅ |
| 2 | Push endpoint: validate → Kafka (acks=all) | §5, §7.1, §8.2 | ✅ |
| 3 | Ingest persister: Kafka → PostgreSQL, idempotent | §9.3, §9.4 | ✅ |
| 4 | Outbox relay: PostgreSQL → Kafka | §4, §9.3 | ✅ |
| 5 | Pull endpoint: change log → device | §7.2 | ✅ |
| 6 | Orchestrator workflow + Recommendation Service stub | §10 | ✅ |
| 7 | Retry topics and dead-letter topic | §8.3, §11 | ✅ |
| 8 | REST baseline endpoint | §13.1 | ✅ |
| 9 | Per-service database roles (least privilege) | §9.4, §12 | ✅ |
| 10 | Evaluation harness: device simulator, faults, metrics | §13 | 🔨 part 1 (device simulator) ✅; part 2 (faults, metrics, experiments) next |
| 11 | Observability: OpenTelemetry, Prometheus, Grafana | §13 | ⬜ |
| 12 | Mobile: Drift queue + sync engine | §6 | ⬜ |

### Architecture v2.1 changes (done before phase 6)

After senior developer review: identity as its own service, an API gateway, and an admin dashboard.
What changes in the architecture: [architecture/v2.1-changes.md](architecture/v2.1-changes.md).

| Step | What | Decision | Status |
|------|------|----------|--------|
| A1 | Decision records and v2.1 changes | 0003, 0004, 0005 | ✅ |
| A2 | identity-service: move auth, MFA and admin out of the gateway; RS256; OIDC discovery + JWKS | 0003 | ✅ |
| A3 | Sync Gateway as resource server only (validates with cached JWKS) | 0003 | ✅ done with A2 (one would not run without the other) |
| A4 | api-gateway (YARP): routing, rate limits, CORS, edge JWT check | 0004 | ✅ |
| A5 | Web-client sessions: `clinical/0008` adds `client_id`, nullable `device_id` for the dashboard; dashboard tokens get their own audience, so the Sync Gateway refuses them | 0005 | ✅ |
| A6 | Contract `auth.schema.json` 1.1: `clientId`, token claims and audiences, discovery, JWKS (compose and tests moved to the edge in A2/A4) | 0003, 0004, 0005 | ✅ |
| A7 | `frontend/admin_dashboard` (Flutter Web) | 0005 | ⬜ |
| A8 | CI jobs and README | — | ⬜ |

Effect on later phases: phase 8 (REST baseline) and phase 10 (device simulator) go through the API gateway;
phase 9 adds an identity-service role, the only one that can read `clinical.clinician_credential`;
phase 12 points the app at the single edge URL.

---

## Phase 1: Clinician auth

- `create-clinician` command on the gateway to register a clinician for local testing
  (`dotnet run --project backend/apps/sync/sync-gateway -- create-clinician <user> <password> <role> <facility>`).
- `POST /v1/auth/login` → 15-minute JWT (`sub`, `device_id`, `facility_id`, `role`) plus an opaque refresh token.
  - Argon2id password check; 5 failures lock the credential for 15 minutes.
  - An unknown `deviceId` is registered to the clinician's facility on first login (prototype rule).
  - The refresh token is stored only as a SHA-256 hash in `clinical.clinician_session`.
- `POST /v1/auth/refresh` rotates both tokens. `POST /v1/auth/logout` revokes the session.
- Every attempt writes `audit.auth_audit`.

**Done when:** a clinician can be created, log in, refresh and log out, and every step appears in `audit.auth_audit`.

### Phase 1b: rest of build step 2 (§14.1: "a clinician can register, log in, and the login is auditable end to end") ✅

- **Registration by facility admins** (role `admin`, everything scoped to the admin's facility from the token):
  `POST /v1/admin/clinicians`, `GET /v1/admin/clinicians`, `POST /v1/admin/clinicians/{username}/unlock`,
  `/deactivate` (also revokes every session) and `/reset-mfa`. Usernames are lower-case, passwords at least
  12 characters. Clinicians of another facility are reported as 404, never 403.
  The `create-clinician` command now goes through the same validation and audit, and is only needed to
  bootstrap each facility's first admin.
- **TOTP MFA** (RFC 6238, works with any authenticator app): `POST /v1/auth/mfa/enroll` → scan the
  `otpauth://` URI → `POST /v1/auth/mfa/confirm` with a first code. Once confirmed, login needs `totp`:
  no code → `MFA_REQUIRED` (not counted as a failure); a wrong code counts towards the 5-attempt lockout;
  a code that was already accepted is refused (replay protection via `mfa_last_used_step`).
  The secret is stored encrypted with AES-256-GCM, bound to the clinician id; the key is
  `Secrets__EncryptionKey`, kept outside the database with the JWT signing key (§12).
  Whether MFA is mandatory for some roles is still an open team decision (§15); it is opt-in for now.
- **Patient display alias** (§9.1): `PUT /v1/patients/{patientRef}/alias`, `GET /v1/patients/{patientRef}`,
  facility-scoped. Only pseudonyms (`p-…`) are accepted. The persister never touches the alias.
- **Audit trail**: `audit.auth_audit` now also records REGISTER, UNLOCK, DEACTIVATE, MFA_ENROLL, MFA_CONFIRM
  and MFA_RESET, with `actor_clinician_id` for actions an admin performs. Admins read their facility's
  trail at `GET /v1/admin/auth-audit`.
- Migrations: `clinical/0007_clinician_mfa.sql`, `audit/0003_auth_audit_admin_actions.sql`.

**Verified:** `python tests/integration/e2e_step2_auth_admin.py` (44 checks) and the gateway unit tests
(RFC 6238 test vectors, encryption binding). Known limit: a deactivated clinician's access token stays valid
until it expires (at most 15 minutes); refresh is refused immediately.

## Phase 2: Push

- `POST /v1/sync/push` needs a JWT. Body: `{ deviceId, batchId, events[] }`.
- The body's `deviceId` must match the token; each event's `facilityId` must match the token.
- Each event is checked against `contracts/wound-event.schema.json` and the 16 KB cap.
- Already stored (`event_id` exists) → `DUPLICATE` without producing again.
- Otherwise produced to `wound-events` (key `woundId`, acks=all, idempotent producer). Only after
  Kafka acknowledges is it `ACCEPTED`, and a `GATEWAY_ACCEPTED` provenance row is written.
- Kafka unreachable → `503` with `Retry-After`; nothing is acknowledged.

**Done when:** a batch with one good, one invalid and one repeated event returns ACCEPTED, REJECTED, DUPLICATE.

## Phase 3: Ingest persister

- Consumer group `persister` on `wound-events`, manual offset commit after the database commit.
- One transaction per message: advisory lock → upsert patient and wound → insert assessment
  `ON CONFLICT (event_id) DO NOTHING` → `PERSISTED` provenance + outbox row + change-log row;
  or a `DEDUPLICATED` provenance row if the insert did nothing.
- Bad messages go to `wound-events.dlq` (invalid payload) and the offset is committed, so the partition keeps moving.

Resolves the §9.1/§9.4 conflict: the persister upserts `patient` with `patient_ref` and `facility_id` only.
`display_alias` is set later by clinicians through the gateway, never from the Kafka payload.

**Done when:** replaying the topic from the beginning leaves the row count unchanged and adds DEDUPLICATED rows.

## Phase 4: Outbox relay

- Poll `messaging.outbox` with `FOR UPDATE SKIP LOCKED`, produce each row, set `published_at`, commit.

**Done when:** every persisted event appears on `wound-events.persisted`.

## Phase 5: Pull

- `GET /v1/sync/changes?cursor=&limit=` returns changes for the token's facility after the cursor,
  **plus** anything created in the last 60 seconds (sequence numbers can commit out of order).
- Response `{ changes[], nextCursor, hasMore }`; the device cursor is recorded in `sync.device_cursor`.

**Done when:** a pushed event shows up as a PERSISTED change on the next pull.

## Phase 6: Orchestrator

- Consumer group `orchestrator` on `wound-events.persisted`.
- Workflow with the six executors from §10.1, built on Microsoft Agent Framework workflows
  (package `Microsoft.Agents.AI.Workflows`).
- `BuildContext` strips `patientRef`, `deviceId`, `facilityId` and loads the healing history.
- `CallRag` → the stub (`backend/tests/rag-stub`) with a 60 s timeout, retries and a circuit breaker.
- `PersistResult`: advisory lock, recommendation + change log (`RECOMMENDATION_READY`) + provenance + outbox + inbox marker.

**Done when:** each persisted event produces exactly one recommendation, even if the worker is killed mid-run. ✅

**Verified:** `python tests/integration/e2e_orchestrator.py` (28 checks: full round trip with all provenance stages
through DELIVERED, superseded revision, topic replay, worker killed mid-call, 503 / 422 / uncited answers) and
`backend/tests/Orchestrator.Tests` (24 tests: every workflow path, request minimisation, request valid against
`rag-request.schema.json`).

- All SQL is in `PostgresOrchestratorStore`; the executors only decide the flow, so the workflow is tested
  without a database.
- Crash safety comes from Kafka and the database, not from workflow checkpoints: the offset is committed only
  after the outcome is durable, and the inbox plus the unique (assessment_id, revision) stop a repeat (§11).
- The device form (wound-event 1.0) does not collect `ulcerLocation`, `probeToBone`, `infectionGrade` or
  `woundBedLabels` yet, so BuildContext sends them as `not_recorded`. Recorded fields are copied unchanged.
  Revisit when the team settles the field list (§15).
- Pull records the `DELIVERED` provenance stage the first time a recommendation goes out.

## Phase 7: Retries and dead-letter topic

- On a transient failure: copy to `retry.30s`, then `retry.5m`, then `dlq`; commit the original offset.
- Retry consumers pause their partition until the message's delay has passed.
- 422/409 from the Recommendation Service go straight to the DLQ.

✅ **Verified:** `python tests/integration/e2e_retry.py` (13 checks: a deferred event recovers after its delay while a
newer event goes straight through; an event that keeps failing goes retry.30s → retry.5m → dlq with its history
in the headers) and `RetryRoutingTests`.

- The retry consumer uses its own group, `orchestrator-retry`, so a paused partition or a rebalance there never
  stalls the main orchestrator consumer.
- Delays default to 30 s and 5 min; `Retry__FirstDelaySeconds` / `Retry__SecondDelaySeconds` shorten them for tests.
- The persister keeps seek-and-redeliver for database errors: §8.1 gives the retry topics to the orchestrator only,
  and while the database is down no event can be persisted anyway.

## Phase 8: REST baseline

- `POST /v1/baseline/assessments`: writes PostgreSQL and calls the same stub inside the request.
  No idempotency, so the comparison shows what duplicates look like without the mechanism.

✅ **Verified:** `python tests/integration/e2e_baseline.py` (16 checks), including the two comparisons for §13:
a retried baseline request is stored twice while the same event pushed twice is stored once; with a 3 s
Recommendation Service the baseline makes the device wait 3 s while push answers in under a second.

- Same token rules, same wound-event validation, same request builder and the same answer validation as the
  event-driven path (`Sync.Common.Recommendations` is shared), so the architecture is the only variable.
- Writes its own `baseline` schema (migration `baseline/0001`), never the clinical record or the audit trail.
- A failed call returns 502 after the assessment is already written: a partial result, as a naive REST design has.

## Phase 9: Database roles

- One login role per service. `audit.provenance` and `audit.auth_audit` are insert-only.
  Only the identity service can read `clinical.clinician_credential` (v2.1: was the gateway). The `rag` role sees only `rag`.

✅ **Verified:** `python tests/integration/e2e_db_roles.py` (63 checks: each service is connected under its own role;
only `identity_svc` reads credentials; nobody can UPDATE, DELETE or TRUNCATE the audit tables; `rag_svc` sees only
`rag`; column-level writes such as the orchestrator changing only `wound_assessment.status`). Every other suite
passes with the services running under these roles, which shows the grants are sufficient.

- Roles: `identity_svc`, `gateway_svc`, `persister_svc`, `relay_svc`, `orchestrator_svc`, `rag_svc`, created in
  `db/migrations/_roles` (applied right after `_bootstrap`) and granted in `db/migrations/grants` (applied last).
- Passwords never go in a migration: db-migrator sets them from `ServiceRoles__<role>`; a role without one cannot
  log in. The owner login (`cdss`) is used only by db-migrate.
- A migration that adds a table grants on it in the same file.
- `INSERT ... ON CONFLICT (cols)` needs SELECT on the conflict columns: `grants/0002` adds those, column-level.
- Local `dotnet run` still uses the owner login from `appsettings.json`; the roles apply in Docker.

## Phase 10: Evaluation harness

- Device simulator (C# console, `tests/device-simulator`): 1–100 simulated devices, real queue states and
  leases, login, push and pull.
- Toxiproxy scenarios: latency, packet loss, disconnect cycles.
- Fault scripts: kill a consumer, oversized event, replay a batch twice, slow stub, credential lockout.
- SQL for each metric: sync latency, duplicate rate + DEDUPLICATED count, consumer lag,
  auditability completeness. Results exported as CSV for the report.

**Part 1 ✅ device simulator** (`tests/device-simulator`, README there): 1–100 phones with the §6 queue, leases,
backoff, gzip push, cursor pull, one clinician per device; event-driven or baseline mode; per-event CSV and a
summary. `backend/tests/DeviceSimulator.Tests` (10 tests) checks its queue and backoff rules. A 100-device ×
10-event run finishes all 1,000 events (894 advice, 106 superseded edits) with save-to-accepted p50 0.7 s / p95 2 s;
the database matches it exactly.

The first 100-device runs found three backend problems, fixed in the same step:

- **Login under load**: the Argon2id check ran while holding a database connection and the credential row lock, so
  100 simultaneous logins exhausted the identity service's pool (HTTP 500). The hash is now checked first with no
  connection held (at most one check per CPU core at a time, 64 MB each); the transaction re-reads the row under
  lock and only trusts the result if it was computed against the stored hash. Lockout, MFA and audit are unchanged.
- **Pull livelock**: the 60 s re-send window shared one `LIMIT` with the rows after the cursor. With more recent
  changes than the limit, every page was re-sent rows and `nextCursor` stopped advancing (62,514 pulls in one run).
  The two are now read separately; only rows after the cursor decide `nextCursor` and `hasMore`.
- **Connection budget**: each Npgsql pool defaulted to 100, enough for one service to take all of PostgreSQL's
  connections. Pools are now bounded in docker-compose (identity 20, gateway 30, persister 10, relay 5,
  orchestrator 10).

## Phase 11: Observability

- OpenTelemetry in every service; W3C `traceparent` carried in Kafka headers; Grafana dashboard for lag and latency.

## Phase 12: Mobile

- Drift queue repository, leases, sync engine in a background isolate, login, pull, SQLCipher.
  Needs Flutter installed.
