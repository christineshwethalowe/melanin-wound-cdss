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
then starts the API gateway, identity service, Sync Gateway, ingest persister, outbox relay, orchestrator and the Recommendation Service stub.

| What | Where |
|------|-------|
| API gateway (the only backend URL) | http://localhost:8080: `/v1/auth/*`, `/v1/admin/*`, `/.well-known/*` → identity service; `/health`, `/v1/sync/*`, `/v1/patients/*`, `/v1/figures/*` → Sync Gateway |
| Recommendation Service stub | http://localhost:5080 |
| Kafka UI | http://localhost:8081 |
| PostgreSQL | `localhost:5432` (cdss / cdss) |
| Demo users (local only) | `admin.demo` / `Demo-Admin-2026!` (admin), `n.silva` / `Demo-Pass-2026!` (nurse), facility `fac-001` |

```bash
docker compose logs -f api-gateway identity-service sync-gateway   # follow logs
docker compose down                                    # stop (keeps data)
docker compose down -v                                 # stop and delete Kafka + Postgres data
```

After changing backend code, run `docker compose up -d --build` again.

### Services with `dotnet run` (for development)

Requirements: Docker, .NET 10 SDK, Flutter 3.x (for the mobile app).

```bash
cp .env.example .env
docker compose up -d kafka postgres kafka-ui   # infrastructure only
docker compose --profile tools up -d           # optional: Toxiproxy, OTel, Prometheus, Grafana
bash infra/kafka/create-topics.sh              # create the topics
dotnet run --project backend/tools/db-migrator # apply db/migrations
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

# end-to-end check: login → push → Kafka → PostgreSQL → outbox → pull
python tests/integration/e2e_smoke.py

# registration, MFA, patient alias and audit trail (build step 2)
python tests/integration/e2e_step2_auth_admin.py

# admin dashboard sessions: no device, admins only, refused by the Sync Gateway (ADR 0005)
python tests/integration/e2e_dashboard_client.py

# orchestrator: recommendation round trip, superseded revisions, replay, killed worker, 503 / 422 / invalid answers.
# Disruptive (restarts the rag-stub and the orchestrator); Docker stack only.
python tests/integration/e2e_orchestrator.py

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

Progress and next steps for the backend: [docs/member4-backend-plan.md](docs/member4-backend-plan.md)

## Working together

- Protect `main`: changes go in through a pull request, which needs one review and passing CI.
- Branch names: `feat/m<member>-<topic>` (e.g. `feat/m4-persister`), `fix/...`, `docs/...`.
- Mention the architecture section in commit messages, e.g. `persister: advisory lock (§9.3)`.
- Once a migration file has been applied anywhere, never edit it. Fix it with a new migration instead.
- Record significant decisions in `docs/decisions/` (one short markdown file per decision).

## Documentation

- [Backend Architecture v2.0 (Member 4)](docs/architecture/backend-architecture-v2.0.pdf)
