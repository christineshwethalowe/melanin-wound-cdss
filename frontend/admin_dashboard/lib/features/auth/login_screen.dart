import 'package:flutter/material.dart';

import '../../core/admin_api.dart';
import '../../core/auth_session.dart';
import '../../app/theme.dart';
import '../../core/toast.dart';

/// Admin sign-in; shows the code field when the server asks for MFA.
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, required this.session});

  final AuthSession session;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _form = GlobalKey<FormState>();
  final _username = TextEditingController();
  final _password = TextEditingController();
  final _totp = TextEditingController();
  final _totpFocus = FocusNode();

  bool _needsCode = false;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    // Back here because the server ended the session (not a sign-out): say so once, after the first frame.
    if (widget.session.takeSessionEndedNotice()) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) {
          Toast.show(context, title: 'Signed out', message: 'Your session has ended. Please sign in again.');
        }
      });
    }
  }

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    _totp.dispose();
    _totpFocus.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_busy || !_form.currentState!.validate()) return;
    setState(() => _busy = true);
    Toast.dismiss();
    try {
      // Success notifies the session's listeners, which swap this screen for the dashboard.
      await widget.session.signIn(
        username: _username.text.trim().toLowerCase(),
        password: _password.text,
        totp: _needsCode ? _totp.text.trim() : null,
      );
    } on Exception catch (e) {
      if (!mounted) return;
      final askForCode = e is ApiException && e.code == 'MFA_REQUIRED';
      // Being asked for a code the first time is the next step, not an error: the code field appears.
      if (!askForCode || _needsCode) showError(context, "Couldn't sign in", e);
      setState(() {
        if (askForCode) _needsCode = true;
        if (e is ApiException && e.code == 'INVALID_TOTP') _totp.clear();
      });
      if (askForCode) _totpFocus.requestFocus();
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _startOver() => setState(() {
    _needsCode = false;
    _totp.clear();
    _password.clear();
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      body: Container(
        // Midnight-blue backdrop with a white card on top.
        decoration: const BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: [AppColors.midnightDeep, AppColors.midnight, AppColors.darkBlue],
          ),
        ),
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Card(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(36, 36, 36, 28),
                  child: Form(
                    key: _form,
                    child: AutofillGroup(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            'MELANIN WOUND CDSS',
                            textAlign: TextAlign.center,
                            style: theme.textTheme.labelMedium?.copyWith(
                              color: theme.colorScheme.secondary,
                              letterSpacing: 1.6,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          const SizedBox(height: 8),
                          Text(
                            'Admin dashboard',
                            textAlign: TextAlign.center,
                            style: theme.textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w600),
                          ),
                          const SizedBox(height: 6),
                          Text(
                            'Sign in with your facility admin account',
                            textAlign: TextAlign.center,
                            style: theme.textTheme.bodyMedium?.copyWith(color: theme.colorScheme.onSurfaceVariant),
                          ),
                          const SizedBox(height: 28),
                          TextFormField(
                            key: const Key('username'),
                            controller: _username,
                            enabled: !_needsCode,
                            autofocus: true,
                            autofillHints: const [AutofillHints.username],
                            decoration: const InputDecoration(labelText: 'Username'),
                            validator: (v) => (v ?? '').trim().isEmpty ? 'Enter your username' : null,
                            textInputAction: TextInputAction.next,
                          ),
                          const SizedBox(height: 16),
                          TextFormField(
                            key: const Key('password'),
                            controller: _password,
                            enabled: !_needsCode,
                            obscureText: true,
                            autofillHints: const [AutofillHints.password],
                            decoration: const InputDecoration(labelText: 'Password'),
                            validator: (v) => (v ?? '').isEmpty ? 'Enter your password' : null,
                            onFieldSubmitted: (_) => _submit(),
                          ),
                          if (_needsCode) ...[
                            const SizedBox(height: 16),
                            TextFormField(
                              key: const Key('totp'),
                              controller: _totp,
                              focusNode: _totpFocus,
                              keyboardType: TextInputType.number,
                              autofillHints: const [AutofillHints.oneTimeCode],
                              decoration: const InputDecoration(
                                labelText: 'Authenticator code',
                                helperText: 'The 6-digit code from your authenticator app',
                              ),
                              validator: (v) =>
                                  RegExp(r'^\d{6}$').hasMatch((v ?? '').trim()) ? null : 'Enter the 6-digit code',
                              onFieldSubmitted: (_) => _submit(),
                            ),
                          ],
                          const SizedBox(height: 24),
                          FilledButton(
                            key: const Key('sign-in'),
                            onPressed: _busy ? null : _submit,
                            child: _busy
                                ? const SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2))
                                : Text(_needsCode ? 'Verify and sign in' : 'Sign in'),
                          ),
                          if (_needsCode)
                            TextButton(
                              onPressed: _busy ? null : _startOver,
                              child: const Text('Use a different account'),
                            ),
                          const SizedBox(height: 20),
                          Text(
                            'Nurses and wound specialists sign in on the mobile app.',
                            textAlign: TextAlign.center,
                            style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
