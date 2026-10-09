/// Build-time settings; e.g. --dart-define=API_BASE_URL=http://localhost:8080.
abstract final class AppConfig {
  static const apiBaseUrl = String.fromEnvironment('API_BASE_URL', defaultValue: 'http://localhost:8080');

  /// The client the identity service issues dashboard sessions to: role admin only, no device (ADR 0005).
  static const clientId = 'admin-dashboard';
}
