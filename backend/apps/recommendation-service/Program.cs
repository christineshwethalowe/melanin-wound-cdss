// Recommendation Service: owned by Member 3 (IT23294202). Placeholder so the solution builds.
// Contract: POST /v1/recommendations and GET /v1/figures/{corpusVersion}/{figureId} (architecture §10.2, §10.4).
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
