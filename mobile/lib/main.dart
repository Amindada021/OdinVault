import 'package:odinvault_mobile/odinvault/app.dart';
import 'package:odinvault_mobile/odinvault/monitoring_service.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  try {
    await MonitoringService.initialize();
  } catch (e, stackTrace) {
    debugPrint('OdinVault monitoring initialization failed: $e\n$stackTrace');
  }
  runApp(const OdinVaultApp());
}
