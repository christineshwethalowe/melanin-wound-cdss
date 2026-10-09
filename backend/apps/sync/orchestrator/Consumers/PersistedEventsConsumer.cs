using System.Text.Json;
using Confluent.Kafka;
using Orchestrator.Graph;
using Sync.Common.Contracts;
using Sync.Common.Kafka;
using Sync.Common.Telemetry;

namespace Orchestrator.Consumers;

/// <summary>
/// Consumes wound-events.persisted in its own group ("orchestrator"), separate from the persister, so a slow
/// Recommendation Service call can never delay persistence (§3, §8.3). One workflow run per message; the offset
/// is committed only after <see cref="OutcomeRouter"/> has made the outcome durable.
/// </summary>
public sealed class PersistedEventsConsumer(
    WorkflowRunner runner, OutcomeRouter router, IConfiguration config, ILogger<PersistedEventsConsumer> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken ct)
    {
        var bootstrap = config["Kafka:BootstrapServers"] ?? KafkaDefaults.DefaultBootstrapServers;
        using var consumer = new ConsumerBuilder<string, byte[]>(
            KafkaDefaults.Consumer(bootstrap, ConsumerGroups.Orchestrator)).Build();
        consumer.Subscribe(Topics.WoundEventsPersisted);
        logger.LogInformation("Orchestrator consuming {Topic} as group {Group}", Topics.WoundEventsPersisted,
            ConsumerGroups.Orchestrator);

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
            catch (Exception ex)
            {
                // Only reached when the outcome could not be made durable (e.g. Kafka down for the retry copy):
                // leave the offset uncommitted and read the message again after a pause.
                logger.LogError(ex, "Could not finish {Ref}; will redeliver", result.TopicPartitionOffset.KafkaRef());
                consumer.Seek(result.TopicPartitionOffset);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }

        consumer.Close();
    }

    /// <summary>Shared with the retry consumer: parse, run the workflow, route the outcome.</summary>
    internal static async Task<OrchestrationOutcome> ProcessAsync(ConsumeResult<string, byte[]> result, WorkflowRunner runner,
        OutcomeRouter router, ILogger logger, CancellationToken ct)
    {
        using var activity = SyncTelemetry.StartConsume(SyncTelemetry.Orchestrator, "orchestrate", result.Message.Headers);

        PersistedEvent evt;
        try
        {
            evt = JsonSerializer.Deserialize<PersistedEvent>(result.Message.Value, JsonSerializerOptions.Web)
                  ?? throw new JsonException("empty payload");
        }
        catch (JsonException ex)
        {
            // Unparseable: retrying cannot help.
            var unknown = new OrchestrationOutcome(new PersistedEvent(Guid.Empty, Guid.Empty, 0, Guid.Empty),
                OutcomeKind.DeadLetter, $"UNPARSEABLE: {ex.Message}");
            await router.RouteAsync(result, unknown, ct);
            return unknown;
        }

        var job = new OrchestrationJob(evt, result.Message.Headers.GetHeader(HeaderNames.TraceParent));
        var outcome = await runner.RunAsync(job, ct);
        await router.RouteAsync(result, outcome, ct);

        logger.Log(outcome.Retry || outcome.Kind == OutcomeKind.DeadLetter ? LogLevel.Warning : LogLevel.Information,
            "{EventId} (assessment {AssessmentId} rev {Revision}) from {Ref}: {Kind} {Detail}", evt.EventId,
            evt.AssessmentId, evt.Revision, result.TopicPartitionOffset.KafkaRef(), outcome.Kind, outcome.Detail);
        return outcome;
    }

    private Task HandleAsync(ConsumeResult<string, byte[]> result, CancellationToken ct) =>
        ProcessAsync(result, runner, router, logger, ct);
}
