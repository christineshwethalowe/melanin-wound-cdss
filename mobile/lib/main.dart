import 'package:flutter/material.dart';

void main() {
  runApp(const WoundCdssApp());
}

class WoundCdssApp extends StatelessWidget {
  const WoundCdssApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Wound Assessment',
      theme: ThemeData(colorSchemeSeed: Colors.teal, useMaterial3: true),
      home: const Scaffold(
        body: Center(child: Text('J26-SE-330 wound assessment')),
      ),
    );
  }
}
