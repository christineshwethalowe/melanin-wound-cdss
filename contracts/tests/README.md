# Contract tests

- Every file named `examples/*.valid.json` must validate against its schema.
- Every file named `examples/*.invalid.json` must fail validation.
- Each producer (Flutter app, gateway, orchestrator, recommendation service) adds a test here that
  serialises its own output and checks it against the schema.

Run locally:

```bash
npx ajv-cli@5 validate --spec=draft2020 -s contracts/wound-event.schema.json -d "contracts/examples/wound-event.*.valid.json"
npx ajv-cli@5 test --spec=draft2020 -s contracts/wound-event.schema.json -d "contracts/examples/wound-event.*.invalid.json" --invalid
```
