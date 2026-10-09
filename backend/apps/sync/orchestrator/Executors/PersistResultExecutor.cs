using Microsoft.Agents.AI.Workflows;
using Npgsql;
using Orchestrator.Graph;

namespace Orchestrator.Executors;

/// <summary>
/// One transaction, advisory-locked on assessment_id (architecture §9.3, §10.1):
///   clinical.recommendation (unique on assessment_id, revision; ON CONFLICT DO NOTHING)
///   + sync.change_log RECOMMENDATION_READY
///   + audit.provenance RAG_RETURNED and RECOMMENDATION_STORED (with corpus version / model audit reference)
///   + messaging.outbox → recommendations.ready (key assessmentId)
///   + messaging.inbox marker for this consumer.
/// On failure the transaction rolls back and the message is redelivered.
/// </summary>
public sealed class PersistResultExecutor(NpgsqlDataSource db) : Executor<ValidatedRecommendation, OrchestrationOutcome>("PersistResult")
{
    public override ValueTask<OrchestrationOutcome> HandleAsync(ValidatedRecommendation message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // TODO(phase 6): write the rows above, then yield new OrchestrationOutcome(message.Event, OutcomeKind.Stored).
        _ = db;
        throw new NotImplementedException("Plan phase 6: PersistResult");
    }
}
