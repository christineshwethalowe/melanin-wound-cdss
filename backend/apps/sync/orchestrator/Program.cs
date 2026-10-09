using Npgsql;
using Orchestrator.Clients;
using Orchestrator.Consumers;
using Orchestrator.Graph;
using Sync.Common.Persistence;

// Orchestrator (architecture §4, §10): consumes wound-events.persisted (group "orchestrator") and runs
// a checkpointed Agent Framework workflow that calls the Recommendation Service.
// It coordinates, retries and records. It never decides clinical content.
var builder = Host.CreateApplicationBuilder(args);

var dataSource = NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Username=cdss;Password=cdss;Database=cdss");

builder.Services.AddSingleton(dataSource);
builder.Services.AddHttpClient("recommendation-service", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["RecommendationService:BaseUrl"] ?? "http://localhost:5080");
    client.Timeout = TimeSpan.FromSeconds(60); // provisional, above the service's 40 s internal budget (§10.3)
});
builder.Services.AddSingleton<IRecommendationClient, HttpRecommendationClient>();
builder.Services.AddSingleton(sp => OrchestratorGraph.Build(dataSource, sp.GetRequiredService<IRecommendationClient>()));
builder.Services.AddHostedService<PersistedEventsConsumer>();
builder.Services.AddHostedService<RetryTopicsConsumer>();

var host = builder.Build();

if (!builder.Configuration.GetValue<bool>("SkipSchemaCheck"))
    await SchemaVersionGuard.EnsureAsync(dataSource, ExpectedSchemaVersions.All);

await host.RunAsync();
