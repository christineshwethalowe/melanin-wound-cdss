namespace Orchestrator.Workflow;

/// <summary>
/// Executor graph (architecture §10.1), built with Microsoft Agent Framework workflows:
///
///   InboxCheck → SupersedeCheck → BuildContext → CallRag → ValidateResponse → PersistResult
///
/// Each step lives in Executors/. Failures route to wound-events.retry.30s / .retry.5m, then the DLQ.
/// </summary>
public sealed class OrchestratorWorker(ILogger<OrchestratorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Orchestrator started (workflow not implemented yet).");
        // TODO(m4): consume Topics.WoundEventsPersisted, build the workflow graph, run one per message.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
