import 'dart:math';

import '../api/sync_api.dart';
import '../auth/auth_session.dart';
import '../data/app_database.dart';
import '../data/queue_repository.dart';
import 'backoff.dart';

enum SyncOutcome {
  /// Pushed what was pending and pulled every change.
  success,

  /// Another run holds the sync (this isolate or another one).
  alreadyRunning,

  /// Still inside a backoff window; not tried. [SyncReport.retryAt] says when.
  backingOff,

  /// The gateway is unreachable (or stopped answering mid-run); backing off.
  offline,

  /// The server asked to slow down (429/503); backing off, honouring Retry-After.
  serverBusy,

  /// No usable session: the app asks the clinician to sign in. The queue is untouched (§7.3).
  needsSignIn,
}

class SyncReport {
  SyncReport(this.outcome, {this.pushed = 0, this.accepted = 0, this.rejected = 0, this.changes = 0, this.retryAt, this.detail});

  final SyncOutcome outcome;
  final int pushed;
  final int accepted;
  final int rejected;
  final int changes;
  final DateTime? retryAt;
  final String? detail;

  @override
  String toString() => 'SyncReport($outcome, pushed $pushed, accepted $accepted, rejected $rejected, changes $changes'
      '${retryAt == null ? '' : ', retry at $retryAt'}${detail == null ? '' : ', $detail'})';
}

/// Why a run stopped early.
class _Stop implements Exception {
  _Stop(this.outcome, [this.retryAfter, this.detail]);

  final SyncOutcome outcome;
  final Duration? retryAfter;
  final String? detail;
}

/// The sync engine (architecture §6.2, §7). One call to [sync] is one sync run:
///
///   1. single flight: one run at a time in this isolate, and a database lease across isolates;
///   2. lapsed leases return to pending (a killed app is safe);
///   3. the access token is refreshed first; with no usable session the run pauses for sign-in, queue untouched;
///   4. if anything waits to be sent: probe /health (reachability, not just connectivity), then push the oldest
///      batch (≤ 50 events / 256 KB, gzip) until the queue is empty, applying per-event results;
///   5. pull every change after the cursor, cursor saved with the changes in one transaction.
///
/// Failures follow §7.1: 401 → refresh and retry once; 413 → split the batch; 429/503, timeout or no answer → rows
/// back to pending and back off (2 s doubling to 5 min, full jitter, Retry-After honoured). REJECTED rows are final.
/// Database work runs on Drift's background isolate; this class only waits on it and on the network.
class SyncEngine {
  SyncEngine({
    required QueueRepository queue,
    required SyncApi api,
    required AuthSession auth,
    DateTime Function()? clock,
    Random? random,
  })  : _queue = queue,
        _api = api,
        _auth = auth,
        _now = clock ?? DateTime.now,
        _backoff = Backoff(random);

  final QueueRepository _queue;
  final SyncApi _api;
  final AuthSession _auth;
  final DateTime Function() _now;
  final Backoff _backoff;

  /// Longer than any run, so a run killed mid-way frees the lease on its own.
  static const syncLease = Duration(minutes: 3);

  Future<SyncReport>? _running;

  /// Runs one sync, or joins the one already running in this isolate. [force] ignores the backoff window
  /// (pull-to-refresh, "sync now").
  Future<SyncReport> sync({bool force = false}) =>
      _running ??= _run(force).whenComplete(() => _running = null);

  Future<SyncReport> _run(bool force) async {
    final retryAt = DateTime.tryParse(await _queue.getState(SyncStateKeys.nextAttemptAt) ?? '');
    if (!force && retryAt != null && retryAt.isAfter(_now())) {
      return SyncReport(SyncOutcome.backingOff, retryAt: retryAt);
    }
    if (!await _queue.tryAcquireSyncLease(syncLease)) return SyncReport(SyncOutcome.alreadyRunning);

    var pushed = 0, accepted = 0, rejected = 0, changes = 0;
    try {
      await _queue.releaseExpiredLeases();
      var token = await _auth.validAccessToken();
      final deviceId = await _queue.deviceId();

      if (await _queue.hasWorkToSend()) {
        if (!await _api.health()) throw _Stop(SyncOutcome.offline, null, 'gateway unreachable');
        while (true) {
          final batch = await _queue.leaseBatch();
          if (batch.isEmpty) break;
          final result = await _push(batch, token, deviceId);
          token = result.token;
          pushed += result.pushed;
          accepted += result.accepted;
          rejected += result.rejected;
        }
      }

      changes = await _pullAll(token, (t) => token = t);

      _backoff.reset();
      await _queue.setState(SyncStateKeys.nextAttemptAt, '');
      await _queue.setState(SyncStateKeys.lastSuccessAt, _now().toIso8601String());
      return SyncReport(SyncOutcome.success, pushed: pushed, accepted: accepted, rejected: rejected, changes: changes);
    } on NeedsSignIn catch (e) {
      return SyncReport(SyncOutcome.needsSignIn,
          pushed: pushed, accepted: accepted, rejected: rejected, changes: changes, detail: e.reason);
    } on _Stop catch (stop) {
      if (stop.outcome == SyncOutcome.needsSignIn) {
        return SyncReport(SyncOutcome.needsSignIn,
            pushed: pushed, accepted: accepted, rejected: rejected, changes: changes, detail: stop.detail);
      }
      final at = _now().add(_backoff.next(retryAfter: stop.retryAfter));
      await _queue.setState(SyncStateKeys.nextAttemptAt, at.toIso8601String());
      return SyncReport(stop.outcome,
          pushed: pushed, accepted: accepted, rejected: rejected, changes: changes, retryAt: at, detail: stop.detail);
    } on TransportException catch (e) {
      final at = _now().add(_backoff.next());
      await _queue.setState(SyncStateKeys.nextAttemptAt, at.toIso8601String());
      return SyncReport(SyncOutcome.offline,
          pushed: pushed, accepted: accepted, rejected: rejected, changes: changes, retryAt: at, detail: e.message);
    } finally {
      await _queue.releaseSyncLease();
    }
  }

