import 'dart:math';

/// Retry delay (architecture §6.2): start at 2 s, double up to 5 min, full jitter, reset on success. A Retry-After from
/// the server (429, 503) is honoured when it asks for longer.
class Backoff {
  Backoff([Random? random]) : _random = random ?? Random();

  static const initial = Duration(seconds: 2);
  static const max = Duration(minutes: 5);

  final Random _random;
  int _failures = 0;

  int get failures => _failures;

  void reset() => _failures = 0;

  /// Records a failure and returns how long to wait before the next try.
  Duration next({Duration? retryAfter}) {
    final ceiling = Duration(microseconds: min(max.inMicroseconds, initial.inMicroseconds * (1 << min(_failures, 20))));
    _failures++;
    final jittered = Duration(microseconds: (_random.nextDouble() * ceiling.inMicroseconds).round());
    return retryAfter != null && retryAfter > jittered ? retryAfter : jittered;
  }
}
