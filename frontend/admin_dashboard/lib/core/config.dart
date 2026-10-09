/// Build-time settings. The dashboard talks only to the API gateway (ADR 0004, 0005), never to a service directly.
///
/// `flutter run -d chrome --web-port 3001 --dart-define=API_BASE_URL=http://localhost:8080`
abstract final class AppConfig {
  static const apiBaseUrl = String.fromEnvironment('API_BASE_URL', defaultValue: 'http://localhost:8080');

  /// The client the identity service issues dashboard sessions to: role admin only, no device (ADR 0005).
  static const clientId = 'admin-dashboard';
}
