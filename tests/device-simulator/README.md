# Device simulator (§13.1)

1–100 simulated phones that speak the real protocol through the API gateway, so the evaluation measures the
system the app will use. Run it on the same host as the stack, so device and server clocks agree for the latency
figures (§13: say so in the method).

Each simulated device follows the §6 rules the real app must follow:

- **Queue** with the §6.1 states: PENDING → IN_FLIGHT (2-minute lease) → ACCEPTED / REJECTED → ADVICE_DEFERRED → COMPLETE
  (or SUPERSEDED when an edit's newer revision got the advice).
- **Sync engine** (§6.2): `/health` probe before pushing; batches of ≤ 50 events / 256 KB, gzip; per-event results;
  DUPLICATE treated as ACCEPTED; backoff 2 s doubling to 5 min with full jitter, reset on success; `Retry-After`
  honoured on 429/503; 401 → refresh then retry; 413 → split the batch; no response → rows back to PENDING.
- **Pull** after its cursor until `hasMore` is false; COMPLETE when RECOMMENDATION_READY arrives.
- **Login**: one clinician per device, registered through `POST /v1/admin/clinicians` (as in a ward), or
  `--shared-user` for the one-account-many-phones case.
- **Captures**: valid wound events (§5), a few wounds per device so healing history builds, ~10 % edits
  (same assessment, revision + 1).

## Run

Needs the Docker stack (`docker compose up -d --build`) and its demo admin (`admin.demo`).

```bash
dotnet run --project tests/device-simulator -- --devices 10 --events 20
dotnet run --project tests/device-simulator -- --devices 10 --events 20 --mode baseline
dotnet run --project tests/device-simulator -- --devices 100 --events 10 --run-id load100
dotnet run --project tests/device-simulator -- --gateway http://localhost:18080   # through Toxiproxy (part 2)
```

| Option | Default | |
|--------|---------|-|
| `--devices` | 10 | 1–100 |
| `--events` | 20 | captures per device |
| `--mode` | event-driven | `baseline` sends each capture to `POST /v1/baseline/assessments` and retries it on failure |
| `--run-id` | timestamp | tags device ids (`sim-<run>-NNN`) and usernames, so the metric SQL can select one run |
| `--capture-interval-ms` | 500 | time between captures on a device |
| `--poll-interval-ms` | 1000 | time between sync runs |
| `--timeout-s` | 600 | unfinished events are reported as such; exit code 2 |
| `--edit-rate` | 0.1 | share of captures that are edits (revision + 1) |
| `--gateway` | `http://localhost:8080` | |
| `--admin-user`, `--admin-password` | `admin.demo` | registers the per-device clinicians |
| `--shared-user`, `--shared-password` | | every device logs in as this clinician |
| `--seed` | 42 | repeatable captures |
| `--out` | `tests/device-simulator/results` | |

## Output

- `<run>-<mode>-events.csv`: one row per event: status, attempts, enqueued / accepted / completed timestamps
  (device clock) and the latencies in ms.
- `<run>-<mode>-summary.json`: counts per status, pushes, pulls, resent events, p50/p95/p99 latencies.

The server side of each event (GATEWAY_ACCEPTED, PERSISTED, … DELIVERED in `audit.provenance`) joins on
`event_id`; the metric SQL is in plan phase 10 part 2.

## What the first 100-device runs found (fixed)

- Login held a database connection and the credential row lock while checking the Argon2id hash: 100
  simultaneous logins exhausted the identity service's pool. The hash is now checked before the transaction.
- Pull's 60 s re-send window shared one LIMIT with the rows after the cursor: under load every page was filled
  with re-sent rows and the cursor stopped advancing. They are now read separately.
- Each service's connection pool is bounded so all of them fit under PostgreSQL's 100 connections.
