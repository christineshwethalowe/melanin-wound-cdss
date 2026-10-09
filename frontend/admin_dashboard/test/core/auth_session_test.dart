import 'dart:async';
import 'dart:convert';

import 'package:admin_dashboard/core/admin_api.dart';
import 'package:admin_dashboard/core/auth_session.dart';
import 'package:admin_dashboard/core/token_store.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

import '../fake_backend.dart';

void main() {
  test('sign-in sends the dashboard client, no device, and reads the admin from the token', () async {
    final backend = FakeBackend((r) async => http.Response(tokens(1), 200));
    final store = MemoryTokenStore();
    final session = backend.session(store: store);
    var notified = 0;
    session.addListener(() => notified++);

    final admin = await session.signIn(username: 'admin.demo', password: 'pw');

    final body = jsonDecode(backend.requests.single.body) as Map<String, dynamic>;
    expect(body['clientId'], 'admin-dashboard');
    expect(body.containsKey('deviceId'), isFalse);
    expect(body.containsKey('totp'), isFalse);
    expect(admin.username, 'admin.demo');
    expect(admin.facilityId, 'fac-001');
    expect(admin.role, 'admin');
    expect(store.values.values, contains('refresh-1'));
    expect(notified, 1);
  });

  test('MFA_REQUIRED reaches the caller with its code, and the code is sent on the second try', () async {
    final backend = FakeBackend((r) async {
      final body = jsonDecode(r.body) as Map<String, dynamic>;
      return body['totp'] == '123456' ? http.Response(tokens(1), 200) : code('MFA_REQUIRED', 401);
    });
    final session = backend.session();

    await expectLater(
      session.signIn(username: 'a', password: 'p'),
      throwsA(isA<ApiException>().having((e) => e.code, 'code', 'MFA_REQUIRED')),
    );
    expect(session.signedIn, isFalse);

    await session.signIn(username: 'a', password: 'p', totp: '123456');
    expect(session.signedIn, isTrue);
  });

  test('a refused access token is refreshed once and the call retried', () async {
    var refreshed = 0;
    final backend = FakeBackend(
      (r) async => switch (r.url.path) {
        '/v1/auth/login' => http.Response(tokens(1), 200),
        '/v1/auth/refresh' => http.Response(tokens(2 + refreshed++), 200),
        _ => refreshed == 0 ? code('INVALID_TOKEN', 401) : json([]),
      },
    );
    final session = backend.session();
    await session.signIn(username: 'a', password: 'p');

    final list = await session.authorized((t) => session.api.clinicians(t));

    expect(list, isEmpty);
    expect(backend.count('/v1/auth/refresh'), 1);
    expect(backend.count('/v1/admin/clinicians'), 2);
  });

  test('concurrent callers share one refresh, so the rotated token is never reused', () async {
    final release = Completer<void>();
    final backend = FakeBackend((r) async {
      if (r.url.path == '/v1/auth/refresh') {
        await release.future;
        return http.Response(tokens(2), 200);
      }
      return json([]);
    });
    final store = MemoryTokenStore()..write('cdss_admin_refresh_v1', 'refresh-1');
    final session = backend.session(store: store);

    final calls = [for (var i = 0; i < 3; i++) session.authorized((t) => session.api.devices(t))];
    release.complete();
    await Future.wait(calls);

    expect(backend.count('/v1/auth/refresh'), 1);
    expect(backend.count('/v1/admin/devices'), 3);
  });

  test('a refresh the server refuses signs the admin out and tells listeners', () async {
    final backend = FakeBackend(
      (r) async => switch (r.url.path) {
        '/v1/auth/login' => http.Response(tokens(1, expiresIn: 30), 200), // inside the refresh margin
        _ => code('INVALID_REFRESH_TOKEN', 401),
      },
    );
    final store = MemoryTokenStore();
    final session = backend.session(store: store);
    await session.signIn(username: 'a', password: 'p');
    var notified = 0;
    session.addListener(() => notified++);

    await expectLater(session.authorized((t) => session.api.clinicians(t)), throwsA(isA<NeedsSignIn>()));

    expect(session.signedIn, isFalse);
    expect(notified, 1);
    expect(store.read('cdss_admin_refresh_v1'), isNull);
    expect(backend.count('/v1/admin/clinicians'), 0);
  });

  test('restore resumes a session from the stored refresh token, and is false without one', () async {
    final backend = FakeBackend((r) async => http.Response(tokens(5), 200));

    expect(await backend.session().restore(), isFalse);
    expect(backend.requests, isEmpty);

    final store = MemoryTokenStore()
      ..write('cdss_admin_refresh_v1', 'refresh-4')
      ..write('cdss_admin_username_v1', 'admin.demo');
    final session = backend.session(store: store);
    expect(await session.restore(), isTrue);
    expect(session.admin!.username, 'admin.demo');
    expect(store.read('cdss_admin_refresh_v1'), 'refresh-5');
  });

  test('sign-out revokes the refresh token server-side and clears it, even when offline', () async {
    final backend = FakeBackend(
      (r) async =>
          r.url.path == '/v1/auth/login' ? http.Response(tokens(1), 200) : throw http.ClientException('offline'),
    );
    final store = MemoryTokenStore();
    final session = backend.session(store: store);
    await session.signIn(username: 'a', password: 'p');

    await session.signOut();

    expect(jsonDecode(backend.requests.last.body), {'refreshToken': 'refresh-1'});
    expect(session.signedIn, isFalse);
    expect(store.read('cdss_admin_refresh_v1'), isNull);
  });

  test('no answer at all is a TransportException, not an ApiException', () async {
    final backend = FakeBackend((r) async => throw http.ClientException('connection refused'));
    await expectLater(backend.api().login(username: 'a', password: 'p'), throwsA(isA<TransportException>()));
  });
}
