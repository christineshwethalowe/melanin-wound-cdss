"""
Plan phase 9 (architecture §9.4, §12): every service logs in with its own least-privilege database role.
Connects as each role and tries what that role must not be able to do. The rest of the suites passing on the
Docker stack (where every service already runs under its role) shows the grants are also sufficient.

    python tests/integration/e2e_db_roles.py
"""
import subprocess
import sys

from client import Checks, sql

sys.stdout.reconfigure(encoding="utf-8")

PASSWORDS = {
    "identity_svc": "identity-local-dev", "gateway_svc": "gateway-local-dev", "persister_svc": "persister-local-dev",
    "relay_svc": "relay-local-dev", "orchestrator_svc": "orchestrator-local-dev", "rag_svc": "rag-local-dev",
}
SERVICES = ["identity_svc", "gateway_svc", "persister_svc", "relay_svc", "orchestrator_svc"]

t = Checks()
check = t.check


def as_role(role, statement, password=None):
    """Runs one statement as `role` over TCP (scram-sha-256), like the services do. Returns (ok, output)."""
    out = subprocess.run(
        ["docker", "exec", "-e", f"PGPASSWORD={password or PASSWORDS[role]}", "postgres", "psql", "-h", "postgres",
         "-U", role, "-d", "cdss", "-v", "ON_ERROR_STOP=1", "-tAc", statement],
        capture_output=True, text=True)
    return out.returncode == 0, (out.stdout + out.stderr).strip()


def denied(role, statement):
    ok, output = as_role(role, statement)
    return not ok and "permission denied" in output, output


def allowed(role, statement):
    return as_role(role, statement)


print("Logins")
users = set(sql("select string_agg(distinct usename, ',') from pg_stat_activity "
                "where datname = 'cdss' and backend_type = 'client backend' and application_name <> 'psql'").split(","))
check("every running service is connected under its own role", set(SERVICES) <= users, users)
check("no service is connected as the owner", "cdss" not in users, users)
check("no service role is a superuser or can create roles or databases",
      sql("select count(*) from pg_roles where rolname like '%\\_svc' and (rolsuper or rolcreaterole or rolcreatedb)") == "0")
ok, output = as_role("gateway_svc", "select 1", password="wrong")
check("a wrong password is refused", not ok and "password authentication failed" in output, output[-120:])
check("tables are owned by the migrator, not by any service",
      sql("select count(*) from pg_tables where schemaname in ('clinical','audit','messaging','sync','baseline') "
          "and tableowner <> 'cdss'") == "0")

print("\n§9.4: only the identity service can read clinician_credential")
check("identity_svc can", allowed("identity_svc", "select count(*) from clinical.clinician_credential")[0])
for role in ["gateway_svc", "persister_svc", "relay_svc", "orchestrator_svc", "rag_svc"]:
    result, detail = denied(role, "select count(*) from clinical.clinician_credential")
    check(f"{role} cannot", result, detail)

print("\n§12: audit.provenance and audit.auth_audit are insert-only for everyone")
for role in SERVICES + ["rag_svc"]:
    for statement in ["update audit.provenance set outcome = outcome where false",
                      "delete from audit.provenance where false",
                      "update audit.auth_audit set success = success where false",
                      "delete from audit.auth_audit where false",
                      "truncate audit.provenance"]:
        result, detail = denied(role, statement)
        check(f"{role}: {statement.split(' where')[0]}", result, detail)

print("\n§10.5: the Recommendation Service role sees the rag schema only")
check("rag_svc can use the rag schema", allowed("rag_svc", "select has_schema_privilege('rag', 'USAGE')")[1] == "t")
for statement in ["select 1 from clinical.wound_assessment limit 1", "select 1 from clinical.patient limit 1",
                  "select 1 from messaging.outbox limit 1", "select 1 from audit.provenance limit 1",
                  "select 1 from baseline.assessment limit 1"]:
    result, detail = denied("rag_svc", statement)
    check(f"rag_svc: {statement}", result, detail)

print("\nColumn-level writes: each service changes only what its job needs")
cases = [
    ("orchestrator_svc", "update clinical.wound_assessment set status = status where false", True),
    ("orchestrator_svc", "update clinical.wound_assessment set analytics = analytics where false", False),
    ("relay_svc", "update messaging.outbox set published_at = published_at where false", True),
    ("relay_svc", "update messaging.outbox set payload = payload where false", False),
    ("gateway_svc", "update clinical.patient set display_alias = display_alias where false", True),
    ("gateway_svc", "update clinical.patient set facility_id = facility_id where false", False),
    ("persister_svc", "update clinical.wound_assessment set status = status where false", False),
    ("persister_svc", "delete from clinical.wound_assessment where false", False),
]
for role, statement, expected in cases:
    if expected:
        ok, detail = allowed(role, statement)
        check(f"{role} may: {statement.split(' where')[0]}", ok, detail)
    else:
        result, detail = denied(role, statement)
        check(f"{role} may not: {statement.split(' where')[0]}", result, detail)

print("\nEach service sees only its own tables")
for role, statement in [("relay_svc", "select 1 from clinical.patient limit 1"),
                        ("relay_svc", "select 1 from audit.provenance limit 1"),
                        ("persister_svc", "select 1 from sync.change_log limit 1"),
                        ("persister_svc", "select 1 from clinical.clinician limit 1"),
                        ("gateway_svc", "select 1 from messaging.outbox limit 1"),
                        ("gateway_svc", "select 1 from clinical.clinician limit 1"),
                        ("orchestrator_svc", "select 1 from clinical.clinician limit 1"),
                        ("identity_svc", "select 1 from clinical.wound_assessment limit 1")]:
    result, detail = denied(role, statement)
    check(f"{role}: {statement}", result, detail)

sys.exit(t.finish())
