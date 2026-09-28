# Member 4 backend plan (IT23227354)

Plan for finishing the backend from the architecture document (v2.0), following the build order in §14.1.
Each phase ends with a **done when** check that can be demonstrated on the local Docker stack.

Status key: ✅ done · 🔨 in progress · ⬜ not started

**Verify phases 1–5:** `python tests/integration/e2e_smoke.py` (18 checks) with the stack, gateway, persister and
relay running. Also verified by hand on 2026-09-28: replaying `wound-events` from offset 0 left the row count
unchanged and wrote a DEDUPLICATED provenance row; stopping Kafka made push return 503 with `Retry-After`,
and the pipeline recovered when Kafka came back.

| Phase | What | Architecture | Status |
|-------|------|--------------|--------|
| 0 | Repo, Docker stack, migrations, shared library | §4, §9.2, App. A | ✅ |
| 1 | Clinician auth in the gateway | §7.3, §9.1, §12 | ✅ |
| 2 | Push endpoint: validate → Kafka (acks=all) | §5, §7.1, §8.2 | ✅ |
| 3 | Ingest persister: Kafka → PostgreSQL, idempotent | §9.3, §9.4 | ✅ |
| 4 | Outbox relay: PostgreSQL → Kafka | §4, §9.3 | ✅ |
| 5 | Pull endpoint: change log → device | §7.2 | ✅ |
| 6 | Orchestrator workflow + Recommendation Service stub | §10 | ⬜ |
| 7 | Retry topics and dead-letter topic | §8.3, §11 | ⬜ |
| 8 | REST baseline endpoint | §13.1 | ⬜ |
| 9 | Per-service database roles (least privilege) | §9.4, §12 | ⬜ |
| 10 | Evaluation harness: device simulator, faults, metrics | §13 | ⬜ |
| 11 | Observability: OpenTelemetry, Prometheus, Grafana | §13 | ⬜ |
| 12 | Mobile: Drift queue + sync engine | §6 | ⬜ |

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

**Done when:** each persisted event produces exactly one recommendation, even if the worker is killed mid-run.

## Phase 7: Retries and dead-letter topic

- On a transient failure: copy to `retry.30s`, then `retry.5m`, then `dlq`; commit the original offset.
- Retry consumers pause their partition until the message's delay has passed.
- 422/409 from the Recommendation Service go straight to the DLQ.

## Phase 8: REST baseline

- `POST /v1/baseline/assessments`: writes PostgreSQL and calls the same stub inside the request.
  No idempotency, so the comparison shows what duplicates look like without the mechanism.

## Phase 9: Database roles

- One login role per service. `audit.provenance` and `audit.auth_audit` are insert-only.
  Only the gateway can read `clinical.clinician_credential`. The `rag` role sees only `rag`.

## Phase 10: Evaluation harness

- Device simulator (C# console, `tests/device-simulator`): 1–100 simulated devices, real queue states and
  leases, login, push and pull.
- Toxiproxy scenarios: latency, packet loss, disconnect cycles.
- Fault scripts: kill a consumer, oversized event, replay a batch twice, slow stub, credential lockout.
- SQL for each metric: sync latency, duplicate rate + DEDUPLICATED count, consumer lag,
  auditability completeness. Results exported as CSV for the report.

## Phase 11: Observability

- OpenTelemetry in every service; W3C `traceparent` carried in Kafka headers; Grafana dashboard for lag and latency.

## Phase 12: Mobile

- Drift queue repository, leases, sync engine in a background isolate, login, pull, SQLCipher.
  Needs Flutter installed.
