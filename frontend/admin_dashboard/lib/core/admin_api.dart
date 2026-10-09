import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'config.dart';
import 'models.dart';

/// No HTTP answer at all: gateway down, CORS refused, timeout.
class TransportException implements Exception {
  TransportException(this.message);

  final String message;

  @override
  String toString() => 'TransportException: $message';
}

/// An HTTP error answer, with the reason code the backend sends (`{"code": "..."}`).
class ApiException implements Exception {
  ApiException(this.status, [this.code]);

  final int status;
  final String? code;

  @override
  String toString() => 'ApiException($status${code == null ? '' : ' $code'})';
}

/// The dashboard's auth and admin API calls, all through the API gateway.
class AdminApi {
  AdminApi(this.baseUri, {http.Client? client}) : _http = client ?? http.Client();

  final Uri baseUri;
  final http.Client _http;

  static const requestTimeout = Duration(seconds: 20);

  Uri _uri(String path, [Map<String, String>? query]) =>
      baseUri.replace(path: '${baseUri.path.replaceAll(RegExp(r'/$'), '')}/$path', queryParameters: query);

  // ---- auth ----

  /// Throws [ApiException] 401 with MFA_REQUIRED, INVALID_TOTP, INVALID_CREDENTIALS, CREDENTIAL_LOCKED or CLIENT_NOT_ALLOWED.
  Future<TokenPair> login({required String username, required String password, String? totp}) async =>
      TokenPair.fromJson(
        await _json(
              _post('v1/auth/login', {
                'username': username,
                'password': password,
                'clientId': AppConfig.clientId,
                if (totp != null && totp.isNotEmpty) 'totp': totp,
              }),
            )
            as Map<String, dynamic>,
      );

  Future<TokenPair> refresh(String refreshToken) async =>
      TokenPair.fromJson(await _json(_post('v1/auth/refresh', {'refreshToken': refreshToken})) as Map<String, dynamic>);

  Future<void> logout(String refreshToken) => _json(_post('v1/auth/logout', {'refreshToken': refreshToken}));

  // ---- clinicians ----

  Future<List<ClinicianSummary>> clinicians(String token) async =>
      _list(await _json(_get('v1/admin/clinicians', token)), ClinicianSummary.fromJson);

  /// Throws [ApiException] 409 USERNAME_TAKEN, or 400 INVALID_USERNAME, PASSWORD_TOO_SHORT, INVALID_ROLE, ...
  Future<void> registerClinician(
    String token, {
    required String username,
    required String password,
    required String fullName,
    required String role,
  }) => _json(
    _post('v1/admin/clinicians', {
      'username': username,
      'password': password,
      'fullName': fullName,
      'role': role,
    }, token),
  );

  Future<void> unlock(String token, String username) =>
      _json(_post('v1/admin/clinicians/${Uri.encodeComponent(username)}/unlock', null, token));

  /// Throws [ApiException] 409 CANNOT_DEACTIVATE_SELF.
  Future<void> deactivate(String token, String username) =>
      _json(_post('v1/admin/clinicians/${Uri.encodeComponent(username)}/deactivate', null, token));

  Future<void> resetMfa(String token, String username) =>
      _json(_post('v1/admin/clinicians/${Uri.encodeComponent(username)}/reset-mfa', null, token));

  // ---- devices ----

  Future<List<DeviceSummary>> devices(String token) async =>
      _list(await _json(_get('v1/admin/devices', token)), DeviceSummary.fromJson);

  /// Throws [ApiException] 409 DEVICE_ALREADY_REVOKED.
  Future<void> revokeDevice(String token, String deviceId) =>
      _json(_post('v1/admin/devices/${Uri.encodeComponent(deviceId)}/revoke', null, token));

  // ---- audit ----

  Future<List<AuthAuditEntry>> authAudit(String token, {int limit = 100}) async =>
      _list(await _json(_get('v1/admin/auth-audit', token, {'limit': '$limit'})), AuthAuditEntry.fromJson);

  // ---- plumbing ----

  Future<http.Response> _get(String path, String token, [Map<String, String>? query]) =>
      _http.get(_uri(path, query), headers: {'Authorization': 'Bearer $token'});

  Future<http.Response> _post(String path, Object? body, [String? token]) => _http.post(
    _uri(path),
    headers: {'Content-Type': 'application/json', if (token != null) 'Authorization': 'Bearer $token'},
    body: jsonEncode(body ?? const {}),
  );

  /// The decoded body of a 2xx answer (null when empty); otherwise [ApiException] or [TransportException].
  Future<Object?> _json(Future<http.Response> request) async {
    final http.Response response;
    try {
      response = await request.timeout(requestTimeout);
    } on TimeoutException {
      throw TransportException('timed out');
    } on http.ClientException catch (e) {
      throw TransportException(e.message);
    }
    final body = response.body.isEmpty ? null : _tryDecode(response.body);
    if (response.statusCode >= 200 && response.statusCode < 300) return body;
    throw ApiException(response.statusCode, body is Map<String, dynamic> ? body['code'] as String? : null);
  }

  static Object? _tryDecode(String body) {
    try {
      return jsonDecode(body);
    } on FormatException {
      return null;
    }
  }

  static List<T> _list<T>(Object? body, T Function(Map<String, dynamic>) parse) => [
    for (final item in body as List<dynamic>) parse(item as Map<String, dynamic>),
  ];
}
