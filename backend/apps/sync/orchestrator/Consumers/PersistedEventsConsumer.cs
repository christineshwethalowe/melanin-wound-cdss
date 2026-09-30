using Microsoft.Agents.AI.Workflows;
using Sync.Common.Kafka;

namespace Orchestrator.Consumers;

/// <summary>
/// Consumes wound-events.persisted in its own group ("orchestrator"), separate from the persister, so a slow
/// Recommendation Service call can never delay persistence (§3, §8.3). One workflow run per message; the
/// offset is committed only after the run's outcome is durable:
///   Stored / AlreadyProcessed / Superseded → commit
///   Deferred     → produce to RetryRouting.NextHop(topic), then commit
///   DeadLetter   → produce to wound-events.dlq, then commit
/// </summary>
public sealed class PersistedEventsConsumer(Workflow workflow, ILogger<PersistedEventsConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Orchestrator workflow '{Name}' built ({Description}); consuming {Topic} as {Group} is plan phase 6",
            workflow.Name, workflow.Description, Topics.WoundEventsPersisted, ConsumerGroups.Orchestrator);

        // TODO(phase 6): consume Topics.WoundEventsPersisted with KafkaDefaults.Consumer(..., ConsumerGroups.Orchestrator);
        // for each message: InProcessExecution.RunAsync(workflow, persistedEvent, checkpointManager, runId: eventId),
        // read the OrchestrationOutcome from the output events, route as above, then commit.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
