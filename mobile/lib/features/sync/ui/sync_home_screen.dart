import 'dart:convert';

import 'package:flutter/material.dart';

import '../../../app/app_services.dart';
import '../data/app_database.dart';
import '../data/queue_repository.dart';
import '../data/tables.dart';
import '../engine/sync_engine.dart';
import '../engine/sync_scheduler.dart';
import 'sample_assessment.dart';

/// The sync status screen (architecture §6.1): what is waiting, what is synced, where advice is, and when the
/// last sync ran. Pull to refresh syncs now. "Save sample assessment" stands in for the capture flow of
/// Members 1–2 until it exists.
class SyncHomeScreen extends StatelessWidget {
  const SyncHomeScreen({super.key, required this.services, required this.onSignedOut});

  final AppServices services;
  final VoidCallback onSignedOut;

  static String statusLabel(QueueStatus s) => switch (s) {
        QueueStatus.pending => 'Waiting to sync',
        QueueStatus.inFlight => 'Sending',
        QueueStatus.accepted => 'Synced',
        QueueStatus.rejected => 'Rejected',
        QueueStatus.adviceDeferred => 'Advice delayed',
        QueueStatus.complete => 'Advice ready',
        QueueStatus.superseded => 'Replaced by edit',
      };

  static String outcomeLabel(SyncReport r) => switch (r.outcome) {
        SyncOutcome.success => 'Up to date',
        SyncOutcome.alreadyRunning => 'Sync already running',
        SyncOutcome.backingOff => 'Waiting to retry',
        SyncOutcome.offline => 'Offline: will retry',
        SyncOutcome.serverBusy => 'Server busy: will retry',
        SyncOutcome.needsSignIn => 'Sign in to sync',
      };

  Future<void> _saveSample(BuildContext context) async {
    final messenger = ScaffoldMessenger.of(context);
    final who = await services.cachedClinician();
    if (who == null || who.facilityId.isEmpty) {
      messenger.showSnackBar(const SnackBar(content: Text('Sign in once on this phone before saving assessments.')));
      return;
    }
    try {
      final event = sampleAssessment(deviceId: await services.queue.deviceId(), facilityId: who.facilityId);
      await services.queue.enqueue(event); // committed before "saved" is shown (§6)
      services.scheduler.onSaved();
      messenger.showSnackBar(const SnackBar(content: Text('Saved. It will sync automatically.')));
    } on EnqueueRejected catch (e) {
      // Never silent: the clinician sees why nothing was saved.
      messenger.showSnackBar(SnackBar(content: Text('Not saved: ${e.errors.first}')));
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          title: const Text('Wound assessments'),
          actions: [
            IconButton(
              key: const Key('signOut'),
              tooltip: 'Sign out',
              icon: const Icon(Icons.logout),
              onPressed: () async {
                await services.signOut();
                onSignedOut();
              },
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton.extended(
          key: const Key('saveSample'),
          onPressed: () => _saveSample(context),
          icon: const Icon(Icons.add),
          label: const Text('Save sample assessment'),
        ),
        body: RefreshIndicator(
          onRefresh: () => services.scheduler.syncNow(),
          child: ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
            children: [
              _StatusCard(services: services, onSignInNeeded: onSignedOut),
              const SizedBox(height: 12),
              StreamBuilder<QueueCounts>(
                stream: services.queue.watchCounts(),
                builder: (context, snap) => _Counts(counts: snap.data ?? const QueueCounts({})),
              ),
              const SizedBox(height: 12),
              StreamBuilder<List<QueuedEvent>>(
                stream: services.queue.watchRecent(),
                builder: (context, snap) {
                  final rows = snap.data ?? const [];
                  if (rows.isEmpty) {
                    return const Padding(
                      padding: EdgeInsets.all(24),
                      child: Text('No assessments saved on this phone yet.', textAlign: TextAlign.center),
                    );
                  }
                  return Column(children: [for (final r in rows) _RecordTile(row: r, services: services)]);
                },
              ),
            ],
          ),
        ),
      );
}

class _StatusCard extends StatelessWidget {
  const _StatusCard({required this.services, required this.onSignInNeeded});

  final AppServices services;
  final VoidCallback onSignInNeeded;

  @override
  Widget build(BuildContext context) => ValueListenableBuilder<SyncStatus>(
        valueListenable: services.scheduler.status,
        builder: (context, status, _) {
          final last = status.last;
          final needsSignIn = last?.outcome == SyncOutcome.needsSignIn;
          return Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(children: [
                    Icon(needsSignIn
                        ? Icons.lock_outline
                        : last?.outcome == SyncOutcome.success
                            ? Icons.cloud_done_outlined
                            : Icons.cloud_off_outlined),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        status.running ? 'Syncing…' : (last == null ? 'Not synced yet' : SyncHomeScreen.outcomeLabel(last)),
                        key: const Key('syncOutcome'),
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                    ),
                    if (status.running)
                      const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
                    else
                      TextButton(
                        key: const Key('syncNow'),
                        onPressed: needsSignIn ? onSignInNeeded : () => services.scheduler.syncNow(),
                        child: Text(needsSignIn ? 'Sign in' : 'Sync now'),
                      ),
                  ]),
                  if (status.lastRunAt != null)
                    Text('Last checked ${TimeOfDay.fromDateTime(status.lastRunAt!).format(context)}'),
                  if (last?.retryAt != null)
                    Text('Next try ${TimeOfDay.fromDateTime(last!.retryAt!.toLocal()).format(context)}'),
                  const Text('Saved assessments are kept on this phone until the server confirms them.'),
                ],
              ),
            ),
          );
        },
      );
}

