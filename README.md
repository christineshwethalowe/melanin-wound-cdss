# melanin-wound-cdss

**AI-Driven Clinical Decision Support for Wound Assessment on Melanin-Rich Skin**
Project J26-SE-330 · SLIIT · Supervisor: Prof. Dasuni Nawinna · Co-supervisor: Ms. Narmada Gamage

An offline-first mobile system for assessing diabetic foot ulcers (DFU) on Fitzpatrick IV–VI skin in
low-connectivity Sri Lankan hospitals. It measures the wound on the device, corrects colours for skin tone,
syncs safely when a connection is available, and returns care recommendations based on the IWGDF guidelines.

## Components

| # | Member | ID | Component | Lives in |
|---|--------|----|-----------|----------|
| 1 | G. S. R. Nanayakkara | IT23343702 | Colour-bias correction and on-device ML pipeline | `research/member1-calibration`, `mobile/lib/features/calibration`, `mobile/native` |
| 2 | H. M. T. S. M. Dissanayake | IT23294066 | Wound segmentation (YOLO11) and measurement | `research/member2-segmentation`, `mobile/lib/features/measurement` |
| 3 | T. H. Nimnath Nadushka | IT23294202 | Recommendations based on guidelines, with an admissibility gate | `backend/apps/recommendation-service`, `backend/apps/ingestion-pipeline`, `db/migrations/rag` |
| 4 | W. C. S. A. Lowe | IT23227354 | Offline-first sync and orchestration | `mobile/lib/features/sync`, `backend/apps/sync/*`, `backend/libs`, `db/migrations/{clinical,messaging,audit,sync}`, `infra` |

Shared by everyone: `contracts/`. This holds the only schemas the components exchange, and any change to it needs review from all four members.

## Repository layout

```
melanin-wound-cdss/
├── contracts/            # JSON Schemas every component builds against (the Wound Event, sync, auth, RAG)
├── mobile/               # Flutter app: calibration → measurement → Drift queue → sync engine
├── frontend/admin_dashboard/  # Flutter Web: facility admin dashboard (clinicians, devices, audit log; ADR 0005)
├── backend/
│   ├── apps/api-gateway/             # Member 4: YARP edge, the only public port (routing, rate limits, CORS)
│   ├── apps/identity-service/        # Member 4: login, MFA, clinician admin; signs RS256 tokens, publishes JWKS
│   ├── apps/sync/        # Member 4: sync-gateway, ingest-persister, outbox-relay, orchestrator
│   ├── apps/recommendation-service/   # Member 3
│   ├── apps/ingestion-pipeline/       # Member 3, offline guideline ingestion (A1–A9)
│   ├── libs/Sync.Common/ # shared Kafka, Postgres, advisory-lock and schema-version code
│   ├── tools/db-migrator/# applies db/migrations and keeps the migration ledger
│   └── tests/rag-stub/   # stand-in for the Recommendation Service while benchmarking
├── db/migrations/        # numbered SQL, one folder per schema
├── infra/                # Kafka topics, OTel, Prometheus, Grafana, Toxiproxy
├── research/             # Python notebooks and training code (Members 1–3)
├── tests/                # system-level tests: integration, device simulator, fault injection
└── docs/                 # architecture documents and decision records
```

## Running locally

### Everything in Docker (only Docker needed)

```bash
docker compose up -d --build
```

This starts Kafka, Postgres, creates the topics, applies the migrations and seed, creates two demo users,
then starts the API gateway, identity service, Sync Gateway, ingest persister, outbox relay, orchestrator, the Recommendation Service stub
and the admin dashboard.

