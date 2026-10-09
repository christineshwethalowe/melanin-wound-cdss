using Microsoft.Agents.AI.Workflows;
using Orchestrator.Graph;
using Orchestrator.Persistence;

namespace Orchestrator.Executors;

/// <summary>
/// Skips a message this consumer already processed (architecture §10.1): messaging.inbox is unique on
/// (consumer_name, event_id). The relay can publish a row twice after a crash, and a crashed run is redelivered;
/// this is where the repeat stops. The inbox row itself is written by PersistResult (or SupersedeCheck), in the
/// same transaction as the result. A database error ends the run as Failed (retry topic).
/// </summary>
[YieldsOutput(typeof(OrchestrationOutcome))]
public sealed class InboxCheckExecutor(IOrchestratorStore store) : Executor<OrchestrationJob, InboxChecked>("InboxCheck")
{
    public override async ValueTask<InboxChecked> HandleAsync(OrchestrationJob message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var processed = await store.BeginAsync(message, cancellationToken);
        if (processed)
            await context.YieldOutputAsync(new OrchestrationOutcome(message.Event, OutcomeKind.AlreadyProcessed), cancellationToken);
        return new InboxChecked(message, processed);
    }
}
