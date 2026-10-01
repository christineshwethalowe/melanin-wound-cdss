import 'package:flutter/material.dart';

import 'app/app_services.dart';
import 'features/sync/ui/sign_in_screen.dart';
import 'features/sync/ui/sync_home_screen.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final services = await AppServices.open();
  runApp(WoundCdssApp(services: services));
}

class WoundCdssApp extends StatefulWidget {
  const WoundCdssApp({super.key, required this.services});

  final AppServices services;

  @override
  State<WoundCdssApp> createState() => _WoundCdssAppState();
}

class _WoundCdssAppState extends State<WoundCdssApp> {
  late final AppLifecycleListener _lifecycle;
  bool? _signedIn;

  @override
  void initState() {
    super.initState();
    // §6.2: the app coming back to the foreground is a sync trigger.
    _lifecycle = AppLifecycleListener(onResume: widget.services.scheduler.onForeground);
    widget.services.auth.hasSession().then((has) {
      if (!mounted) return;
      setState(() => _signedIn = has);
      widget.services.scheduler.start();
    });
  }

  @override
  void dispose() {
    _lifecycle.dispose();
    widget.services.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'Wound Assessment',
        theme: ThemeData(colorSchemeSeed: Colors.teal, useMaterial3: true),
        home: switch (_signedIn) {
          null => const Scaffold(body: Center(child: CircularProgressIndicator())),
          true => SyncHomeScreen(services: widget.services, onSignedOut: () => setState(() => _signedIn = false)),
          false => SignInScreen(
              services: widget.services,
              onSignedIn: () {
                setState(() => _signedIn = true);
                widget.services.scheduler.syncNow();
              },
            ),
        },
      );
}
