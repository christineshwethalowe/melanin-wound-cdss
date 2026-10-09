# Grafana dashboards

Provisioned automatically (`infra/grafana/provisioning`): the Prometheus data source and every JSON file in this
folder, in the "Melanin Wound CDSS" folder at http://localhost:3000 (anonymous viewer).

| Dashboard | What it shows |
|-----------|---------------|
| `sync-pipeline.json` | §11 guarantees over the selected range (accepted, resends answered DUPLICATE, redeliveries absorbed, DLQ), Kafka consumer lag per group, throughput per stage, latency (push, persist, workflow, Recommendation Service call), HTTP by service |

`sync-pipeline.json` is generated: edit `infra/grafana/build_dashboard.py` and run it, rather than editing the JSON.
