import 'package:admin_dashboard/core/admin_api.dart';
import 'package:admin_dashboard/core/auth_session.dart';
import 'package:admin_dashboard/core/messages.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('no message shows a status number or a raw reason code', () {
    final errors = <Object>[
      for (var status = 400; status < 600; status++) ApiException(status),
      ApiException(400, 'SOME_NEW_CODE'),
      ApiException(500, 'INTERNAL'),
      TransportException('connection refused'),
      NeedsSignIn('INVALID_REFRESH_TOKEN'),
      StateError('boom'),
    ];
    for (final e in errors) {
      final text = describeError(e);
      expect(text, isNot(matches(RegExp(r'\d{3}'))), reason: '$e -> $text');
      expect(text, isNot(matches(RegExp(r'[A-Z]{2,}_[A-Z_]+'))), reason: '$e -> $text');
      expect(text, isNot(contains('boom')), reason: 'exception details stay out of the message');
    }
  });

  test('known codes get their own sentence', () {
    expect(describeError(ApiException(401, 'CREDENTIAL_LOCKED')), contains('locked'));
    expect(describeError(ApiException(401, 'CLIENT_NOT_ALLOWED')), contains('Only admins'));
    expect(describeError(ApiException(409, 'USERNAME_TAKEN')), contains('already in use'));
  });
}
