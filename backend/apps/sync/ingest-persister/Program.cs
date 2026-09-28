using IngestPersister.Consumers;
using Npgsql;
using Sync.Common.Persistence;

// Ingest persister (architecture §4): consumes wound-events in its own group and writes PostgreSQL.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Username=cdss;Password=cdss;Database=cdss"));
builder.Services.AddHostedService<WoundEventsConsumer>();

var host = builder.Build();

if (!builder.Configuration.GetValue<bool>("SkipSchemaCheck"))
    await SchemaVersionGuard.EnsureAsync(host.Services.GetRequiredService<NpgsqlDataSource>(), ExpectedSchemaVersions.All);

host.Run();
