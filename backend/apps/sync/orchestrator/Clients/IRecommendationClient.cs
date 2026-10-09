using System.Text.Json;
using Sync.Common.Contracts;

namespace Orchestrator.Clients;

public sealed record RecommendationCallResult(int StatusCode, JsonElement? Body, string? Error);

/// <summary>
/// Boundary to Member 3's Recommendation Service (C#/.NET 10), contract v1.0 (§10.2). The orchestrator only
/// talks to it through this interface, so the benchmark can swap in backend/tests/rag-stub exactly as the
/// methodology plans.
/// </summary>
public interface IRecommendationClient
{
    Task<RecommendationCallResult> RecommendAsync(RecommendationRequest request, CancellationToken ct);
}

/// <summary>
/// HTTP implementation. The named HttpClient "recommendation-service" (Program.cs) carries the timeout;
/// retries and the circuit breaker are added with Microsoft.Extensions.Http.Resilience in plan phase 6.
/// </summary>
public sealed class HttpRecommendationClient(IHttpClientFactory factory) : IRecommendationClient
{
    public Task<RecommendationCallResult> RecommendAsync(RecommendationRequest request, CancellationToken ct)
    {
        // TODO(phase 6): POST /v1/recommendations with the request serialized as camelCase JSON; return the status
        // code and body. Do not throw for 4xx/5xx — CallRag maps them.
        _ = factory;
        throw new NotImplementedException("Plan phase 6: HttpRecommendationClient");
    }
}