| What | Where |
|------|-------|
| Admin dashboard: sign in as `admin.demo` / `Demo-Admin-2026!` (admins only) | http://localhost:3001 |
| API gateway (the only backend URL) | http://localhost:8080: `/v1/auth/*`, `/v1/admin/*`, `/.well-known/*` → identity service; `/health`, `/v1/sync/*`, `/v1/patients/*`, `/v1/figures/*` → Sync Gateway |
| Recommendation Service stub | http://localhost:5080 |
| Kafka UI | http://localhost:8081 |
| Grafana: dashboard "Sync pipeline (§13)" (`--profile tools`) | http://localhost:3000 |
| Jaeger: traces across services (`--profile tools`) | http://localhost:16686 |
| Prometheus (`--profile tools`) | http://localhost:9090 |
| PostgreSQL | `localhost:5432` (cdss / cdss) |
| Demo users (local only) | `admin.demo` / `Demo-Admin-2026!` (admin), `n.silva` / `Demo-Pass-2026!` (nurse), facility `fac-001` |

```bash
docker compose logs -f api-gateway identity-service sync-gateway   # follow logs
docker compose down                                    # stop (keeps data)
docker compose down -v                                 # stop and delete Kafka + Postgres data
```

After changing backend code, run `docker compose up -d --build` again.

### Run from VS Code (F5)

The repository includes `.vscode/launch.json` and `tasks.json`:

1. Run the task **infrastructure: up** (Terminal → Run Task). If the full stack is already running in Docker,
   run **infrastructure: stop backend containers** first so ports 8080, 8085 and 8086 are free.
2. Choose **Backend: all services** in Run and Debug and press F5. It builds once, then starts the identity service
   (8085), sync-gateway (8086), API gateway (8080, the URL clients use), ingest persister, outbox relay and
   orchestrator, each with the debugger attached. To start one service on its own, build first (Ctrl+Shift+B).
3. Run the task **test: e2e smoke**, or launch **device simulator**.

Tasks also cover the unit tests and the observability stack (**observability: up**).

### Services with `dotnet run` (for development)

Requirements: Docker, .NET 10 SDK, Flutter 3.x (for the mobile app).

No `.env` is needed: every setting has a local-dev default in `docker-compose.yml` (`${NAME:-default}`). To
override one, put it in a local `.env` (never committed), e.g. `DB_PASSWORD_GATEWAY=...`. The `dotnet run` services
read `appsettings.json`, which expects the PostgreSQL login `cdss`/`cdss`: if you set `POSTGRES_PASSWORD`, change
those files too.

```bash
# infrastructure: Kafka + topics, Postgres + migrations + demo users, the Recommendation Service stub
docker compose up -d kafka postgres kafka-ui kafka-init db-migrate seed-demo-users rag-stub
docker compose --profile tools up -d           # optional: Toxiproxy, OTel collector, Jaeger, Prometheus, Grafana
dotnet build MelaninWoundCdss.slnx

# local test clinician (dev only). Bootstrap one admin per facility this way; the admin then
# registers everyone else through POST /v1/admin/clinicians
dotnet run --project backend/apps/identity-service -- create-clinician n.silva Demo-Pass-2026! nurse fac-001 N. Silva

# run the pipeline (separate terminals)
dotnet run --project backend/apps/identity-service --urls http://localhost:8085
dotnet run --project backend/apps/sync/sync-gateway --urls http://localhost:8086
dotnet run --project backend/apps/api-gateway --urls http://localhost:8080   # routes to 8085 and 8086
dotnet run --project backend/apps/sync/ingest-persister
dotnet run --project backend/apps/sync/outbox-relay

# everything non-disruptive in one go, with a pass/fail summary (add --unit, --mobile or --all)
python tests/check_backend.py

# end-to-end check: login → push → Kafka → PostgreSQL → outbox → pull
python tests/integration/e2e_smoke.py

# registration, MFA, patient alias and audit trail (build step 2)
python tests/integration/e2e_step2_auth_admin.py

# device revocation (§12): a revoked phone cannot log in or refresh, and its access token is refused within 30 s
python tests/integration/e2e_device_revocation.py

# admin dashboard sessions: no device, admins only, refused by the Sync Gateway (ADR 0005)
python tests/integration/e2e_dashboard_client.py

# orchestrator: recommendation round trip, superseded revisions, replay, killed worker, 503 / 422 / invalid answers.
# Disruptive (restarts the rag-stub and the orchestrator); Docker stack only.
python tests/integration/e2e_orchestrator.py

# figures proxy (§10.4): guideline figures through the gateway, licence headers, ETag/304, unsafe ids refused
python tests/integration/e2e_figures.py

# REST baseline (§13.1): one synchronous request, no idempotency; compared with push on duplicates and waiting.
python tests/integration/e2e_baseline.py

# device simulator (§13.1): 1-100 phones with the real queue, login, push and pull; CSV + summary per run
dotnet run --project tests/device-simulator -- --devices 10 --events 20

# evaluation scenarios (§13): faults through Toxiproxy, event-driven vs baseline, metrics into results/summary.csv
docker compose --profile tools up -d toxiproxy
python tests/evaluation/run_experiment.py loss --devices 10 --events 10

# auth health (§13): login failure rate, lockouts per day, average session lifetime (metrics.py + Grafana)
python tests/integration/e2e_auth_health.py
python tests/evaluation/metrics.py auth 24      # the same numbers for the last 24 hours

# housekeeping (§9.4): outbox cleanup, change-log archival (no device misses a change), inbox retention
python tests/integration/e2e_housekeeping.py
docker compose run --rm housekeeping run-once   # one cycle by hand; prints what it removed

# per-service database roles: insert-only audit, credentials readable only by the identity service
python tests/integration/e2e_db_roles.py

# retry topics: delayed redelivery without head-of-line blocking, then the DLQ. Disruptive, about 2 minutes.
python tests/integration/e2e_retry.py

# whole stack against the architecture, section by section, including §11 failures.
# Disruptive (stops Kafka, replays the topic, scales services); Docker stack only, takes a few minutes.
python tests/integration/e2e_architecture.py
```

