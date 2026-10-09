using System.Text.Json;
using Confluent.Kafka;
using Npgsql;
using NpgsqlTypes;
using Sync.Common.Contracts;
using Sync.Common.Kafka;
using Sync.Common.Persistence;

namespace IngestPersister.Persistence;

public enum PersistOutcome { Persisted, Deduplicated, RevisionConflict }

/// <summary>
/// One transaction per event (architecture §9.4):
///   1. advisory lock on assessment_id
///   2. upsert clinical.patient (patient_ref + facility only; display_alias is never taken from Kafka)
///   3. upsert clinical.wound
///   4. insert clinical.wound_assessment, ignoring conflicts
///   5a. inserted  → PERSISTED provenance + outbox row (wound-events.persisted) + change_log PERSISTED
///   5b. duplicate → DEDUPLICATED provenance only
/// The caller commits the Kafka offset only after this returns.
/// </summary>
public sealed class PersisterTransaction(NpgsqlDataSource db)
{
    public async Task<PersistOutcome> ExecuteAsync(
        WoundEvent evt, JsonElement raw, TopicPartitionOffset source, string? traceId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await AdvisoryLock.AcquireForAssessmentAsync(conn, tx, evt.AssessmentId, ct);

        await ExecAsync(conn, tx, """
            INSERT INTO clinical.patient (patient_ref, facility_id) VALUES (@p, @f)
            ON CONFLICT (patient_ref) DO UPDATE SET updated_at = now()
            """, ct, ("p", evt.PatientRef), ("f", evt.FacilityId));

        await ExecAsync(conn, tx, """
            INSERT INTO clinical.wound (wound_id, patient_ref) VALUES (@w, @p) ON CONFLICT (wound_id) DO NOTHING
            """, ct, ("w", evt.WoundId), ("p", evt.PatientRef));

        int inserted;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO clinical.wound_assessment
              (event_id, assessment_id, revision, wound_id, patient_ref, device_id,
               captured_at, analytics, clinical_assessment, kafka_topic, kafka_partition, kafka_offset)
            VALUES
              (@event_id, @assessment_id, @revision, @wound_id, @patient_ref, @device_id,
               @captured_at, @analytics, @clinical_assessment, @kafka_topic, @kafka_partition, @kafka_offset)
            ON CONFLICT DO NOTHING
            """, conn, tx))
        {
            var p = insert.Parameters;
            p.AddWithValue("event_id", evt.EventId);
            p.AddWithValue("assessment_id", evt.AssessmentId);
            p.AddWithValue("revision", evt.Revision);
            p.AddWithValue("wound_id", evt.WoundId);
            p.AddWithValue("patient_ref", evt.PatientRef);
            p.AddWithValue("device_id", evt.DeviceId);
            p.AddWithValue("captured_at", evt.CapturedAt.ToUniversalTime()); // timestamptz stores UTC
            p.Add(new NpgsqlParameter("analytics", NpgsqlDbType.Jsonb) { Value = raw.GetProperty("analytics").GetRawText() });
            p.Add(new NpgsqlParameter("clinical_assessment", NpgsqlDbType.Jsonb) { Value = raw.GetProperty("clinicalAssessment").GetRawText() });
            p.AddWithValue("kafka_topic", source.Topic);
            p.AddWithValue("kafka_partition", source.Partition.Value);
            p.AddWithValue("kafka_offset", source.Offset.Value);
            inserted = await insert.ExecuteNonQueryAsync(ct);
        }

        PersistOutcome outcome;
        if (inserted == 1)
        {
            outcome = PersistOutcome.Persisted;
            await ProvenanceAsync(conn, tx, evt, "PERSISTED", "OK", source, traceId, ct);

            var message = JsonSerializer.Serialize(
                new PersistedEvent(evt.EventId, evt.AssessmentId, evt.Revision, evt.WoundId), WoundEvent.JsonOptions);
            var headers = traceId is null ? "{}" : JsonSerializer.Serialize(new Dictionary<string, string> { [HeaderNames.TraceParent] = traceId });
            await using (var outbox = new NpgsqlCommand(
                "INSERT INTO messaging.outbox (topic, msg_key, payload, headers) VALUES (@t, @k, @p, @h)", conn, tx))
            {
                outbox.Parameters.AddWithValue("t", Topics.WoundEventsPersisted);
                outbox.Parameters.AddWithValue("k", evt.WoundId.ToString());
                outbox.Parameters.Add(new NpgsqlParameter("p", NpgsqlDbType.Jsonb) { Value = message });
                outbox.Parameters.Add(new NpgsqlParameter("h", NpgsqlDbType.Jsonb) { Value = headers });
                await outbox.ExecuteNonQueryAsync(ct);
            }

            await ExecAsync(conn, tx, """
                INSERT INTO sync.change_log (facility_id, device_id, change_type, assessment_id, revision)
                VALUES (@f, @d, 'PERSISTED', @a, @r)
                """, ct, ("f", evt.FacilityId), ("d", evt.DeviceId), ("a", evt.AssessmentId), ("r", evt.Revision));
        }
        else
        {
            // Nothing inserted: either this exact event was stored before (a redelivery, absorbed by design),
            // or another event already holds this (assessment_id, revision) — a client bug, not a duplicate.
            await using var check = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM clinical.wound_assessment WHERE event_id = @e)", conn, tx);
            check.Parameters.AddWithValue("e", evt.EventId);
            var sameEvent = (bool)(await check.ExecuteScalarAsync(ct))!;

            outcome = sameEvent ? PersistOutcome.Deduplicated : PersistOutcome.RevisionConflict;
            await ProvenanceAsync(conn, tx, evt, "DEDUPLICATED", sameEvent ? "OK" : "REVISION_CONFLICT", source, traceId, ct);
        }

        await tx.CommitAsync(ct);
        return outcome;
    }

    private static Task ProvenanceAsync(NpgsqlConnection conn, NpgsqlTransaction tx, WoundEvent evt, string stage,
        string outcome, TopicPartitionOffset source, string? traceId, CancellationToken ct) =>
        ExecAsync(conn, tx, """
            INSERT INTO audit.provenance (event_id, stage, outcome, device_id, kafka_ref, trace_id)
            VALUES (@e, @s, @o, @d, @k, @t)
            """, ct, ("e", evt.EventId), ("s", stage), ("o", outcome), ("d", evt.DeviceId),
            ("k", source.KafkaRef()), ("t", (object?)traceId ?? DBNull.Value));

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
