import 'token_store_memory.dart' if (dart.library.js_interop) 'token_store_browser.dart' as platform;

/// Holds the refresh token in sessionStorage, so it's gone when the tab closes.
abstract class TokenStore {
  /// sessionStorage in the browser; memory elsewhere (tests).
  factory TokenStore.platform() => platform.create();

  String? read(String key);
  void write(String key, String value);
  void delete(String key);
}

class MemoryTokenStore implements TokenStore {
  final Map<String, String> values = {};

  @override
  String? read(String key) => values[key];

  @override
  void write(String key, String value) => values[key] = value;

  @override
  void delete(String key) => values.remove(key);
}
