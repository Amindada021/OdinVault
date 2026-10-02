import 'dart:convert';

import 'package:odinvault_mobile/odinvault/api_client.dart';
import 'package:odinvault_mobile/odinvault/server_store.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:workmanager/workmanager.dart';

const _monitorTaskUniqueName = 'odinvault-background-monitor';
const _monitorImmediateName = 'odinvault-monitor-now';
const _monitorRunErrorKey = 'odinvault_monitor_run_error';
const _monitorAttemptPrefix = 'odinvault_monitor_attempt_';
const _monitorTaskName = 'odinvault-monitor-agents';
const _backupMonitorSinceKey = 'odinvault_backup_monitor_since';
const _notifiedBackupsPrefix = 'odinvault_notified_backups_';
const _monitorEnabledKey = 'odinvault_monitoring_enabled';
const _lastHealthPrefix = 'odinvault_monitor_health_';
const _notifiedAlertsPrefix = 'odinvault_notified_alerts_';
const _notificationHistoryKey = 'odinvault_notification_history';
const _pendingNavigationKey = 'odinvault_pending_notification_navigation';
const _monitorCursorPrefix = 'odinvault_monitor_cursor_';
const _monitorLastSuccessPrefix = 'odinvault_monitor_last_success_';
const _monitorLastErrorPrefix = 'odinvault_monitor_last_error_';

final FlutterLocalNotificationsPlugin _notifications =
    FlutterLocalNotificationsPlugin();

class OdinVaultNotificationTarget {
  const OdinVaultNotificationTarget({
    required this.serverId,
    required this.kind,
    this.alertKey,
    this.databaseId,
  });

  final String serverId;
  final String kind;
  final String? alertKey;
  final String? databaseId;

  Map<String, dynamic> toJson() => {
        'serverId': serverId,
        'kind': kind,
        if (alertKey != null) 'alertKey': alertKey,
        if (databaseId != null) 'databaseId': databaseId,
      };

  factory OdinVaultNotificationTarget.fromJson(Map<String, dynamic> json) =>
      OdinVaultNotificationTarget(
        serverId: json['serverId']?.toString() ?? '',
        kind: json['kind']?.toString() ?? '',
        alertKey: json['alertKey']?.toString(),
        databaseId: json['databaseId']?.toString(),
      );
}

class OdinVaultNotificationHistoryItem {
  const OdinVaultNotificationHistoryItem({
    required this.id,
    required this.serverId,
    required this.serverName,
    required this.kind,
    required this.title,
    required this.body,
    required this.createdAtUtc,
    required this.isRead,
    this.alertKey,
  });

  final String id;
  final String serverId;
  final String serverName;
  final String kind;
  final String title;
  final String body;
  final DateTime createdAtUtc;
  final bool isRead;
  final String? alertKey;

  Map<String, dynamic> toJson() => {
        'id': id,
        'serverId': serverId,
        'serverName': serverName,
        'kind': kind,
        'title': title,
        'body': body,
        'createdAtUtc': createdAtUtc.toIso8601String(),
        'isRead': isRead,
        if (alertKey != null) 'alertKey': alertKey,
      };

  factory OdinVaultNotificationHistoryItem.fromJson(Map<String, dynamic> json) =>
      OdinVaultNotificationHistoryItem(
        id: json['id']?.toString() ?? '',
        serverId: json['serverId']?.toString() ?? '',
        serverName: json['serverName']?.toString() ?? '',
        kind: json['kind']?.toString() ?? '',
        title: json['title']?.toString() ?? '',
        body: json['body']?.toString() ?? '',
        createdAtUtc: DateTime.tryParse(json['createdAtUtc']?.toString() ?? '') ??
            DateTime.now().toUtc(),
        isRead: json['isRead'] == true,
        alertKey: json['alertKey']?.toString(),
      );
}

@pragma('vm:entry-point')
void odinVaultBackgroundDispatcher() {
  Workmanager().executeTask((taskName, inputData) async {
    if (taskName != _monitorTaskName) return true;

    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.reload();
      if (!(prefs.getBool(_monitorEnabledKey) ?? true)) return true;

      return await MonitoringService.runNow();
    } catch (e, stackTrace) {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_monitorRunErrorKey, e.toString());
      debugPrint('OdinVault background monitor failed: $e\n$stackTrace');
      return false;
    }
  });
}

