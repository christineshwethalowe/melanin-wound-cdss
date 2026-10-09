import 'dart:convert';

import 'package:admin_dashboard/core/toast.dart';
import 'package:admin_dashboard/main.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

import '../fake_backend.dart';

Map<String, Object?> clinician(String username, {bool locked = false, bool mfa = false, bool active = true}) => {
  'clinicianId': 'id-$username',
  'username': username,
  'fullName': 'Name $username',
  'role': username.startsWith('admin') ? 'admin' : 'nurse',
  'active': active,
  'locked': locked,
  'mfaEnabled': mfa,
  'createdAt': '2026-10-01T08:30:00Z',
};

/// A desktop-sized window: wide enough for the navigation rail and the full tables.
Future<void> pumpApp(WidgetTester tester, FakeBackend backend) async {
  tester.view
    ..physicalSize = const Size(1600, 1000)
    ..devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(AdminDashboardApp(session: backend.session()));
  await tester.pumpAndSettle();
}

/// Lets any toast run out its timer, so no timer is left pending when the test ends.
Future<void> settleToasts(WidgetTester tester) async {
  await tester.pump(const Duration(seconds: 8));
  Toast.dismiss();
}

Future<void> signIn(WidgetTester tester) async {
  await tester.enterText(find.byKey(const Key('username')), 'admin.demo');
  await tester.enterText(find.byKey(const Key('password')), 'Demo-Admin-2026!');
  await tester.tap(find.byKey(const Key('sign-in')));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('wrong password shows the reason and stays on the login screen', (tester) async {
    final backend = FakeBackend((r) async => code('INVALID_CREDENTIALS', 401));
    await pumpApp(tester, backend);

    await signIn(tester);

    expect(find.text("Couldn't sign in"), findsOneWidget);
    expect(find.text('The username or password is incorrect.'), findsOneWidget);
    expect(find.byKey(const Key('sign-out')), findsNothing);
    await settleToasts(tester);
  });

  testWidgets('MFA_REQUIRED asks for a code, then the dashboard opens on Clinicians', (tester) async {
    final backend = FakeBackend((r) async {
      if (r.url.path == '/v1/auth/login') {
        final body = jsonDecode(r.body) as Map<String, dynamic>;
        return body['totp'] == '654321' ? http.Response(tokens(1), 200) : code('MFA_REQUIRED', 401);
      }
      return json([clinician('admin.demo', mfa: true), clinician('n.silva', locked: true)]);
    });
    await pumpApp(tester, backend);

    await signIn(tester);
    expect(find.byKey(const Key('totp')), findsOneWidget);
    expect(find.byKey(const Key('toast')), findsNothing, reason: 'being asked for a code is not an error');

    await tester.enterText(find.byKey(const Key('totp')), '654321');
    await tester.tap(find.byKey(const Key('sign-in')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('signed-in-as')), findsOneWidget);
    expect(find.text('Name n.silva'), findsOneWidget);
    expect(find.text('Locked'), findsOneWidget);
    expect(find.text('admin.demo (you)'), findsOneWidget);
  });

  testWidgets('an admin cannot deactivate themselves; unlock asks first, then calls the API', (tester) async {
    final backend = FakeBackend(
      (r) async => switch (r.url.path) {
        '/v1/auth/login' => http.Response(tokens(1), 200),
        '/v1/admin/clinicians' => json([clinician('admin.demo'), clinician('n.silva', locked: true)]),
        _ => http.Response('', 204),
      },
    );
    await pumpApp(tester, backend);
    await signIn(tester);

    // Own row: active, not locked, no MFA, and self, so there is nothing to do (an admin never deactivates themselves).
    expect(find.byKey(const Key('actions-admin.demo')), findsNothing);

    await tester.tap(find.byKey(const Key('actions-n.silva')));
    await tester.pumpAndSettle();
    expect(find.text('Deactivate'), findsOneWidget);
    await tester.tap(find.text('Unlock'));
    await tester.pumpAndSettle();
    expect(find.text('Unlock n.silva?'), findsOneWidget);
    expect(backend.count('/v1/admin/clinicians/n.silva/unlock'), 0, reason: 'nothing happens before confirming');

    await tester.tap(find.widgetWithText(FilledButton, 'Unlock'));
    await tester.pumpAndSettle();
    expect(backend.count('/v1/admin/clinicians/n.silva/unlock'), 1);
    expect(find.text('Unlocked n.silva.'), findsOneWidget);
    await settleToasts(tester);
  });

  testWidgets('register checks the server rules before sending', (tester) async {
    final backend = FakeBackend(
      (r) async => switch (r.url.path) {
        '/v1/auth/login' => http.Response(tokens(1), 200),
        '/v1/admin/clinicians' when r.method == 'GET' => json([clinician('admin.demo')]),
        _ => json({'clinicianId': 'new'}, 201),
      },
    );
    await pumpApp(tester, backend);
    await signIn(tester);

    await tester.tap(find.byKey(const Key('register-clinician')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('reg-full-name')), 'K. Perera');
    await tester.enterText(find.byKey(const Key('reg-username')), 'K'); // too short
    await tester.enterText(find.byKey(const Key('reg-password')), 'short');
    await tester.tap(find.byKey(const Key('reg-submit')));
    await tester.pumpAndSettle();
    expect(backend.requests.where((r) => r.method == 'POST' && r.url.path == '/v1/admin/clinicians'), isEmpty);

    await tester.enterText(find.byKey(const Key('reg-username')), 'K.Perera');
    await tester.enterText(find.byKey(const Key('reg-password')), 'a-long-initial-pw');
    await tester.tap(find.byKey(const Key('reg-submit')));
    await tester.pumpAndSettle();

    final post = backend.requests.singleWhere((r) => r.method == 'POST' && r.url.path == '/v1/admin/clinicians');
    expect((jsonDecode(post.body) as Map)['username'], 'k.perera');
    expect(find.text('Clinician registered'), findsOneWidget);
    await settleToasts(tester);
  });

  testWidgets('devices and audit pages load; the audit filter hides other rows', (tester) async {
    final backend = FakeBackend(
      (r) async => switch (r.url.path) {
        '/v1/auth/login' => http.Response(tokens(1), 200),
        '/v1/admin/clinicians' => json([clinician('admin.demo')]),
        '/v1/admin/devices' => json([
          {
            'deviceId': 'dev-7',
            'registeredAt': '2026-10-01T08:30:00Z',
            'revokedAt': null,
            'activeSessions': 2,
            'lastSeenAt': null,
            'lastUsername': 'n.silva',
          },
        ]),
        _ => json([
          for (final (i, user, ok, reason) in [
            (1, 'n.silva', false, 'INVALID_CREDENTIALS'),
            (2, 'admin.demo', true, null),
            (3, 'k.perera', false, 'MFA_REQUIRED'),
          ])
            {
              'id': i,
              'recordedAt': '2026-10-07T21:00:00Z',
              'action': 'LOGIN',
              'success': ok,
              'username': user,
              'reasonCode': reason,
              'deviceId': null,
              'actorUsername': null,
            },
        ]),
      },
    );
    await pumpApp(tester, backend);
    await signIn(tester);

    await tester.tap(find.text('Devices'));
    await tester.pumpAndSettle();
    expect(find.text('dev-7'), findsOneWidget);
    expect(find.byKey(const Key('revoke-dev-7')), findsOneWidget);

    await tester.tap(find.text('Audit log'));
    await tester.pumpAndSettle();
    // The app bar also shows admin.demo, so look inside the table only.
    Finder inTable(String text) => find.descendant(of: find.byType(DataTable), matching: find.text(text));
    expect(inTable('n.silva'), findsOneWidget);
    expect(inTable('k.perera'), findsOneWidget);
    expect(inTable('admin.demo'), findsOneWidget);

    // MFA_REQUIRED is a prompt, not a failure.
    await tester.tap(find.byKey(const Key('failures-only')));
    await tester.pumpAndSettle();
    expect(inTable('n.silva'), findsOneWidget);
    expect(inTable('k.perera'), findsNothing);
    expect(inTable('admin.demo'), findsNothing);
  });

  testWidgets('a register error shows as a toast above the dialog, which stays open with what was typed', (
    tester,
  ) async {
    final backend = FakeBackend(
      (r) async => switch (r.url.path) {
        '/v1/auth/login' => http.Response(tokens(1), 200),
        '/v1/admin/clinicians' when r.method == 'GET' => json([clinician('admin.demo')]),
        _ => code('USERNAME_TAKEN', 409),
      },
    );
    await pumpApp(tester, backend);
    await signIn(tester);
    await tester.tap(find.byKey(const Key('register-clinician')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('reg-full-name')), 'N. Silva');
    await tester.enterText(find.byKey(const Key('reg-username')), 'n.silva');
    await tester.enterText(find.byKey(const Key('reg-password')), 'a-long-initial-pw');
    await tester.tap(find.byKey(const Key('reg-submit')));
    await tester.pumpAndSettle();

    expect(find.text("Couldn't register n.silva"), findsOneWidget);
    expect(find.text('That username is already in use. Choose another one.'), findsOneWidget);
    expect(find.byKey(const Key('reg-submit')), findsOneWidget, reason: 'the dialog stays open');
    expect(find.widgetWithText(TextFormField, 'n.silva'), findsOneWidget);
    await settleToasts(tester);
  });

  testWidgets('a list that fails to load says so in words, without the status number', (tester) async {
    final backend = FakeBackend(
      (r) async => r.url.path == '/v1/auth/login' ? http.Response(tokens(1), 200) : http.Response('', 503),
    );
    await pumpApp(tester, backend);
    await signIn(tester);

    expect(find.text("Couldn't load clinicians"), findsOneWidget);
    expect(find.text('The server had a problem. Please try again in a moment.'), findsWidgets);
    expect(find.textContaining('503'), findsNothing);
    await settleToasts(tester);
  });

  testWidgets('when the server ends the session the admin is told why on the sign-in screen', (tester) async {
    final backend = FakeBackend(
      (r) async => switch (r.url.path) {
        '/v1/auth/login' => http.Response(tokens(1, expiresIn: 30), 200), // inside the refresh margin
        '/v1/auth/refresh' => code('INVALID_REFRESH_TOKEN', 401),
        _ => json([]),
      },
    );
    await pumpApp(tester, backend);
    await signIn(tester);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('sign-in')), findsOneWidget, reason: 'back on the sign-in screen');
    expect(find.text('Signed out'), findsOneWidget);
    expect(find.text('Your session has ended. Please sign in again.'), findsOneWidget);
    await settleToasts(tester);
  });
}
