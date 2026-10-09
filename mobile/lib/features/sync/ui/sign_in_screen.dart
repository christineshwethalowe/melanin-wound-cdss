import 'package:flutter/material.dart';

import '../../../app/app_services.dart';
import '../api/sync_api.dart';

/// Sign in (architecture §7.3). The code field appears only when the server says MFA_REQUIRED. Capturing never needs
/// this: only syncing does, so a clinician can keep working offline and sign in later.
class SignInScreen extends StatefulWidget {
  const SignInScreen({super.key, required this.services, required this.onSignedIn});

  final AppServices services;
  final VoidCallback onSignedIn;

  /// The backend's reason codes (contracts/auth.schema.json), in words a clinician can act on.
  static String message(String? code) => switch (code) {
        'INVALID_CREDENTIALS' => 'Username or password is not right.',
        'CREDENTIAL_LOCKED' => 'Too many failed attempts. The account is locked for 15 minutes; an admin can unlock it.',
        'MFA_REQUIRED' => 'Enter the 6-digit code from your authenticator app.',
        'INVALID_TOTP' => 'That code is not right or has expired. Try the current one.',
        'DEVICE_NOT_ALLOWED' => 'This phone is not allowed for your facility. Ask an admin.',
        _ => 'Could not sign in${code == null ? '' : ' ($code)'}.',
      };

  @override
  State<SignInScreen> createState() => _SignInScreenState();
}

class _SignInScreenState extends State<SignInScreen> {
  final _username = TextEditingController();
  final _password = TextEditingController();
  final _code = TextEditingController();
  bool _needsCode = false;
  bool _busy = false;
  String? _error;

  Future<void> _submit() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.services.signIn(
        username: _username.text.trim(),
        password: _password.text,
        totp: _needsCode ? _code.text.trim() : null,
      );
      widget.onSignedIn();
    } on ApiException catch (e) {
      setState(() {
        _needsCode = _needsCode || e.code == 'MFA_REQUIRED' || e.code == 'INVALID_TOTP';
        _error = SignInScreen.message(e.code);
      });
    } on TransportException {
      setState(() => _error = 'Cannot reach the server. Your saved assessments are safe; sign in when online.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    _code.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Sign in')),
        body: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            TextField(
              key: const Key('username'),
              controller: _username,
              decoration: const InputDecoration(labelText: 'Username'),
              autocorrect: false,
              textInputAction: TextInputAction.next,
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('password'),
              controller: _password,
              decoration: const InputDecoration(labelText: 'Password'),
              obscureText: true,
            ),
            if (_needsCode) ...[
              const SizedBox(height: 12),
              TextField(
                key: const Key('totp'),
                controller: _code,
                decoration: const InputDecoration(labelText: 'Authenticator code'),
                keyboardType: TextInputType.number,
                maxLength: 6,
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 12),
              Text(_error!, key: const Key('signInError'), style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
            const SizedBox(height: 24),
            FilledButton(
              key: const Key('signInButton'),
              onPressed: _busy ? null : _submit,
              child: _busy
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Sign in'),
            ),
          ],
        ),
      );
}
