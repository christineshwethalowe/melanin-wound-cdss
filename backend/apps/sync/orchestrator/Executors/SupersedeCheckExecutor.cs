using Microsoft.Agents.AI.Workflows;
using Orchestrator.Graph;
using Orchestrator.Persistence;

namespace Orchestrator.Executors;

/// <summary>
/// If a higher revision of the same assessment is already stored, marks this one SUPERSEDED and ends the
/// run (architecture §10.1). Needed because retry topics can reorder revisions, and because a device can push
/// revisions 1 and 2 together: only the newest one gets advice. Advisory lock first, then SELECT ... FOR UPDATE
/// on the latest row (§9.3 lock order).
/// </summary>
[YieldsOutput(typeof(OrchestrationOutcome))]
public sealed class SupersedeCheckExecutor(IOrchestratorStore store) : Executor<InboxChecked, SupersedeChecked>("SupersedeCheck")
{
    public override async ValueTask<SupersedeChecked> HandleAsync(InboxChecked message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var superseded = await store.SupersedeIfOutdatedAsync(message.Job, cancellationToken);
        if (superseded)
            await context.YieldOutputAsync(new OrchestrationOutcome(message.Job.Event, OutcomeKind.Superseded), cancellationToken);
        return new SupersedeChecked(message.Job, superseded);
    }
}
