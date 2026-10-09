# 0002: Migration order and bootstrap ledger

- Status: accepted
- Date: 2026-09-28

## Context

Appendix A of the architecture document created `clinical.patient` in migration 0006, after
`clinical.wound` (0002), which needs to reference it. It also placed the migration ledger in
`sync/0002`, but the ledger must exist before any migration, including `sync/0001`, can be recorded.

## Decision

- `clinical` order: schema and device, patient, wound and assessment, recommendation, clinician and credential, session.
- The ledger lives in `db/migrations/_bootstrap/0000_schema_migrations.sql` and is always applied first.

## Consequences

The architecture document's Appendix A should be updated to match.
