@Tags(['integration'])
library;

import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:melanin_wound_cdss/features/sync/api/sync_api.dart';
import 'package:melanin_wound_cdss/features/sync/api/sync_models.dart';
import 'package:melanin_wound_cdss/features/sync/auth/auth_session.dart';
import 'package:melanin_wound_cdss/features/sync/data/app_database.dart';
import 'package:melanin_wound_cdss/features/sync/data/figure_repository.dart';
import 'package:melanin_wound_cdss/features/sync/data/queue_repository.dart';
import 'package:melanin_wound_cdss/features/sync/data/tables.dart';
import 'package:melanin_wound_cdss/features/sync/engine/sync_engine.dart';

import '../sync/support/fakes.dart';

/// The real sync engine against the real backend (Docker stack, through the API gateway): login, push, the full
/// server pipeline, pull, advice stored on the device. Skipped unless SYNC_BACKEND_URL is set:
///
///   SYNC_BACKEND_URL=http://localhost:8080 flutter test --tags integration
void main() {
  final url = Platform.environment['SYNC_BACKEND_URL'];

  test('round trip against the real backend', () async {
    final dir = Directory.systemTemp.createTempSync('cdss-it');
    final secrets = MemorySecretStore();
    final db = AppDatabase.encrypted(File('${dir.path}/it.db'), await DatabaseKeyStore.getOrCreate(secrets));
    final queue = QueueRepository(db);
    final api = _SmallPagesApi(Uri.parse(url!));
    final auth = AuthSession(api, secrets);
    final engine = SyncEngine(queue: queue, api: api, auth: auth);

    final deviceId = await queue.deviceId();
    final clinician = await auth.signIn(username: 'n.silva', password: 'Demo-Pass-2026!', deviceId: deviceId);
    expect(clinician.facilityId, 'fac-001');

    // A brand-new phone: cursor 0, so the first sync pages through the facility's change history (hasMore) before
    // reaching this run's changes. Pages are kept tiny so paging always happens: housekeeping archives history every
    // device has read (§4), so how much history there is depends on when the test runs.
    expect(await queue.cursor(), 0);

    final first = sampleEvent(deviceId: deviceId);
    await queue.enqueue(first);
    await queue.enqueue(sampleEvent(deviceId: deviceId));
    await queue.enqueue(sampleEvent(
        deviceId: deviceId, assessmentId: first['assessmentId'] as String, woundId: first['woundId'] as String, revision: 2));

    final pushed = await engine.sync();
    expect(pushed.outcome, SyncOutcome.success, reason: '$pushed');
    expect(pushed.accepted, 3);
    expect(await queue.cursor(), greaterThan(0), reason: 'pulled the history');

    // Advice arrives through the orchestrator; pull until every record is final.
    for (var i = 0; i < 30 && (await queue.counts()).of(QueueStatus.complete) + (await queue.counts()).of(QueueStatus.superseded) < 3; i++) {
      await Future<void>.delayed(const Duration(seconds: 2));
      await engine.sync(force: true);
    }
    final rows = {for (final r in await queue.all()) '${r.assessmentId}/${r.revision}': r};
    expect(rows['${first['assessmentId']}/1']!.status, QueueStatus.superseded, reason: 'revision 2 got the advice');
    expect(rows['${first['assessmentId']}/2']!.status, QueueStatus.complete);
    expect((await queue.counts()).of(QueueStatus.complete), 2);
    expect(await queue.recommendationFor(first['assessmentId'] as String, 2), isNotNull);
    expect(api.pagesWithMore, greaterThan(0), reason: 'a pull needed more than one page and followed hasMore');

    // A guideline figure through the gateway's figures proxy (§10.4; the stub serves F1-F3), cached with its licence.
    final figures = FigureRepository(db, api, auth);
    final figure = await figures.figureFor('iwgdf-2023.r1', 'F1');
    expect(figure.isFound, isTrue, reason: '${figure.miss}');
    expect(figure.figure!.contentType, 'image/png');
    expect(figure.figure!.licence, isNotEmpty);
    expect((await figures.figureFor('iwgdf-2023.r1', 'F404')).miss, FigureMiss.notFound);

    await auth.signOut();
    expect(await auth.hasSession(), isFalse);
    expect((await figures.figureFor('iwgdf-2023.r1', 'F1')).isFound, isTrue, reason: 'cached: no session needed');
    await db.close();
    dir.deleteSync(recursive: true);
  }, skip: url == null ? 'set SYNC_BACKEND_URL (e.g. http://localhost:8080) with the Docker stack running' : false,
     timeout: const Timeout(Duration(minutes: 3)));
}

/// The real gateway client with a tiny page size, counting pages that said hasMore, so the paging path is exercised
/// however much change history the server holds.
class _SmallPagesApi extends HttpSyncApi {
  _SmallPagesApi(super.baseUri);

  int pagesWithMore = 0;

  @override
  Future<PullPage> pull(String accessToken, int cursor, {int limit = 2}) async {
    final page = await super.pull(accessToken, cursor, limit: 2);
    if (page.hasMore) pagesWithMore++;
    return page;
  }
}
