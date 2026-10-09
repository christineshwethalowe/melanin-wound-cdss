import 'package:flutter/material.dart';

import '../../core/auth_session.dart';
import '../../core/toast.dart';
import '../../core/models.dart';

/// Register-clinician dialog that checks the server's rules up front and returns the new username.
class RegisterClinicianDialog extends StatefulWidget {
  const RegisterClinicianDialog({super.key, required this.session});

  final AuthSession session;

  @override
  State<RegisterClinicianDialog> createState() => _RegisterClinicianDialogState();
}

class _RegisterClinicianDialogState extends State<RegisterClinicianDialog> {
  final _form = GlobalKey<FormState>();
  final _fullName = TextEditingController();
  final _username = TextEditingController();
  final _password = TextEditingController();
  String _role = clinicianRoles.first;
  bool _busy = false;

  @override
  void dispose() {
    _fullName.dispose();
    _username.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_busy || !_form.currentState!.validate()) return;
    setState(() => _busy = true);
    Toast.dismiss();
    final username = _username.text.trim().toLowerCase();
    try {
      await widget.session.authorized(
        (t) => widget.session.api.registerClinician(
          t,
          username: username,
          password: _password.text,
          fullName: _fullName.text.trim(),
          role: _role,
        ),
      );
      if (mounted) Navigator.pop(context, username);
    } on Exception catch (e) {
      // The form stays open with what was typed; the toast shows above the dialog.
      if (mounted) showError(context, "Couldn't register $username", e);
      if (e is NeedsSignIn && mounted) Navigator.pop(context);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Register clinician'),
    content: SizedBox(
      width: 420,
      child: Form(
        key: _form,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextFormField(
              key: const Key('reg-full-name'),
              controller: _fullName,
              autofocus: true,
              decoration: const InputDecoration(labelText: 'Full name'),
              validator: (v) => (v ?? '').trim().isEmpty ? 'Enter the full name' : null,
            ),
            const SizedBox(height: 12),
            TextFormField(
              key: const Key('reg-username'),
              controller: _username,
              decoration: const InputDecoration(
                labelText: 'Username',
                helperText: 'e.g. n.silva — lowercase letters, digits, . _ -',
              ),
              validator: (v) =>
                  usernamePattern.hasMatch((v ?? '').trim().toLowerCase()) ? null : 'Use 3–64 of a–z, 0–9, . _ -',
            ),
            const SizedBox(height: 12),
            TextFormField(
              key: const Key('reg-password'),
              controller: _password,
              obscureText: true,
              decoration: InputDecoration(
                labelText: 'Initial password',
                helperText: 'At least $minPasswordLength characters. Share it with the clinician in person.',
              ),
              validator: (v) => (v ?? '').length < minPasswordLength ? 'At least $minPasswordLength characters' : null,
            ),
            const SizedBox(height: 12),
            DropdownButtonFormField<String>(
              key: const Key('reg-role'),
              initialValue: _role,
              decoration: const InputDecoration(labelText: 'Role'),
              items: [for (final r in clinicianRoles) DropdownMenuItem(value: r, child: Text(roleLabel(r)))],
              onChanged: (r) => setState(() => _role = r!),
            ),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(onPressed: _busy ? null : () => Navigator.pop(context), child: const Text('Cancel')),
      FilledButton(
        key: const Key('reg-submit'),
        onPressed: _busy ? null : _submit,
        child: _busy
            ? const SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2))
            : const Text('Register'),
      ),
    ],
  );
}
