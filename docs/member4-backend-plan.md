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
| 10 | Evaluation harness: device simulator, faults, metrics | §13 | ✅ |
| 11 | Observability: OpenTelemetry, Prometheus, Grafana | §13 | ✅ |
| 12 | Mobile: Drift queue + sync engine | §6 | ✅ (to verify on a device: see below) |

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
| A8 | CI jobs, dev setup (VS Code launch/tasks), README | — | ✅ |

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

### Phase 1c: device revocation (§12: "devices are registered and can be revoked") ✅

- `GET /v1/admin/devices`: the facility's devices with registration time, revocation time, active sessions and
  the last clinician who used each one.
- `POST /v1/admin/devices/{deviceId}/revoke`: for a lost or stolen phone. In one transaction it sets
  `clinical.device.revoked_at`, ends every session on the device and writes `DEVICE_REVOKE` to `audit.auth_audit`
  with the admin as actor. Devices of another facility are 404; revoking twice is 409 `DEVICE_ALREADY_REVOKED`.
  Revocation is permanent: a recovered phone is re-enrolled under a new device id.
- Effect: login on the device is refused (`DEVICE_NOT_ALLOWED`, which the app already explains). Refresh is
  refused immediately: it checks the device as well as the session. The Sync Gateway also refuses the device's
  unexpired access tokens, because `DeviceRevocationCheck` looks up `revoked_at` after the signature is validated.
  The answer per device is cached for 30 s, so revocation takes effect within 30 s instead of the token's
  15 minutes, at the cost of at most one lookup per device every 30 s. Admin-dashboard tokens have no device
  and skip the check.
- Migrations: `audit/0004_auth_audit_device_revoke.sql`, `grants/0004_device_revocation.sql` (identity may set
  `revoked_at` and nothing else on `clinical.device`; the gateway may read `device_id, revoked_at` only).

**Verified:** `python tests/integration/e2e_device_revocation.py` (21 checks), plus 5 new checks in
`e2e_db_roles.py` for the column grants.

### Phase 1d: housekeeping (§9.4: outbox, change log and inbox are short-lived by design) ✅

A separate worker, `backend/apps/sync/housekeeping`, with its own database login `housekeeping_svc`. One cycle every
10 minutes, in batches of 5,000 rows. An advisory lock means that if it is ever scaled out, only one instance works at
a time. `docker compose run --rm housekeeping run-once` runs one cycle by hand.

