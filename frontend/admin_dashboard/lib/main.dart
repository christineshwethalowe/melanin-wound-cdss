import 'package:flutter/material.dart';

import 'app/dashboard_shell.dart';
import 'app/theme.dart';
import 'core/admin_api.dart';
import 'core/auth_session.dart';
import 'core/config.dart';
import 'core/token_store.dart';
import 'features/auth/login_screen.dart';

void main() {
  final session = AuthSession(AdminApi(Uri.parse(AppConfig.apiBaseUrl)), TokenStore.platform());
  runApp(AdminDashboardApp(session: session));
}

class AdminDashboardApp extends StatefulWidget {
  const AdminDashboardApp({super.key, required this.session});

  final AuthSession session;

  @override
  State<AdminDashboardApp> createState() => _AdminDashboardAppState();
}

class _AdminDashboardAppState extends State<AdminDashboardApp> {
  bool _restoring = true;

  @override
  void initState() {
    super.initState();
    // A reload keeps the tab's refresh token; resume that session instead of asking for a password again.
    widget.session.restore().whenComplete(() {
      if (mounted) setState(() => _restoring = false);
    });
  }

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Wound CDSS admin',
    debugShowCheckedModeBanner: false,
    theme: AppTheme.light(),
    themeMode: ThemeMode.light,
    home: ListenableBuilder(
      listenable: widget.session,
      builder: (context, _) => _restoring
          ? const Scaffold(body: Center(child: CircularProgressIndicator()))
          : widget.session.signedIn
          ? DashboardShell(session: widget.session)
          : LoginScreen(session: widget.session),
    ),
  );
}
