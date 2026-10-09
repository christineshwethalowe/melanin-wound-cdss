# Mobile app (Flutter, Android)

The on-device flow: **capture → calibration (Member 1) → segmentation and measurement (Member 2) →
clinical form → Drift queue → sync (Member 4) → recommendations shown (Member 3's content)**.

| Folder | Owner | What goes there |
|--------|-------|-----------------|
| `lib/features/calibration/` | Member 1 | Dart side of the colour-correction pipeline |
| `native/` | Member 1 | OpenCV C++ code called through Flutter FFI |
| `lib/features/measurement/` | Member 2 | YOLO11 (LiteRT) segmentation, area in mm², colour regions |
| `lib/features/recommendations/` | Member 3 | Screens that display cited recommendations and refusals |
| `lib/features/sync/` | Member 4 | Drift tables, queue repository, sync engine, auth |
| `lib/core/` | All | Shared models, theme, routing |

## First-time setup

The platform folders are not committed yet. Create them once:

```bash
cd mobile
flutter create . --project-name melanin_wound_cdss --platforms android
flutter pub get
dart run build_runner build --delete-conflicting-outputs
```

`flutter create .` does not overwrite existing files such as `lib/main.dart` or `pubspec.yaml`.

## Sync (Member 4, `lib/features/sync`, architecture §6, §7)

| Piece | File | What it does |
|-------|------|--------------|
| Queue | `data/queue_repository.dart` | The only way an assessment leaves the device: validate (§5), store as pending, lease batches of ≤ 50 events / 256 KB for 2 min, apply push results (DUPLICATE = ACCEPTED, REJECTED final) and pulled changes with the cursor in one transaction (§7.2) |
| Database | `data/app_database.dart`, `data/tables.dart` | Drift on SQLCipher (§12), key of 32 random bytes in the Android Keystore; all queries on a background isolate |
| Validation | `domain/wound_event_validator.dart` | `contracts/wound-event.schema.json` rule for rule, plus the 16 KB cap; tested against the contract's examples |
| Auth | `auth/auth_session.dart` | Login (with MFA), refresh before expiry, logout (§7.3). Access token in memory only, rotating refresh token in the keystore, never in the database |
| API | `api/sync_api.dart` | Through the API gateway: `/health`, gzip push, paged pull, figures |
| Triggers | `engine/sync_scheduler.dart` | When to sync (§6.2): connectivity regained, app back in the foreground, 2 s after a save (debounced), pull-to-refresh / "Sync now", every 5 min while open, and at the engine's retry time after a backoff |
| Screens | `ui/sign_in_screen.dart`, `ui/sync_home_screen.dart` | Sign in (the code field appears on MFA_REQUIRED); sync status: counts, last sync, next retry, records with their status, the delivered advice. "Save sample assessment" stands in for the capture flow (Members 1–2) |
| Wiring | `lib/app/app_services.dart` | Opens the encrypted database, keystore, API, session, engine and scheduler; releases lapsed leases at start |
| Engine | `engine/sync_engine.dart` | One sync run (§6.2): single flight (in-isolate + database lease), lapsed leases back to pending, refresh first (paused for sign-in if that fails, queue untouched), probe, push, pull. 401 → refresh + retry once, 413 → split, 429/503/no answer → back off 2 s → 5 min with full jitter and Retry-After |

Encryption uses `package:sqlite3`'s SQLCipher build (`hooks.user_defines.sqlite3.source: sqlcipher` in
`pubspec.yaml`); the old `sqlcipher_flutter_libs` package is end-of-life.

### Running on a phone or emulator

Not verified on this machine (no Android SDK platform installed). To try it:

1. `flutter create . --project-name melanin_wound_cdss --platforms android` (once; see "First-time setup").
2. In `android/app/src/main/AndroidManifest.xml`, add `<uses-permission android:name="android.permission.INTERNET"/>`
   (Flutter adds it only to the debug manifest) and `ACCESS_NETWORK_STATE` for connectivity changes.
3. The local backend is plain HTTP: allow it in debug only, e.g. `android:usesCleartextTraffic="true"` in
   `android/app/src/debug/AndroidManifest.xml`. Production uses HTTPS (§12).
4. `flutter_secure_storage` needs `minSdkVersion` 23 or higher in `android/app/build.gradle(.kts)`.
5. Run against the Docker stack: the emulator reaches the host as `10.0.2.2` (the default), a phone needs the PC's
   address: `flutter run --dart-define=SYNC_BASE_URL=http://192.168.x.x:8080`. Sign in as `n.silva` /
   `Demo-Pass-2026!`, tap "Save sample assessment", watch it go Waiting → Synced → Advice ready.

Still open: Android's periodic *background* sync (WorkManager; the foreground triggers above cover an open app),
and the §13 frame-time metric (no frame over 200 ms during sync), which needs a device.

### Tests

```bash
dart run build_runner build --delete-conflicting-outputs
flutter test                                                    # 48 tests, no device needed
SYNC_BACKEND_URL=http://localhost:8080 flutter test --tags integration   # against the Docker backend
```

- §14.1 step 3: 1,000 offline saves survive the app being killed mid-sync (`test/sync/persistence_test.dart`).
- §14.1 step 4: a flaky gateway never causes a lost or stuck row: 300 events with a third of pushes failing at
  random, every one stored exactly once and completed (`test/sync/sync_engine_test.dart`).
- The database file is encrypted: no plaintext, a wrong key cannot open it.
- Trigger timing with a fake clock (debounce, connectivity, retry at the engine's time, periodic) and the screens
  (sign-in with MFA, save → sync → advice shown, a refused save is never silent).
- The integration test signs in as the demo nurse, pushes three events (one an edit), pages through the facility's
  history and ends with the advice stored on the device, through the real pipeline.
