// Recommendation Service stub (architecture §13.1, build step 7).
// Stands in for Member 3's service so the orchestrator and the REST baseline can be benchmarked
// against the same, controllable dependency. Behaviour is set with environment variables:
//   STUB_DELAY_MS   extra latency per call (default 0)
//   STUB_FAIL_MODE  none | 503 | 422 | 409 (default none)
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var delayMs = int.TryParse(Environment.GetEnvironmentVariable("STUB_DELAY_MS"), out var d) ? d : 0;
var failMode = Environment.GetEnvironmentVariable("STUB_FAIL_MODE") ?? "none";

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/v1/recommendations", async (StubRequest request) =>
{
    if (delayMs > 0) await Task.Delay(delayMs);

    return failMode switch
    {
        "503" => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
        "422" => Results.UnprocessableEntity(new { error = "stub validation failure" }),
        "409" => Results.Conflict(new { error = "stub idempotency conflict" }),
        _ => Results.Ok(new
        {
            contractVersion = "1.0",
            caseId = request.CaseId,
            revision = request.Revision,
            mode = "extractive",
            retrievalMode = "hybrid",
            corpusVersion = "stub-0",
            sections = Array.Empty<object>(),
            citations = Array.Empty<object>(),
            withheld = Array.Empty<object>(),
        }),
    };
});

app.Run();

internal sealed record StubRequest(Guid CaseId, int Revision);
