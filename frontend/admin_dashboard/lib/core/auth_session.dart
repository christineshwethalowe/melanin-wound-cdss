import 'dart:convert';

import 'package:flutter/foundation.dart';

import 'admin_api.dart';
import 'models.dart';
import 'token_store.dart';

/// No usable session: the admin has to sign in again.
class NeedsSignIn implements Exception {
  NeedsSignIn(this.reason);

  final String reason;

  @override
  String toString() => 'NeedsSignIn: $reason';
}

/// The signed-in admin, from the access token's claims (sub, facility_id, role) plus the username typed at login.
class Admin {
  const Admin({required this.id, required this.username, required this.role, required this.facilityId});

  final String id;
  final String username;
  final String role;
  final String facilityId;
}

/// Login, refresh and logout for the dashboard (ADR 0005). Mirrors the mobile AuthSession: the access token stays in
/// memory, the rotating refresh token in [TokenStore]. Listeners hear when the admin signs in or out, including a
/// session the server stopped accepting.
class AuthSession extends ChangeNotifier {
  AuthSession(this.api, this._store, {DateTime Function()? clock}) : _now = clock ?? DateTime.now;

  static const _refreshKey = 'cdss_admin_refresh_v1';
  static const _usernameKey = 'cdss_admin_username_v1';

  /// Refresh a little before expiry so a request never leaves with a token about to lapse.
  static const refreshMargin = Duration(seconds: 60);

  final AdminApi api;
  final TokenStore _store;
  final DateTime Function() _now;

  String? _accessToken;
  DateTime? _expiresAt;
  Admin? _admin;
  Future<String>? _refreshing;
  bool _ended = false;

  Admin? get admin => _admin;
  bool get signedIn => _admin != null;

  /// True once after the server stopped accepting a signed-in session (expired, revoked, deactivated), so the
  /// sign-in screen can say why the admin is back there. A deliberate sign-out does not count.
  bool takeSessionEndedNotice() {
    final ended = _ended;
    _ended = false;
    return ended;
  }

  /// After a page reload only the refresh token is left; trade it for a new access token. False when there is no
  /// session to resume.
  Future<bool> restore() async {
    if (_store.read(_refreshKey) == null) return false;
    try {
      await refresh();
      return true;
    } on NeedsSignIn {
      return false;
    }
  }

  /// Throws [ApiException] with the backend's reason code (401 MFA_REQUIRED: ask for a code).
  Future<Admin> signIn({required String username, required String password, String? totp}) async {
    final tokens = await api.login(username: username, password: password, totp: totp);
    _store.write(_usernameKey, username);
    return _accept(tokens);
  }

  /// Runs an admin call with a valid access token. A 401 means the token was refused (expired early, key rotated):
  /// refresh once and retry; if refreshing fails too, the admin is signed out and [NeedsSignIn] is thrown.
  Future<T> authorized<T>(Future<T> Function(String accessToken) call) async {
    try {
      return await call(await _validAccessToken());
    } on ApiException catch (e) {
      if (e.status != 401) rethrow;
      return await call(await refresh());
    }
  }

  /// Rotates both tokens. Concurrent callers share one request: the old refresh token stops working the moment the
  /// first rotation succeeds, so a second request with it would sign the admin out.
  Future<String> refresh() => _refreshing ??= _refresh().whenComplete(() => _refreshing = null);

  Future<void> signOut() async {
    final refreshToken = _store.read(_refreshKey);
    if (refreshToken != null) {
      try {
        await api.logout(refreshToken);
      } on Exception {
        // The local session ends anyway; the server-side one expires on its own.
      }
    }
    _clear();
  }

  Future<String> _validAccessToken() async {
    final token = _accessToken;
    if (token != null && _expiresAt!.isAfter(_now().add(refreshMargin))) return token;
    return refresh();
  }

  Future<String> _refresh() async {
    final refreshToken = _store.read(_refreshKey);
    if (refreshToken == null) {
      _clear();
      throw NeedsSignIn('NOT_SIGNED_IN');
    }
    try {
      final tokens = await api.refresh(refreshToken);
      _accept(tokens);
      return tokens.accessToken;
    } on ApiException catch (e) {
      if (e.status == 401) {
        // Expired, logged out elsewhere, deactivated, or already rotated: only a new sign-in helps.
        _ended = signedIn;
        _clear();
        throw NeedsSignIn(e.code ?? 'INVALID_REFRESH_TOKEN');
      }
      rethrow;
    }
  }

  void _clear() {
    final wasSignedIn = signedIn;
    _accessToken = null;
    _expiresAt = null;
    _admin = null;
    _store.delete(_refreshKey);
    if (wasSignedIn) notifyListeners();
  }

  /// The rotated refresh token is stored before the new access token is used: the old one is already revoked.
  Admin _accept(TokenPair tokens) {
    _store.write(_refreshKey, tokens.refreshToken);
    _accessToken = tokens.accessToken;
    _expiresAt = _now().add(tokens.expiresIn);
    final claims = _claims(tokens.accessToken);
    final wasSignedIn = signedIn;
    _admin = Admin(
      id: claims['sub'] as String? ?? '',
      username: _store.read(_usernameKey) ?? '',
      role: claims['role'] as String? ?? '',
      facilityId: claims['facility_id'] as String? ?? '',
    );
    if (!wasSignedIn) notifyListeners();
    return _admin!;
  }

  /// Reads the JWT payload for display; checking the signature is the backend's job, never the dashboard's.
  static Map<String, dynamic> _claims(String jwt) {
    final parts = jwt.split('.');
    if (parts.length != 3) return {};
    try {
      return jsonDecode(utf8.decode(base64Url.decode(base64Url.normalize(parts[1])))) as Map<String, dynamic>;
    } catch (_) {
      return {};
    }
  }
}
