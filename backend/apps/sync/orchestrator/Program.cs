using Orchestrator.Workflow;

// Orchestrator (architecture §4, §10): consumes wound-events.persisted (group "orchestrator") and runs
// a checkpointed Agent Framework workflow that calls the Recommendation Service.
// It coordinates, retries and records. It never decides clinical content.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient("recommendation-service", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["RecommendationService:BaseUrl"] ?? "http://localhost:5080");
    client.Timeout = TimeSpan.FromSeconds(60); // provisional, above the service's 40 s internal budget (§10.3)
});
builder.Services.AddHostedService<OrchestratorWorker>();

builder.Build().Run();
