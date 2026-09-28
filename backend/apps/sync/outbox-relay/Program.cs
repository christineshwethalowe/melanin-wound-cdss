using Confluent.Kafka;
using Npgsql;
using OutboxRelay;
using Sync.Common.Kafka;
using Sync.Common.Persistence;

// Outbox relay (architecture §4): publishes messaging.outbox rows to Kafka.
// Can run inside the persister's process for the prototype; kept separate so it can be split out.
var builder = Host.CreateApplicationBuilder(args);

var dataSource = NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Username=cdss;Password=cdss;Database=cdss");

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<IProducer<string, byte[]>>(_ => new ProducerBuilder<string, byte[]>(
    KafkaDefaults.Producer(builder.Configuration["Kafka:BootstrapServers"] ?? KafkaDefaults.DefaultBootstrapServers)).Build());
builder.Services.AddHostedService<RelayWorker>();

var host = builder.Build();

if (!builder.Configuration.GetValue<bool>("SkipSchemaCheck"))
    await SchemaVersionGuard.EnsureAsync(dataSource, ExpectedSchemaVersions.All);

await host.RunAsync();
