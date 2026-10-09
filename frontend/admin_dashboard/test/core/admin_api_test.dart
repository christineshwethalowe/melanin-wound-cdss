import 'dart:convert';

import 'package:admin_dashboard/core/admin_api.dart';
import 'package:flutter_test/flutter_test.dart';

import '../fake_backend.dart';

void main() {
  // Bodies as the identity service sends them (checked against a running stack).
  final clinician = {
    'clinicianId': '7c9e6679-7425-40de-944b-e07fc1f90ae7',
    'username': 'n.silva',
    'fullName': 'N. Silva',
    'role': 'nurse',
    'active': true,
    'locked': true,
    'mfaEnabled': false,
    'createdAt': '2026-10-01T08:30:00.123456Z',
  };
  final device = {
    'deviceId': 'dev-1',
    'registeredAt': '2026-10-01T08:30:00Z',
    'revokedAt': null,
    'activeSessions': 1,
    'lastSeenAt': '2026-10-07T21:00:00Z',
    'lastUsername': 'n.silva',
  };
  final audit = {
    'id': 42,
    'recordedAt': '2026-10-07T21:00:00Z',
    'action': 'LOGIN',
    'success': false,
    'username': 'n.silva',
    'reasonCode': 'INVALID_CREDENTIALS',
    'deviceId': 'dev-1',
    'actorUsername': null,
  };

  test('admin lists parse and send the bearer token', () async {
    final backend = FakeBackend(
      (r) async => switch (r.url.path) {
        '/v1/admin/clinicians' => json([clinician]),
        '/v1/admin/devices' => json([device]),
        _ => json([audit]),
      },
    );
    final api = backend.api();

    final c = (await api.clinicians('tok')).single;
    final d = (await api.devices('tok')).single;
    final a = (await api.authAudit('tok', limit: 250)).single;

    expect(c.username, 'n.silva');
    expect(c.locked, isTrue);
    expect(c.createdAt.isUtc, isTrue);
    expect(d.revoked, isFalse);
    expect(d.lastSeenAt, DateTime.utc(2026, 10, 7, 21));
    expect(a.reasonCode, 'INVALID_CREDENTIALS');
    expect(a.actorUsername, isNull);
    expect(backend.requests.every((r) => r.headers['Authorization'] == 'Bearer tok'), isTrue);
    expect(backend.requests.last.url.queryParameters['limit'], '250');
  });

  test('actions post to the username or device in the path, escaped', () async {
    final backend = FakeBackend((r) async => json({}, 204));
    final api = backend.api();

    await api.unlock('t', 'n.silva');
    await api.resetMfa('t', 'n.silva');
    await api.deactivate('t', 'n.silva');
    await api.revokeDevice('t', 'dev/1');

    expect(backend.requests.map((r) => r.url.path), [
      '/v1/admin/clinicians/n.silva/unlock',
      '/v1/admin/clinicians/n.silva/reset-mfa',
      '/v1/admin/clinicians/n.silva/deactivate',
      '/v1/admin/devices/dev%2F1/revoke',
    ]);
    expect(backend.requests.every((r) => r.method == 'POST' && r.headers['Authorization'] == 'Bearer t'), isTrue);
  });

  test('register sends the four fields and surfaces the server code', () async {
    final backend = FakeBackend((r) async => code('USERNAME_TAKEN', 409));

    await expectLater(
      backend.api().registerClinician(
        't',
        username: 'n.silva',
        password: 'long-enough-pw',
        fullName: 'N',
        role: 'nurse',
      ),
      throwsA(
        isA<ApiException>().having((e) => e.status, 'status', 409).having((e) => e.code, 'code', 'USERNAME_TAKEN'),
      ),
    );
    expect(jsonDecode(backend.requests.single.body), {
      'username': 'n.silva',
      'password': 'long-enough-pw',
      'fullName': 'N',
      'role': 'nurse',
    });
  });

  test('a base URL with a path prefix keeps it', () async {
    final backend = FakeBackend((r) async => json([]));
    await AdminApi(Uri.parse('http://host/api/'), client: backend.client).devices('t');
    expect(backend.requests.single.url.toString(), 'http://host/api/v1/admin/devices');
  });
}
