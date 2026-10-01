import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:melanin_wound_cdss/app/app_services.dart';
import 'package:melanin_wound_cdss/features/sync/auth/auth_session.dart';
import 'package:melanin_wound_cdss/features/sync/data/app_database.dart';
import 'package:melanin_wound_cdss/features/sync/ui/sign_in_screen.dart';
import 'package:melanin_wound_cdss/features/sync/ui/sync_home_screen.dart';

import 'support/fakes.dart';

/// The sign-in and sync screens against the fake gateway and an in-memory database.
void main() {
  late AppServices services;
  late FakeGateway gateway;

  Future<void> open(WidgetTester tester, {bool requireMfa = false}) async {
    gateway = FakeGateway(requireMfa: requireMfa);
    services = await tester.runAsync(() => AppServices.open(
          db: AppDatabase.inMemory(),
          secrets: MemorySecretStore(),
          api: gateway,
          online: const Stream.empty(),
        )) as AppServices;
  }

  Future<void> close(WidgetTester tester) async {
    await tester.pumpWidget(const SizedBox());
    await tester.runAsync(services.close);
  }

  testWidgets('sign-in: wrong password, then the MFA code is asked for and accepted', (tester) async {
    await open(tester, requireMfa: true);
    var signedIn = false;
    await tester.pumpWidget(MaterialApp(home: SignInScreen(services: services, onSignedIn: () => signedIn = true)));

    await tester.enterText(find.byKey(const Key('username')), 'n.silva');
    await tester.enterText(find.byKey(const Key('password')), 'wrong');
    await tester.runAsync(() => tester.tap(find.byKey(const Key('signInButton'))));
    await tester.pump();
    expect(find.text(SignInScreen.message('INVALID_CREDENTIALS')), findsOneWidget);
    expect(find.byKey(const Key('totp')), findsNothing);

    await tester.enterText(find.byKey(const Key('password')), 'correct-password');
    await tester.runAsync(() => tester.tap(find.byKey(const Key('signInButton'))));
    await tester.pump();
    expect(find.byKey(const Key('totp')), findsOneWidget, reason: 'MFA_REQUIRED shows the code field');
    expect(signedIn, isFalse);

    await tester.enterText(find.byKey(const Key('totp')), '000000');
    await tester.runAsync(() => tester.tap(find.byKey(const Key('signInButton'))));
    await tester.pump();
    expect(find.text(SignInScreen.message('INVALID_TOTP')), findsOneWidget);

    await tester.enterText(find.byKey(const Key('totp')), '123456');
    await tester.runAsync(() async {
      await tester.tap(find.byKey(const Key('signInButton')));
      await Future<void>.delayed(const Duration(milliseconds: 50));
    });
    await tester.pump();
    expect(signedIn, isTrue);
    final cached = await tester.runAsync(services.cachedClinician);
    expect(cached, isNotNull, reason: 'who is signed in is cached for offline display, without the refresh token');
    await close(tester);
  });

  /// Lets the in-memory database (same isolate) and Drift's stream updates run on the test's fake clock.
  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 10; i++) {
      await tester.pump(const Duration(milliseconds: 20));
    }
  }

  Future<AppServices> openInZone({bool requireMfa = false}) async {
    gateway = FakeGateway(requireMfa: requireMfa);
    return AppServices.open(
        db: AppDatabase.inMemory(), secrets: MemorySecretStore(), api: gateway, online: const Stream.empty());
  }

  Future<void> closeInZone(WidgetTester tester) async {
    await tester.pumpWidget(const SizedBox()); // cancels the screen's database streams first
    await settle(tester);
    services.scheduler.dispose();
    final closing = services.db.close();
    await settle(tester);
    await closing;
  }

  testWidgets('sync screen: save a sample, sync, advice ready, open it', (tester) async {
    services = await openInZone();
    await services.signIn(username: 'n.silva', password: 'correct-password');
    await tester.pumpWidget(MaterialApp(home: SyncHomeScreen(services: services, onSignedOut: () {})));
    await settle(tester);
    expect(find.text('No assessments saved on this phone yet.'), findsOneWidget);

    await tester.tap(find.byKey(const Key('saveSample')));
    await settle(tester);
    expect(find.text('Waiting: 1'), findsOneWidget);
    expect(find.textContaining('Waiting to sync'), findsOneWidget);

    // The save trigger (§6.2): one sync two seconds after the save.
    await tester.pump(const Duration(seconds: 2));
    await settle(tester);
    expect(gateway.stored, hasLength(1), reason: 'the sample is valid and reached the server');
    expect(find.text('Advice ready: 1'), findsOneWidget);
    expect(find.text('Up to date'), findsOneWidget);

    await tester.tap(find.textContaining('Advice ready ·'));
    await settle(tester);
    expect(find.text('Advice (extractive)'), findsOneWidget);
    expect(find.text('Fake advice [S1].'), findsOneWidget);
    await tester.tap(find.text('Close'));
    await settle(tester);
    await closeInZone(tester);
  });

  testWidgets('sync screen without a session says to sign in', (tester) async {
    services = await openInZone();
    await tester.pumpWidget(MaterialApp(home: SyncHomeScreen(services: services, onSignedOut: () {})));
    await settle(tester);
    await tester.tap(find.byKey(const Key('syncNow')));
    await settle(tester);
    expect(find.text('Sign in to sync'), findsOneWidget);
    expect(find.widgetWithText(TextButton, 'Sign in'), findsOneWidget);
    await closeInZone(tester);
  });

  testWidgets('saving before anyone signed in on this phone is refused with a message, never silently', (tester) async {
    services = await openInZone();
    await tester.pumpWidget(MaterialApp(home: SyncHomeScreen(services: services, onSignedOut: () {})));
    await settle(tester);
    await tester.tap(find.byKey(const Key('saveSample')));
    await settle(tester);
    expect(find.text('Sign in once on this phone before saving assessments.'), findsOneWidget);
    expect(find.text('Waiting: 0'), findsOneWidget);
    await closeInZone(tester);
  });
}
