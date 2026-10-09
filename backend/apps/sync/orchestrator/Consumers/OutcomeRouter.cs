using System.Globalization;
using System.Text;
using Confluent.Kafka;
using Orchestrator.Graph;
using Orchestrator.Persistence;
using Sync.Common.Kafka;

namespace Orchestrator.Consumers;

/// <summary>
/// Makes a run's outcome durable before the consumer commits its offset (architecture §8.3, §10.3, §11):
///   Stored / AlreadyProcessed / Superseded → nothing to do
///   Deferred / Failed → ADVICE_DEFERRED for the device, copy to the next retry topic
///   DeadLetter        → ADVICE_DEFERRED for the device, copy to wound-events.dlq
/// If producing fails (Kafka down) this throws, the offset stays uncommitted, and the message is read again.
/// </summary>
public sealed class OutcomeRouter(IProducer<string, byte[]> producer, IOrchestratorStore store, ILogger<OutcomeRouter> logger)
{
    public async Task RouteAsync(ConsumeResult<string, byte[]> source, OrchestrationOutcome outcome, CancellationToken ct)
    {
        if (!outcome.Retry && outcome.Kind != OutcomeKind.DeadLetter) return;

        try
        {
            // The device sees "advice deferred" instead of waiting silently (§6.1). Best effort: when the
            // database itself is the problem, the retry still goes ahead.
            await store.RecordDeferredAsync(outcome.Event, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not record ADVICE_DEFERRED for {EventId}", outcome.Event.EventId);
        }

        var target = outcome.Kind == OutcomeKind.DeadLetter ? Topics.DeadLetter : RetryRouting.NextHop(source.Topic);
        var headers = new Headers();
        foreach (var h in source.Message.Headers ?? [])
            if (h.Key is not (RetryRouting.RetryCountHeader or RetryRouting.ErrorHeader))
                headers.Add(h.Key, h.GetValueBytes());

        var attempts = int.TryParse(source.Message.Headers.GetHeader(RetryRouting.RetryCountHeader), out var n) ? n : 0;
        headers.Add(RetryRouting.RetryCountHeader, Encoding.UTF8.GetBytes((attempts + 1).ToString(CultureInfo.InvariantCulture)));
        headers.Add(RetryRouting.ErrorHeader, Encoding.UTF8.GetBytes($"{outcome.Kind}: {outcome.Detail}"));
        if (source.Message.Headers.GetHeader(RetryRouting.OriginalTopicHeader) is null)
            headers.Add(RetryRouting.OriginalTopicHeader, Encoding.UTF8.GetBytes(source.Topic));
        if (target == Topics.DeadLetter)
        {
            headers.Add("dlq-reason", Encoding.UTF8.GetBytes(outcome.Kind.ToString()));
            headers.Add("dlq-detail", Encoding.UTF8.GetBytes(outcome.Detail ?? ""));
            headers.Add("dlq-source", Encoding.UTF8.GetBytes(source.TopicPartitionOffset.KafkaRef()));
        }

        await producer.ProduceAsync(target,
            new Message<string, byte[]> { Key = source.Message.Key, Value = source.Message.Value, Headers = headers }, ct);
        logger.LogWarning("{EventId} {Kind} ({Detail}); sent to {Target}", outcome.Event.EventId, outcome.Kind, outcome.Detail, target);
    }
}