| Table | Rule | Default |
|---|---|---|
| `messaging.outbox` | Published rows older than the retention are deleted. Unpublished rows are never touched. | 1 hour |
| `sync.change_log` | Moved to `sync.change_log_archive` once **every device of the facility that is not revoked** has a cursor at or past the row (a device that has never pulled counts as 0), and the row is older than the margin | 24 hours (minimum 10 minutes, far above pull's 60 s re-send window) |
| `messaging.inbox` | Rows older than the redelivery window are deleted. The window is read from Kafka: the longest `retention.ms` of the topics the orchestrator reads (persisted, both retry topics, DLQ) plus one day, never less than the configured minimum. If Kafka cannot be asked, or a topic is kept forever, nothing is deleted. | 8 days (Kafka's 7 + 1) |

- Why no device can miss a change: pull returns rows after the cursor the device sends, and the server records that
  cursor (`sync.device_cursor`). A row is archived only when it is at or below every active device's recorded cursor,
  so every one of them has already applied it. The margin also covers rows that commit out of sequence order.
- A phone that has stopped syncing holds its facility's archival back. Each cycle logs which device it is ("revoke it
  if the phone is lost"); revoking it (phase 1c) releases the rows. A phone enrolled later starts from the archival
  point: it does not receive the ward's archived history.
- Metric `sync_housekeeping_rows_total{table}` (outbox, change_log, inbox).
- Migrations: `_roles/0002` (role), `messaging/0002` (age indexes), `sync/0002` (archive table), `grants/0005`
  (housekeeping may delete outbox/inbox/change-log rows and read only the columns its rules use; no payloads,
  no clinical data, cannot change devices or the archive).
- First run on the development database: the 18,611 published outbox rows were deleted. No inbox row was older than
  8 days. No change-log row was archived, because test devices that never pulled still hold `fac-001`, as the
  rule intends.

**Verified:** `python tests/integration/e2e_housekeeping.py` (21 checks: a lost phone holds archival back until it is
revoked; only rows every phone acknowledged are archived; the phone behind resumes and receives every later row;
a whole-database check finds no archived row ahead of any active device's cursor; outbox and inbox rules).
16 new checks in `e2e_db_roles.py`, 8 unit tests in `Housekeeping.Tests` (retention policy, option validation).

### Phase 1e: auth-health metrics (§13 metric table: "login failure rate, lockouts per day, average session lifetime") ✅

- **Definitions** (the same in `metrics.py` and on the dashboard):
  - *Login failure rate* = failed attempts / attempts. A `LOCKOUT` row is the failed attempt that locked the account,
    so it counts as a failure. `MFA_REQUIRED` is the server asking for a code, not a failed attempt, so it counts as
    neither (the lockout counter ignores it too).
  - *Lockouts per day* = `LOCKOUT` events in 24 hours.
  - *Session lifetime*: a session is a login plus its chain of refreshes. Refresh rotates the token by ending one
    `clinician_session` row and issuing another, so the new `family_id` column links them: a login starts a family,
    each refresh carries it on (`clinical/0009_session_family.sql`, with a default so the previous service version
    still works). Lifetime = login to logout or revocation (logout, deactivation, device revocation). Sessions whose
    refresh token expired unused are counted apart, because their last use is not recorded precisely.
- **Identity service** (`AuthMetrics`, `AuthHealthSampler`): every `audit.auth_audit` write also increments
  `auth_events_total{action, success, reason}`. Once a minute the sampler reads the session table and exports
  `auth_sessions_active{client}`, `auth_session_lifetime_seconds{client, state=active|ended}` and
  `auth_sessions_ended{client, how=revoked|expired}` for the last 24 hours. The counter series the dashboard reads are
  created at 0 on startup, because Prometheus' `increase()` ignores the first value of a new series.
- **Grafana**: new row "Auth health" with login failure rate, lockouts per day, average session lifetime (mobile,
  ended in 24 h), active sessions, login attempts by result, and lifetime by client.
- **`metrics.py`**: `auth_health(since, until)` returns logins, failures, lockouts, MFA prompts, failure rate,
  lockouts per day, failures by reason, and sessions started, ended and active at the end, with mean and median
  lifetime. Every evaluation run includes it, and summary.csv gains `login_failure_rate` and `lockouts`.
  `python tests/evaluation/metrics.py auth [hours]` prints it for any period.
- `run_experiment.py` now starts simulated devices at the head of the change log *or* its archive, because
  housekeeping may archive an idle facility's newest rows.

**Verified:** `python tests/integration/e2e_auth_health.py` (17 checks): known traffic gives exactly 2 logins,
5 failures, 1 lockout and rate 5/7. The MFA prompt is not counted. A refresh stays in its login's family. The logout
gives a lifetime of about 3 s across the refresh. The counters and gauges reach Prometheus. Every new dashboard query
evaluates in Prometheus.

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

**Member 3 handover ✅** (`docs/integration/recommendation-service.md`):

- Figures proxy `GET /v1/figures/{corpusVersion}/{figureId}` (§10.4), was 501: forwards to the Recommendation
  Service for device tokens only, refuses unsafe ids before forwarding, passes through the image, Content-Type, ETag
  / 304 and licence / attribution headers, marks figures immutable (frozen corpus), 502 FIGURE_UNAVAILABLE with
  Retry-After when the service is down. The stub serves sample figures F1-F3. `tests/integration/e2e_figures.py`
  (16 checks).
- docker-compose entry `recommendation-service` (profile `rag`, port 5081, logs in as `rag_svc`), and one variable,
  `RECOMMENDATION_SERVICE_URL`, that switches the orchestrator, figures proxy and baseline from the stub to it.
- Integration guide: what the service receives, how each answer is handled, limits (60 s, retries, idempotency,
  concurrency), figures, the rag schema and role, how to switch and check.

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

✅ **Verified:** `python tests/integration/e2e_db_roles.py` (84 checks: each service is connected under its own role;
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

**Part 2a ✅ scenarios and metrics** (`tests/evaluation`, README there): `run_experiment.py` runs the simulator
through Toxiproxy under a fault (`clean`, `latency`, `loss`, `flaky`, `slow-advice`, `consumer-kill`) in event-driven
and baseline mode, samples consumer lag, and writes the §13 metrics (`metrics.py`: sync latency, duplicates and
DEDUPLICATED rows, device resends, auditability completeness, auth health) per run plus a `summary.csv` row.

- Every scenario uses the same simulator settings so they compare: a new connection per request (Toxiproxy faults
  are per connection), a capture every 2 s, and clinicians registered directly before the run.
- The simulator now survives network errors like a real device: login retries with backoff, a failed step backs
  off, and rows leased by an interrupted push are released at once.

First results (10 devices × 10 events, through Toxiproxy): every event finished in every scenario with audit
completeness 1.0. The event-driven path stored **0 extra rows in every scenario** (21 resends under `loss`); the
baseline stored 8 (`loss`) and 1 (`flaky`). With a 3 s Recommendation Service the event-driven path confirmed a save
in 0.6 s against the baseline's 8.9 s. On a clean or merely slow network the baseline is quicker (one request,
fewer round trips).

**Part 2b ✅ scaling** (`tests/evaluation/scaling.py`, §8.1):

- `slow-advice` showed the orchestrator handled one message at a time (advice p50 118 s). It now processes
  partitions in parallel and each partition in order (`Consumers/PartitionWorkers.cs`, §4 "concurrency per
  consumer"): offsets are committed by the consume thread only after a message's outcome is durable, a full
  partition is paused, and a partition handed over in a rebalance finishes its message and commits first.
  `Orchestrator__MaxConcurrency` (default 6, 1 = one at a time). Same scenario: advice p50 19 s, p95 38 s.
- Orchestrator, 120 events, 1 s Recommendation Service, one message at a time per replica: advice p50 54.8 s (1
  replica) → 28.9 s (2) → 20.1 s (3) → 12.8 s (6); one replica with per-partition concurrency: 12.2 s.
- Persister, 1,000-capture burst from 50 devices: throughput 36/s (1 replica) → 53/s (2), then flat at about 50/s
  while the backlog keeps shrinking (232 → 22): past two replicas the devices' arrival rate is the limit, not the
  persister.
- 0 extra rows and audit completeness 1.0 at every scale.

**Part 2c ✅ duplicate ablation** (§13: "an ablation run with the constraint and inbox switched off"):

- Dropping the real constraints would corrupt the clinical record, so `Ablation__Enabled=true` records **shadow
  rows** instead (`ablation` schema, no unique constraints): the gateway stops answering DUPLICATE, the persister
  writes every message it receives to `ablation.wound_assessment`, and the orchestrator skips its inbox and writes
  every recommendation it would store to `ablation.recommendation`. The real tables keep their protections. Each
  service logs a warning in this mode; it is off by default and the runner switches it back off.
- `run_experiment.py --ablation`, and a `replay` scenario (both consumer groups rewound to the run's start: every
  message delivered twice, §11).
- Results, 100 events: under `loss` the device's 27 resends would have been 27 duplicate assessments (shadow 127
  rows; real store 100, 27 absorbed). Under `replay`: shadow 200 assessments and 168 recommendations (100 and 84
  duplicates); real store 100 and 84, 0 duplicates, 100 absorbed.

Phase 10 complete: simulator, scenarios, metrics, scaling and ablation. Results are written to
`tests/evaluation/results/` (not committed).

## Phase 11: Observability

- OpenTelemetry in every service; W3C `traceparent` carried in Kafka headers; Grafana dashboard for lag and latency.

✅ `docker compose --profile tools up -d`, then Grafana http://localhost:3000, Jaeger http://localhost:16686,
Prometheus http://localhost:9090.

- `Sync.Common/Telemetry/TelemetrySetup.cs`: one call per service (`builder.AddSyncTelemetry("name")`) for traces and
  metrics over OTLP, with HttpClient, Npgsql and runtime instrumentation; web services add ASP.NET Core. Exporting
  is on only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (compose sets it), so a plain `dotnet run` exports nothing.
- `SyncMetrics`: push results (ACCEPTED / DUPLICATE / REJECTED), persister outcomes (incl. deduplicated) and
  duration, outbox publishes, orchestration outcomes and duration.
- Collector → Jaeger (traces) and Prometheus (metrics, `service_name` label); kafka-exporter for consumer lag.
- One trace follows an assessment: api-gateway → sync-gateway push → ingest-persister → orchestrator → the
  Recommendation Service call (a sample trace had 55 spans across five services).
- Dashboard "Sync pipeline (§13)", provisioned from `infra/grafana/dashboards/sync-pipeline.json` (generated by
  `infra/grafana/build_dashboard.py`); every query checked against Prometheus. During `consumer-kill` it shows the
  persister's lag rise to ~76 and drain; during `loss`, the resends answered DUPLICATE.
- Docker Desktop on Windows sometimes drops container creation when many start at once; if `up` fails with
  `error during connect ... EOF`, run it again with `COMPOSE_PARALLEL_LIMIT=2`.

## Phase 12: Mobile

- Drift queue repository, leases, sync engine in a background isolate, login, pull, SQLCipher.
  Needs Flutter installed.

**12a ✅** (`mobile/lib/features/sync`, details in `mobile/README.md`): queue repository, SQLCipher database with the
key in the keystore, wound-event validator, auth session, API client and sync engine. 38 tests without a device,
including §14.1 step 3 (1,000 saves survive a kill mid-sync) and step 4 (a flaky gateway never causes a lost or
stuck row), plus an integration test against the Docker backend.

**12b ✅:** sync triggers (`SyncScheduler`: connectivity regained, foreground, 2 s after a save, pull-to-refresh,
periodic, retry at the engine's time), sign-in (with MFA) and sync-status screens, a "save sample assessment" button
standing in for the capture flow of Members 1–2, and the app wiring (`AppServices`, `main.dart`). 48 tests in all.

**To verify on a device** (no Android SDK platform here): the app on an emulator or phone (steps in
`mobile/README.md`), the §13 frame-time metric, and Android periodic background sync (WorkManager, not built).
