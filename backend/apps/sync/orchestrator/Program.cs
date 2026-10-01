using Confluent.Kafka;
using Npgsql;
using Orchestrator.Clients;
using Orchestrator.Consumers;
using Orchestrator.Graph;
using Orchestrator.Persistence;
using Sync.Common.Kafka;
using Sync.Common.Persistence;
using Sync.Common.Recommendations;

// Orchestrator (architecture §4, §10): consumes wound-events.persisted (group "orchestrator") and runs an
// Agent Framework workflow that calls the Recommendation Service and stores the result.
// It coordinates, retries and records. It never decides clinical content.
var builder = Host.CreateApplicationBuilder(args);

var dataSource = NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Username=cdss;Password=cdss;Database=cdss");
var rag = builder.Configuration.GetSection("RecommendationService");

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<IOrchestratorStore, PostgresOrchestratorStore>();
builder.Services.AddSingleton(RecommendationResponseValidator.FromOutputDirectory());
builder.Services.AddSingleton<OrchestratorGraph>();
builder.Services.AddSingleton<WorkflowRunner>();
builder.Services.AddSingleton<OutcomeRouter>();
builder.Services.AddSingleton<IProducer<string, byte[]>>(_ => new ProducerBuilder<string, byte[]>(
    KafkaDefaults.Producer(builder.Configuration["Kafka:BootstrapServers"] ?? KafkaDefaults.DefaultBootstrapServers)).Build());

// §10.3: the service makes up to two 20 s generation attempts internally, so one call may take ~40 s; the
// orchestrator allows 60 s per attempt, retries transient failures (5xx, 408, 429, timeouts) a bounded number
// of times with backoff, and opens a circuit when the service keeps failing. Whatever is still failing after
// that becomes ADVICE_DEFERRED and moves on to the retry topics.
var attemptTimeout = TimeSpan.FromSeconds(rag.GetValue("AttemptTimeoutSeconds", 60));
builder.Services.AddHttpClient(HttpRecommendationClient.ClientName, client =>
    {
        client.BaseAddress = new Uri(rag["BaseUrl"] ?? "http://localhost:5080");
        client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns all timeouts
    })
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = attemptTimeout;
        o.Retry.MaxRetryAttempts = rag.GetValue("MaxRetryAttempts", 2);
        o.Retry.Delay = TimeSpan.FromSeconds(rag.GetValue("RetryDelaySeconds", 2.0));
        o.TotalRequestTimeout.Timeout = attemptTimeout * 3 + TimeSpan.FromSeconds(30);
        o.CircuitBreaker.SamplingDuration = attemptTimeout * 2;
    });
builder.Services.AddSingleton<IRecommendationClient, HttpRecommendationClient>();

builder.Services.AddHostedService<PersistedEventsConsumer>();
builder.Services.AddHostedService<RetryTopicsConsumer>();

var host = builder.Build();

if (!builder.Configuration.GetValue<bool>("SkipSchemaCheck"))
    await SchemaVersionGuard.EnsureAsync(dataSource, ExpectedSchemaVersions.All);

await host.RunAsync();
