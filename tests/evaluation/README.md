# Evaluation experiments (§13)

Runs the device simulator (`tests/device-simulator`) under a fault, through Toxiproxy, against the event-driven
path and the REST baseline, then computes the §13 metrics from the simulator's CSV and the database.

```bash
docker compose up -d --build
docker compose --profile tools up -d toxiproxy
python tests/evaluation/run_experiment.py clean --devices 10 --events 10
python tests/evaluation/run_experiment.py loss --devices 20 --events 10
python tests/evaluation/run_experiment.py consumer-kill --devices 10 --events 20 --modes event-driven
```

Disruptive: it adds Toxiproxy toxics, restarts the rag-stub and kills consumers. Everything is put back at the end.
Local Docker stack only.

## Scenarios

| Scenario | Fault |
|----------|-------|
| `clean` | none: the reference |
| `latency` | +300 ms ±100 ms each way on every connection |
| `loss` | 10 % of connections reset, 5 % stall until timeout |
| `flaky` | network down for 8 s, up for 12 s, from 5 s in (disconnect cycles) |
| `slow-advice` | Recommendation Service answers after 3 s |
| `consumer-kill` | persister and orchestrator killed mid-run, restarted 10 s later |
| `replay` | after the run both consumer groups are rewound to its start: every message delivered twice (§11) |

Toxiproxy works per TCP connection, so `loss` approximates packet loss with resets and stalls. Say so in the
method, or reproduce it with Linux `tc netem` for true packet loss.

Every run uses the same simulator settings so the scenarios compare: a new TCP connection per request (so
per-connection faults reach every request), a capture every 2 s per device (so faults overlap the run), and the
clinicians registered directly on port 8080 before the run (setup is not measured). Devices start pulling at the
facility's current cursor, like phones already in use, so a run measures its own traffic and not the download of
the facility's history from earlier runs.

## Duplicate ablation (`--ablation`, §13)

```bash
python tests/evaluation/run_experiment.py replay --modes event-driven             # protections on
python tests/evaluation/run_experiment.py replay --modes event-driven --ablation  # protections off
```

Dropping the real unique constraints would corrupt the clinical record, so `--ablation` restarts the gateway,
persister and orchestrator with `Ablation__Enabled=true`: the gateway stops answering DUPLICATE, the orchestrator
skips its inbox, and both record **shadow rows** without unique constraints (`ablation.wound_assessment`,
`ablation.recommendation`). The real tables keep their protections, so the system stays correct; the shadow tables
show what a store without the mechanisms would hold (`ablation_*_extra_rows` in `summary.csv`). The runner switches
the mode off again afterwards, also when a run fails.

## Scaling experiment (`scaling.py`, §8.1)

The same load against 1, 2, 3 and 6 consumers (six partitions are the ceiling):

```bash
python tests/evaluation/scaling.py orchestrator   # Recommendation Service takes 1 s; one message at a time per replica
python tests/evaluation/scaling.py persister      # a burst of 1,000 captures
```

For the orchestrator each replica runs with `Orchestrator__MaxConcurrency=1`, so the replica count is the only
parallelism; a last run uses one replica with per-partition concurrency (the default, 6) for comparison. Each run
records the group's peak lag, the time its backlog takes to drain, events per second, latency, extra rows and
audit completeness into `results/scaling.csv`. Services are set back to one replica afterwards.

## Metrics (`metrics.py`)

| Metric | Definition |
|--------|------------|
| Sync latency | device enqueue → `PERSISTED` (the §13 definition); also → accepted, → advice delivered to the same device |
| Duplicates | extra stored rows per event (0 by construction on the event-driven path) and `DEDUPLICATED` rows: repeats the pipeline absorbed |
| Device resends | events the device had to send more than once |
| Auditability completeness | events with every expected provenance stage / all events (§13 SQL) |
| Consumer lag | sampled every 2 s for the `persister` and `orchestrator` groups: rises, then drains |
| Auth health | login failures and lockouts in the run window |

Device and server timestamps share this host's clock: run everything on one machine (§13).

## Output

`tests/evaluation/results/<timestamp>-<scenario>/`, per mode:

- `<run>-eventdriven-events.csv` / `<run>-baseline-events.csv`: the simulator's per-event rows
- `<run>-<mode>-metrics-events.csv`: per event, joined with the server side (stage latencies, stored rows, audit)
- `<run>-<mode>-metrics.json`: all metrics for the run
- `<run>-<mode>-lag.csv`: consumer lag over time

and one row per run in `tests/evaluation/results/summary.csv`, which collects every run for the report's tables.