class MonitoringService {
  MonitoringService._();

  static Future<bool> runNow() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.reload();
    await prefs.remove(_monitorRunErrorKey);
    try {
      await initializeNotifications();
      await _ensureBackupMonitorSince(prefs);
      final servers = await OdinVaultServerStore().load();
      for (final server in servers) {
        await prefs.setString('$_monitorAttemptPrefix${server.id}', DateTime.now().toUtc().toIso8601String());
        final api = OdinVaultApiClient(server, requestTimeout: const Duration(seconds: 15));
        final healthKey = '$_lastHealthPrefix${server.id}';
        final previousHealth = prefs.getBool(healthKey);

        var healthy = false;
        try {
          healthy = await api.reachable();
        } catch (_) {
          healthy = false;
        }

        if (!healthy) {
          await prefs.setString('$_monitorLastErrorPrefix${server.id}', 'اتصال به Agent برقرار نشد.');
          if (previousHealth != false) {
            await MonitoringService.showNotification(
              id: _notificationId('offline:${server.id}'),
              serverId: server.id,
              serverName: server.name,
              kind: 'offline',
              title: 'OdinVault Agent در دسترس نیست',
              body: 'اتصال به «${server.name}» برقرار نشد.',
            );
          }
          await prefs.setBool(healthKey, false);
          continue;
        }

        if (previousHealth == false) {
          await MonitoringService.showNotification(
            id: _notificationId('online:${server.id}'),
            serverId: server.id,
            serverName: server.name,
            kind: 'online',
            title: 'اتصال OdinVault برقرار شد',
            body: '«${server.name}» دوباره در دسترس است.',
          );
        }
        await prefs.setBool(healthKey, true);

        var backupResultsAvailable = false;
        try {
          await MonitoringService.notifyBackupResults(api, server.id, server.name);
          backupResultsAvailable = true;
        } catch (e) {
          await prefs.setString('$_monitorLastErrorPrefix${server.id}', 'دریافت نتیجه بکاپ: $e');
          // Keep alerts as a fallback for older agents.
        }

        try {
          final result = await api.alerts(includeRead: false);
          final notifiedKey = '$_notifiedAlertsPrefix${server.id}';
          final known = prefs.getStringList(notifiedKey)?.toSet() ?? <String>{};
          final next = <String>{...known};

          for (final alert in result.alerts) {
            if (alert.key.isEmpty || alert.isRead || known.contains(alert.key)) {
              continue;
            }

            final severity = alert.severity.toLowerCase();
            final important = severity == 'critical' ||
                severity == 'error' ||
                alert.category.toLowerCase().contains('backup') ||
                alert.category.toLowerCase().contains('verify') ||
                alert.category.toLowerCase().contains('replica');

            if (!important) continue;
            if (backupResultsAvailable &&
                (alert.key.startsWith('backup-failed:') ||
                 alert.key.startsWith('verify-failed:'))) {
              continue;
            }

            await MonitoringService.showNotification(
              id: _notificationId('${server.id}:${alert.key}'),
              serverId: server.id,
              serverName: server.name,
              kind: 'alert',
              alertKey: alert.key,
              title: alert.title.isEmpty ? 'هشدار OdinVault' : alert.title,
              body: [
                if (alert.databaseName.isNotEmpty) alert.databaseName,
                if (alert.message.isNotEmpty) alert.message,
                server.name,
              ].join(' • '),
            );
            next.add(alert.key);
          }

          if (next.length > 100) {
            final trimmed = next.toList().reversed.take(100).toList().reversed;
            await prefs.setStringList(notifiedKey, trimmed.toList());
          } else {
            await prefs.setStringList(notifiedKey, next.toList());
          }
        } catch (e) {
          final key = '$_monitorLastErrorPrefix${server.id}';
          final previous = prefs.getString(key);
          await prefs.setString(key, '${previous == null ? '' : '$previous • '}دریافت هشدارها: $e');
        }
      }
      return true;
    } catch (e) {
      await prefs.setString(_monitorRunErrorKey, e.toString());
      return false;
    }
  }

  static Future<void> testNotification() async {
    await initializeNotifications();
    if (!await requestNotificationPermission()) {
      throw StateError('مجوز اعلان غیرفعال است.');
    }
    await showNotification(
      id: _notificationId('monitor-test'), serverId: '', serverName: 'OdinVault',
      kind: 'test', title: 'اعلان آزمایشی OdinVault',
      body: 'نمایش اعلان روی گوشی فعال است. این تست اتصال به Agent را بررسی نمی‌کند.',
    );
  }

  static Future<void> initialize() async {
    await initializeNotifications();
    await Workmanager().initialize(odinVaultBackgroundDispatcher);

    final prefs = await SharedPreferences.getInstance();
    final enabled = prefs.getBool(_monitorEnabledKey) ?? true;
    if (enabled) {
      await _ensureBackupMonitorSince(prefs);
      await _schedule();
    }
  }

  static final ValueNotifier<OdinVaultNotificationTarget?> navigationTarget =
      ValueNotifier<OdinVaultNotificationTarget?>(null);
  static final ValueNotifier<int> unreadHistoryCount = ValueNotifier<int>(0);

  static Future<void> initializeNotifications() async {
    const android = AndroidInitializationSettings('@mipmap/ic_launcher');
    const settings = InitializationSettings(android: android);
    await _notifications.initialize(
      settings: settings,
      onDidReceiveNotificationResponse: _onNotificationResponse,
    );

    final launchDetails = await _notifications.getNotificationAppLaunchDetails();
    final response = launchDetails?.notificationResponse;
    if (launchDetails?.didNotificationLaunchApp == true && response != null) {
      await _onNotificationResponse(response);
    }

    await refreshHistoryBadge();
  }

  @pragma('vm:entry-point')
  static Future<void> _onNotificationResponse(
    NotificationResponse response,
  ) async {
    final payload = response.payload;
    if (payload == null || payload.isEmpty) return;

    try {
      final decoded = jsonDecode(payload);
      if (decoded is! Map) return;
      final target = OdinVaultNotificationTarget.fromJson(
        Map<String, dynamic>.from(decoded),
      );
      if (target.serverId.isEmpty) return;

      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_pendingNavigationKey, payload);
      navigationTarget.value = target;
    } catch (_) {}
  }

  static Future<OdinVaultNotificationTarget?> consumePendingNavigation() async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getString(_pendingNavigationKey);
    if (raw == null || raw.isEmpty) return null;
    await prefs.remove(_pendingNavigationKey);

    try {
      final decoded = jsonDecode(raw);
      if (decoded is! Map) return null;
      return OdinVaultNotificationTarget.fromJson(
        Map<String, dynamic>.from(decoded),
      );
    } catch (_) {
      return null;
    }
  }

  static Future<bool> requestNotificationPermission() async {
    final android = _notifications
        .resolvePlatformSpecificImplementation<
            AndroidFlutterLocalNotificationsPlugin>();
    return await android?.requestNotificationsPermission() ?? true;
  }

  static Future<bool> notificationPermissionGranted() async {
    final android = _notifications.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>();
    return await android?.areNotificationsEnabled() ?? true;
  }

  static Future<Map<String, String?>> monitorStatus(String serverId) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.reload();
    return {
      'lastAttempt': prefs.getString('$_monitorAttemptPrefix$serverId'),
      'workerError': prefs.getString(_monitorRunErrorKey),
      'lastSuccess': prefs.getString('$_monitorLastSuccessPrefix$serverId'),
      'lastError': prefs.getString('$_monitorLastErrorPrefix$serverId'),
    };
  }

  static Future<bool> isEnabled() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getBool(_monitorEnabledKey) ?? true;
  }

  static Future<void> setEnabled(bool enabled) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_monitorEnabledKey, enabled);

    if (enabled) {
      await requestNotificationPermission();
      await _ensureBackupMonitorSince(prefs);
      await _schedule();
    } else {
      await Workmanager().cancelByUniqueName(_monitorTaskUniqueName);
      await Workmanager().cancelByUniqueName(_monitorImmediateName);
    }
  }

  static Future<void> _ensureBackupMonitorSince(SharedPreferences prefs) async {
    if (!prefs.containsKey(_backupMonitorSinceKey)) {
      await prefs.setString(_backupMonitorSinceKey, DateTime.now().toUtc().toIso8601String());
    }
  }

  static Future<void> notifyBackupResults(
    OdinVaultApiClient api, String serverId, String serverName,
  ) async {
    final prefs = await SharedPreferences.getInstance();
    final cursorKey = '$_monitorCursorPrefix$serverId';
    await prefs.reload();
    await _ensureBackupMonitorSince(prefs);
    if (!await notificationPermissionGranted()) {
      throw StateError('مجوز اعلان غیرفعال است؛ نتیجه‌ها برای بررسی بعدی نگه داشته شدند.');
    }
    final initial = prefs.getString(cursorKey) ?? prefs.getString(_backupMonitorSinceKey)!;
    final since = DateTime.parse(initial).toUtc();
    DateTime? beforeUtc;
    DateTime? newestServerUtc;
    final key = '$_notifiedBackupsPrefix$serverId';
    final known = prefs.getStringList(key)?.toSet() ?? <String>{};

    Future<void> notify(String eventId, String databaseId, String database, DateTime? completed,
        String kind, String body) async {
      if (completed == null || !completed.isAfter(since) || known.contains(eventId)) return;
      await showNotification(
        id: _notificationId('$serverId:$eventId'), serverId: serverId, serverName: serverName,
        kind: kind, databaseId: databaseId, title: '$serverName • $database', body: body,
      );
      known.add(eventId);
      await prefs.setStringList(key, known.toList().reversed.take(2000).toList().reversed.toList());
    }

    while (true) {
      final overview = await api.backupOverview(take: 200, beforeUtc: beforeUtc);
      newestServerUtc ??= overview.utc;
      final activeBackupIds = overview.jobs.where((job) => job.status == 0 || job.status == 1).map((job) => job.backupRecordId).toSet();
      final backupIds = overview.backups.map((backup) => backup.id).toSet();

      for (final backup in overview.backups.reversed) {
        final at = backup.completedAtUtc ?? backup.startedAtUtc;
        if (at == null || !at.isAfter(since) || activeBackupIds.contains(backup.id) || backup.verificationStatus == 1) continue;
        if (backup.status == 2 && backup.verificationStatus == 3) {
          await notify('verify:${backup.id}:failed', backup.databaseEndpointId, backup.databaseName, at, 'verify',
              'بررسی سلامت بکاپ ناموفق بود: ${backup.error?.trim().isNotEmpty == true ? backup.error : 'جزئیات را در Agent بررسی کنید.'}');
        } else if (backup.status == 2) {
          await notify('backup:${backup.id}:ok', backup.databaseEndpointId, backup.databaseName, at, 'backup',
              'بکاپ با موفقیت انجام شد.');
        } else if (backup.status == 3) {
          await notify('backup:${backup.id}:failed', backup.databaseEndpointId, backup.databaseName, at, 'backup',
              'ساخت بکاپ ناموفق بود: ${backup.error?.trim().isNotEmpty == true ? backup.error : 'خطای نامشخص؛ گزارش سرور را بررسی کنید.'}');
        }
      }
      for (final job in overview.jobs.reversed) {
        final at = job.completedAtUtc ?? job.updatedAtUtc;
        if ((job.status != 3 && job.status != 4) || backupIds.contains(job.backupRecordId)) continue;
        await notify('job:${job.id}', job.databaseEndpointId, job.databaseName, at, 'backup',
            'اجرای بکاپ ناموفق بود: ${job.errorMessage ?? job.errorCode ?? 'اجرای بکاپ قطع شد.'}');
      }

      final next = overview.nextBeforeUtc;
      if (next == null || !next.isAfter(since) || overview.jobs.isEmpty && overview.backups.isEmpty) break;
      beforeUtc = next;
    }

    if (newestServerUtc != null) {
      await prefs.setString(cursorKey, newestServerUtc.toUtc().toIso8601String());
      await prefs.setString('$_monitorLastSuccessPrefix$serverId', DateTime.now().toUtc().toIso8601String());
      await prefs.remove('$_monitorLastErrorPrefix$serverId');
    }
  }

  static Future<void> _schedule() async {
    await Workmanager().registerPeriodicTask(
      _monitorTaskUniqueName,
      _monitorTaskName,
      frequency: const Duration(minutes: 15),
      existingWorkPolicy: ExistingPeriodicWorkPolicy.update,
      constraints: Constraints(networkType: NetworkType.connected),
      tag: 'odinvault-monitoring',
    );
    await Workmanager().registerOneOffTask(
      _monitorImmediateName, _monitorTaskName,
      existingWorkPolicy: ExistingWorkPolicy.keep,
      constraints: Constraints(networkType: NetworkType.connected),
    );
  }

  static Future<void> showNotification({
    required int id,
    required String serverId,
    required String serverName,
    required String kind,
    required String title,
    required String body,
    String? alertKey,
    String? databaseId,
  }) async {
    final android = AndroidNotificationDetails(
      'odinvault_monitoring',
      'پایش OdinVault',
      channelDescription: 'هشدارهای وضعیت Agent و بکاپ‌های OdinVault',
      importance: Importance.high,
      priority: Priority.high,
      styleInformation: BigTextStyleInformation(body),
    );
    final details = NotificationDetails(android: android);
    final target = OdinVaultNotificationTarget(
      serverId: serverId,
      kind: kind,
      alertKey: alertKey,
      databaseId: databaseId,
    );
    final payload = jsonEncode(target.toJson());

    await _appendHistory(
      OdinVaultNotificationHistoryItem(
        id: '${DateTime.now().microsecondsSinceEpoch}-$id',
        serverId: serverId,
        serverName: serverName,
        kind: kind,
        title: title,
        body: body,
        createdAtUtc: DateTime.now().toUtc(),
        isRead: false,
        alertKey: alertKey,
      ),
    );

    await _notifications.show(
      id: id,
      title: title,
      body: body,
      notificationDetails: details,
      payload: payload,
    );
  }

  static Future<List<OdinVaultNotificationHistoryItem>> history() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.reload();
    final raw = prefs.getString(_notificationHistoryKey);
    if (raw == null || raw.isEmpty) return const [];

    try {
      final decoded = jsonDecode(raw);
      if (decoded is! List) return const [];
      return decoded
          .whereType<Map>()
          .map((e) => OdinVaultNotificationHistoryItem.fromJson(
                Map<String, dynamic>.from(e),
              ))
          .toList();
    } catch (_) {
      return const [];
    }
  }

  static Future<void> markHistoryRead([String? id]) async {
    final items = await history();
    if (items.isEmpty) return;

    final next = items
        .map(
          (item) => id == null || item.id == id
              ? OdinVaultNotificationHistoryItem(
                  id: item.id,
                  serverId: item.serverId,
                  serverName: item.serverName,
                  kind: item.kind,
                  title: item.title,
                  body: item.body,
                  createdAtUtc: item.createdAtUtc,
                  isRead: true,
                  alertKey: item.alertKey,
                )
              : item,
        )
        .toList();
    await _saveHistory(next);
    await refreshHistoryBadge();
  }

  static Future<void> clearHistory() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_notificationHistoryKey);
    unreadHistoryCount.value = 0;
  }

  static Future<void> refreshHistoryBadge() async {
    final items = await history();
    unreadHistoryCount.value = items.where((x) => !x.isRead).length;
  }

  static Future<void> _appendHistory(
    OdinVaultNotificationHistoryItem item,
  ) async {
    final items = await history();
    final next = [item, ...items].take(100).toList();
    await _saveHistory(next);
    unreadHistoryCount.value = next.where((x) => !x.isRead).length;
  }

  static Future<void> _saveHistory(
    List<OdinVaultNotificationHistoryItem> items,
  ) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(
      _notificationHistoryKey,
      jsonEncode(items.map((x) => x.toJson()).toList()),
    );
  }
}

int _notificationId(String value) {
  var hash = 0x811c9dc5;
  for (final unit in value.codeUnits) {
    hash ^= unit;
    hash = (hash * 0x01000193) & 0x7fffffff;
  }
  return hash;
}
