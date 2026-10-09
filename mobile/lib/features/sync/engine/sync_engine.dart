/// Sync engine rules (architecture §6.2). Runs in a background isolate so the UI never
/// freezes for more than 200 ms.
///
/// - Triggers: connectivity change, app foreground, 2 s after an enqueue, pull-to-refresh,
///   periodic background task.
/// - Probe GET /health before pushing: reachability, not just connectivity.
/// - Single flight: one sync run at a time (mutex + database lease).
/// - Backoff: 2 s doubling to 5 min, full jitter, reset on success; honour Retry-After.
/// - Refresh the access token first; if the refresh token expired, pause and ask to sign in.
class SyncEngine {
  // TODO(m4): implement push (§7.1) and pull (§7.2).
}
