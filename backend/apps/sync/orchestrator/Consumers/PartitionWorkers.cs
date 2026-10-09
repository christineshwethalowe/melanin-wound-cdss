using System.Collections.Concurrent;
using System.Threading.Channels;
using Confluent.Kafka;
using Sync.Common.Kafka;

namespace Orchestrator.Consumers;

/// <summary>
/// "Concurrency per consumer" (architecture §4): messages from different partitions are handled in parallel (up to
/// <c>maxConcurrency</c> at once), messages within one partition strictly one after another. Keys are woundIds, so a
/// wound's revisions stay in order (§8.1) while different wounds overlap — a slow Recommendation Service call no longer
/// holds up every other wound.
///
/// Offsets keep the at-least-once rule (§9.4, §11): a worker records an offset as done only after its message's outcome
/// is durable, and only the consume thread commits (the Kafka client is not called from workers). Because a partition
/// is processed in order, committing its last done offset never skips unfinished work. A partition with too much queued
/// work is paused until it drains, so memory stays bounded.
/// </summary>
public sealed class PartitionWorkers(
    Func<ConsumeResult<string, byte[]>, CancellationToken, Task> handle, int maxConcurrency, int maxQueuedPerPartition,
    ILogger logger, CancellationToken stopping)
{
    private sealed class Worker
    {
        public required Channel<ConsumeResult<string, byte[]>> Queue { get; init; }
        public required Task Loop { get; set; }
        public long LastDone = -1;
        public long LastCommitted = -1;
        public int Queued;
        public volatile bool Stopping;
    }

    private readonly ConcurrentDictionary<TopicPartition, Worker> _workers = new();
    private readonly HashSet<TopicPartition> _paused = [];
    private readonly SemaphoreSlim _slots = new(Math.Max(1, maxConcurrency));

    /// <summary>Hands a consumed message to its partition's worker; pauses the partition when its queue is full.</summary>
    public void Dispatch(IConsumer<string, byte[]> consumer, ConsumeResult<string, byte[]> result)
    {
        var worker = _workers.GetOrAdd(result.TopicPartition, Start);
        Interlocked.Increment(ref worker.Queued);
        worker.Queue.Writer.TryWrite(result);
        if (worker.Queued >= maxQueuedPerPartition && _paused.Add(result.TopicPartition))
            consumer.Pause([result.TopicPartition]);
    }

    /// <summary>Called on the consume thread between polls: commits finished work and resumes drained partitions.</summary>
    public void Tick(IConsumer<string, byte[]> consumer)
    {
        foreach (var (tp, worker) in _workers)
        {
            var done = Interlocked.Read(ref worker.LastDone);
            if (done > worker.LastCommitted)
            {
                consumer.Commit([new TopicPartitionOffset(tp, done + 1)]);
                worker.LastCommitted = done;
            }
        }

        var drained = _paused.Where(tp => !_workers.TryGetValue(tp, out var w) || w.Queued <= maxQueuedPerPartition / 2).ToList();
        if (drained.Count == 0) return;
        consumer.Resume(drained);
        foreach (var tp in drained) _paused.Remove(tp);
    }

    /// <summary>
    /// Partitions taken away in a rebalance, or the service stopping: each worker finishes the message in hand,
    /// its done work is committed, and anything still queued is left for the next owner (redelivered, idempotent).
    /// </summary>
    public void Stop(IConsumer<string, byte[]> consumer, IEnumerable<TopicPartition> partitions, bool commit)
    {
        var stopping = partitions.Select(tp => (tp, w: _workers.TryRemove(tp, out var w) ? w : null))
            .Where(x => x.w is not null).ToList();
        foreach (var (_, w) in stopping)
        {
            w!.Stopping = true;
            w.Queue.Writer.TryComplete();
        }
        // The message in hand may be a Recommendation Service call: up to its 60 s attempt timeout plus retries.
        Task.WaitAll(stopping.Select(x => x.w!.Loop).ToArray(), TimeSpan.FromSeconds(150));

        foreach (var (tp, w) in stopping)
        {
            _paused.Remove(tp);
            var done = Interlocked.Read(ref w!.LastDone);
            if (commit && done > w.LastCommitted)
            {
                try { consumer.Commit([new TopicPartitionOffset(tp, done + 1)]); }
                catch (KafkaException ex) { logger.LogWarning(ex, "Could not commit {Partition} on handover", tp); }
            }
        }
    }

    public IReadOnlyCollection<TopicPartition> Partitions => _workers.Keys.ToList();

    private Worker Start(TopicPartition tp)
    {
        var worker = new Worker
        {
            Queue = Channel.CreateUnbounded<ConsumeResult<string, byte[]>>(new UnboundedChannelOptions { SingleReader = true }),
            Loop = Task.CompletedTask,
        };
        worker.Loop = Task.Run(() => RunAsync(tp, worker));
        return worker;
    }

    private async Task RunAsync(TopicPartition tp, Worker worker)
    {
        await foreach (var result in worker.Queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            if (worker.Stopping || stopping.IsCancellationRequested) return;
            await _slots.WaitAsync(CancellationToken.None);
            try
            {
                // Retried in place until the outcome is durable (e.g. Kafka down for the retry copy): the partition
                // waits rather than skipping ahead, so its committed offset never passes unfinished work.
                while (true)
                {
                    try
                    {
                        await handle(result, stopping);
                        break;
                    }
                    catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Could not finish {Ref}; retrying", result.TopicPartitionOffset.KafkaRef());
                        if (worker.Stopping) return;
                        await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
                    }
                }
            }
            finally
            {
                _slots.Release();
            }
            Interlocked.Exchange(ref worker.LastDone, result.Offset.Value);
            Interlocked.Decrement(ref worker.Queued);
        }
    }
}
