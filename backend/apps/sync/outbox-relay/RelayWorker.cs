namespace OutboxRelay;

/// <summary>
/// Polls messaging.outbox, publishes each row to its topic, then marks it published.
/// FOR UPDATE SKIP LOCKED lets several relay replicas run without double-publishing (§9.3).
/// A crash after publishing re-sends the row; downstream consumers drop it via messaging.inbox.
/// </summary>
public sealed class RelayWorker(ILogger<RelayWorker> logger) : BackgroundService
{
    public const string ClaimBatchSql = """
        SELECT outbox_id, topic, msg_key, payload, headers
        FROM messaging.outbox
        WHERE published_at IS NULL
        ORDER BY outbox_id
        LIMIT @batch
        FOR UPDATE SKIP LOCKED
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox relay started (poll loop not implemented yet).");
        // TODO(m4): in a transaction, claim a batch, produce each row (acks=all), set published_at, commit.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
