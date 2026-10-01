using Microsoft.Agents.AI.Workflows;
using Orchestrator.Graph;
using Sync.Common.Recommendations;

namespace Orchestrator.Executors;

/// <summary>
/// Checks the response against contracts/rag-response.schema.json, that it answers the case and revision that
/// were asked, and that every section's citation tags resolve in the citations list (architecture §10.1), using
/// the <see cref="RecommendationResponseValidator"/> the REST baseline shares. It does not re-judge clinical
/// content: that stays behind the Recommendation Service boundary.
/// Invalid → Deferred (retry topics, then DLQ).
/// </summary>
[YieldsOutput(typeof(OrchestrationOutcome))]
public sealed class ValidateResponseExecutor(RecommendationResponseValidator validator)
    : Executor<RagCallResult, ValidatedRecommendation>("ValidateResponse")
{
    public override async ValueTask<ValidatedRecommendation> HandleAsync(RagCallResult message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var body = message.Body ?? default;
        var (response, error) = validator.Validate(message.Request, body);
        if (error is not null)
            await context.YieldOutputAsync(new OrchestrationOutcome(message.Job.Event, OutcomeKind.Deferred,
                $"INVALID_RESPONSE: {error}"), cancellationToken);
        return new ValidatedRecommendation(message.Job, response, body, error);
    }
}
