namespace IngestPersister.Consumers;

/// <summary>
/// Reads wound-events (group "persister"), runs one <see cref="Persistence.PersisterTransaction"/> per
/// message, and commits the Kafka offset only after the database commit (architecture §9.4).
/// On failure the message goes to the next retry topic and the original offset is committed,
/// so one bad message never stalls the partition (§8.3).
/// </summary>
public sealed class WoundEventsConsumer(ILogger<WoundEventsConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ingest persister started (consumer loop not implemented yet).");
        // TODO(m4): build a consumer from KafkaDefaults.Consumer(..., ConsumerGroups.Persister),
        // subscribe to Topics.WoundEvents, and for each message:
        //   1. deserialize + validate the WoundEvent
        //   2. PersisterTransaction.ExecuteAsync(...)
        //   3. consumer.Commit(result)
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
