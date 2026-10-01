using Microsoft.Agents.AI.Workflows;
using Orchestrator.Clients;
using Orchestrator.Graph;

namespace Orchestrator.Executors;

/// <summary>
/// Calls POST /v1/recommendations (architecture §10.1, §10.3) through <see cref="IRecommendationClient"/>,
/// which carries the 60 s timeout, bounded retries and circuit breaker.
/// - 200 (generated or extractive) → Ok
/// - 422 / 409 → ContractError: straight to the DLQ, never retried
/// - anything else (timeout, 503, open circuit, unreachable) → Deferred: ADVICE_DEFERRED, then the retry topics
/// Idempotency on the service side is keyed on (caseId, revision), so a repeated call returns the stored answer.
/// </summary>
[YieldsOutput(typeof(OrchestrationOutcome))]
public sealed class CallRagExecutor(IRecommendationClient client) : Executor<ContextBuilt, RagCallResult>("CallRag")
{
    public override async ValueTask<RagCallResult> HandleAsync(ContextBuilt message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var request = message.Request!;
        var call = await client.RecommendAsync(request, cancellationToken);

        var status = call.StatusCode switch
        {
            200 => RagCallStatus.Ok,
            409 or 422 => RagCallStatus.ContractError,
            _ => RagCallStatus.Deferred,
        };
        var error = status == RagCallStatus.Ok ? null : call.Error ?? $"HTTP {call.StatusCode}";

        if (status != RagCallStatus.Ok)
            await context.YieldOutputAsync(new OrchestrationOutcome(message.Job.Event,
                status == RagCallStatus.ContractError ? OutcomeKind.DeadLetter : OutcomeKind.Deferred,
                $"RECOMMENDATION_SERVICE: {error}"), cancellationToken);

        return new RagCallResult(message.Job, request, status, call.Body, error);
    }
}
