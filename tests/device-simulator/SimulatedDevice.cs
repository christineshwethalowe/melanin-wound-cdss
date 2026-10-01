using System.Net;

namespace DeviceSimulator;

/// <summary>
/// One simulated phone (§13.1). Captures go into its <see cref="DeviceQueue"/> while a single sync loop runs the §6.2
/// rules: probe /health, push the oldest batch (gzip, per-event results), back off with full jitter on failure,
/// honour Retry-After, split on 413, then pull changes after its cursor until every record is final.
/// In baseline mode it instead sends each capture to the REST baseline and retries the same request on failure,
/// as a naive client would (§13.1).
/// </summary>
public sealed class SimulatedDevice(string deviceId, GatewayClient gateway, EventFactory events, SimOptions options,
    TimeProvider clock, Random random)
{
    public string DeviceId { get; } = deviceId;
    public DeviceQueue Queue { get; } = new(clock);
    public int Pushes { get; private set; }
    public int Pulls { get; private set; }

    private readonly Backoff _backoff = new(random);
    private long _cursor;

    public async Task RunAsync(CancellationToken ct)
    {
        await gateway.LoginAsync(ct);
        var capturing = CaptureAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            if (options.Mode == SimMode.Baseline) await BaselineStepAsync(ct);
            else await SyncStepAsync(ct);

            if (capturing.IsCompleted && Queue.AllFinal()) break;
            await Task.Delay(options.PollInterval, ct);
        }
        await capturing;
    }

    /// <summary>The clinician saves assessments; each one commits to the queue before anything else (§6).</summary>
    private async Task CaptureAsync(CancellationToken ct)
    {
        for (var i = 0; i < options.EventsPerDevice; i++)
        {
            Queue.Enqueue(events.Next());
            if (i < options.EventsPerDevice - 1) await Task.Delay(options.CaptureInterval, ct);
        }
    }

    private async Task SyncStepAsync(CancellationToken ct)
    {
        while (Queue.HasWorkToSend() && !ct.IsCancellationRequested)
        {
            if (!await gateway.HealthAsync(ct))
            {
                await Task.Delay(_backoff.NextDelay(), ct);
                return;
            }

            var batch = Queue.LeaseBatch();
            if (batch.Count == 0) break;
            if (!await PushAsync(batch, ct)) return;
        }

        await PullAsync(ct);
    }

    /// <returns>false when the sync run should stop and back off.</returns>
    private async Task<bool> PushAsync(IReadOnlyList<QueuedEvent> batch, CancellationToken ct)
    {
        Pushes++;
        var outcome = await gateway.PushAsync(batch, ct);
        switch (outcome.Status)
        {
            case HttpStatusCode.OK:
                var byId = batch.ToDictionary(r => r.EventId);
                foreach (var (eventId, status, code) in outcome.Results)
                    if (byId.TryGetValue(eventId, out var row)) Queue.ApplyPushResult(row, status, code);
                _backoff.Reset();
                return true;

            case HttpStatusCode.RequestEntityTooLarge when batch.Count > 1:
                // §7.1: split the batch and retry. Push the first half now; the rest is picked up on the next pass.
                Queue.Release(batch.Skip(batch.Count / 2), "split after 413");
                return await PushAsync(batch.Take(batch.Count / 2).ToList(), ct);

            default:
                // 429, 503, timeout, no response: rows back to PENDING and back off (§7.1).
                Queue.Release(batch, outcome.Error ?? "push failed");
                await Task.Delay(_backoff.NextDelay(outcome.RetryAfter), ct);
                return false;
        }
    }

    private async Task PullAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Pulls++;
            var page = await gateway.PullAsync(_cursor, ct);
            if (page is null) return;
            foreach (var change in page.Value.Changes)
                Queue.ApplyChange(change.AssessmentId, change.Revision, change.Type);
            _cursor = page.Value.NextCursor;
            if (!page.Value.HasMore) return;
        }
    }

    private async Task BaselineStepAsync(CancellationToken ct)
    {
        // One synchronous request per assessment, one after another.
        while (Queue.LeaseBatch(maxEvents: 1) is [var row] && !ct.IsCancellationRequested)
        {
            var (status, error) = await gateway.BaselineAsync(row, ct);
            Pushes++;
            if (status == HttpStatusCode.OK)
            {
                Queue.ApplyPushResult(row, "ACCEPTED", null);
                Queue.ApplyChange(row.AssessmentId, row.Revision, "RECOMMENDATION_READY");
                _backoff.Reset();
            }
            else if (status is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
            {
                Queue.ApplyPushResult(row, "REJECTED", error);
            }
            else
            {
                // A naive client retries the same request; the baseline stores it again each time.
                Queue.Release([row], error ?? "baseline failed");
                await Task.Delay(_backoff.NextDelay(), ct);
                return;
            }
        }
    }
}
