import 'package:flutter/material.dart';

import '../../core/auth_session.dart';
import '../../core/toast.dart';
import '../../core/models.dart';
import '../../core/ui.dart';
import 'register_clinician_dialog.dart';

/// Facility clinicians: register, unlock, reset MFA or deactivate (all audited).
class CliniciansPage extends StatefulWidget {
  const CliniciansPage({super.key, required this.session});

  final AuthSession session;

  @override
  State<CliniciansPage> createState() => _CliniciansPageState();
}

enum _Action { unlock, resetMfa, deactivate }

class _CliniciansPageState extends State<CliniciansPage> {
  late Future<List<ClinicianSummary>> _clinicians;

  @override
  void initState() {
    super.initState();
    _load();
  }

  void _load() {
    final clinicians = widget.session.authorized((t) => widget.session.api.clinicians(t));
    setState(() {
      _clinicians = clinicians;
    });
    reportLoadFailure(this, "Couldn't load clinicians", clinicians);
  }

  Future<void> _register() async {
    final created = await showDialog<String>(
      context: context,
      barrierDismissible: false,
      builder: (_) => RegisterClinicianDialog(session: widget.session),
    );
    if (created == null || !mounted) return;
    showSuccess(context, 'Clinician registered', message: '$created can now sign in on the mobile app.');
    _load();
  }

  Future<void> _run(_Action action, ClinicianSummary c) async {
    final api = widget.session.api;
    final (title, message, label, destructive, done, failed, call) = switch (action) {
      _Action.unlock => (
        'Unlock ${c.username}?',
        'Clears the failed sign-in count so ${c.fullName} can sign in again.',
        'Unlock',
        false,
        'Unlocked ${c.username}.',
        "Couldn't unlock ${c.username}",
        (String t) => api.unlock(t, c.username),
      ),
      _Action.resetMfa => (
        'Reset MFA for ${c.username}?',
        '${c.fullName} will sign in with password only until they enrol a new authenticator. '
            'Do this only after confirming who is asking.',
        'Reset MFA',
        true,
        'MFA reset for ${c.username}.',
        "Couldn't reset MFA for ${c.username}",
        (String t) => api.resetMfa(t, c.username),
      ),
      _Action.deactivate => (
        'Deactivate ${c.username}?',
        '${c.fullName} is signed out on every device and can no longer sign in. '
            'Records they captured are kept.',
        'Deactivate',
        true,
        'Deactivated ${c.username}.',
        "Couldn't deactivate ${c.username}",
        (String t) => api.deactivate(t, c.username),
      ),
    };
    if (!await confirm(context, title: title, message: message, action: label, destructive: destructive)) return;
    try {
      await widget.session.authorized(call);
      if (mounted) showSuccess(context, done);
    } on Exception catch (e) {
      if (mounted) showError(context, failed, e);
    }
    _load();
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      PageHeader(
        title: 'Clinicians',
        subtitle: 'Everyone who can sign in at your facility.',
        actions: [
          OutlinedButton(onPressed: _load, child: const Text('Refresh')),
          FilledButton(
            key: const Key('register-clinician'),
            onPressed: _register,
            child: const Text('Register clinician'),
          ),
        ],
      ),
      Expanded(
        child: FutureBuilder(
          future: _clinicians,
          builder: (context, snapshot) {
            if (snapshot.hasError) return LoadError(error: snapshot.error!, onRetry: _load);
            if (!snapshot.hasData) return const Center(child: CircularProgressIndicator());
            final clinicians = snapshot.data!;
            if (clinicians.isEmpty) return const Center(child: Text('No clinicians yet.'));
            return _table(clinicians);
          },
        ),
      ),
    ],
  );

  Widget _table(List<ClinicianSummary> clinicians) {
    final colors = Theme.of(context).colorScheme;
    final me = widget.session.admin?.username;
    return SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(24, 0, 24, 24),
      child: Card(
        clipBehavior: Clip.antiAlias,
        child: FullWidthTable(
          table: DataTable(
            columns: const [
              DataColumn(label: Text('Name')),
              DataColumn(label: Text('Username')),
              DataColumn(label: Text('Role')),
              DataColumn(label: Text('Status')),
              DataColumn(label: Text('Created')),
              DataColumn(label: Text('')),
            ],
            rows: [
              for (final c in clinicians)
                DataRow(
                  cells: [
                    DataCell(Text(c.fullName)),
                    DataCell(Text(c.username + (c.username == me ? ' (you)' : ''))),
                    DataCell(Text(roleLabel(c.role))),
                    DataCell(
                      Wrap(
                        spacing: 6,
                        runSpacing: 4,
                        children: [
                          c.active
                              ? StatusChip('Active', color: colors.secondary)
                              : StatusChip('Deactivated', color: colors.outline),
                          if (c.locked) StatusChip('Locked', color: colors.error),
                          if (c.mfaEnabled) StatusChip('MFA on', color: colors.primary),
                        ],
                      ),
                    ),
                    DataCell(Text(formatDateTime(c.createdAt))),
                    DataCell(_actions(c, isSelf: c.username == me)),
                  ],
                ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _actions(ClinicianSummary c, {required bool isSelf}) {
    final available = [
      if (c.locked) (_Action.unlock, 'Unlock'),
      if (c.mfaEnabled) (_Action.resetMfa, 'Reset MFA'),
      if (c.active && !isSelf) (_Action.deactivate, 'Deactivate'),
    ];
    if (available.isEmpty) return const SizedBox.shrink();
    return PopupMenuButton<_Action>(
      key: Key('actions-${c.username}'),
      tooltip: 'Actions',
      onSelected: (a) => _run(a, c),
      itemBuilder: (_) => [for (final (action, label) in available) PopupMenuItem(value: action, child: Text(label))],
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        child: Text('Actions', style: TextStyle(color: Theme.of(context).colorScheme.primary)),
      ),
    );
  }
}
