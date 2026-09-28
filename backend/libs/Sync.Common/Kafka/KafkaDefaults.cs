using Confluent.Kafka;

namespace Sync.Common.Kafka;

/// <summary>The client settings that matter (architecture §8.2), in one place so no service drifts.</summary>
public static class KafkaDefaults
{
    public static ProducerConfig Producer(string bootstrapServers) => new()
    {
        BootstrapServers = bootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = true,
    };

    /// <summary>Offsets are committed by hand, only after the database transaction commits.</summary>
    public static ConsumerConfig Consumer(string bootstrapServers, string groupId) => new()
    {
        BootstrapServers = bootstrapServers,
        GroupId = groupId,
        EnableAutoCommit = false,
        AutoOffsetReset = AutoOffsetReset.Earliest,
    };
}
