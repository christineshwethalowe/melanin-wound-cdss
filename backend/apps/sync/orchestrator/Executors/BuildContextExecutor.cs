using Microsoft.Agents.AI.Workflows;
using Orchestrator.Graph;
using Orchestrator.Persistence;

namespace Orchestrator.Executors;

/// <summary>
/// Builds the Recommendation Service request (architecture §10.1, §10.2) from the stored assessment and the
/// earlier captures of the same wound (healing history). <see cref="RecommendationRequestMapper"/> keeps
/// patientRef, deviceId, facilityId and fitzpatrickClass out of it. This is why PostgreSQL comes before
/// orchestration: the history it needs was just stored by the persister.
/// </summary>
[YieldsOutput(typeof(OrchestrationOutcome))]
public sealed class BuildContextExecutor(IOrchestratorStore store) : Executor<SupersedeChecked, ContextBuilt>("BuildContext")
{
    public override async ValueTask<ContextBuilt> HandleAsync(SupersedeChecked message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var stored = await store.LoadContextAsync(message.Job.Event, cancellationToken);
        if (stored is null)
        {
            // The outbox row is written in the same transaction as the assessment, so this means a bug or a
            // hand-made message. Retrying cannot fix it.
            await context.YieldOutputAsync(new OrchestrationOutcome(message.Job.Event, OutcomeKind.DeadLetter,
                "ASSESSMENT_NOT_FOUND"), cancellationToken);
            return new ContextBuilt(message.Job, null);
        }
        return new ContextBuilt(message.Job, RecommendationRequestMapper.Build(stored));
    }
}
