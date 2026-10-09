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
