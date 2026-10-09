import 'package:drift/drift.dart';

import 'tables.dart';

part 'app_database.g.dart';

/// Encrypted with SQLCipher; the key is held in the Android Keystore (architecture §12).
/// Run `dart run build_runner build` to generate app_database.g.dart.
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

  @override
  int get schemaVersion => 1;
}
