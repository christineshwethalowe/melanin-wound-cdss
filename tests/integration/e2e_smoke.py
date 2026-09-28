"""
End-to-end smoke test for plan phases 1-5 (docs/member4-backend-plan.md).

Needs: docker compose stack up, migrations applied, a clinician created, and the gateway (port 8080),
ingest persister and outbox relay running. Standard library only.

    python tests/integration/e2e_smoke.py

Test clinician (local dev only), created with:
    dotnet run --project backend/apps/sync/sync-gateway -- create-clinician n.silva Demo-Pass-2026! nurse fac-001 N. Silva
"""
import json
import os
import secrets
import subprocess
import sys
import time
import urllib.error
import urllib.request

GATEWAY = os.environ.get("GATEWAY", "http://localhost:8080")
USERNAME = os.environ.get("E2E_USER", "n.silva")
PASSWORD = os.environ.get("E2E_PASSWORD", "Demo-Pass-2026!")
DEVICE = "dev-a41c"
FACILITY = "fac-001"

passed = failed = 0


def check(name, condition, detail=""):
    global passed, failed
    if condition:
        passed += 1
        print(f"  PASS  {name}")
    else:
        failed += 1
        print(f"  FAIL  {name}  {detail}")


def uuid7():
    ms = int(time.time() * 1000)
    rand = int.from_bytes(secrets.token_bytes(10), "big")
    value = (ms << 80) | (0x7 << 76) | ((rand >> 68) & 0xFFF) << 64 | (0b10 << 62) | (rand & ((1 << 62) - 1))
    h = f"{value:032x}"
    return f"{h[:8]}-{h[8:12]}-{h[12:16]}-{h[16:20]}-{h[20:]}"


