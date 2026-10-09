import 'dart:io';

import 'package:drift/drift.dart';
import 'package:drift/native.dart';
import 'package:sqlite3/sqlite3.dart' as sqlite;

import 'tables.dart';

part 'app_database.g.dart';

/// Keys used in the sync_state table.
abstract final class SyncStateKeys {
  static const serverCursor = 'server_cursor';
  static const lastSuccessAt = 'last_success_at';
  static const deviceId = 'device_id';
  static const nextAttemptAt = 'next_attempt_at';

  /// Single flight across isolates (§6.2): the time until which a sync run holds the lease.
  static const syncLeaseUntil = 'sync_lease_until';
}

/// The device database (§6), encrypted at rest with SQLCipher (§12): the key is 32 random bytes held in the platform
/// keystore (see DatabaseKeyStore), never on disk next to the file.
@DriftDatabase(tables: [
  WoundEventQueue,
  AssessmentLocal,
  RecommendationLocal,
  FiguresLocal,
  SyncState,
  AuthLocal,
])
class AppDatabase extends _$AppDatabase {
  AppDatabase(super.executor);

  /// Opens (or creates) the encrypted database file. All database work runs on a background isolate
  /// (createInBackground), so queries never block the UI thread (§6.2: the 200 ms frame budget).
  factory AppDatabase.encrypted(File file, String hexKey) => AppDatabase(
        NativeDatabase.createInBackground(file, setup: (raw) => applyKey(raw, hexKey)),
      );

  /// For tests: an in-memory database (not encrypted).
  factory AppDatabase.inMemory() => AppDatabase(NativeDatabase.memory());

  /// A raw 256-bit key (no passphrase derivation), then a read to fail fast on a wrong key.
  static void applyKey(sqlite.Database raw, String hexKey) {
    if (!RegExp(r'^[0-9a-f]{64}$').hasMatch(hexKey)) {
      throw ArgumentError('The database key must be 64 lower-case hex characters (32 bytes).');
    }
    raw.execute("PRAGMA key = \"x'$hexKey'\"");
    raw.execute('SELECT count(*) FROM sqlite_master');
  }

  @override
  int get schemaVersion => 1;
}
