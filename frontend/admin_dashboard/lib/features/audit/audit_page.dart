import 'package:flutter/material.dart';

import '../../core/auth_session.dart';
import '../../core/messages.dart';
import '../../core/models.dart';
import '../../core/toast.dart';
import '../../core/ui.dart';

/// Recent sign-in and admin events for the facility, filtered in the browser (max 1000 rows).
class AuditPage extends StatefulWidget {
  const AuditPage({super.key, required this.session});

  final AuthSession session;

  @override
  State<AuditPage> createState() => _AuditPageState();
}

class _AuditPageState extends State<AuditPage> {
  static const _limits = [100, 250, 500, 1000];

  late Future<List<AuthAuditEntry>> _entries;
  int _limit = _limits.first;
  String _query = '';
  bool _failuresOnly = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  void _load() {
    final entries = widget.session.authorized((t) => widget.session.api.authAudit(t, limit: _limit));
    setState(() {
      _entries = entries;
    });
    reportLoadFailure(this, "Couldn't load the audit log", entries);
  }

  /// MFA_REQUIRED is the server asking for a code, not a failed attempt (same rule as the auth-health metrics).
  static bool _isFailure(AuthAuditEntry e) => !e.success && e.reasonCode != 'MFA_REQUIRED';

  bool _matches(AuthAuditEntry e) {
    if (_failuresOnly && !_isFailure(e)) return false;
    if (_query.isEmpty) return true;
    final q = _query.toLowerCase();
    return [
      e.username,
      auditActionLabel(e.action),
      if (!e.success) auditReasonLabel(e.reasonCode),
      e.deviceId,
      e.actorUsername,
    ].any((field) => field != null && field.toLowerCase().contains(q));
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      PageHeader(
        title: 'Audit log',
        subtitle: 'Sign-ins, lockouts and admin actions at your facility, newest first.',
        actions: [OutlinedButton(onPressed: _load, child: const Text('Refresh'))],
      ),
      Padding(
        padding: const EdgeInsets.fromLTRB(24, 0, 24, 12),
        child: Wrap(
          spacing: 12,
          runSpacing: 12,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox(
              width: 280,
              child: TextField(
                key: const Key('audit-filter'),
                decoration: const InputDecoration(hintText: 'Search by user, action, result or device', isDense: true),
                onChanged: (v) => setState(() => _query = v.trim()),
              ),
            ),
            FilterChip(
              key: const Key('failures-only'),
              label: const Text('Failures only'),
              selected: _failuresOnly,
              onSelected: (v) => setState(() => _failuresOnly = v),
            ),
            DropdownButton<int>(
              value: _limit,
              items: [for (final l in _limits) DropdownMenuItem(value: l, child: Text('Last $l events'))],
              onChanged: (l) {
                _limit = l!;
                _load();
              },
            ),
          ],
        ),
      ),
      Expanded(
        child: FutureBuilder(
          future: _entries,
          builder: (context, snapshot) {
            if (snapshot.hasError) return LoadError(error: snapshot.error!, onRetry: _load);
            if (!snapshot.hasData) return const Center(child: CircularProgressIndicator());
            final rows = snapshot.data!.where(_matches).toList();
            if (rows.isEmpty) return const Center(child: Text('No matching events.'));
            return _table(rows);
          },
        ),
      ),
    ],
  );

  Widget _table(List<AuthAuditEntry> rows) {
    final colors = Theme.of(context).colorScheme;
    return SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(24, 0, 24, 24),
      child: Card(
        clipBehavior: Clip.antiAlias,
        child: FullWidthTable(
          table: DataTable(
            columns: const [
              DataColumn(label: Text('Time')),
              DataColumn(label: Text('Action')),
              DataColumn(label: Text('Result')),
              DataColumn(label: Text('User')),
              DataColumn(label: Text('By admin')),
              DataColumn(label: Text('Device')),
            ],
            rows: [
              for (final e in rows)
                DataRow(
                  cells: [
                    DataCell(Text(formatDateTime(e.recordedAt))),
                    DataCell(Text(auditActionLabel(e.action))),
                    DataCell(
                      e.success
                          ? StatusChip('Succeeded', color: colors.secondary)
                          : StatusChip(
                              auditReasonLabel(e.reasonCode),
                              color: _isFailure(e) ? colors.error : colors.primary,
                            ),
                    ),
                    DataCell(Text(e.username.isEmpty ? '—' : e.username)),
                    DataCell(Text(e.actorUsername ?? '—')),
                    DataCell(Text(e.deviceId ?? '—')),
                  ],
                ),
            ],
          ),
        ),
      ),
    );
  }
}