  /// Pushes one leased batch. Every path leaves each row accepted, rejected or back to pending.
  Future<({String token, int pushed, int accepted, int rejected})> _push(
      List<QueuedEvent> batch, String token, String deviceId,
      {bool refreshed = false}) async {
    final ids = [for (final r in batch) r.eventId];
    PushResponse response;
    try {
      response = await _api.push(token, deviceId, [for (final r in batch) r.payloadJson]);
    } on TransportException catch (e) {
      // No answer: the server may or may not have the events. Resending is safe (DUPLICATE).
      await _queue.release(ids, 'no response: ${e.message}');
      throw _Stop(SyncOutcome.offline, null, e.message);
    }

    switch (response.status) {
      case 200:
        await _queue.applyPushResults(response.results);
        final answered = {for (final r in response.results) r.eventId};
        final missing = ids.where((id) => !answered.contains(id)).toList();
        if (missing.isNotEmpty) await _queue.release(missing, 'no result in the batch answer');
        return (
          token: token,
          pushed: ids.length,
          accepted: response.results.where((r) => r.status != 'REJECTED').length,
          rejected: response.results.where((r) => r.status == 'REJECTED').length,
        );
      case 401 when !refreshed:
        // §7.1: refresh the token, retry the batch (still leased).
        final String fresh;
        try {
          fresh = await _auth.refresh();
        } on NeedsSignIn {
          await _queue.release(ids, 'session ended');
          rethrow;
        }
        return _push(batch, fresh, deviceId, refreshed: true);
      case 413 when batch.length > 1:
        // §7.1: split the batch. The second half goes back to pending and follows in the next batch.
        final half = batch.length ~/ 2;
        await _queue.release(ids.sublist(half), 'split after 413');
        return _push(batch.sublist(0, half), token, deviceId, refreshed: refreshed);
      case 413:
        await _queue.reject(ids, 'PAYLOAD_TOO_LARGE');
        return (token: token, pushed: 1, accepted: 0, rejected: 1);
      case 403:
        // DEVICE_MISMATCH: the session belongs to another device id; a new sign-in binds this one.
        await _queue.release(ids, 'HTTP 403 ${response.code ?? ''}');
        throw _Stop(SyncOutcome.needsSignIn, null, response.code ?? 'FORBIDDEN');
      case 401:
        await _queue.release(ids, 'HTTP 401 after refresh');
        throw _Stop(SyncOutcome.needsSignIn, null, 'unauthorised after refresh');
      case 429:
      case 503:
        await _queue.release(ids, 'HTTP ${response.status}');
        throw _Stop(SyncOutcome.serverBusy, response.retryAfter, 'HTTP ${response.status}');
      default:
        await _queue.release(ids, 'HTTP ${response.status}');
        throw _Stop(SyncOutcome.offline, response.retryAfter, 'HTTP ${response.status}');
    }
  }

  /// Pulls until hasMore is false; each page and its cursor are stored together.
  Future<int> _pullAll(String token, void Function(String) onToken) async {
    var count = 0;
    var refreshed = false;
    while (true) {
      try {
        final page = await _api.pull(token, await _queue.cursor());
        await _queue.applyPullPage(page);
        count += page.changes.length;
        if (!page.hasMore) return count;
      } on ApiException catch (e) {
        if (e.status == 401 && !refreshed) {
          token = await _auth.refresh();
          onToken(token);
          refreshed = true;
          continue;
        }
        if (e.status == 401) throw _Stop(SyncOutcome.needsSignIn, null, 'unauthorised after refresh');
        throw _Stop(e.status == 429 || e.status == 503 ? SyncOutcome.serverBusy : SyncOutcome.offline,
            e.retryAfter, 'pull HTTP ${e.status}');
      }
    }
  }
}