def http(method, path, body=None, token=None):
    req = urllib.request.Request(GATEWAY + path, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    data = json.dumps(body).encode() if body is not None else None
    try:
        with urllib.request.urlopen(req, data, timeout=30) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read()
        return e.code, (json.loads(raw) if raw else None)


def sql(query):
    out = subprocess.run(["docker", "exec", "postgres", "psql", "-U", "cdss", "-d", "cdss", "-tAc", query],
                         capture_output=True, text=True, check=True)
    return out.stdout.strip()


def wound_event(assessment_id, wound_id, revision=1, **overrides):
    evt = {
        "schemaVersion": "1.0",
        "eventId": uuid7(),
        "assessmentId": assessment_id,
        "revision": revision,
        "woundId": wound_id,
        "patientRef": "p-" + secrets.token_hex(4),
        "deviceId": DEVICE,
        "facilityId": FACILITY,
        "capturedAt": "2026-10-03T09:41:12+05:30",
        "analytics": {
            "areaMm2": 412.6,
            "colourRegions": [{"cluster": 1, "percent": 61.2}, {"cluster": 2, "percent": 27.9}],
            "fitzpatrickClass": "V",
            "pipeline": {"calibration": "2.1.0", "segmentation": "yolo11n-seg-0.4"},
        },
        "clinicalAssessment": {"pedalPulses": "not_recorded", "protectiveSensation": "absent"},
    }
    evt.update(overrides)
    return evt


def wait_for(query, expected, seconds=20):
    deadline = time.time() + seconds
    while time.time() < deadline:
        if sql(query) == expected:
            return True
        time.sleep(0.5)
    return False


print("Phase 1: auth")
status, body = http("POST", "/v1/auth/login", {"username": USERNAME, "password": PASSWORD, "deviceId": DEVICE})
check("login returns tokens", status == 200 and body and "accessToken" in body, f"{status} {body}")
if status != 200:
    sys.exit(f"Cannot continue without a login ({status} {body}). Is the clinician created?")
token, refresh = body["accessToken"], body["refreshToken"]

status, body = http("POST", "/v1/auth/refresh", {"refreshToken": refresh})
check("refresh rotates tokens", status == 200 and body["refreshToken"] != refresh, f"{status}")
old_refresh, refresh, token = refresh, body["refreshToken"], body["accessToken"]
status, _ = http("POST", "/v1/auth/refresh", {"refreshToken": old_refresh})
check("old refresh token is rejected after rotation", status == 401, f"{status}")

status, _ = http("POST", "/v1/sync/push", {"deviceId": DEVICE, "events": [wound_event(uuid7(), uuid7())]})
check("push without token is 401", status == 401, f"{status}")

print("Phase 2: push")
assessment, wound = uuid7(), uuid7()
good = wound_event(assessment, wound)
invalid = wound_event(uuid7(), wound, clinicalAssessment={"protectiveSensation": "absent"})
other_facility = wound_event(uuid7(), wound, facilityId="fac-999")
status, body = http("POST", "/v1/sync/push",
                    {"deviceId": DEVICE, "batchId": "b1", "events": [good, invalid, good, other_facility]}, token)
statuses = [r["status"] for r in body["results"]] if body else []
check("batch returns a result per event", status == 200 and len(statuses) == 4, f"{status} {body}")
check("valid event ACCEPTED", statuses[:1] == ["ACCEPTED"], statuses)
check("missing tri-state key REJECTED", statuses[1:2] == ["REJECTED"] and body["results"][1]["code"] == "SCHEMA_INVALID", body)
check("repeat inside batch is DUPLICATE", statuses[2:3] == ["DUPLICATE"], statuses)
check("foreign facility REJECTED", statuses[3:4] == ["REJECTED"] and body["results"][3]["code"] == "FACILITY_MISMATCH", body)

status, body = http("POST", "/v1/sync/push", {"deviceId": DEVICE, "events": [good]}, token)
check("re-sent event is DUPLICATE (reconnect case)", status == 200 and body["results"][0]["status"] == "DUPLICATE", body)

status, _ = http("POST", "/v1/sync/push", {"deviceId": "dev-other", "events": [good]}, token)
check("other device id in body is 403", status == 403, f"{status}")

print("Phase 3: persister")
eid = good["eventId"]
check("assessment persisted once",
      wait_for(f"select count(*) from clinical.wound_assessment where event_id = '{eid}'", "1"))
stages = sql(f"select string_agg(stage, ',' order by provenance_id) from audit.provenance where event_id = '{eid}'")
check("provenance GATEWAY_ACCEPTED,PERSISTED", stages.startswith("GATEWAY_ACCEPTED,PERSISTED"), stages)

print("Phase 4: outbox relay")
check("outbox row published", wait_for(
    f"select count(*) from messaging.outbox where payload->>'eventId' = '{eid}' and published_at is not null", "1"))

print("Phase 5: pull")
status, body = http("GET", "/v1/sync/changes?cursor=0&limit=500", token=token)
found = [c for c in (body or {}).get("changes", []) if c["assessmentId"] == assessment and c["type"] == "PERSISTED"]
check("PERSISTED change visible on pull", status == 200 and len(found) == 1, f"{status}")
check("nextCursor advances", status == 200 and body["nextCursor"] >= found[0]["seq"] if found else False)

print("Phase 1: lockout (uses a second throwaway clinician if E2E_LOCKOUT_USER is set)")
lock_user = os.environ.get("E2E_LOCKOUT_USER")
if lock_user:
    for _ in range(5):
        http("POST", "/v1/auth/login", {"username": lock_user, "password": "wrong", "deviceId": DEVICE})
    status, body = http("POST", "/v1/auth/login", {"username": lock_user, "password": "wrong", "deviceId": DEVICE})
    check("5 failures lock the credential", status == 401 and body["code"] == "CREDENTIAL_LOCKED", body)
else:
    print("  skip  set E2E_LOCKOUT_USER to a throwaway clinician to test lockout")

status, _ = http("POST", "/v1/auth/logout", {"refreshToken": refresh})
status2, _ = http("POST", "/v1/auth/refresh", {"refreshToken": refresh})
check("logout revokes the session", status == 204 and status2 == 401, f"{status} {status2}")

print(f"\n{passed} passed, {failed} failed")
sys.exit(1 if failed else 0)
