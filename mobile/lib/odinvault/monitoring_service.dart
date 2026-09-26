import 'dart:convert';

import 'package:odinvault_mobile/odinvault/api_client.dart';
import 'package:odinvault_mobile/odinvault/server_store.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:workmanager/workmanager.dart';

const _monitorTaskUniqueName = 'odinvault-background-monitor';
const _monitorTaskName = 'odinvault-monitor-agents';
const _monitorEnabledKey = 'odinvault_monitoring_enabled';
const _lastHealthPrefix = 'odinvault_monitor_health_';
const _notifiedAlertsPrefix = 'odinvault_notified_alerts_';
const _notificationHistoryKey = 'odinvault_notification_history';
const _pendingNavigationKey = 'odinvault_pending_notification_navigation';

final FlutterLocalNotificationsPlugin _notifications =
    FlutterLocalNotificationsPlugin();

class OdinVaultNotificationTarget {
  const OdinVaultNotificationTarget({
    required this.serverId,
    required this.kind,
    this.alertKey,
  });

  final String serverId;
  final String kind;
  final String? alertKey;

  Map<String, dynamic> toJson() => {
        'serverId': serverId,
        'kind': kind,
        if (alertKey != null) 'alertKey': alertKey,
      };

  factory OdinVaultNotificationTarget.fromJson(Map<String, dynamic> json) =>
      OdinVaultNotificationTarget(
        serverId: json['serverId']?.toString() ?? '',
        kind: json['kind']?.toString() ?? '',
        alertKey: json['alertKey']?.toString(),
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
      await MonitoringService.initializeNotifications();
      final prefs = await SharedPreferences.getInstance();
      if (!(prefs.getBool(_monitorEnabledKey) ?? true)) return true;

      final servers = await OdinVaultServerStore().load();
      for (final server in servers) {
        final api = OdinVaultApiClient(server);
        final healthKey = '$_lastHealthPrefix${server.id}';
        final previousHealth = prefs.getBool(healthKey);

        var healthy = false;
        try {
          healthy = await api.reachable();
        } catch (_) {
          healthy = false;
        }

        if (!healthy) {
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
        } catch (_) {
          // Agent health is already known; alert retrieval should not fail the worker.
        }
      }
      return true;
    } catch (e, stackTrace) {
      debugPrint('OdinVault background monitor failed: $e\n$stackTrace');
      return false;
    }
  });
}

class MonitoringService {
  MonitoringService._();

  static Future<void> initialize() async {
    await initializeNotifications();
    await Workmanager().initialize(odinVaultBackgroundDispatcher);

    final prefs = await SharedPreferences.getInstance();
    final enabled = prefs.getBool(_monitorEnabledKey) ?? true;
    if (enabled) {
      await _schedule();
    }
  }

  static final ValueNotifier<OdinVaultNotificationTarget?> navigationTarget =
      ValueNotifier<OdinVaultNotificationTarget?>(null);
  static final ValueNotifier<int> unreadHistoryCount = ValueNotifier<int>(0);

  static Future<void> initializeNotifications() async {
    const android = AndroidInitializationSettings('ic_launcher');
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

  static Future<bool> isEnabled() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getBool(_monitorEnabledKey) ?? true;
  }

  static Future<void> setEnabled(bool enabled) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_monitorEnabledKey, enabled);

    if (enabled) {
      await requestNotificationPermission();
      await _schedule();
    } else {
      await Workmanager().cancelByUniqueName(_monitorTaskUniqueName);
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
  }

  static Future<void> showNotification({
    required int id,
    required String serverId,
    required String serverName,
    required String kind,
    required String title,
    required String body,
    String? alertKey,
  }) async {
    const android = AndroidNotificationDetails(
      'odinvault_monitoring',
      'پایش OdinVault',
      channelDescription: 'هشدارهای وضعیت Agent و بکاپ‌های OdinVault',
      importance: Importance.high,
      priority: Priority.high,
    );
    const details = NotificationDetails(android: android);
    final target = OdinVaultNotificationTarget(
      serverId: serverId,
      kind: kind,
      alertKey: alertKey,
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
