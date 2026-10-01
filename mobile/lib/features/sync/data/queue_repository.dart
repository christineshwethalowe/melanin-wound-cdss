import 'dart:convert';

import 'package:drift/drift.dart';
import 'package:uuid/uuid.dart';

import '../api/sync_models.dart';
import '../domain/wound_event_validator.dart';
import 'app_database.dart';
import 'tables.dart';

/// Thrown by [QueueRepository.enqueue] when the event breaks the contract; nothing was stored.
class EnqueueRejected implements Exception {
  EnqueueRejected(this.errors);

  final List<String> errors;

  @override
  String toString() => 'EnqueueRejected: ${errors.join('; ')}';
}

/// Counts per status, for the sync screen.
class QueueCounts {
  const QueueCounts(this.byStatus);

  final Map<QueueStatus, int> byStatus;

  int of(QueueStatus s) => byStatus[s] ?? 0;
  int get waitingToSend => of(QueueStatus.pending) + of(QueueStatus.inFlight);
  int get total => byStatus.values.fold(0, (a, b) => a + b);
}

/// The only way an assessment leaves the device (architecture §6). Members 1–3 call [enqueue]; nothing else writes
/// wound_event_queue. Every method is one transaction, so a killed app never leaves a half-applied change.
class QueueRepository {
  QueueRepository(this._db, {DateTime Function()? clock}) : _now = clock ?? DateTime.now;

  final AppDatabase _db;
  final DateTime Function() _now;

  /// §6.2: rows sent are leased for about two minutes; a lapsed lease returns them to pending.
  static const lease = Duration(minutes: 2);

  /// §6.2: up to 50 events or 256 KB per request.
  static const maxBatchEvents = 50;
  static const maxBatchBytes = 256 * 1024;

  // ---- writing the queue ------------------------------------------------------------------------------------------

  /// Validates the event (§5) and stores it as pending. Commits before returning, so the UI may say "saved".
  Future<QueuedEvent> enqueue(Map<String, dynamic> event) async {
    final errors = WoundEventValidator.validate(event);
    if (errors.isNotEmpty) throw EnqueueRejected(errors);

    final json = jsonEncode(event);
    final row = WoundEventQueueCompanion.insert(
      eventId: event['eventId'] as String,
      assessmentId: event['assessmentId'] as String,
      revision: event['revision'] as int,
      payloadJson: json,
      sizeBytes: utf8.encode(json).length,
      status: QueueStatus.pending,
      createdAt: _now(),
    );
    return _db.transaction(() async {
      await _db.into(_db.woundEventQueue).insert(row);
      await _upsertAssessment(event);
      return (_db.select(_db.woundEventQueue)..where((q) => q.eventId.equals(event['eventId'] as String))).getSingle();
    });
  }

  /// The local read model (§6): latest revision per assessment, for reviewing history offline.
  Future<void> _upsertAssessment(Map<String, dynamic> event) async {
    final assessmentId = event['assessmentId'] as String;
    final revision = event['revision'] as int;
    final existing = await (_db.select(_db.assessmentLocal)..where((a) => a.assessmentId.equals(assessmentId)))
        .getSingleOrNull();
    if (existing != null && existing.latestRevision >= revision) return;
    final analytics = event['analytics'] as Map<String, dynamic>;
    final summary = jsonEncode({
      'woundId': event['woundId'],
      'patientRef': event['patientRef'],
      'capturedAt': event['capturedAt'],
      'areaMm2': analytics['areaMm2'],
      'clinicalAssessment': event['clinicalAssessment'],
    });
    await _db.into(_db.assessmentLocal).insertOnConflictUpdate(
        AssessmentLocalCompanion.insert(assessmentId: assessmentId, latestRevision: revision, summaryJson: summary));
  }

  /// On app start and before every sync run: rows whose lease lapsed (the app was killed mid-sync) go back to
  /// pending. Safe because every event is idempotent.
  Future<int> releaseExpiredLeases() => (_db.update(_db.woundEventQueue)
        ..where((q) => q.status.equalsValue(QueueStatus.inFlight) & q.leaseExpiresAt.isSmallerOrEqualValue(_now())))
      .write(const WoundEventQueueCompanion(status: Value(QueueStatus.pending), leaseExpiresAt: Value(null)));

