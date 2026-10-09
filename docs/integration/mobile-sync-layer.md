# Using the sync layer from the app (Members 1–3)

How the capture, measurement and recommendation screens hand assessments to Member 4's offline sync layer
(`mobile/lib/features/sync`) and show what comes back. You never call the network yourself: you save to the
queue, and the sync layer gets it to the server and brings the advice back, online or not (architecture §6, §7).

Everything below hangs off one object, `AppServices` (`mobile/lib/app/app_services.dart`), created once in
`main.dart` and passed to your screens.

| You want to | Call | Who |
|---|---|---|
| Save an assessment | `services.queue.enqueue(event)`, then `services.scheduler.onSaved()` | Members 1 and 2 (capture → measurement) |
| Edit a saved assessment | the same, with `revision + 1` | Members 1 and 2 |
| Show sync status | `services.queue.watchRecent()`, `services.queue.watchCounts()` | anyone |
| Show the advice for an assessment | `services.queue.recommendationFor(assessmentId, revision)` | Member 3 |
| Show a guideline figure | `services.figures.figureFor(corpusVersion, figureId)` | Member 3 |
| Know who is signed in, offline | `services.cachedClinician()` | anyone |

## 1. Saving an assessment

Build one Wound Event, the JSON document in `contracts/wound-event.schema.json` (architecture §5), and enqueue it.
`enqueue` validates it against the contract, stores it encrypted and **commits before it returns**, so when it
returns you may tell the clinician "saved", even with no network.

```dart
import 'package:uuid/uuid.dart';

final clinician = await services.cachedClinician();      // facilityId of whoever signed in
final event = {
  'schemaVersion': '1.0',
  'eventId': const Uuid().v7(),                           // new for every save, including edits
  'assessmentId': assessmentId,                           // Uuid().v7() for a new assessment
  'revision': 1,
  'woundId': woundId,                                     // the same for every assessment of this wound
  'patientRef': 'p-7f3a9c',                               // the pseudonym, never a name or MRN
  'deviceId': await services.queue.deviceId(),
  'facilityId': clinician!.facilityId,
  'capturedAt': '2026-10-03T09:41:12+05:30',              // RFC 3339 with the device's offset
  'analytics': {
    'areaMm2': 412.6,                                     // Member 2
    'colourRegions': [{'cluster': 1, 'percent': 61.2}, {'cluster': 2, 'percent': 27.9}],
    'fitzpatrickClass': 'V',                              // Member 1
    'pipeline': {'calibration': '2.1.0', 'segmentation': 'yolo11n-seg-0.4'},
  },
  'clinicalAssessment': {
    'pedalPulses': 'not_recorded',                        // present | absent | not_recorded
    'protectiveSensation': 'absent',
  },
};

try {
  await services.queue.enqueue(event);
  services.scheduler.onSaved();                           // one sync, two seconds after the last save
  showSaved();
} on EnqueueRejected catch (e) {
  showFormErrors(e.errors);                               // nothing was stored; let the clinician fix it
}
```

`mobile/lib/features/sync/ui/sample_assessment.dart` builds a complete valid event and is a good template.

**Rules the validator enforces** (the gateway enforces the same ones, and so do the CI contract tests):

- `eventId`, `assessmentId` and `woundId` are lower-case **UUIDv7**, made on the phone. The server never makes one.
- **Tri-state is strict.** Every clinical key must be present, with `present`, `absent` or `not_recorded`. A
  missing key is an error; it is never read as `not_recorded`. Use `not_recorded` when the clinician skipped it.
- **Analytics only.** No image bytes, no names, no free text. Unknown keys are rejected. The whole event is at most
  16 KB.
- `patientRef` is a pseudonym like `p-7f3a9c`. The friendly label ("Bed 12 / K.P.") is set separately by the
  patient screen and never goes into the event.
- Do not change the contract on your own. `contracts/` is shared by all four members (README). The final clinical
  field list and `woundBedLabels` are open team items (§15). When they are agreed, the schema, the validator and
  the backend change together.

**Editing** creates a new revision; nothing is updated in place. Enqueue a new event with the same `assessmentId`
and `woundId`, `revision + 1` and a new `eventId`. Only the newest revision gets advice: older ones end as
`superseded`.