class _Counts extends StatelessWidget {
  const _Counts({required this.counts});

  final QueueCounts counts;

  @override
  Widget build(BuildContext context) => Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          for (final (label, n, key) in [
            ('Waiting', counts.waitingToSend, 'countWaiting'),
            ('Synced', counts.of(QueueStatus.accepted) + counts.of(QueueStatus.adviceDeferred), 'countSynced'),
            ('Advice ready', counts.of(QueueStatus.complete), 'countComplete'),
            ('Rejected', counts.of(QueueStatus.rejected), 'countRejected'),
          ])
            Chip(key: Key(key), label: Text('$label: $n')),
        ],
      );
}

class _RecordTile extends StatelessWidget {
  const _RecordTile({required this.row, required this.services});

  final QueuedEvent row;
  final AppServices services;

  @override
  Widget build(BuildContext context) {
    final analytics = (jsonDecode(row.payloadJson) as Map<String, dynamic>)['analytics'] as Map<String, dynamic>;
    return ListTile(
      contentPadding: EdgeInsets.zero,
      title: Text('${analytics['areaMm2']} mm² · revision ${row.revision}'),
      subtitle: Text(row.status == QueueStatus.rejected
          ? 'Rejected: ${row.lastError ?? ''}'
          : '${SyncHomeScreen.statusLabel(row.status)} · saved ${TimeOfDay.fromDateTime(row.createdAt.toLocal()).format(context)}'),
      trailing: row.status == QueueStatus.complete ? const Icon(Icons.chevron_right) : null,
      onTap: row.status == QueueStatus.complete ? () => _showAdvice(context) : null,
    );
  }

  /// A plain view of the delivered advice. Member 3 owns the real recommendation screens (citations, refusals,
  /// figures); this only shows that the advice arrived.
  Future<void> _showAdvice(BuildContext context) async {
    final rec = await services.queue.recommendationFor(row.assessmentId, row.revision);
    if (rec == null || !context.mounted) return;
    final payload = jsonDecode(rec.payloadJson) as Map<String, dynamic>;
    final sections = (payload['sections'] as List? ?? const []).cast<Map<String, dynamic>>();
    await showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Advice (${rec.mode})'),
        content: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              for (final s in sections) ...[
                Text(s['heading'] as String? ?? '', style: Theme.of(context).textTheme.titleSmall),
                Text(s['text'] as String? ?? ''),
                const SizedBox(height: 8),
              ],
              if (sections.isEmpty) const Text('No sections in this answer.'),
            ],
          ),
        ),
        actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('Close'))],
      ),
    );
  }
}
