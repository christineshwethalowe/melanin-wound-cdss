import 'dart:async';

import 'package:flutter/foundation.dart';

import 'sync_engine.dart';

/// What the sync screen shows.
@immutable
class SyncStatus {
  const SyncStatus({this.running = false, this.last, this.lastRunAt});

  final bool running;
  final SyncReport? last;
  final DateTime? lastRunAt;

  SyncStatus copyWith({bool? running, SyncReport? last, DateTime? lastRunAt}) =>
      SyncStatus(running: running ?? this.running, last: last ?? this.last, lastRunAt: lastRunAt ?? this.lastRunAt);
}

/// When to sync (architecture §6.2). The engine decides how; this decides when:
///
/// - connectivity regained, and the app coming back to the foreground;
/// - two seconds after a save (debounced, so a burst of saves is one run);
/// - pull-to-refresh / "sync now" (ignores the backoff window);
/// - every [periodic] while the app is open (Android limits background work to about every 15 minutes, so the
///   foreground triggers do most of the work, §6.2);
/// - when the engine backed off, at the time it said to retry.
///
/// Takes the sync function rather than the engine, so its timing can be tested on its own.
class SyncScheduler {
  SyncScheduler(this._sync, {Stream<bool>? online, this.periodic = const Duration(minutes: 5), DateTime Function()? clock})
      : _online = online,
        _now = clock ?? DateTime.now;

  static const saveDebounce = Duration(seconds: 2);

  final Future<SyncReport> Function({bool force}) _sync;
  final Stream<bool>? _online;
  final Duration periodic;
  final DateTime Function() _now;

  final ValueNotifier<SyncStatus> status = ValueNotifier(const SyncStatus());

  StreamSubscription<bool>? _onlineSub;
  Timer? _debounce;
  Timer? _periodic;
  Timer? _retry;
  bool _wasOnline = true;

  void start() {
    _onlineSub = _online?.listen((online) {
      if (online && !_wasOnline) _run('connectivity regained');
      _wasOnline = online;
    });
    _periodic = Timer.periodic(periodic, (_) => _run('periodic'));
    _run('start');
  }

  /// Called after every enqueue: one run two seconds after the last save.
  void onSaved() {
    _debounce?.cancel();
    _debounce = Timer(saveDebounce, () => _run('saved'));
  }

  void onForeground() => _run('foreground');

  /// Pull-to-refresh and the "sync now" button: runs now, even inside a backoff window.
  Future<SyncReport> syncNow() => _run('manual', force: true);

  Future<SyncReport> _run(String reason, {bool force = false}) async {
    status.value = status.value.copyWith(running: true);
    try {
      final report = await _sync(force: force);
      status.value = SyncStatus(running: false, last: report, lastRunAt: _now());
      _scheduleRetry(report);
      return report;
    } catch (e) {
      status.value = status.value.copyWith(running: false);
      rethrow;
    }
  }

  void _scheduleRetry(SyncReport report) {
    _retry?.cancel();
    final at = report.retryAt;
    if (at == null) return;
    final wait = at.difference(_now());
    _retry = Timer(wait.isNegative ? Duration.zero : wait, () => _run('retry after backoff'));
  }

  void dispose() {
    _onlineSub?.cancel();
    _debounce?.cancel();
    _periodic?.cancel();
    _retry?.cancel();
    status.dispose();
  }
}
