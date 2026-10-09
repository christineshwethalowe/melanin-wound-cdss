/// Wire types of the sync protocol (architecture §7). Mirrors contracts/auth.schema.json and
/// contracts/sync-changes.schema.json.
library;

/// §7.1: one result per pushed event. DUPLICATE is treated exactly like ACCEPTED.
class PushEventResult {
  const PushEventResult(this.eventId, this.status, [this.code]);

  final String eventId;
  final String status; // ACCEPTED | DUPLICATE | REJECTED
  final String? code;

  factory PushEventResult.fromJson(Map<String, dynamic> j) =>
      PushEventResult(j['eventId'] as String, j['status'] as String, j['code'] as String?);
}

/// §7.2: one change after the device's cursor. Upserted by (assessmentId, revision, type), so re-sent changes
/// are harmless.
class SyncChange {
  const SyncChange({
    required this.seq,
    required this.type,
    required this.assessmentId,
    required this.revision,
    this.mode,
    this.recommendation,
  });

  final int seq;
  final String type; // PERSISTED | RECOMMENDATION_READY | ADVICE_DEFERRED | SUPERSEDED
  final String assessmentId;
  final int revision;
  final String? mode;
  final Map<String, dynamic>? recommendation;

  factory SyncChange.fromJson(Map<String, dynamic> j) => SyncChange(
        seq: j['seq'] as int,
        type: j['type'] as String,
        assessmentId: j['assessmentId'] as String,
        revision: j['revision'] as int,
        mode: j['mode'] as String?,
        recommendation: j['recommendation'] as Map<String, dynamic>?,
      );
}

class PullPage {
  const PullPage(this.changes, this.nextCursor, this.hasMore);

  final List<SyncChange> changes;
  final int nextCursor;
  final bool hasMore;

  factory PullPage.fromJson(Map<String, dynamic> j) => PullPage(
        (j['changes'] as List).map((c) => SyncChange.fromJson(c as Map<String, dynamic>)).toList(),
        j['nextCursor'] as int,
        j['hasMore'] as bool,
      );
}

/// §7.3: a 15-minute access JWT and a rotating opaque refresh token.
class TokenPair {
  const TokenPair(this.accessToken, this.refreshToken, this.expiresIn);

  final String accessToken;
  final String refreshToken;
  final Duration expiresIn;

  factory TokenPair.fromJson(Map<String, dynamic> j) =>
      TokenPair(j['accessToken'] as String, j['refreshToken'] as String, Duration(seconds: j['expiresIn'] as int));
}
