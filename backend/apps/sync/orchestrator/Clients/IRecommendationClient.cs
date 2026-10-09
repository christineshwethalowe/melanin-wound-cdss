using System.Net.Http.Json;
using System.Text.Json;
using Sync.Common.Contracts;

namespace Orchestrator.Clients;

/// <summary>StatusCode 0 means no HTTP answer at all (timeout, unreachable, open circuit); Error then says why.</summary>
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
/// HTTP implementation. The named HttpClient "recommendation-service" (Program.cs) carries the resilience
/// pipeline: 60 s per attempt, bounded retries with backoff, and a circuit breaker. Never throws for an HTTP
/// status or a transport failure; CallRag maps the result.
/// </summary>
public sealed class HttpRecommendationClient(IHttpClientFactory factory, ILogger<HttpRecommendationClient> logger)
    : IRecommendationClient
{
    public const string ClientName = "recommendation-service";

    public async Task<RecommendationCallResult> RecommendAsync(RecommendationRequest request, CancellationToken ct)
    {
        try
        {
            using var response = await factory.CreateClient(ClientName)
                .PostAsJsonAsync("v1/recommendations", request, JsonSerializerOptions.Web, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonElement? body = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { body = JsonDocument.Parse(text).RootElement.Clone(); }
                catch (JsonException) { /* not JSON: ValidateResponse reports it */ }
            }
            return new RecommendationCallResult((int)response.StatusCode, body,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Timeout (Polly TimeoutRejectedException), open circuit (BrokenCircuitException) or unreachable.
            logger.LogWarning("Recommendation Service call for case {CaseId} failed: {Error}", request.CaseId, ex.Message);
            return new RecommendationCallResult(0, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
