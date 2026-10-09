using Sync.Common.Kafka;

namespace Orchestrator.Consumers;

/// <summary>
/// Delayed redelivery (architecture §8.1, §8.3): consumes wound-events.retry.30s and .retry.5m (group
/// "orchestrator"). For each message it pauses that partition until message timestamp + RetryRouting.DelayFor
/// has passed, then runs the same workflow. A failure moves it one hop further (RetryRouting.NextHop), ending
/// in wound-events.dlq for the manual replay tool. This — not partitioning alone — removes head-of-line blocking.
/// </summary>
public sealed class RetryTopicsConsumer(ILogger<RetryTopicsConsumer> logger) : BackgroundService
{
    public static readonly string[] RetryTopics = [Topics.Retry30s, Topics.Retry5m];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Retry consumer for {Topics} is plan phase 7", string.Join(", ", RetryTopics));
        // TODO(phase 7): subscribe to RetryTopics; consumer.Pause/Resume per partition around the delay.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
