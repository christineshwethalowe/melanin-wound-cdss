using Npgsql;
using Sync.Common.Persistence;
using SyncGateway.Endpoints;

// Sync Gateway (architecture §4): push/pull, clinician auth, figure proxy. Stateless; scale by replicas.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Username=cdss;Password=cdss;Database=cdss"));

var app = builder.Build();

if (!app.Configuration.GetValue<bool>("SkipSchemaCheck"))
    await SchemaVersionGuard.EnsureAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), ExpectedSchemaVersions.All);

// The device probes this before pushing: reachability, not just connectivity (§6.2).
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var v1 = app.MapGroup("/v1");
v1.MapAuthEndpoints();
v1.MapPushEndpoint();
v1.MapPullEndpoint();
v1.MapFiguresProxyEndpoint();
v1.MapBaselineEndpoint();

app.Run();
