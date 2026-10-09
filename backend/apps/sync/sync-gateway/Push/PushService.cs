using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Npgsql;
using NpgsqlTypes;
using Sync.Common.Kafka;
using SyncGateway.Validation;

namespace SyncGateway.Push;

public sealed record PushRequest(string DeviceId, string? BatchId, List<JsonElement> Events);

public sealed record PushEventResult(string EventId, string Status, string? Code = null, string? Detail = null);

public sealed record PushResponse(string? BatchId, List<PushEventResult> Results);

/// <summary>Thrown when Kafka did not acknowledge every event; the endpoint turns it into 503 + Retry-After.</summary>
public sealed class BackboneUnavailableException(Exception inner) : Exception("Kafka did not acknowledge the batch", inner);

/// <summary>
/// Push protocol (architecture §7.1). An event is ACCEPTED only after Kafka acknowledges it with acks=all.
/// Anything already accepted earlier is answered DUPLICATE without producing it again. Invalid events are
/// REJECTED individually and never block the rest of the batch.
/// </summary>
public sealed class PushService(
    NpgsqlDataSource db, IProducer<string, byte[]> producer, WoundEventValidator validator, ILogger<PushService> logger)
{
    public const int MaxEventsPerBatch = 50;

    public async Task<PushResponse> PushAsync(PushRequest request, string tokenDeviceId, string tokenFacilityId, CancellationToken ct)
    {
        var results = new PushEventResult?[request.Events.Count];
        var candidates = new List<(int Index, Guid EventId, Guid WoundId, JsonElement Json)>();

        for (var i = 0; i < request.Events.Count; i++)
        {
            var evt = request.Events[i];
            var eventIdText = evt.ValueKind == JsonValueKind.Object && evt.TryGetProperty("eventId", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()! : $"#{i}";

            if (validator.Validate(evt) is { } error)
            {
                results[i] = new PushEventResult(eventIdText, "REJECTED", error.Code, error.Detail);
                continue;
            }
            if (evt.GetProperty("deviceId").GetString() != tokenDeviceId)
            {
                results[i] = new PushEventResult(eventIdText, "REJECTED", "DEVICE_MISMATCH");
                continue;
            }
            if (evt.GetProperty("facilityId").GetString() != tokenFacilityId)
            {
                results[i] = new PushEventResult(eventIdText, "REJECTED", "FACILITY_MISMATCH");
                continue;
            }
            candidates.Add((i, Guid.Parse(eventIdText), evt.GetProperty("woundId").GetGuid(), evt));
        }

        // Anything the gateway accepted before (or that is already persisted) is a DUPLICATE: a repeated
        // reconnect ends safely without producing the event again.
        var known = await KnownEventIdsAsync(candidates.Select(c => c.EventId).ToArray(), ct);
        var toProduce = new List<(int Index, Guid EventId, Guid WoundId, JsonElement Json)>();
        foreach (var c in candidates)
        {
            if (known.Contains(c.EventId)) results[c.Index] = new PushEventResult(c.EventId.ToString(), "DUPLICATE");
            else toProduce.Add(c);
        }

        // Produce the rest (also de-duplicating repeats inside one batch) and wait for every ack.
        var traceParent = Activity.Current?.Id;
        var deliveries = new List<(int Index, Guid EventId, Task<DeliveryResult<string, byte[]>> Task)>();
        var seenInBatch = new HashSet<Guid>();
        foreach (var c in toProduce)
        {
            if (!seenInBatch.Add(c.EventId))
            {
                results[c.Index] = new PushEventResult(c.EventId.ToString(), "DUPLICATE");
                continue;
            }
            var message = new Message<string, byte[]>
            {
                Key = c.WoundId.ToString(),
                Value = Encoding.UTF8.GetBytes(c.Json.GetRawText()),
                Headers = new Headers
                {
                    { HeaderNames.EventId, Encoding.UTF8.GetBytes(c.EventId.ToString()) },
                    { HeaderNames.SchemaVersion, Encoding.UTF8.GetBytes(c.Json.GetProperty("schemaVersion").GetString()!) },
                    { HeaderNames.DeviceId, Encoding.UTF8.GetBytes(tokenDeviceId) },
                },
            };
            if (traceParent is not null)
                message.Headers.Add(HeaderNames.TraceParent, Encoding.UTF8.GetBytes(traceParent));
            deliveries.Add((c.Index, c.EventId, producer.ProduceAsync(Topics.WoundEvents, message, ct)));
        }

        try
        {
            await Task.WhenAll(deliveries.Select(d => d.Task));
        }
        catch (Exception ex) when (ex is ProduceException<string, byte[]> or KafkaException)
        {
            // Nothing is reported as accepted unless every produce was acknowledged. The device returns the
            // rows to PENDING and retries; events that did land will then come back as DUPLICATE.
            throw new BackboneUnavailableException(ex);
        }

        foreach (var d in deliveries)
            results[d.Index] = new PushEventResult(d.EventId.ToString(), "ACCEPTED");

        await RecordProvenanceAsync(deliveries.Select(d => (d.EventId, d.Task.Result.TopicPartitionOffset)).ToList(),
            tokenDeviceId, traceParent, ct);

        return new PushResponse(request.BatchId, results.Select(r => r!).ToList());
    }

    private async Task<HashSet<Guid>> KnownEventIdsAsync(Guid[] eventIds, CancellationToken ct)
    {
        var known = new HashSet<Guid>();
        if (eventIds.Length == 0) return known;

        await using var cmd = db.CreateCommand("""
            SELECT event_id FROM audit.provenance WHERE stage = 'GATEWAY_ACCEPTED' AND event_id = ANY(@ids)
            UNION
            SELECT event_id FROM clinical.wound_assessment WHERE event_id = ANY(@ids)
            """);
        cmd.Parameters.AddWithValue("ids", eventIds);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) known.Add(r.GetGuid(0));
        return known;
    }

    private async Task RecordProvenanceAsync(List<(Guid EventId, TopicPartitionOffset Tpo)> accepted, string deviceId,
        string? traceId, CancellationToken ct)
    {
        if (accepted.Count == 0) return;
        try
        {
            await using var cmd = db.CreateCommand("""
                INSERT INTO audit.provenance (event_id, stage, outcome, device_id, kafka_ref, trace_id)
                SELECT e, 'GATEWAY_ACCEPTED', 'OK', @d, k, @t FROM unnest(@events, @refs) AS x(e, k)
                """);
            cmd.Parameters.AddWithValue("events", accepted.Select(a => a.EventId).ToArray());
            cmd.Parameters.AddWithValue("refs", accepted.Select(a => a.Tpo.KafkaRef()).ToArray());
            cmd.Parameters.AddWithValue("d", deviceId);
            cmd.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.Text) { Value = (object?)traceId ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (NpgsqlException ex)
        {
            // The events are already durable in Kafka, so the device must still be told ACCEPTED.
            // The missing stage shows up in the auditability-completeness metric.
            logger.LogError(ex, "Could not record GATEWAY_ACCEPTED provenance for {Count} events", accepted.Count);
        }
    }
}
