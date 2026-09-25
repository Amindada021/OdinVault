import 'package:flutter/foundation.dart';
import 'package:odinvault_mobile/odinvault/models.dart';
import 'package:dio/dio.dart';

String normalizeAgentBaseUrl(String value) {
  var normalized = value.replaceAll(RegExp(r'[‎‏‪-‮⁦-⁩]'), '').trim();
  if (normalized.isEmpty) {
    throw const OdinVaultApiException('آدرس Agent را وارد کنید.');
  }

  if (normalized.startsWith('://')) {
    normalized = 'http$normalized';
  } else {
    final lower = normalized.toLowerCase();
    if (!lower.startsWith('http://') && !lower.startsWith('https://')) {
      normalized = 'http://$normalized';
    }
  }

  while (normalized.endsWith('/')) {
    normalized = normalized.substring(0, normalized.length - 1);
  }

  final uri = Uri.tryParse(normalized);
  if (uri == null ||
      (uri.scheme != 'http' && uri.scheme != 'https') ||
      uri.host.isEmpty ||
      uri.userInfo.isNotEmpty ||
      uri.hasQuery ||
      uri.hasFragment ||
      RegExp(r'\s').hasMatch(normalized)) {
    throw const OdinVaultApiException('آدرس Agent معتبر نیست.');
  }

  return uri.toString();
}

class OdinVaultApiClient {
  OdinVaultApiClient(OdinVaultServer server)
      : _dio = Dio(BaseOptions(
          baseUrl: normalizeAgentBaseUrl(server.baseUrl),
          connectTimeout: const Duration(seconds: 10),
          receiveTimeout: const Duration(minutes: 120),
          sendTimeout: const Duration(minutes: 120),
          headers: {'Accept': 'application/json', if (server.apiKey.isNotEmpty) 'X-OdinVault-Key': server.apiKey},
          followRedirects: false,
        )) {
    _dio.interceptors.add(InterceptorsWrapper(
      onRequest: (options, handler) {
        if (kDebugMode) debugPrint('[OdinVault] ${options.method} ${safeRequestUri(options.uri)}');
        handler.next(options);
      },
      onError: (error, handler) {
        // Never log headers, payloads, raw errors, OAuth state, or server bodies.
        if (kDebugMode) {
          debugPrint('[OdinVault] ${safeRequestUri(error.requestOptions.uri)} '
              'type=${error.type.name} status=${error.response?.statusCode ?? '-'}');
        }
        handler.next(error);
      },
    ));
  }
  final Dio _dio;
  String get baseUrl => _dio.options.baseUrl;

  Future<bool> health() async {
    final response = await _dio.get<Object>('/api/health',
        options: Options(receiveTimeout: const Duration(seconds: 15)));
    final data = response.data;
    return response.statusCode == 200 && data is Map &&
        data['service'] == 'OdinVault.Agent' && data['status'] == 'healthy';
  }

  Future<List<OdinVaultDatabase>> databases() async {
    final data = (await _dio.get<Object>('/api/databases')).data;
    if (data is! List) throw const OdinVaultApiException('پاسخ لیست دیتابیس‌ها نامعتبر است.');
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
    if (data is! List) throw const OdinVaultApiException('پاسخ تاریخچه بکاپ نامعتبر است.');
    return data.whereType<Map>().map((e) => OdinVaultBackup.fromJson(Map<String, dynamic>.from(e))).toList();
  }

  Future<List<OdinVaultStorageTarget>> storageTargets() async {
    final data = (await _dio.get<Object>('/api/storage-targets')).data;
    if (data is! List) throw const OdinVaultApiException('پاسخ مقصد ذخیره‌سازی نامعتبر است.');
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
    if (data is! List) throw const OdinVaultApiException('پاسخ نسخه پشتیبان ثانویه نامعتبر است.');
    return data.whereType<Map>().map((e) => OdinVaultReplica.fromJson(Map<String, dynamic>.from(e))).toList();
  }

  Future<List<OdinVaultReplica>> retryReplication(String backupId) async {
    final data = (await _dio.post<Object>('/api/backups/$backupId/replicate')).data;
    if (data is! List) throw const OdinVaultApiException('پاسخ تکثیر بکاپ نامعتبر است.');
    return data.whereType<Map>().map((e) => OdinVaultReplica.fromJson(Map<String, dynamic>.from(e))).toList();
  }

  Map<String, dynamic> _json(Object? data) {
    if (data is Map<String, dynamic>) return data;
    if (data is Map) return Map<String, dynamic>.from(data);
    throw const OdinVaultApiException('پاسخ سرور نامعتبر است.');
  }
}

class OdinVaultApiException implements Exception {
  const OdinVaultApiException(this.message);
  final String message;
  @override String toString() => message;
  static OdinVaultApiException from(Object error) {
    if (error is OdinVaultApiException) return error;
    if (error is DioException) {
      final status = error.response?.statusCode;
      final message = status == 401
          ? 'کلید API مربوط به Agent معتبر نیست.'
          : status == 403
              ? 'اجازه دسترسی به این بخش را ندارید.'
              : switch (error.type) {
                  DioExceptionType.connectionTimeout ||
                  DioExceptionType.receiveTimeout ||
                  DioExceptionType.sendTimeout => 'مهلت اتصال یا دریافت پاسخ از Agent تمام شد.',
                  DioExceptionType.badCertificate => 'گواهی امنیتی Agent معتبر نیست.',
                  DioExceptionType.cancel => 'درخواست لغو شد.',
                  DioExceptionType.badResponse => 'Agent پاسخ ناموفق برگرداند (HTTP ${status ?? '-'}).',
                  _ => 'اتصال به OdinVault Agent برقرار نشد.',
                };
      return OdinVaultApiException('$message\nآدرس درخواست: \u2066'
          '${safeRequestUri(error.requestOptions.uri)}\u2069\nنوع خطا: ${error.type.name}');
    }
    return const OdinVaultApiException('عملیات انجام نشد. دوباره تلاش کنید.');
  }
}

// Query strings and OAuth state can contain credentials.
String safeRequestUri(Uri uri) {
  final path = uri.path.replaceAll(
      RegExp(r'/google-drive/pair/status/[^/]+'), '/google-drive/pair/status/[redacted]');
  return uri.replace(userInfo: '', path: path, query: '', fragment: '').toString()
      .replaceFirst(RegExp(r'\?$'), '');
}
