# Integrating the Recommendation Service (Member 3)

How Member 3's service plugs into the backend Member 4 built. Everything on the backend side is in place and tested
against the stub (`backend/tests/rag-stub`); switching to the real service is one environment variable.

Architecture: §10 (orchestrator and RAG contract), §10.4 (figures), §10.5 (rag schema), §12 (data minimisation).

## 1. What the service must provide

| Endpoint | Called by | Contract |
|----------|-----------|----------|
| `POST /v1/recommendations` | orchestrator (and the REST baseline, §13.1) | `contracts/rag-request.schema.json` → `contracts/rag-response.schema.json` (RAG contract 1.0) |
| `GET /v1/figures/{corpusVersion}/{figureId}` | Sync Gateway's figures proxy (§10.4) | image bytes; see §4 below |
| `GET /health` | Docker, operators | 200 when ready |

The service listens on port 8080 inside the container (the shared `backend/Dockerfile` sets `ASPNETCORE_URLS`).

## 2. What the service receives

Built by the orchestrator's BuildContext step (`Sync.Common/Recommendations/RecommendationRequestMapper.cs`) and
validated in tests against `rag-request.schema.json`:

- `caseId` is the assessment id; `revision` the integer revision. **No identifiers cross the boundary**: no patientRef,
  deviceId, facilityId or eventId, and `fitzpatrickClass` is dropped (§10.2, data minimisation).
- `clinicalAssessment` carries every field the contract requires. Fields the clinician recorded are copied unchanged.
  The device form (wound-event 1.0) does not collect `ulcerLocation`, `probeToBone`, `infectionGrade` or
  `woundBedLabels` yet, so those arrive as `"not_recorded"` until the team settles the field list (§15).
- `healingHistory`: the latest revision of every earlier assessment of the same wound, oldest first, with each
  capture's pipeline versions (for the stalled-healing rule and the comparability check).

## 3. How each answer is handled

Implemented in `backend/apps/sync/orchestrator` and covered by `tests/integration/e2e_orchestrator.py` / `e2e_retry.py`.

| Service answers | Backend does |
|-----------------|--------------|
| 200, `mode: generated` or `extractive` | Checked (§10.1 ValidateResponse), stored, delivered. Degraded answers count as success (§10.3). |
| 200 but invalid: schema mismatch, answers another `caseId`/`revision`, or a section's citation tag does not resolve in `citations` | Not stored. Retried after 30 s, then 5 min, then dead-lettered. The device sees ADVICE_DEFERRED. |
| 503, timeout, connection refused | Retried within the call (2 retries, backoff), then ADVICE_DEFERRED and the retry topics (30 s, 5 min), then the DLQ. |
| 422 or 409 | **Never retried**: a contract or ordering bug. Straight to `wound-events.dlq`; the device sees ADVICE_DEFERRED. |

Limits to design for:

- **Time**: 60 s per attempt (`RecommendationService__AttemptTimeoutSeconds`), above the service's own two 20 s
  generation attempts (§10.3). A circuit breaker opens if calls keep failing.
- **Idempotency on `(caseId, revision)`**: the same request can arrive more than once (a retry after a timeout,
  a redelivery after a crash). Answer it with the stored response; do not generate again (§10.3). The orchestrator's
  inbox already stops repeats it knows about, so this is the safety net.
- **Concurrency**: up to 6 requests at once per orchestrator replica (`Orchestrator__MaxConcurrency`, one per
  Kafka partition), more with replicas.
- **Tracing**: requests carry a W3C `traceparent` header. If the service uses `builder.AddSyncTelemetry("recommendation-service")`
  from `Sync.Common`, its spans join the same trace in Jaeger (plan phase 11).

## 4. Figures (§10.4)

The device never calls the service: it asks the Sync Gateway, which forwards `GET /v1/figures/{corpusVersion}/{figureId}`
(`backend/apps/sync/sync-gateway/Endpoints/FiguresProxyEndpoint.cs`, tested by `tests/integration/e2e_figures.py`).

- Ids are checked before forwarding: `^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$`, no `..`. Use ids in that form.
- Return the image with its `Content-Type`. These headers are passed through to the device:
  `ETag`, `Last-Modified`, `Cache-Control`, `X-Figure-Licence`, `X-Figure-Attribution`, `X-Figure-Tier`.
  (The `X-Figure-*` names are a proposal; agree them, or tell Member 4 the names you use.)
- Answer `If-None-Match` with 304 when the ETag matches. Unknown figure: 404.
- A corpus version is frozen, so without a `Cache-Control` from the service the gateway marks figures immutable.
- Figures over 10 MB are refused (`FIGURE_TOO_LARGE`); anything other than 200/304/404 becomes `FIGURE_UNAVAILABLE`.

## 5. Database (§10.5, plan phase 9)

- The service logs in as **`rag_svc`**, which sees the `rag` schema only (no grant on clinical, messaging, audit,
  sync or baseline). Its password is set by db-migrate from `DB_PASSWORD_RAG`; the compose entry already passes the
  connection string.
- Tables come from **migrations in `db/migrations/rag/`**, applied by the db-migrator as the owner. `rag_svc` is granted
  SELECT, INSERT, UPDATE, DELETE on every table and USAGE on every sequence created there, automatically (default
  privileges in `grants/0001`): no grant statements needed.
- **Add `0002_...sql` and onwards. Never edit `0001_create_rag_schema.sql`**: it is already applied everywhere, and
  the migrator refuses to run when an applied file changes (checksum, §9.2), even a comment. Its note "Member 3
  replaces this" predates that rule; extend it instead.
- `pgvector` is installed in the `rag` schema (`CREATE EXTENSION vector SCHEMA rag` in 0001).
- `rag` is not in the other services' schema-version check, so rag migrations never block them. The service can run
  its own check with `SchemaVersionGuard.EnsureAsync(dataSource, new Dictionary<string, int> { ["rag"] = N })`.

## 6. Running it

The project is `backend/apps/recommendation-service` (own `Directory.Packages.props`). docker-compose has an entry for
it under the `rag` profile (host port 5081). One variable switches the orchestrator, the figures proxy and the REST
baseline from the stub to it:

```bash
RECOMMENDATION_SERVICE_URL=http://recommendation-service:8080 docker compose --profile rag up -d --build
```

Without the variable everything keeps using the stub. `dotnet run`: set `RecommendationService__BaseUrl` for the
orchestrator and sync-gateway instead.

## 7. Checking the integration

With the variable set and the stack up:

1. `curl http://localhost:5081/health`.
2. `python tests/integration/e2e_smoke.py`, then push one assessment (`dotnet run --project tests/device-simulator --
   --devices 1 --events 3`) and check that it reaches COMPLETE: the round trip through the real service.
3. Validate a few real answers against `contracts/rag-response.schema.json` and confirm every section's tags appear in
   `citations` (the orchestrator dead-letters answers that fail this).
4. Rerun the evaluation against the real service: `python tests/evaluation/run_experiment.py clean` and `slow-advice`.

The failure-mode sections of `e2e_orchestrator.py`, `e2e_retry.py`, `e2e_baseline.py` and `e2e_figures.py` restart
the **stub** in 503 / 422 / uncited modes on purpose; run those against the default (stub) setup.

## 8. Open with the team

- The clinical field list and `woundBedLabels` (§15): four fields arrive as `not_recorded` until then.
- The figure headers' names (§4 above).
- Whether `rag_svc` needs any table outside `rag` (§10.5 says no; ask Member 4 if that changes).
