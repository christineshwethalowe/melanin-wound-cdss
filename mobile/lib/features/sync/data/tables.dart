import 'package:drift/drift.dart';

/// Record lifecycle on the device (architecture §6.1):
/// pending → inFlight → accepted (or rejected) → adviceDeferred → complete.
/// "accepted" means durable in Kafka; "complete" means advice has been delivered.
enum QueueStatus { pending, inFlight, accepted, rejected, adviceDeferred, complete }

/// The offline queue. One row per event; each revision is a separate row.
/// Every write commits before the UI shows "saved".
@DataClassName('QueuedEvent')
@TableIndex(name: 'ix_queue_status_next', columns: {#status, #nextAttemptAt})
class WoundEventQueue extends Table {
  @override
  String get tableName => 'wound_event_queue';

  TextColumn get eventId => text()();
  TextColumn get assessmentId => text()();
  IntColumn get revision => integer()();
  TextColumn get payloadJson => text()();
  TextColumn get status => textEnum<QueueStatus>()();
  IntColumn get attemptCount => integer().withDefault(const Constant(0))();
  DateTimeColumn get nextAttemptAt => dateTime().nullable()();
  DateTimeColumn get leaseExpiresAt => dateTime().nullable()();
  TextColumn get lastError => text().nullable()();
  DateTimeColumn get createdAt => dateTime()();
  DateTimeColumn get ackedAt => dateTime().nullable()();

  @override
  Set<Column> get primaryKey => {eventId};
}

/// Read model so clinicians can review history offline. Latest revision per assessment.
class AssessmentLocal extends Table {
  TextColumn get assessmentId => text()();
  IntColumn get latestRevision => integer()();
  TextColumn get summaryJson => text()();

  @override
  Set<Column> get primaryKey => {assessmentId};
}

/// Advice received from the server.
class RecommendationLocal extends Table {
  TextColumn get assessmentId => text()();
  IntColumn get revision => integer()();
  TextColumn get payloadJson => text()();
  TextColumn get mode => text()();
  DateTimeColumn get receivedAt => dateTime()();

  @override
  Set<Column> get primaryKey => {assessmentId, revision};
}

/// Cache of delivered guideline figures so they survive an app restart.
class FiguresLocal extends Table {
  TextColumn get figureId => text()();
  TextColumn get corpusVersion => text()();
  BlobColumn get bytes => blob()();
  TextColumn get licence => text()();
  DateTimeColumn get cachedAt => dateTime()();

  @override
  Set<Column> get primaryKey => {figureId};
}

/// Small key/value table: server_cursor, last_success_at, device_id.
class SyncState extends Table {
  TextColumn get key => text()();
  TextColumn get value => text()();

  @override
  Set<Column> get primaryKey => {key};
}

/// Cached clinician session state. Never the raw refresh token (that lives in the keystore).
class AuthLocal extends Table {
  TextColumn get clinicianId => text()();
  TextColumn get displayName => text()();
  TextColumn get role => text()();
  DateTimeColumn get accessTokenExpiresAt => dateTime()();

  @override
  Set<Column> get primaryKey => {clinicianId};
}
