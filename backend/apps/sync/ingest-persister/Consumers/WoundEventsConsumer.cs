using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using IngestPersister.Persistence;
using Npgsql;
using Sync.Common.Contracts;
using Sync.Common.Kafka;

namespace IngestPersister.Consumers;

/// <summary>
/// Reads wound-events (group "persister"), runs one <see cref="PersisterTransaction"/> per message and
/// commits the Kafka offset only after the database commit (architecture §9.4).
///
/// - A message that cannot be parsed, or that conflicts with a stored revision, goes to the DLQ and its
///   offset is committed, so one bad message never stalls the partition.
/// - A database failure does not commit the offset: the consumer seeks back and the message is redelivered.
///   (Plan phase 7 moves transient failures onto the retry topics instead.)
/// </summary>
public sealed class WoundEventsConsumer(
    PersisterTransaction persister, IProducer<string, byte[]> producer, IConfiguration config, ILogger<WoundEventsConsumer> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken ct)
    {
        var bootstrap = config["Kafka:BootstrapServers"] ?? KafkaDefaults.DefaultBootstrapServers;
        using var consumer = new ConsumerBuilder<string, byte[]>(KafkaDefaults.Consumer(bootstrap, ConsumerGroups.Persister)).Build();
        consumer.Subscribe(Topics.WoundEvents);
        logger.LogInformation("Ingest persister consuming {Topic} as group {Group}", Topics.WoundEvents, ConsumerGroups.Persister);

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, byte[]> result;
            try
            {
                result = consumer.Consume(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ConsumeException ex)
            {
                logger.LogWarning(ex, "Consume failed; retrying");
                continue;
            }

            try
            {
                await HandleAsync(result, ct);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
            {
                // Transient: leave the offset uncommitted and read the message again after a pause.
                logger.LogError(ex, "Database write failed for {Ref}; will redeliver", result.TopicPartitionOffset.KafkaRef());
                consumer.Seek(result.TopicPartitionOffset);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }

        consumer.Close();
    }

    private async Task HandleAsync(ConsumeResult<string, byte[]> result, CancellationToken ct)
    {
        var traceId = result.Message.Headers.GetHeader(HeaderNames.TraceParent);

        WoundEvent evt;
        JsonElement raw;
        try
        {
            using var doc = JsonDocument.Parse(result.Message.Value);
            raw = doc.RootElement.Clone();
            evt = raw.Deserialize<WoundEvent>(WoundEvent.JsonOptions)
                  ?? throw new JsonException("empty payload");
        }
        catch (JsonException ex)
        {
            await DeadLetterAsync(result, "UNPARSEABLE", ex.Message, ct);
            return;
        }

        var outcome = await persister.ExecuteAsync(evt, raw, result.TopicPartitionOffset, traceId, ct);
        switch (outcome)
        {
            case PersistOutcome.Persisted:
                logger.LogInformation("Persisted {EventId} (assessment {AssessmentId} rev {Revision}) from {Ref}",
                    evt.EventId, evt.AssessmentId, evt.Revision, result.TopicPartitionOffset.KafkaRef());
                break;
            case PersistOutcome.Deduplicated:
                logger.LogInformation("Deduplicated {EventId} from {Ref}", evt.EventId, result.TopicPartitionOffset.KafkaRef());
                break;
            case PersistOutcome.RevisionConflict:
                await DeadLetterAsync(result, "REVISION_CONFLICT",
                    $"assessment {evt.AssessmentId} revision {evt.Revision} is already stored under another eventId", ct);
                break;
        }
    }

    private async Task DeadLetterAsync(ConsumeResult<string, byte[]> result, string code, string detail, CancellationToken ct)
    {
        var headers = result.Message.Headers ?? new Headers();
        headers.Add("dlq-reason", Encoding.UTF8.GetBytes(code));
        headers.Add("dlq-detail", Encoding.UTF8.GetBytes(detail));
        headers.Add("dlq-source", Encoding.UTF8.GetBytes(result.TopicPartitionOffset.KafkaRef()));

        await producer.ProduceAsync(Topics.DeadLetter,
            new Message<string, byte[]> { Key = result.Message.Key, Value = result.Message.Value, Headers = headers }, ct);
        logger.LogWarning("Sent {Ref} to {Dlq}: {Code} {Detail}", result.TopicPartitionOffset.KafkaRef(), Topics.DeadLetter, code, detail);
    }
}
