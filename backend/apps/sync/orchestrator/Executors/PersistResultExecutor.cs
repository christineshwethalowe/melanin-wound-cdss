using Microsoft.Agents.AI.Workflows;
using Orchestrator.Graph;
using Orchestrator.Persistence;

namespace Orchestrator.Executors;

/// <summary>
/// One transaction, advisory-locked on assessment_id (architecture §9.3, §10.1):
///   clinical.recommendation (unique on assessment_id, revision; ON CONFLICT DO NOTHING)
///   + sync.change_log RECOMMENDATION_READY
///   + audit.provenance RAG_RETURNED and RECOMMENDATION_STORED (with the corpus version as audit reference)
///   + messaging.outbox → recommendations.ready (key assessmentId)
///   + messaging.inbox marker for this consumer.
/// A worker killed before the commit leaves nothing behind and the redelivered message runs again; one killed
/// after the commit is stopped by the inbox marker. Either way there is one recommendation (§11).
/// </summary>
[YieldsOutput(typeof(OrchestrationOutcome))]
public sealed class PersistResultExecutor(IOrchestratorStore store) : Executor<ValidatedRecommendation>("PersistResult")
{
    public override async ValueTask HandleAsync(ValidatedRecommendation message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var stored = await store.StoreRecommendationAsync(message.Job, message.Response!, message.RawBody, cancellationToken);
        await context.YieldOutputAsync(new OrchestrationOutcome(message.Job.Event,
            stored ? OutcomeKind.Stored : OutcomeKind.AlreadyProcessed), cancellationToken);
    }
}
