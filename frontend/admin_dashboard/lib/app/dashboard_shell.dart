import 'package:flutter/material.dart';

import '../core/auth_session.dart';
import 'theme.dart';
import '../features/audit/audit_page.dart';
import '../features/clinicians/clinicians_page.dart';
import '../features/devices/devices_page.dart';

/// The signed-in frame: navigation, who is signed in and for which facility, and sign-out. Every page is scoped to
/// that facility by the server.
class DashboardShell extends StatefulWidget {
  const DashboardShell({super.key, required this.session});

  final AuthSession session;

  @override
  State<DashboardShell> createState() => _DashboardShellState();
}

class _DashboardShellState extends State<DashboardShell> {
  int _index = 0;

  static const _labels = ['Clinicians', 'Devices', 'Audit log'];

  Widget _page() => switch (_index) {
    0 => CliniciansPage(session: widget.session),
    1 => DevicesPage(session: widget.session),
    _ => AuditPage(session: widget.session),
  };

  @override
  Widget build(BuildContext context) {
    final admin = widget.session.admin!;
    return DefaultTabController(
      length: _labels.length,
      child: Scaffold(
        appBar: AppBar(
          title: const Text.rich(
            TextSpan(
              children: [
                TextSpan(text: 'Wound CDSS'),
                TextSpan(
                  text: '  ADMIN',
                  style: TextStyle(color: AppColors.tealBright, fontSize: 12, letterSpacing: 1.6),
                ),
              ],
            ),
          ),
          actions: [
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(
                    admin.username,
                    key: const Key('signed-in-as'),
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                  Text(
                    'Facility ${admin.facilityId}',
                    style: TextStyle(fontSize: 12, color: AppColors.white.withValues(alpha: 0.7)),
                  ),
                ],
              ),
            ),
            OutlinedButton(
              key: const Key('sign-out'),
              style: OutlinedButton.styleFrom(
                foregroundColor: AppColors.white,
                side: BorderSide(color: AppColors.white.withValues(alpha: 0.4)),
                minimumSize: const Size(0, 36),
              ),
              onPressed: widget.session.signOut,
              child: const Text('Sign out'),
            ),
            const SizedBox(width: 8),
          ],
          bottom: TabBar(
            isScrollable: true,
            tabAlignment: TabAlignment.start,
            onTap: (i) => setState(() => _index = i),
            tabs: [for (final label in _labels) Tab(text: label)],
          ),
        ),
        body: KeyedSubtree(key: ValueKey(_index), child: _page()),
      ),
    );
  }
}
