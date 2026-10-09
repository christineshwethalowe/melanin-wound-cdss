import 'package:flutter/material.dart';

import '../../core/auth_session.dart';
import '../../core/toast.dart';
import '../../core/models.dart';
import '../../core/ui.dart';

/// The facility's registered phones. Revoking one ends its sessions and the Sync Gateway refuses it within 30 s;
/// its queued, unsynced records stay on the phone.
class DevicesPage extends StatefulWidget {
  const DevicesPage({super.key, required this.session});

  final AuthSession session;

  @override
  State<DevicesPage> createState() => _DevicesPageState();
}

class _DevicesPageState extends State<DevicesPage> {
  late Future<List<DeviceSummary>> _devices;

  @override
  void initState() {
    super.initState();
    _load();
  }

  void _load() {
    final devices = widget.session.authorized((t) => widget.session.api.devices(t));
    setState(() {
      _devices = devices;
    });
    reportLoadFailure(this, "Couldn't load devices", devices);
  }

  Future<void> _revoke(DeviceSummary d) async {
    final ok = await confirm(
      context,
      title: 'Revoke ${d.deviceId}?',
      message:
          'Use this for a lost or stolen phone. It is signed out, cannot sign in or sync again, and this '
          'cannot be undone. Records not yet synced from it stay on the phone.',
      action: 'Revoke device',
      destructive: true,
    );
    if (!ok) return;
    try {
      await widget.session.authorized((t) => widget.session.api.revokeDevice(t, d.deviceId));
      if (mounted) showSuccess(context, 'Device revoked', message: '${d.deviceId} can no longer sign in or sync.');
    } on Exception catch (e) {
      if (mounted) showError(context, "Couldn't revoke ${d.deviceId}", e);
    }
    _load();
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      PageHeader(
        title: 'Devices',
        subtitle: 'Phones registered at your facility. Revoke a lost or stolen one.',
        actions: [OutlinedButton(onPressed: _load, child: const Text('Refresh'))],
      ),
      Expanded(
        child: FutureBuilder(
          future: _devices,
          builder: (context, snapshot) {
            if (snapshot.hasError) return LoadError(error: snapshot.error!, onRetry: _load);
            if (!snapshot.hasData) return const Center(child: CircularProgressIndicator());
            final devices = snapshot.data!;
            if (devices.isEmpty) return const Center(child: Text('No devices have signed in yet.'));
            return _table(devices);
          },
        ),
      ),
    ],
  );

  Widget _table(List<DeviceSummary> devices) {
    final colors = Theme.of(context).colorScheme;
    return SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(24, 0, 24, 24),
      child: Card(
        clipBehavior: Clip.antiAlias,
        child: FullWidthTable(
          table: DataTable(
            columns: const [
              DataColumn(label: Text('Device')),
              DataColumn(label: Text('Status')),
              DataColumn(label: Text('Last user')),
              DataColumn(label: Text('Last seen')),
              DataColumn(label: Text('Sessions'), numeric: true),
              DataColumn(label: Text('Registered')),
              DataColumn(label: Text('')),
            ],
            rows: [
              for (final d in devices)
                DataRow(
                  cells: [
                    DataCell(SelectableText(d.deviceId)),
                    DataCell(
                      d.revoked
                          ? Tooltip(
                              message: 'Revoked ${formatDateTime(d.revokedAt)}',
                              child: StatusChip('Revoked', color: colors.error),
                            )
                          : StatusChip('Active', color: colors.secondary),
                    ),
                    DataCell(Text(d.lastUsername ?? '—')),
                    DataCell(Text(formatDateTime(d.lastSeenAt))),
                    DataCell(Text('${d.activeSessions}')),
                    DataCell(Text(formatDateTime(d.registeredAt))),
                    DataCell(
                      d.revoked
                          ? const SizedBox.shrink()
                          : TextButton(
                              key: Key('revoke-${d.deviceId}'),
                              style: TextButton.styleFrom(foregroundColor: colors.error),
                              onPressed: () => _revoke(d),
                              child: const Text('Revoke'),
                            ),
                    ),
                  ],
                ),
            ],
          ),
        ),
      ),
    );
  }
}
