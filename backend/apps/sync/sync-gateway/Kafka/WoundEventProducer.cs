using Confluent.Kafka;
using Sync.Common.Kafka;

namespace SyncGateway.Kafka;

/// <summary>
/// Produces accepted events to wound-events, keyed by woundId so every revision of one wound
/// stays in order on one partition (architecture §8.1). An event counts as ACCEPTED only once
/// Kafka has acknowledged it with acks=all.
/// </summary>
public sealed class WoundEventProducer(IProducer<string, byte[]> producer)
{
    public Task<DeliveryResult<string, byte[]>> ProduceAsync(
        Guid woundId, Guid eventId, string deviceId, string schemaVersion, byte[] payload, CancellationToken ct)
    {
        var message = new Message<string, byte[]>
        {
            Key = woundId.ToString(),
            Value = payload,
            Headers = new Confluent.Kafka.Headers
            {
                { Sync.Common.Kafka.Headers.EventId, System.Text.Encoding.UTF8.GetBytes(eventId.ToString()) },
                { Sync.Common.Kafka.Headers.SchemaVersion, System.Text.Encoding.UTF8.GetBytes(schemaVersion) },
                { Sync.Common.Kafka.Headers.DeviceId, System.Text.Encoding.UTF8.GetBytes(deviceId) },
            },
        };
        return producer.ProduceAsync(Topics.WoundEvents, message, ct);
    }
}
