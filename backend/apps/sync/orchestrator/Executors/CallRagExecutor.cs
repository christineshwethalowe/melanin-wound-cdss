using Microsoft.Agents.AI.Workflows;
using Orchestrator.Clients;
using Orchestrator.Graph;
using Sync.Common.Contracts;

namespace Orchestrator.Executors;

/// <summary>
/// Calls POST /v1/recommendations (architecture §10.1, §10.3) through <see cref="IRecommendationClient"/>,
/// which carries the 60 s timeout, bounded retries and circuit breaker.
/// - 200 (generated or extractive) → Ok
/// - timeout / 503 / open circuit → Deferred: ADVICE_DEFERRED is recorded and the message goes to a retry topic
/// - 422 / 409 → ContractError: straight to the DLQ, never retried
/// Idempotency on the service side is keyed on (caseId, revision), so a repeated call returns the stored answer.
/// </summary>
public sealed class CallRagExecutor(IRecommendationClient client) : Executor<RecommendationRequest, RagCallResult>("CallRag")
{
    public override ValueTask<RagCallResult> HandleAsync(RecommendationRequest message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // TODO(phase 6): var result = await client.RecommendAsync(message, ct); map to RagCallStatus.
        _ = client;
        throw new NotImplementedException("Plan phase 6: CallRag");
    }
}
