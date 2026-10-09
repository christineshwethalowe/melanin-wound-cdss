# System tests and evaluation harness (architecture §13)

| Folder | Purpose |
|--------|---------|
| `integration/` | One wound assessment, device to device |
| `device-simulator/` | 1–100 simulated devices with a real queue, leases, login, push and pull |
| `fault-injection/` | Kill a consumer, send an oversized event, replay a batch twice, lock out a credential |

Network faults go through Toxiproxy (`infra/toxiproxy`, port 18080 → gateway on 8080).
Metrics: consumer lag, duplicate rate (plus DEDUPLICATED count), sync latency, frame time under 200 ms, auditability completeness.
