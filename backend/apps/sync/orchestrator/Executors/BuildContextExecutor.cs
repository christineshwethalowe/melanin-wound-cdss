using Microsoft.Agents.AI.Workflows;
using Npgsql;
using Orchestrator.Graph;
using Sync.Common.Contracts;

namespace Orchestrator.Executors;

/// <summary>
/// Builds the Recommendation Service request (architecture §10.1, §10.2):
/// - loads the stored assessment and earlier captures of the same wound (healing history, oldest first);
/// - removes patientRef, deviceId and facilityId, and drops fitzpatrickClass (data minimisation);
/// - caseId = assessment id.
/// This is why PostgreSQL comes before orchestration: the history it needs was just stored by the persister.
/// On a database error the message goes to a retry topic.
/// </summary>
public sealed class BuildContextExecutor(NpgsqlDataSource db) : Executor<SupersedeChecked, RecommendationRequest>("BuildContext")
{
    public override ValueTask<RecommendationRequest> HandleAsync(SupersedeChecked message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // TODO(phase 6): SELECT analytics, clinical_assessment, captured_at, received_at FROM clinical.wound_assessment
        // for this (assessment_id, revision), plus earlier rows of the same wound_id for HealingHistory.
        // Keep the PersistedEvent in workflow state (context.QueueStateUpdateAsync) for the later steps.
        _ = db;
        throw new NotImplementedException("Plan phase 6: BuildContext");
    }
}
