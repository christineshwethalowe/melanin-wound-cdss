import 'dart:math';

import 'package:uuid/uuid.dart';

/// A synthetic wound assessment for demos and testing the sync before the capture screens exist (calibration and
/// measurement belong to Members 1 and 2). Valid against contracts/wound-event.schema.json; analytics only.
/// It uses this phone's device id and the signed-in clinician's facility, as a real capture would (§7.1).
Map<String, dynamic> sampleAssessment({required String deviceId, required String facilityId, Random? random}) {
  final rng = random ?? Random();
  const triState = ['present', 'absent', 'not_recorded'];
  final first = (40 + rng.nextDouble() * 40).toStringAsFixed(1);
  return {
    'schemaVersion': '1.0',
    'eventId': const Uuid().v7(),
    'assessmentId': const Uuid().v7(),
    'revision': 1,
    'woundId': const Uuid().v7(),
    'patientRef': 'p-${List.generate(6, (_) => rng.nextInt(16).toRadixString(16)).join()}',
    'deviceId': deviceId,
    'facilityId': facilityId,
    'capturedAt': _offsetTimestamp(DateTime.now()),
    'analytics': {
      'areaMm2': double.parse((200 + rng.nextDouble() * 400).toStringAsFixed(1)),
      'colourRegions': [
        {'cluster': 1, 'percent': double.parse(first)},
        {'cluster': 2, 'percent': double.parse((95 - double.parse(first)).toStringAsFixed(1))},
      ],
      'fitzpatrickClass': ['IV', 'V', 'VI'][rng.nextInt(3)],
      'pipeline': {'calibration': '2.1.0', 'segmentation': 'yolo11n-seg-0.4'},
    },
    'clinicalAssessment': {
      'pedalPulses': triState[rng.nextInt(3)],
      'protectiveSensation': triState[rng.nextInt(3)],
    },
  };
}

/// RFC 3339 with the device's offset, as §5 shows (e.g. 2026-10-03T09:41:12+05:30).
String _offsetTimestamp(DateTime local) {
  final o = local.timeZoneOffset;
  final sign = o.isNegative ? '-' : '+';
  String two(int v) => v.abs().toString().padLeft(2, '0');
  final t = local.toIso8601String().split('.').first;
  return '$t$sign${two(o.inHours)}:${two(o.inMinutes % 60)}';
}
