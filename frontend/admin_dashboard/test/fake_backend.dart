import 'dart:convert';

import 'package:admin_dashboard/core/admin_api.dart';
import 'package:admin_dashboard/core/auth_session.dart';
import 'package:admin_dashboard/core/token_store.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

/// An unsigned JWT with the dashboard's claims; the dashboard only reads the payload.
String fakeJwt({String sub = 'c-1', String role = 'admin', String facility = 'fac-001'}) {
  String part(Map<String, Object> m) => base64Url.encode(utf8.encode(jsonEncode(m))).replaceAll('=', '');
  return '${part({'alg': 'none'})}.${part({'sub': sub, 'role': role, 'facility_id': facility})}.sig';
}

String tokens(int n, {int expiresIn = 900}) =>
    jsonEncode({'accessToken': fakeJwt(), 'refreshToken': 'refresh-$n', 'expiresIn': expiresIn});

/// Routes requests to [handler] and records them.
class FakeBackend {
  FakeBackend(this.handler);

  final Future<http.Response> Function(http.Request request) handler;
  final List<http.Request> requests = [];

  late final client = MockClient((request) {
    requests.add(request);
    return handler(request);
  });

  AdminApi api() => AdminApi(Uri.parse('http://gateway.test'), client: client);

  AuthSession session({TokenStore? store, DateTime Function()? clock}) =>
      AuthSession(api(), store ?? MemoryTokenStore(), clock: clock);

  int count(String path) => requests.where((r) => r.url.path == path).length;
}

http.Response json(Object body, [int status = 200]) =>
    http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json'});

http.Response code(String code, int status) => json({'code': code}, status);
