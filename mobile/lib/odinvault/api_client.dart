import 'package:odinvault_mobile/odinvault/models.dart';
import 'package:dio/dio.dart';

class OdinVaultApiClient {
  OdinVaultApiClient(OdinVaultServer server)
      : _dio = Dio(BaseOptions(
          baseUrl: server.baseUrl.replaceAll(RegExp(r'/$'), ''),
          connectTimeout: const Duration(seconds: 10),
          receiveTimeout: const Duration(minutes: 120),
          sendTimeout: const Duration(minutes: 120),
          headers: {'Accept': 'application/json', if (server.apiKey.isNotEmpty) 'X-OdinVault-Key': server.apiKey},
        ));
  final Dio _dio;
  String get baseUrl => _dio.options.baseUrl;

  Future<bool> health() async => (await _dio.get<Object>('/api/health')).statusCode == 200;

  Future<List<OdinVaultDatabase>> databases() async {
    final data = (await _dio.get<Object>('/api/databases')).data;
    if (data is! List) throw const OdinVaultApiException('Invalid database response.');
    return data.whereType<Map>().map((e) => OdinVaultDatabase.fromJson(Map<String, dynamic>.from(e))).toList();
  }

  Future<OdinVaultDatabase> database(String id) async => OdinVaultDatabase.fromJson(_json((await _dio.get<Object>('/api/databases/$id')).data));

  Future<void> createDatabase({required String name, required String host, int? port, required String databaseName, required String username, required String password, required bool trustServerCertificate, required String backupDirectory, required int maxLocalBackups, required bool verifyAfterBackup, String? scheduleCron, required bool isEnabled}) async {
    await _dio.post<Object>('/api/databases', data: {
      'name': name, 'host': host, 'port': port, 'databaseName': databaseName, 'username': username, 'password': password,
      'trustServerCertificate': trustServerCertificate, 'backupDirectory': backupDirectory, 'maxLocalBackups': maxLocalBackups,
      'verifyAfterBackup': verifyAfterBackup, 'scheduleCron': scheduleCron, 'isEnabled': isEnabled,
    });
  }

  Future<void> updateDatabase(OdinVaultDatabase db, {required String name, required String host, int? port, required String databaseName, required String username, String? password, bool clearPassword = false, required bool trustServerCertificate, required bool isEnabled}) async {
    await _dio.put<Object>('/api/databases/${db.id}', data: {
      'name': name, 'host': host, 'port': port, 'databaseName': databaseName, 'username': username, 'password': password,
      'clearPassword': clearPassword, 'trustServerCertificate': trustServerCertificate, 'isEnabled': isEnabled,
    });
  }

  Future<void> updatePolicy(String databaseId, {required String backupDirectory, required int maxLocalBackups, required bool verifyAfterBackup, String? scheduleCron, required bool isEnabled}) async {
    await _dio.put<Object>('/api/databases/$databaseId/policy', data: {
      'backupDirectory': backupDirectory, 'maxLocalBackups': maxLocalBackups, 'verifyAfterBackup': verifyAfterBackup,
      'scheduleCron': scheduleCron, 'isEnabled': isEnabled,
    });
  }

  Future<void> deleteDatabase(String id, {bool deleteHistory = true, bool deleteFiles = false}) async =>
      _dio.delete<Object>(
        '/api/databases/$id',
        queryParameters: {'deleteHistory': deleteHistory, 'deleteFiles': deleteFiles},
      );

  Future<List<DiscoveredDatabase>> discoverDatabases({
    required String host,
    int? port,
    required String username,
    required String password,
    required bool trustServerCertificate,
  }) async {
    final data = (await _dio.post<Object>('/api/sql-server/discover', data: {
      'host': host,
      'port': port,
      'username': username.isEmpty ? null : username,
      'password': password.isEmpty ? null : password,
      'trustServerCertificate': trustServerCertificate,
    })).data;
    if (data is! List) throw const OdinVaultApiException('پاسخ شناسایی دیتابیس‌ها نامعتبر است.');
    return data
        .whereType<Map>()
        .map((e) => DiscoveredDatabase.fromJson(Map<String, dynamic>.from(e)))
        .toList();
  }

  Future<void> testDatabase(String id) async => _dio.post<Object>('/api/databases/$id/test');
  Future<OdinVaultBackup> backupNow(String id) async => OdinVaultBackup.fromJson(_json((await _dio.post<Object>('/api/databases/$id/backups')).data));

  Future<List<OdinVaultBackup>> backups(String id, {int take = 100}) async {
    final data = (await _dio.get<Object>('/api/databases/$id/backups', queryParameters: {'take': take})).data;
    if (data is! List) throw const OdinVaultApiException('Invalid backup history response.');
    return data.whereType<Map>().map((e) => OdinVaultBackup.fromJson(Map<String, dynamic>.from(e))).toList();
  }

