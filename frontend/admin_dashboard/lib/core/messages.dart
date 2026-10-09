import 'admin_api.dart';
import 'auth_session.dart';
import 'models.dart';

/// What went wrong, in words an admin can act on: never a status number or a reason code. Known codes from the
/// backend (contracts/auth.schema.json and the /v1/admin answers) get their own sentence; anything else falls back on
/// what the kind of answer means.
String describeError(Object error) => switch (error) {
  ApiException(code: final code?) when _codes.containsKey(code) => _codes[code]!,
  ApiException(status: 400) => 'Some of the details are not valid. Check the form and try again.',
  ApiException(status: 401) => 'Your session has ended. Please sign in again.',
  ApiException(status: 403) => 'Your account does not have permission to do this.',
  ApiException(status: 404) =>
    'This item could not be found. It may have been changed by someone else; refresh the list.',
  ApiException(status: 409) => 'This conflicts with a change made in the meantime. Refresh the list and try again.',
  ApiException(status: 413) => 'That request is too large to send.',
  ApiException(status: 429) => 'Too many attempts in a short time. Please wait a minute and try again.',
  ApiException(status: final s) when s >= 500 => 'The server had a problem. Please try again in a moment.',
  ApiException() => 'The request could not be completed. Please try again.',
  TransportException() => 'Cannot reach the server. Check your connection and try again.',
  NeedsSignIn() => 'Your session has ended. Please sign in again.',
  _ => 'Something went wrong. Please try again.',
};

/// An audit action as an admin would say it.
String auditActionLabel(String action) => switch (action) {
  'LOGIN' => 'Sign-in',
  'REFRESH' => 'Session renewed',
  'LOGOUT' => 'Sign-out',
  'LOCKOUT' => 'Account locked',
  'REGISTER' => 'Account created',
  'UNLOCK' => 'Account unlocked',
  'DEACTIVATE' => 'Account deactivated',
  'MFA_ENROLL' => 'Two-step setup started',
  'MFA_CONFIRM' => 'Two-step turned on',
  'MFA_RESET' => 'Two-step reset',
  'DEVICE_REVOKE' => 'Device revoked',
  _ => 'Other',
};

/// Why an audited attempt did not succeed, in a word or two.
String auditReasonLabel(String? reason) => switch (reason) {
  'INVALID_CREDENTIALS' => 'Wrong password',
  'CREDENTIAL_LOCKED' => 'Locked',
  'MFA_REQUIRED' => 'Code requested',
  'INVALID_TOTP' => 'Wrong code',
  'CLIENT_NOT_ALLOWED' => 'Not an admin',
  'DEVICE_NOT_ALLOWED' => 'Device revoked',
  'INVALID_REFRESH_TOKEN' => 'Session ended',
  _ => 'Failed',
};

const _codes = {
  // Sign-in
  'INVALID_CREDENTIALS': 'The username or password is incorrect.',
  'CREDENTIAL_LOCKED': 'This account is locked after too many failed attempts. Another admin can unlock it.',
  'MFA_REQUIRED': 'Enter the 6-digit code from your authenticator app.',
  'INVALID_TOTP': 'That code is not valid. Check your authenticator app and try again.',
  'CLIENT_NOT_ALLOWED': 'Only admins can sign in to the dashboard. Nurses and wound specialists use the mobile app.',
  'MISSING_FIELDS': 'Enter your username and password.',
  'DEVICE_NOT_EXPECTED': 'This sign-in is not allowed from the dashboard.',
  'DEVICE_NOT_ALLOWED': 'This device has been revoked and can no longer be used.',
  'UNKNOWN_CLIENT': 'This version of the dashboard is not recognised. Reload the page.',
  'INVALID_REFRESH_TOKEN': 'Your session has ended. Please sign in again.',
  // Clinicians
  'USERNAME_TAKEN': 'That username is already in use. Choose another one.',
  'INVALID_USERNAME':
      'Usernames are 3 to 64 characters: lowercase letters, digits, dots, dashes and underscores, '
      'starting with a letter or digit.',
  'PASSWORD_TOO_SHORT': 'The password must be at least $minPasswordLength characters.',
  'INVALID_ROLE': 'Choose a role: nurse, wound specialist or admin.',
  'MISSING_FULL_NAME': 'Enter the full name.',
  'UNKNOWN_FACILITY': 'Your facility is not registered. Contact the system administrator.',
  'CANNOT_DEACTIVATE_SELF': 'You cannot deactivate your own account. Ask another admin.',
  'NOT_FOUND': 'This item could not be found. It may have been changed by someone else; refresh the list.',
  // Devices
  'DEVICE_ALREADY_REVOKED': 'This device is already revoked.',
};
