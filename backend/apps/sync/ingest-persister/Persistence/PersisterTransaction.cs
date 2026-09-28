namespace IngestPersister.Persistence;

/// <summary>
/// One transaction per event (architecture §9.4):
///   1. advisory lock on assessment_id (Sync.Common.Persistence.AdvisoryLock)
///   2. upsert clinical.patient
///   3. insert clinical.wound_assessment ON CONFLICT (event_id) DO NOTHING
///   4a. inserted: provenance PERSISTED + outbox row (wound-events.persisted) + change_log PERSISTED
///   4b. duplicate: provenance DEDUPLICATED only
/// The caller commits the Kafka offset only after this returns.
/// </summary>
public sealed class PersisterTransaction
{
    public const string InsertAssessmentSql = """
        INSERT INTO clinical.wound_assessment
          (event_id, assessment_id, revision, wound_id, patient_ref, device_id,
           captured_at, analytics, clinical_assessment, kafka_topic, kafka_partition, kafka_offset)
        VALUES
          (@event_id, @assessment_id, @revision, @wound_id, @patient_ref, @device_id,
           @captured_at, @analytics::jsonb, @clinical_assessment::jsonb, @kafka_topic, @kafka_partition, @kafka_offset)
        ON CONFLICT (event_id) DO NOTHING
        """;

    // TODO(m4): implement ExecuteAsync with Npgsql + Dapper using the steps above.
}
