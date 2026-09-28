# Database

PostgreSQL 16 with pgvector. There is one schema per concern: `clinical`, `messaging`, `audit`, `sync` and `rag`.

## Rules for writing migrations (architecture §9.2)

1. File name: `migrations/<schema>/NNNN_description.sql`, numbered per schema.
2. Once a file has been applied to any environment, **never edit it**. Write a new migration instead.
3. Additive first: add the column as nullable, backfill it, then set NOT NULL in a later migration.
4. When you add a migration, bump the expected schema version constant in the services (same PR).
5. `_bootstrap/0000` creates the ledger `sync.schema_migrations` and always runs first.
6. Apply order: `clinical`, `messaging`, `audit`, `sync`, `rag`.

Apply the migrations with:

```bash
dotnet run --project backend/tools/db-migrator
```

`seed/` is for local development only.