  Future<List<OdinVaultStorageTarget>> storageTargets() async {
    final data = (await _dio.get<Object>('/api/storage-targets')).data;
    if (data is! List) throw const OdinVaultApiException('Invalid storage target response.');
    return data.whereType<Map>().map((e) => OdinVaultStorageTarget.fromJson(Map<String, dynamic>.from(e))).toList();
  }

  Future<void> createReplicaTarget({required String name, required String baseUrl, required String apiKey}) async =>
      _dio.post<Object>(
        '/api/storage-targets/odinvault-replica',
        data: {
          'name': name,
          'baseUrl': baseUrl,
          'apiKey': apiKey,
          'isEnabled': true,
          'testConnection': true,
        },
      );
  Future<void> testReplicaTarget(String id) async => _dio.post<Object>('/api/storage-targets/$id/test');

  Future<GoogleDrivePairingStart> startGoogleDrivePairing({required String targetName, String? folderId}) async {
    final redirectUri = '${baseUrl.replaceAll(RegExp(r'/$'), '')}/api/storage-targets/google-drive/callback';
    final data = _json((await _dio.post<Object>('/api/storage-targets/google-drive/pair/start', data: {'targetName': targetName, 'redirectUri': redirectUri, 'folderId': folderId})).data);
    return GoogleDrivePairingStart(authorizationUrl: data['authorizationUrl'].toString(), state: data['state'].toString());
  }

  Future<GoogleDrivePairingStatus> googleDrivePairingStatus(String state) async {
    final data = _json((await _dio.get<Object>('/api/storage-targets/google-drive/pair/status/$state')).data);
    return GoogleDrivePairingStatus(status: data['status']?.toString() ?? 'pending', storageTargetId: data['storageTargetId']?.toString(), error: data['error']?.toString());
  }

  Future<List<DatabaseStorageLink>> databaseStorageTargets(String databaseId) async {
    final data = (await _dio.get<Object>('/api/databases/$databaseId/storage-targets')).data;
    if (data is! List) throw const OdinVaultApiException('پاسخ مقصدهای ذخیره‌سازی نامعتبر است.');
    return data
        .whereType<Map>()
        .map((e) => DatabaseStorageLink.fromJson(Map<String, dynamic>.from(e)))
        .toList();
  }

  Future<void> linkStorageTarget(String databaseId, String targetId, {bool enabled = true}) async =>
      _dio.put<Object>('/api/databases/$databaseId/storage-targets/$targetId', data: {'isEnabled': enabled});

  Future<void> unlinkStorageTarget(String databaseId, String targetId) async =>
      _dio.delete<Object>('/api/databases/$databaseId/storage-targets/$targetId');

  Future<void> updateStorageTarget(
    String id, {
    required String name,
    String? folderId,
    required bool isEnabled,
  }) async =>
      _dio.put<Object>('/api/storage-targets/$id', data: {
        'name': name,
        'folderId': folderId,
        'isEnabled': isEnabled,
      });

  Future<void> deleteStorageTarget(String id) async =>
      _dio.delete<Object>('/api/storage-targets/$id');

  Future<List<OdinVaultReplica>> replicas(String backupId) async {
    final data = (await _dio.get<Object>('/api/backups/$backupId/replicas')).data;
    if (data is! List) throw const OdinVaultApiException('Invalid replica response.');
    return data.whereType<Map>().map((e) => OdinVaultReplica.fromJson(Map<String, dynamic>.from(e))).toList();
  }

  Future<List<OdinVaultReplica>> retryReplication(String backupId) async {
    final data = (await _dio.post<Object>('/api/backups/$backupId/replicate')).data;
    if (data is! List) throw const OdinVaultApiException('Invalid replication response.');
    return data.whereType<Map>().map((e) => OdinVaultReplica.fromJson(Map<String, dynamic>.from(e))).toList();
  }

  Map<String, dynamic> _json(Object? data) {
    if (data is Map<String, dynamic>) return data;
    if (data is Map) return Map<String, dynamic>.from(data);
    throw const OdinVaultApiException('Invalid server response.');
  }
}

class OdinVaultApiException implements Exception {
  const OdinVaultApiException(this.message);
  final String message;
  @override String toString() => message;
  static OdinVaultApiException from(Object error) {
    if (error is DioException) {
      final data = error.response?.data;
      if (data is Map && data['message'] != null) return OdinVaultApiException(data['message'].toString());
      if (error.response?.statusCode == 401) return const OdinVaultApiException('کلید API مربوط به Agent معتبر نیست.');
      return OdinVaultApiException(error.message ?? 'اتصال به OdinVault Agent برقرار نشد.');
    }
    return OdinVaultApiException(error.toString());
  }
}