## 2. What happens next, and the statuses you will see

You don't drive any of this; the sync engine does it in the background and retries on its own.

| Status (`QueueStatus`) | Meaning | Show the clinician |
|---|---|---|
| `pending` | Saved on the phone, waiting to be sent | "Saved, will sync" |
| `inFlight` | Being sent now | "Syncing" |
| `accepted` | Safely on the server | "Synced, advice pending" |
| `adviceDeferred` | Synced; the recommendation service is busy, it retries on its own | "Synced, advice delayed" |
| `complete` | The advice is on the phone | the advice |
| `superseded` | A newer revision replaced this one | link to the newest revision |
| `rejected` | The server refused it (contract violation). Final, never retried | the error, from `lastError` |

```dart
StreamBuilder<List<QueuedEvent>>(
  stream: services.queue.watchRecent(),
  builder: (context, snap) => ...,                        // rebuilds whenever a status changes
);
```

`watchCounts()` gives totals per status, for a badge such as "3 waiting to sync". Sync runs when connectivity
returns, when the app comes to the foreground, two seconds after a save, on pull-to-refresh
(`services.scheduler.syncNow()`) and periodically while the app is open. If the clinician's session has expired,
sync pauses until they sign in again. The queue is never touched, and saving keeps working offline.

## 3. Showing the advice (Member 3)

Advice arrives with the next sync after the server has produced it. It is stored on the phone, so it can be shown
offline from then on.

```dart
final rec = await services.queue.recommendationFor(assessmentId, revision);
if (rec == null) {
  // not here yet: show the row's status from section 2
} else {
  final payload = jsonDecode(rec.payloadJson) as Map<String, dynamic>;
  // contracts/rag-response.schema.json: mode, sections, citations, withheld, nextBestAssessment, escalation, figures
}
```

`rec.mode` is `generated` or `extractive`. Both are valid answers and are shown the same way (§10.3). The phone
also keeps advice for other phones' assessments in the same facility, for a shared ward view.

## 4. Showing a guideline figure (Member 3)

A recommendation lists the figures it cites in `figures: [{corpusVersion, figureId, tier, caption}]`. Ask the figure
repository for each one. It serves the figure from the phone's cache, or fetches it through the gateway and caches
it. After every sync the engine already fetches the figures that new advice cites, so they are usually cached
before the screen opens, and still there when the phone is offline later.

```dart
final result = await services.figures.figureFor(ref['corpusVersion'], ref['figureId']);
if (result.isFound) {
  final f = result.figure!;
  Image.memory(f.bytes);                                  // f.contentType, e.g. image/png
  Text('${f.attribution} · ${f.licence}');                // licence and attribution must be shown with it
} else {
  switch (result.miss!) {
    case FigureMiss.offline:     /* "Figure available when online" */
    case FigureMiss.notFound:    /* hide it */
    case FigureMiss.needsSignIn: /* "Sign in to load figures" */
    case FigureMiss.unavailable: /* "Figure unavailable, try later" */
  }
}
```

A corpus version is a frozen snapshot, so a cached figure is never stale and is never fetched again.

## 5. Testing your screen without the backend

- Unit and widget tests: `test/sync/support/fakes.dart` has `FakeGateway` (an in-memory server: deduplicates, makes
  advice, serves figures, injects faults) and `sampleEvent()`. `test/sync/screens_test.dart` shows a screen
  built on `AppServices.open(db: AppDatabase.inMemory(), secrets: MemorySecretStore(), api: FakeGateway(), ...)`.
- Against the real stack: `docker compose up -d --build`, then
  `SYNC_BACKEND_URL=http://localhost:8080 flutter test --tags integration`.
- Running the app on the emulator: `flutter run --dart-define=SYNC_BASE_URL=http://10.0.2.2:8080`. Demo login:
  `n.silva`, password in the README.

## 6. Who to ask

The sync layer, the queue, statuses and figures: Member 4. The event fields: the contract, which all four
members review. Changes to the advice payload: Member 3, through `contracts/rag-response.schema.json`.
