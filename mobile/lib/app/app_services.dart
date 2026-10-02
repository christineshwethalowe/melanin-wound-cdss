import 'dart:io';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

import '../features/sync/api/sync_api.dart';
import '../features/sync/auth/auth_session.dart';
import '../features/sync/data/app_database.dart';
import '../features/sync/data/figure_repository.dart';
import '../features/sync/data/queue_repository.dart';
import '../features/sync/engine/sync_engine.dart';
import '../features/sync/engine/sync_scheduler.dart';

/// Where the app reaches the backend: the API gateway (ADR 0004). Set at build time:
///   flutter run --dart-define=SYNC_BASE_URL=http://192.168.1.20:8080
/// The default is the Android emulator's address for the host machine.
const syncBaseUrl = String.fromEnvironment('SYNC_BASE_URL', defaultValue: 'http://10.0.2.2:8080');

/// Everything the sync needs, wired once at start-up. Other features use [queue] to save assessments and call
/// [scheduler].onSaved() afterwards, and [figures] to show the guideline figures advice cites
/// (docs/integration/mobile-sync-layer.md).
class AppServices {
  AppServices({required this.db, required this.queue, required this.figures, required this.api, required this.auth,
      required this.engine, required this.scheduler});

  final AppDatabase db;
  final QueueRepository queue;
  final FigureRepository figures;
  final SyncApi api;
  final AuthSession auth;
  final SyncEngine engine;
  final SyncScheduler scheduler;

  /// On the phone: the encrypted database in app support storage, the key and refresh token in the Keystore,
  /// connectivity from the platform. Tests pass in-memory replacements.
  static Future<AppServices> open({
    AppDatabase? db,
    SecretStore? secrets,
    SyncApi? api,
    Stream<bool>? online,
    DateTime Function()? clock,
  }) async {
    final store = secrets ?? const KeystoreSecretStore();
    final database = db ??
        AppDatabase.encrypted(
          File(p.join((await getApplicationSupportDirectory()).path, 'wound_queue.db')),
          await DatabaseKeyStore.getOrCreate(store),
        );
    final queue = QueueRepository(database, clock: clock);
    // A killed app leaves rows leased: they go back to pending at start (§6.2).
    await queue.releaseExpiredLeases();

    final client = api ?? HttpSyncApi(Uri.parse(syncBaseUrl));
    final auth = AuthSession(client, store, clock: clock);
    final figures = FigureRepository(database, client, auth, clock: clock);
    final engine = SyncEngine(queue: queue, api: client, auth: auth, figures: figures, clock: clock);
    final scheduler = SyncScheduler(engine.sync,
        online: online ??
            Connectivity().onConnectivityChanged.map((r) => r.any((c) => c != ConnectivityResult.none)),
        clock: clock);
    return AppServices(
        db: database, queue: queue, figures: figures, api: client, auth: auth, engine: engine, scheduler: scheduler);
  }

  /// Signs in with this phone's device id and caches who is signed in for offline display (auth_local, §6: never
  /// the refresh token).
  Future<Clinician> signIn({required String username, required String password, String? totp}) async {
    final clinician =
        await auth.signIn(username: username, password: password, deviceId: await queue.deviceId(), totp: totp);
    await db.delete(db.authLocal).go();
    await db.into(db.authLocal).insert(AuthLocalCompanion.insert(
          clinicianId: clinician.id,
          username: clinician.username,
          role: clinician.role,
          facilityId: clinician.facilityId,
          accessTokenExpiresAt: DateTime.now().add(const Duration(minutes: 15)),
        ));
    return clinician;
  }

  /// Who was signed in last, available offline.
  Future<AuthLocalData?> cachedClinician() => db.select(db.authLocal).getSingleOrNull();

  Future<void> signOut() async {
    await auth.signOut();
    await db.delete(db.authLocal).go();
  }

  Future<void> close() async {
    scheduler.dispose();
    await db.close();
  }
}
