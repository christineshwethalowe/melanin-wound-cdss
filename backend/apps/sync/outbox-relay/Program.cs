using OutboxRelay;

// Outbox relay (architecture §4): publishes messaging.outbox rows to Kafka.
// Can run inside the persister's process for the prototype; kept separate so it can be split out.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<RelayWorker>();

builder.Build().Run();
