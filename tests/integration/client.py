"""Shared helpers for the integration scripts in this folder. Standard library only."""
import base64
import hashlib
import hmac
import json
import os
import secrets
import struct
import subprocess
import time
import urllib.error
import urllib.request

GATEWAY = os.environ.get("GATEWAY", "http://localhost:8080")
REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


class Checks:
    def __init__(self):
        self.passed = 0
        self.failed = 0

    def check(self, name, condition, detail=""):
        if condition:
            self.passed += 1
            print(f"  PASS  {name}")
        else:
            self.failed += 1
            print(f"  FAIL  {name}  {detail}")

    def finish(self):
        print(f"\n{self.passed} passed, {self.failed} failed")
        return 1 if self.failed else 0


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


def wait_for(query, expected, seconds=20):
    deadline = time.time() + seconds
    while time.time() < deadline:
        if sql(query) == expected:
            return True
        time.sleep(0.5)
    return False


def create_clinician(username, password, role, facility, full_name="Test User"):
    """Uses the gateway's create-clinician command (the bootstrap path for a facility's first admin)."""
    out = subprocess.run(
        ["dotnet", "run", "--no-build", "--project", "backend/apps/sync/sync-gateway", "--",
         "create-clinician", username, password, role, facility, full_name],
        cwd=REPO_ROOT, capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(f"create-clinician failed: {out.stderr or out.stdout}")


def login(username, password, device, totp=None):
    body = {"username": username, "password": password, "deviceId": device}
    if totp:
        body["totp"] = totp
    return http("POST", "/v1/auth/login", body)


def totp_code(base32_secret, step=None):
    """RFC 6238 code, matching the gateway's Totp class (SHA-1, 30 s, 6 digits)."""
    key = base64.b32decode(base32_secret + "=" * (-len(base32_secret) % 8))
    step = int(time.time()) // 30 if step is None else step
    digest = hmac.new(key, struct.pack(">Q", step), hashlib.sha1).digest()
    offset = digest[-1] & 0x0F
    value = struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF
    return f"{value % 1_000_000:06d}"


def wound_event(assessment_id, wound_id, device, facility, revision=1, **overrides):
    evt = {
        "schemaVersion": "1.0",
        "eventId": uuid7(),
        "assessmentId": assessment_id,
        "revision": revision,
        "woundId": wound_id,
        "patientRef": "p-" + secrets.token_hex(4),
        "deviceId": device,
        "facilityId": facility,
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
