/// Shapes of the identity service's auth and admin answers (contracts/auth.schema.json, /v1/admin/*).
library;

class TokenPair {
  const TokenPair({required this.accessToken, required this.refreshToken, required this.expiresIn});

  factory TokenPair.fromJson(Map<String, dynamic> json) => TokenPair(
    accessToken: json['accessToken'] as String,
    refreshToken: json['refreshToken'] as String,
    expiresIn: Duration(seconds: json['expiresIn'] as int),
  );

  final String accessToken;
  final String refreshToken;
  final Duration expiresIn;
}

/// Roles the identity service accepts on registration (ClinicianAdminService.Roles).
const clinicianRoles = ['nurse', 'wound_specialist', 'admin'];

/// How a role reads on screen; the stored value stays as above.
String roleLabel(String role) => switch (role) {
  'nurse' => 'Nurse',
  'wound_specialist' => 'Wound specialist',
  'admin' => 'Admin',
  _ => role,
};

/// ClinicianAdminService.MinPasswordLength.
const minPasswordLength = 12;

/// ClinicianAdminService.UsernamePattern (the server lower-cases and trims first).
final usernamePattern = RegExp(r'^[a-z0-9][a-z0-9._-]{2,63}$');

class ClinicianSummary {
  const ClinicianSummary({
    required this.clinicianId,
    required this.username,
    required this.fullName,
    required this.role,
    required this.active,
    required this.locked,
    required this.mfaEnabled,
    required this.createdAt,
  });

  factory ClinicianSummary.fromJson(Map<String, dynamic> json) => ClinicianSummary(
    clinicianId: json['clinicianId'] as String,
    username: json['username'] as String,
    fullName: json['fullName'] as String,
    role: json['role'] as String,
    active: json['active'] as bool,
    locked: json['locked'] as bool,
    mfaEnabled: json['mfaEnabled'] as bool,
    createdAt: DateTime.parse(json['createdAt'] as String),
  );

  final String clinicianId;
  final String username;
  final String fullName;
  final String role;
  final bool active;
  final bool locked;
  final bool mfaEnabled;
  final DateTime createdAt;
}

class DeviceSummary {
  const DeviceSummary({
    required this.deviceId,
    required this.registeredAt,
    required this.revokedAt,
    required this.activeSessions,
    required this.lastSeenAt,
    required this.lastUsername,
  });

  factory DeviceSummary.fromJson(Map<String, dynamic> json) => DeviceSummary(
    deviceId: json['deviceId'] as String,
    registeredAt: DateTime.parse(json['registeredAt'] as String),
    revokedAt: _date(json['revokedAt']),
    activeSessions: json['activeSessions'] as int,
    lastSeenAt: _date(json['lastSeenAt']),
    lastUsername: json['lastUsername'] as String?,
  );

  final String deviceId;
  final DateTime registeredAt;
  final DateTime? revokedAt;
  final int activeSessions;
  final DateTime? lastSeenAt;
  final String? lastUsername;

  bool get revoked => revokedAt != null;
}

class AuthAuditEntry {
  const AuthAuditEntry({
    required this.id,
    required this.recordedAt,
    required this.action,
    required this.success,
    required this.username,
    required this.reasonCode,
    required this.deviceId,
    required this.actorUsername,
  });

  factory AuthAuditEntry.fromJson(Map<String, dynamic> json) => AuthAuditEntry(
    id: json['id'] as int,
    recordedAt: DateTime.parse(json['recordedAt'] as String),
    action: json['action'] as String,
    success: json['success'] as bool,
    username: json['username'] as String,
    reasonCode: json['reasonCode'] as String?,
    deviceId: json['deviceId'] as String?,
    actorUsername: json['actorUsername'] as String?,
  );

  final int id;
  final DateTime recordedAt;
  final String action;
  final bool success;
  final String username;
  final String? reasonCode;
  final String? deviceId;
  final String? actorUsername;
}

DateTime? _date(Object? value) => value == null ? null : DateTime.parse(value as String);
