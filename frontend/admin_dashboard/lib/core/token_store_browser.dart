import 'package:web/web.dart' as web;

import 'token_store.dart';

TokenStore create() => _SessionStorageTokenStore();

class _SessionStorageTokenStore implements TokenStore {
  @override
  String? read(String key) => web.window.sessionStorage.getItem(key);

  @override
  void write(String key, String value) => web.window.sessionStorage.setItem(key, value);

  @override
  void delete(String key) => web.window.sessionStorage.removeItem(key);
}
