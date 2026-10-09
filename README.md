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

Requirements: Docker, .NET 10 SDK, Flutter 3.x (for the mobile app).

```bash
cp .env.example .env
docker compose up -d                     # Kafka (KRaft), Postgres + pgvector, Kafka UI
docker compose --profile tools up -d     # optional: Toxiproxy, OTel, Prometheus, Grafana
bash infra/kafka/create-topics.sh        # create the topics
dotnet run --project backend/tools/db-migrator   # apply db/migrations
dotnet build MelaninWoundCdss.slnx

# local test clinician (dev only)
dotnet run --project backend/apps/sync/sync-gateway -- create-clinician n.silva Demo-Pass-2026! nurse fac-001 N. Silva

# run the pipeline (separate terminals)
dotnet run --project backend/apps/sync/sync-gateway --urls http://localhost:8080
dotnet run --project backend/apps/sync/ingest-persister
dotnet run --project backend/apps/sync/outbox-relay

# end-to-end check: login → push → Kafka → PostgreSQL → outbox → pull
python tests/integration/e2e_smoke.py
```

Progress and next steps for the backend: [docs/member4-backend-plan.md](docs/member4-backend-plan.md)

## Working together

- Protect `main`: changes go in through a pull request, which needs one review and passing CI.
- Branch names: `feat/m<member>-<topic>` (e.g. `feat/m4-persister`), `fix/...`, `docs/...`.
- Mention the architecture section in commit messages, e.g. `persister: advisory lock (§9.3)`.
- Once a migration file has been applied anywhere, never edit it. Fix it with a new migration instead.
- Record significant decisions in `docs/decisions/` (one short markdown file per decision).

## Documentation

- [Backend Architecture v2.0 (Member 4)](docs/architecture/backend-architecture-v2.0.pdf)