Tokens are signed by the identity service with RS256; other services validate them locally against its
public keys at `/.well-known/jwks.json` ([ADR 0003](docs/decisions/0003-identity-service.md)). Locally the
signing key is generated at start-up, so restarting the identity service makes devices refresh their token.
All client traffic goes through the API gateway ([ADR 0004](docs/decisions/0004-api-gateway.md)), which also
checks the token, rate-limits login (`RateLimits` in its `appsettings.json`; raise them for load tests) and
allows CORS only for the admin dashboard origin.
Outside local development, set these for the identity service instead of using the defaults:

- `Jwt__SigningKeyPem`: RSA private key in PEM, e.g. from `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048`
- `Secrets__EncryptionKey`: at least 32 characters (encrypts MFA secrets)

Each service logs in to PostgreSQL with its own least-privilege role (plan phase 9). Docker uses local-dev
passwords; outside a laptop demo set `DB_PASSWORD_IDENTITY`, `DB_PASSWORD_GATEWAY`, `DB_PASSWORD_PERSISTER`,
`DB_PASSWORD_RELAY`, `DB_PASSWORD_ORCHESTRATOR`, `DB_PASSWORD_RAG` and `DB_PASSWORD_HOUSEKEEPING` in `.env`;
db-migrate applies them.

Progress and next steps for the backend: [docs/member4-backend-plan.md](docs/member4-backend-plan.md)

Plugging in Member 3's Recommendation Service (contract, error handling, figures, database role, how to switch from
the stub): [docs/integration/recommendation-service.md](docs/integration/recommendation-service.md)

## Working together

- Protect `main`: changes go in through a pull request, which needs one review and passing CI.
- Branch names: `feat/m<member>-<topic>` (e.g. `feat/m4-persister`), `fix/...`, `docs/...`.
- Mention the architecture section in commit messages, e.g. `persister: advisory lock (§9.3)`.
- Once a migration file has been applied anywhere, never edit it. Fix it with a new migration instead.
- Record significant decisions in `docs/decisions/` (one short markdown file per decision).

## Documentation

- [Backend Architecture v2.0 (Member 4)](docs/architecture/backend-architecture-v2.0.pdf)
