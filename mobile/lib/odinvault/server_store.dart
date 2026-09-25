import 'dart:convert';

import 'package:odinvault_mobile/odinvault/models.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

class OdinVaultServerStore {
  OdinVaultServerStore({FlutterSecureStorage? storage})
      : _storage = storage ?? const FlutterSecureStorage();

  static const _key = 'odinvault_servers_v1';
  final FlutterSecureStorage _storage;

  Future<List<OdinVaultServer>> load() async {
    final raw = await _storage.read(key: _key);
    if (raw == null || raw.isEmpty) return <OdinVaultServer>[];
    final decoded = jsonDecode(raw);
    if (decoded is! List) return <OdinVaultServer>[];
    return decoded
        .whereType<Map>()
        .map((e) => OdinVaultServer.fromJson(Map<String, dynamic>.from(e)))
        .toList();
  }

  Future<void> save(List<OdinVaultServer> servers) => _storage.write(
        key: _key,
        value: jsonEncode(servers.map((e) => e.toJson()).toList()),
      );
}