  /// Takes the next batch, oldest first, within 50 events / 256 KB (at least one event), and leases it.
  Future<List<QueuedEvent>> leaseBatch({int maxEvents = maxBatchEvents}) => _db.transaction(() async {
        final now = _now();
        final candidates = await (_db.select(_db.woundEventQueue)
              ..where((q) =>
                  q.status.equalsValue(QueueStatus.pending) |
                  (q.status.equalsValue(QueueStatus.inFlight) & q.leaseExpiresAt.isSmallerOrEqualValue(now)))
              ..orderBy([(q) => OrderingTerm.asc(q.createdAt), (q) => OrderingTerm.asc(q.eventId)])
              ..limit(maxEvents))
            .get();

        final batch = <QueuedEvent>[];
        var bytes = 0;
        for (final row in candidates) {
          if (batch.isNotEmpty && bytes + row.sizeBytes > maxBatchBytes) break;
          batch.add(row);
          bytes += row.sizeBytes;
        }
        final leased = <QueuedEvent>[];
        for (final row in batch) {
          final updated = row.copyWith(
            status: QueueStatus.inFlight,
            leaseExpiresAt: Value(now.add(lease)),
            attemptCount: row.attemptCount + 1,
          );
          await _db.update(_db.woundEventQueue).replace(updated);
          leased.add(updated);
        }
        return leased;
      });

  /// A push that failed or got no answer: its rows go back to pending (§7.1: 429, 503, timeout, no response).
  Future<void> release(Iterable<String> eventIds, String error) => (_db.update(_db.woundEventQueue)
        ..where((q) => q.eventId.isIn(eventIds) & q.status.equalsValue(QueueStatus.inFlight)))
      .write(WoundEventQueueCompanion(
          status: const Value(QueueStatus.pending), leaseExpiresAt: const Value(null), lastError: Value(error)));

  /// Applies the per-event results of a push (§7.1). DUPLICATE is ACCEPTED; REJECTED is final.
  Future<void> applyPushResults(List<PushEventResult> results) => _db.transaction(() async {
        final now = _now();
        for (final r in results) {
          if (r.status == 'ACCEPTED' || r.status == 'DUPLICATE') {
            // Never move a record backwards: a pulled change may already have taken it further.
            await (_db.update(_db.woundEventQueue)
                  ..where((q) =>
                      q.eventId.equals(r.eventId) &
                      q.status.isInValues([QueueStatus.inFlight, QueueStatus.pending])))
                .write(WoundEventQueueCompanion(
                    status: const Value(QueueStatus.accepted), leaseExpiresAt: const Value(null), ackedAt: Value(now)));
          } else {
            await reject([r.eventId], r.code ?? 'REJECTED');
          }
        }
      });

  /// Marks events rejected: shown to the clinician, never retried automatically (§6.1).
  Future<void> reject(Iterable<String> eventIds, String code) => (_db.update(_db.woundEventQueue)
        ..where((q) => q.eventId.isIn(eventIds)))
      .write(WoundEventQueueCompanion(
          status: const Value(QueueStatus.rejected), leaseExpiresAt: const Value(null), lastError: Value(code)));

  /// Applies one pull page and stores the new cursor in the same transaction (§7.2), so a crash can never record
  /// a cursor without the changes behind it.
  Future<void> applyPullPage(PullPage page) => _db.transaction(() async {
        final now = _now();
        for (final c in page.changes) {
          final where = (_db.update(_db.woundEventQueue)
            ..where((q) => q.assessmentId.equals(c.assessmentId) & q.revision.equals(c.revision)));
          switch (c.type) {
            case 'PERSISTED':
              // Stored on the server: proof the push landed even if its response was lost.
              await (_db.update(_db.woundEventQueue)
                    ..where((q) =>
                        q.assessmentId.equals(c.assessmentId) &
                        q.revision.equals(c.revision) &
                        q.status.isInValues([QueueStatus.pending, QueueStatus.inFlight])))
                  .write(WoundEventQueueCompanion(
                      status: const Value(QueueStatus.accepted),
                      leaseExpiresAt: const Value(null),
                      ackedAt: Value(now)));
            case 'ADVICE_DEFERRED':
              await (_db.update(_db.woundEventQueue)
                    ..where((q) =>
                        q.assessmentId.equals(c.assessmentId) &
                        q.revision.equals(c.revision) &
                        q.status.isInValues([QueueStatus.pending, QueueStatus.inFlight, QueueStatus.accepted])))
                  .write(WoundEventQueueCompanion(
                      status: const Value(QueueStatus.adviceDeferred),
                      leaseExpiresAt: const Value(null),
                      ackedAt: Value(now)));
            case 'RECOMMENDATION_READY':
              // Facility-scoped: advice for another phone's assessment is kept too, for the shared ward view.
              if (c.recommendation != null) {
                await _db.into(_db.recommendationLocal).insertOnConflictUpdate(RecommendationLocalCompanion.insert(
                    assessmentId: c.assessmentId,
                    revision: c.revision,
                    payloadJson: jsonEncode(c.recommendation),
                    mode: c.mode ?? (c.recommendation!['mode'] as String? ?? 'unknown'),
                    receivedAt: now));
              }
              await where.write(WoundEventQueueCompanion(
                  status: const Value(QueueStatus.complete),
                  leaseExpiresAt: const Value(null),
                  completedAt: Value(now)));
            case 'SUPERSEDED':
              await (_db.update(_db.woundEventQueue)
                    ..where((q) =>
                        q.assessmentId.equals(c.assessmentId) &
                        q.revision.equals(c.revision) &
                        q.status.equalsValue(QueueStatus.complete).not()))
                  .write(WoundEventQueueCompanion(
                      status: const Value(QueueStatus.superseded),
                      leaseExpiresAt: const Value(null),
                      completedAt: Value(now)));
          }
        }
        await setState(SyncStateKeys.serverCursor, '${page.nextCursor}');
      });

