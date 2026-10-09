import 'app_database.dart';

/// The only way an assessment leaves the device (architecture §6).
/// Members 1–3 call [enqueue]; nobody writes to wound_event_queue directly.
class QueueRepository {
  QueueRepository(this._db);

  // ignore: unused_field
  final AppDatabase _db;

  // TODO(m4): enqueue(WoundEvent): validate against contracts/wound-event.schema.json, then insert as pending.
  // TODO(m4): leaseBatch(): mark up to 50 events / 256 KB as inFlight with lease_expires_at ≈ now + 2 min.
  // TODO(m4): releaseExpiredLeases(): on app start, return lapsed inFlight rows to pending.
  // TODO(m4): applyResults(): ACCEPTED/DUPLICATE → accepted, REJECTED → rejected (never retried).
}
