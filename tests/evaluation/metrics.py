"""
The evaluation metrics from architecture §13, for one simulator run (device ids sim-<run>-NNN).

Device-side timestamps come from the simulator's CSV; server-side ones from audit.provenance (and the baseline
tables). Both clocks are this host's, which is why the simulator runs on the same machine as the stack.

    sync latency          device enqueue → PERSISTED (§13 definition); also → accepted, → advice stored, → delivered
    duplicates            rows per event_id in the store (0 by construction) and DEDUPLICATED provenance rows
                          (repeats the pipeline absorbed); baseline: extra rows per event
    auditability          events whose provenance has every expected stage / all events
    auth health           login failures and lockouts during the run window
"""
import csv
import statistics
import subprocess


def psql_csv(query):
    out = subprocess.run(["docker", "exec", "postgres", "psql", "-U", "cdss", "-d", "cdss", "--csv", "-c", query],
                         capture_output=True, text=True, check=True)
    return list(csv.DictReader(out.stdout.splitlines()))


def percentiles(values):
    values = sorted(v for v in values if v is not None)
    if not values:
        return {"count": 0}
    at = lambda q: values[min(len(values) - 1, max(0, int(round(q * len(values) + 0.5)) - 1))]
    return {"count": len(values), "p50": round(at(0.50)), "p95": round(at(0.95)), "p99": round(at(0.99)),
            "max": round(values[-1]), "mean": round(statistics.fmean(values))}


def _ms(later, earlier):
    return None if not later or not earlier else (later - earlier).total_seconds() * 1000


def collect(run_id, mode, sim_csv_path, started_at, finished_at):
    """Returns (metrics dict, per-event rows) for one run."""
    from datetime import datetime
    parse = lambda s: datetime.fromisoformat(s) if s else None

    with open(sim_csv_path, newline="", encoding="utf-8") as f:
        device = {row["event_id"]: row for row in csv.DictReader(f)}
    pattern = f"sim-{run_id}-%"

    events = []
    if mode == "baseline":
        stored = psql_csv(f"""
            select a.event_id, count(*) as rows, min(a.received_at) as first_received, min(r.created_at) as advice_at
            from baseline.assessment a left join baseline.recommendation r on r.assessment_row_id = a.row_id
            where a.device_id like '{pattern}' group by a.event_id""")
        by_event = {s["event_id"]: s for s in stored}
        for event_id, d in device.items():
            s = by_event.get(event_id, {})
            enq = parse(d["enqueued_at"])
            events.append({"event_id": event_id, "status": d["status"], "attempts": int(d["attempts"]),
                           "stored_rows": int(s.get("rows", 0)),
                           "accept_ms": d["accept_ms"] or None,
                           "persisted_ms": _ms(parse(s.get("first_received")), enq),
                           "advice_stored_ms": _ms(parse(s.get("advice_at")), enq),
                           "delivered_ms": d["complete_ms"] or None})
        rows_total = sum(e["stored_rows"] for e in events)
        duplicates = {"stored_rows": rows_total, "distinct_events": sum(1 for e in events if e["stored_rows"]),
                      "extra_rows": rows_total - sum(1 for e in events if e["stored_rows"]),
                      "deduplicated_absorbed": 0}
        audit = {"applicable": False, "note": "the REST baseline writes no provenance (§13.1)"}
    else:
        stages = psql_csv(f"""
            select p.event_id, p.stage, min(p.recorded_at) as at, count(*) as n
            from audit.provenance p
            where p.device_id like '{pattern}'
               or p.event_id in (select event_id from clinical.wound_assessment where device_id like '{pattern}')
            group by p.event_id, p.stage""")
        per_event = {}
        for s in stages:
            per_event.setdefault(s["event_id"], {})[s["stage"]] = (parse(s["at"]), int(s["n"]))
        store = psql_csv(f"""
            select event_id, status, (select count(*) from clinical.recommendation r
                                      where r.assessment_id = w.assessment_id and r.revision = w.revision) as recs
            from clinical.wound_assessment w where device_id like '{pattern}'""")
        stored_rows = {}
        for s in store:
            stored_rows[s["event_id"]] = stored_rows.get(s["event_id"], 0) + 1

        complete = 0
        for event_id, d in device.items():
            st = per_event.get(event_id, {})
            enq = parse(d["enqueued_at"])
            at = lambda stage: st.get(stage, (None, 0))[0]
            superseded = d["status"] == "Superseded"
            expected = {"GATEWAY_ACCEPTED", "PERSISTED", "ORCHESTRATION_STARTED"} if superseded else \
                {"GATEWAY_ACCEPTED", "PERSISTED", "ORCHESTRATION_STARTED", "RAG_RETURNED", "RECOMMENDATION_STORED", "DELIVERED"}
            has_all = expected <= set(st)
            complete += has_all
            events.append({"event_id": event_id, "status": d["status"], "attempts": int(d["attempts"]),
                           "stored_rows": stored_rows.get(event_id, 0),
                           "accept_ms": d["accept_ms"] or None,
                           "persisted_ms": _ms(at("PERSISTED"), enq),
                           "advice_stored_ms": _ms(at("RECOMMENDATION_STORED"), enq),
                           # Pull is facility-scoped, so another phone may pull the advice first; the originating
                           # phone's own completion time is the delivery latency that matters (DELIVERED stays in
                           # the audit check).
                           "delivered_ms": d["complete_ms"] or None,
                           "deduplicated": st.get("DEDUPLICATED", (None, 0))[1],
                           "audit_complete": has_all})
        rows_total = sum(e["stored_rows"] for e in events)
        duplicates = {"stored_rows": rows_total, "distinct_events": sum(1 for e in events if e["stored_rows"]),
                      "extra_rows": rows_total - sum(1 for e in events if e["stored_rows"]),
                      "deduplicated_absorbed": sum(e["deduplicated"] for e in events)}
        audit = {"applicable": True, "complete": complete, "events": len(device),
                 "completeness": round(complete / len(device), 4) if device else None}

    auth = psql_csv(f"""
        select count(*) filter (where action = 'LOGIN' and not success) as login_failures,
               count(*) filter (where action = 'LOGIN' and success) as logins,
               count(*) filter (where action = 'LOCKOUT') as lockouts
        from audit.auth_audit
        where recorded_at between '{started_at.isoformat()}' and '{finished_at.isoformat()}'""")[0]

    to_float = lambda v: float(v) if v not in (None, "") else None
    metrics = {
        "run_id": run_id, "mode": mode, "events": len(device),
        "by_status": {s: sum(1 for e in events if e["status"] == s) for s in sorted({e["status"] for e in events})},
        "device_resends": sum(max(0, e["attempts"] - 1) for e in events),
        "latency_ms": {
            "enqueue_to_accepted": percentiles(to_float(e["accept_ms"]) for e in events),
            "enqueue_to_persisted": percentiles(e["persisted_ms"] for e in events),
            "enqueue_to_advice_stored": percentiles(e["advice_stored_ms"] for e in events),
            "enqueue_to_delivered": percentiles(to_float(e["delivered_ms"]) for e in events),
        },
        "duplicates": duplicates,
        "auditability": audit,
        "auth": {k: int(v) for k, v in auth.items()},
    }
    return metrics, events


def write_events_csv(path, events):
    if not events:
        return
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(events[0].keys()))
        w.writeheader()
        w.writerows(events)