  // ---- reading -----------------------------------------------------------------------------------------------------

  Future<bool> hasWorkToSend() async {
    final now = _now();
    final row = await (_db.select(_db.woundEventQueue)
          ..where((q) =>
              q.status.equalsValue(QueueStatus.pending) |
              (q.status.equalsValue(QueueStatus.inFlight) & q.leaseExpiresAt.isSmallerOrEqualValue(now)))
          ..limit(1))
        .getSingleOrNull();
    return row != null;
  }

  Future<QueueCounts> counts() async => QueueCounts(await _countRows().get().then(_toMap));

  Stream<QueueCounts> watchCounts() => _countRows().watch().map((rows) => QueueCounts(_toMap(rows)));

  Selectable<TypedResult> _countRows() {
    final count = _db.woundEventQueue.eventId.count();
    return (_db.selectOnly(_db.woundEventQueue)
          ..addColumns([_db.woundEventQueue.status, count])
          ..groupBy([_db.woundEventQueue.status]));
  }

  Map<QueueStatus, int> _toMap(List<TypedResult> rows) => {
        for (final r in rows)
          QueueStatus.values.byName(r.read(_db.woundEventQueue.status)!): r.read(_db.woundEventQueue.eventId.count())!,
      };

  Future<List<QueuedEvent>> all() =>
      (_db.select(_db.woundEventQueue)..orderBy([(q) => OrderingTerm.asc(q.createdAt)])).get();

  Future<RecommendationLocalData?> recommendationFor(String assessmentId, int revision) =>
      (_db.select(_db.recommendationLocal)
            ..where((r) => r.assessmentId.equals(assessmentId) & r.revision.equals(revision)))
          .getSingleOrNull();

  // ---- sync_state --------------------------------------------------------------------------------------------------

  Future<int> cursor() async => int.tryParse(await getState(SyncStateKeys.serverCursor) ?? '') ?? 0;

  /// This phone's id, created on first use and kept for the life of the install (§7.1: the gateway binds tokens to it).
  Future<String> deviceId() async {
    final existing = await getState(SyncStateKeys.deviceId);
    if (existing != null) return existing;
    final id = 'dev-${const Uuid().v4()}';
    await setState(SyncStateKeys.deviceId, id);
    return id;
  }

  Future<String?> getState(String key) async =>
      (await (_db.select(_db.syncState)..where((s) => s.key.equals(key))).getSingleOrNull())?.value;

  Future<void> setState(String key, String value) =>
      _db.into(_db.syncState).insertOnConflictUpdate(SyncStateCompanion.insert(key: key, value: value));

  /// Single flight across isolates (§6.2): takes the database lease unless another run holds an unexpired one.
  Future<bool> tryAcquireSyncLease(Duration duration) => _db.transaction(() async {
        final now = _now();
        final until = DateTime.tryParse(await getState(SyncStateKeys.syncLeaseUntil) ?? '');
        if (until != null && until.isAfter(now)) return false;
        await setState(SyncStateKeys.syncLeaseUntil, now.add(duration).toIso8601String());
        return true;
      });

  Future<void> releaseSyncLease() => setState(SyncStateKeys.syncLeaseUntil, '');
}
