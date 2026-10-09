# Backend (.NET 10)

| Project | Owner | Kind |
|---------|-------|------|
| `apps/sync/sync-gateway` | Member 4 | ASP.NET Core API |
| `apps/sync/ingest-persister` | Member 4 | Worker service |
| `apps/sync/outbox-relay` | Member 4 | Worker service |
| `apps/sync/orchestrator` | Member 4 | Worker service with Microsoft Agent Framework |
| `apps/recommendation-service` | Member 3 | ASP.NET Core API (placeholder) |
| `apps/ingestion-pipeline` | Member 3 | Console app (placeholder) |
| `libs/Sync.Common` | Member 4 | Shared contracts, Kafka settings, advisory lock, schema-version guard |
| `tools/db-migrator` | Member 4 | Applies `db/migrations` |
| `tests/rag-stub` | Member 4 | Controllable stand-in for the Recommendation Service |
| `tests/Sync.Common.Tests` | Member 4 | Unit and contract tests |

## Package versions

Package versions are managed centrally in `Directory.Packages.props` files. MSBuild uses the one
**nearest** each project:

- `backend/Directory.Packages.props` covers Member 4's services, `libs`, `tools` and `tests`.
- `apps/recommendation-service/` and `apps/ingestion-pipeline/` have their own, so Member 3 pins
  versions independently.

Add a package with `dotnet add <project> package <name>`. The version goes into the nearest props file.

## Build and test

```bash
dotnet build MelaninWoundCdss.slnx
dotnet test MelaninWoundCdss.slnx
```

Each service checks the database schema version on startup. Set `SkipSchemaCheck=true` to run one without a database.
