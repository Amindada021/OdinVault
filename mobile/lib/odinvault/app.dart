import 'dart:async';
import 'dart:math';
import 'backup_download.dart';

import 'package:odinvault_mobile/odinvault/api_client.dart';
import 'package:odinvault_mobile/odinvault/models.dart';
import 'package:odinvault_mobile/odinvault/monitoring_service.dart';
import 'package:odinvault_mobile/odinvault/schedule_editor.dart';
import 'package:odinvault_mobile/odinvault/server_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:url_launcher/url_launcher.dart';

class OdinVaultApp extends StatelessWidget {
  const OdinVaultApp({super.key, this.home});

  final Widget? home;

  @override
  Widget build(BuildContext context) => MaterialApp(
        debugShowCheckedModeBanner: false,
        title: 'OdinVault',
        locale: const Locale('fa'),
        supportedLocales: const [Locale('fa'), Locale('en')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: ThemeData(
          useMaterial3: true,
          colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF254D3F)),
          inputDecorationTheme: const InputDecorationTheme(
            border: OutlineInputBorder(),
          ),
        ),
        darkTheme: ThemeData(
          useMaterial3: true,
          colorScheme: ColorScheme.fromSeed(
            seedColor: const Color(0xFF4E8E79),
            brightness: Brightness.dark,
          ),
          inputDecorationTheme: const InputDecorationTheme(
            border: OutlineInputBorder(),
          ),
        ),
        themeMode: ThemeMode.system,
        home: Directionality(
          textDirection: TextDirection.rtl,
          child: home ?? const ServersPage(),
        ),
      );
}

class ServersPage extends StatefulWidget {
  const ServersPage({super.key});

  @override
  State<ServersPage> createState() => _ServersPageState();
}

class _ServersPageState extends State<ServersPage> {
  final _store = OdinVaultServerStore();
  List<OdinVaultServer> _servers = [];
  final Map<String, bool?> _health = {};
  bool _loading = true;
  bool _checking = false;
  bool? _monitoringEnabled;
  String? _error;

  @override
  void initState() {
    super.initState();
    MonitoringService.navigationTarget.addListener(_handleNotificationTarget);
    _reload();
    _loadMonitoringState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _consumePendingNavigation());
  }

  @override
  void dispose() {
    MonitoringService.navigationTarget.removeListener(_handleNotificationTarget);
    super.dispose();
  }

  void _handleNotificationTarget() async {
    final target = MonitoringService.navigationTarget.value;
    if (target == null) return;
    MonitoringService.navigationTarget.value = null;
    await MonitoringService.consumePendingNavigation();
    await _openNotificationTarget(target);
  }

  Future<void> _consumePendingNavigation() async {
    final target = await MonitoringService.consumePendingNavigation();
    if (target != null) {
      await _openNotificationTarget(target);
    }
  }

  Future<void> _openNotificationTarget(
    OdinVaultNotificationTarget target,
  ) async {
    if (_loading) {
      await _reload();
    }
    if (!mounted) return;

    OdinVaultServer? server;
    for (final item in _servers) {
      if (item.id == target.serverId) {
        server = item;
        break;
      }
    }
    if (server == null) return;

    await Navigator.push(
      context,
      MaterialPageRoute(builder: (_) => AgentPage(server: server!)),
    );
    await MonitoringService.refreshHistoryBadge();
    await _checkAll();
  }

  Future<void> _loadMonitoringState() async {
    final enabled = await MonitoringService.isEnabled();
    if (!mounted) return;
    setState(() => _monitoringEnabled = enabled);
    if (enabled) {
      await MonitoringService.requestNotificationPermission();
    }
  }

  Future<void> _showMonitoringSettings() async {
    var enabled = _monitoringEnabled ?? await MonitoringService.isEnabled();
    if (!mounted) return;

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: const Text('پایش پس‌زمینه'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                value: enabled,
                onChanged: (value) async {
                  final previous = enabled;
                  setDialogState(() => enabled = value);
                  try {
                    await MonitoringService.setEnabled(value);
                    if (!mounted) return;
                    setState(() => _monitoringEnabled = value);
                  } catch (e) {
                    setDialogState(() => enabled = previous);
                    if (!mounted) return;
                    ScaffoldMessenger.of(context).showSnackBar(
                      SnackBar(
                        content: Text('تغییر وضعیت پایش ناموفق بود: $e'),
                      ),
                    );
                  }
                },
                title: const Text('پایش Agentها در پس‌زمینه'),
                subtitle: const Text(
                  'در صورت قطع Agent یا خطای جدید بکاپ، Verify و Replica اعلان می‌دهد.',
                ),
              ),
              const SizedBox(height: 8),
              Text(
                'اندروید زمان اجرای دقیق را مدیریت می‌کند؛ بررسی‌ها با WorkManager و حداقل فاصله ۱۵ دقیقه انجام می‌شوند.',
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
          actions: [
            FilledButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text('بستن'),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _reload() async {
    try {
      final servers = await _store.load();
      if (!mounted) return;
      setState(() {
        _servers = servers;
        _loading = false;
        _error = null;
      });
      await _checkAll();
      await MonitoringService.refreshHistoryBadge();
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = 'خواندن اتصال‌های ذخیره‌شده ناموفق بود: $e';
      });
    }
  }

  Future<void> _checkAll() async {
    if (_checking || _servers.isEmpty) return;
    setState(() => _checking = true);

    final results = await Future.wait(
      _servers.map((server) async {
        try {
          final ok = await OdinVaultApiClient(server).reachable();
          return MapEntry(server.id, ok);
        } catch (_) {
          return MapEntry(server.id, false);
        }
      }),
    );

    if (!mounted) return;
    setState(() {
      _health
        ..clear()
        ..addEntries(results);
      _checking = false;
    });
  }

  Future<void> _add() async {
    final server = await showDialog<OdinVaultServer>(
      context: context,
      builder: (_) => const AddServerDialog(),
    );
    if (server == null) return;

    final next = [..._servers, server];
    try {
      await _store.save(next);
      if (!mounted) return;
      setState(() {
        _servers = next;
        _health[server.id] = true;
      });
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('ذخیره اتصال ناموفق بود: $e')),
      );
    }
  }

  Future<void> _edit(OdinVaultServer server) async {
    final updated = await showDialog<OdinVaultServer>(
      context: context,
      builder: (_) => AddServerDialog(server: server),
    );
    if (updated == null) return;

    final next = _servers
        .map((x) => x.id == server.id ? updated : x)
        .toList();
    try {
      await _store.save(next);
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('ذخیره تغییرات اتصال ناموفق بود: $e')),
      );
      return;
    }
    if (!mounted) return;
    setState(() {
      _servers = next;
      _health[server.id] = null;
    });
    await _checkAll();
  }

  Future<void> _remove(OdinVaultServer server) async {
    final answer = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('حذف Agent'),
        content: Text('اتصال «${server.name}» از گوشی حذف شود؟\nاطلاعات خود سرور و بکاپ‌ها حذف نمی‌شوند.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('انصراف')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('حذف')),
        ],
      ),
    );
    if (answer != true) return;

    final next = _servers.where((x) => x.id != server.id).toList();
    try {
      await _store.save(next);
      if (!mounted) return;
      setState(() {
        _servers = next;
        _health.remove(server.id);
      });
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('حذف اتصال ناموفق بود: $e')),
      );
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          title: const Text('OdinVault'),
          actions: [
            ValueListenableBuilder<int>(
              valueListenable: MonitoringService.unreadHistoryCount,
              builder: (context, count, _) => IconButton(
                tooltip: count > 0 ? 'تاریخچه اعلان‌ها ($count)' : 'تاریخچه اعلان‌ها',
                onPressed: () async {
                  final target = await Navigator.push<OdinVaultNotificationTarget>(
                    context,
                    MaterialPageRoute(
                      builder: (_) => const NotificationHistoryPage(),
                    ),
                  );
                  await MonitoringService.refreshHistoryBadge();
                  if (target != null) {
                    await _openNotificationTarget(target);
                  }
                },
                icon: Stack(
                  clipBehavior: Clip.none,
                  children: [
                    const Icon(Icons.notifications_none_outlined),
                    if (count > 0)
                      Positioned(
                        top: -5,
                        right: -7,
                        child: Container(
                          constraints: const BoxConstraints(
                            minWidth: 16,
                            minHeight: 16,
                          ),
                          padding: const EdgeInsets.symmetric(horizontal: 4),
                          alignment: Alignment.center,
                          decoration: BoxDecoration(
                            color: Theme.of(context).colorScheme.error,
                            borderRadius: BorderRadius.circular(10),
                          ),
                          child: Text(
                            count > 99 ? '99+' : '$count',
                            style: TextStyle(
                              color: Theme.of(context).colorScheme.onError,
                              fontSize: 9,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ),
            IconButton(
              tooltip: 'پایش پس‌زمینه',
              onPressed: _showMonitoringSettings,
              icon: Icon(
                _monitoringEnabled == false
                    ? Icons.notifications_off_outlined
                    : Icons.notifications_active_outlined,
              ),
            ),
            IconButton(
              tooltip: 'بروزرسانی',
              onPressed: _checking ? null : _reload,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: _add,
          icon: const Icon(Icons.add),
          label: const Text('افزودن Agent'),
        ),
        body: _loading
            ? const Center(child: CircularProgressIndicator())
            : _error != null
                ? _StateMessage(
                    icon: Icons.error_outline,
                    title: 'بارگذاری اتصال‌ها ناموفق بود',
                    subtitle: _error!,
                    actionText: 'تلاش مجدد',
                    onAction: _reload,
                  )
                : _servers.isEmpty
                    ? _StateMessage(
                        icon: Icons.dns_outlined,
                        title: 'هنوز Agent اضافه نشده',
                        subtitle: 'برای مدیریت بکاپ‌ها، OdinVault Agent سرور را اضافه کنید.',
                        actionText: 'افزودن Agent',
                        onAction: _add,
                      )
                    : RefreshIndicator(
                        onRefresh: _reload,
                        child: ListView.separated(
                          physics: const AlwaysScrollableScrollPhysics(),
                          padding: const EdgeInsets.all(16),
                          itemCount: _servers.length + 1,
                          separatorBuilder: (_, _) => const SizedBox(height: 10),
                          itemBuilder: (_, i) {
                            if (i == 0) {
                              final healthy = _health.values.where((x) => x == true).length;
                              return Card(
                                child: Padding(
                                  padding: const EdgeInsets.all(16),
                                  child: Row(
                                    children: [
                                      const Icon(Icons.shield_outlined),
                                      const SizedBox(width: 10),
                                      Expanded(
                                        child: Column(
                                          crossAxisAlignment: CrossAxisAlignment.start,
                                          children: [
                                            Text(
                                              'Agentهای ذخیره‌شده',
                                              style: Theme.of(context).textTheme.titleMedium,
                                            ),
                                            Text(
                                              _checking
                                                  ? 'در حال بررسی وضعیت اتصال...'
                                                  : '$healthy از ${_servers.length} Agent در دسترس است',
                                              style: Theme.of(context).textTheme.bodySmall,
                                            ),
                                          ],
                                        ),
                                      ),
                                      if (_checking)
                                        const SizedBox(
                                          width: 22,
                                          height: 22,
                                          child: CircularProgressIndicator(strokeWidth: 2),
                                        ),
                                    ],
                                  ),
                                ),
                              );
                            }

                            final server = _servers[i - 1];
                            final health = _health[server.id];
                            return Card(
                              clipBehavior: Clip.antiAlias,
                              child: InkWell(
                                onTap: () => Navigator.push(
                                  context,
                                  MaterialPageRoute(builder: (_) => AgentPage(server: server)),
                                ).then((_) => _checkAll()),
                                child: Padding(
                                  padding: const EdgeInsets.all(14),
                                  child: Row(
                                    children: [
                                      CircleAvatar(
                                        child: Icon(
                                          health == true
                                              ? Icons.dns
                                              : health == false
                                                  ? Icons.cloud_off_outlined
                                                  : Icons.hourglass_empty,
                                        ),
                                      ),
                                      const SizedBox(width: 12),
                                      Expanded(
                                        child: Column(
                                          crossAxisAlignment: CrossAxisAlignment.start,
                                          children: [
                                            Row(
                                              children: [
                                                Expanded(
                                                  child: Text(
                                                    server.name,
                                                    style: Theme.of(context).textTheme.titleMedium,
                                                  ),
                                                ),
                                                _ConnectionBadge(health: health),
                                              ],
                                            ),
                                            const SizedBox(height: 4),
                                            Directionality(
                                              textDirection: TextDirection.ltr,
                                              child: Text(
                                                server.baseUrl,
                                                textAlign: TextAlign.right,
                                                style: Theme.of(context).textTheme.bodySmall,
                                              ),
                                            ),
                                          ],
                                        ),
                                      ),
                                      PopupMenuButton<String>(
                                        tooltip: 'گزینه‌ها',
                                        onSelected: (value) {
                                          if (value == 'edit') _edit(server);
                                          if (value == 'remove') _remove(server);
                                        },
                                        itemBuilder: (_) => const [
                                          PopupMenuItem(
                                            value: 'edit',
                                            child: ListTile(
                                              leading: Icon(Icons.edit_outlined),
                                              title: Text('ویرایش اتصال'),
                                            ),
                                          ),
                                          PopupMenuItem(
                                            value: 'remove',
                                            child: ListTile(
                                              leading: Icon(Icons.delete_outline),
                                              title: Text('حذف اتصال'),
                                            ),
                                          ),
                                        ],
                                      ),
                                    ],
                                  ),
                                ),
                              ),
                            );
                          },
                        ),
                      ),
      );
}

class NotificationHistoryPage extends StatefulWidget {
  const NotificationHistoryPage({super.key});

  @override
  State<NotificationHistoryPage> createState() => _NotificationHistoryPageState();
}

class _NotificationHistoryPageState extends State<NotificationHistoryPage> {
  List<OdinVaultNotificationHistoryItem>? items;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    final value = await MonitoringService.history();
    if (!mounted) return;
    setState(() => items = value);
  }

  Future<void> markAllRead() async {
    await MonitoringService.markHistoryRead();
    await load();
  }

  Future<void> clear() async {
    final answer = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('پاک کردن تاریخچه اعلان‌ها'),
        content: const Text('همه اعلان‌های ذخیره‌شده از تاریخچه حذف شوند؟'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('پاک کردن'),
          ),
        ],
      ),
    );
    if (answer != true) return;
    await MonitoringService.clearHistory();
    await load();
  }

  @override
  Widget build(BuildContext context) {
    final value = items;
    return Scaffold(
      appBar: AppBar(
        title: const Text('تاریخچه اعلان‌ها'),
        actions: [
          IconButton(
            tooltip: 'خواندن همه',
            onPressed: (value?.any((x) => !x.isRead) ?? false) ? markAllRead : null,
            icon: const Icon(Icons.done_all),
          ),
          IconButton(
            tooltip: 'پاک کردن تاریخچه',
            onPressed: (value?.isNotEmpty ?? false) ? clear : null,
            icon: const Icon(Icons.delete_sweep_outlined),
          ),
        ],
      ),
      body: value == null
          ? const Center(child: CircularProgressIndicator())
          : value.isEmpty
              ? const _EmptyState(
                  icon: Icons.notifications_none,
                  title: 'تاریخچه اعلان‌ها خالی است',
                  subtitle: 'هشدارهای پس‌زمینه OdinVault اینجا نمایش داده می‌شوند.',
                )
              : ListView.separated(
                  padding: const EdgeInsets.all(16),
                  itemCount: value.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 8),
                  itemBuilder: (_, index) {
                    final item = value[index];
                    return Card(
                      child: ListTile(
                        leading: Icon(
                          item.kind == 'offline'
                              ? Icons.cloud_off_outlined
                              : item.kind == 'online'
                                  ? Icons.cloud_done_outlined
                                  : Icons.warning_amber_outlined,
                        ),
                        title: Text(item.title),
                        subtitle: Text(
                          [
                            if (item.serverName.isNotEmpty) item.serverName,
                            item.body,
                            _formatDate(item.createdAtUtc),
                          ].join('\n'),
                        ),
                        trailing: item.isRead
                            ? null
                            : const Icon(Icons.circle, size: 10),
                        onTap: () async {
                          await MonitoringService.markHistoryRead(item.id);
                          if (!mounted) return;
                          Navigator.pop(
                            context,
                            OdinVaultNotificationTarget(
                              serverId: item.serverId,
                              kind: item.kind,
                              alertKey: item.alertKey,
                            ),
                          );
                        },
                      ),
                    );
                  },
                ),
    );
  }
}

class AddServerDialog extends StatefulWidget {
  const AddServerDialog({super.key, this.server});

  final OdinVaultServer? server;

  @override
  State<AddServerDialog> createState() => _AddServerDialogState();
}

class _AddServerDialogState extends State<AddServerDialog> {
  late final name = TextEditingController(text: widget.server?.name ?? '');
  late final url = TextEditingController(text: widget.server?.baseUrl ?? 'http://');
  late final key = TextEditingController(text: widget.server?.apiKey ?? '');
  String? error;
  bool busy = false;
  bool showKey = false;

  @override
  void dispose() {
    name.dispose();
    url.dispose();
    key.dispose();
    super.dispose();
  }

  Future<void> save() async {
    String baseUrl;
    try {
      baseUrl = normalizeAgentBaseUrl(url.text);
    } catch (e) {
      setState(() => error = OdinVaultApiException.from(e).message);
      return;
    }
    if (name.text.trim().isEmpty || key.text.trim().isEmpty) {
      setState(() => error = 'نام نمایشی و کلید API الزامی است.');
      return;
    }
    url.text = baseUrl;
    url.selection = TextSelection.collapsed(offset: url.text.length);

    setState(() {
      busy = true;
      error = null;
    });

    final server = OdinVaultServer(
      id: widget.server?.id ?? '${DateTime.now().microsecondsSinceEpoch}-${Random().nextInt(1 << 20)}',
      name: name.text.trim(),
      baseUrl: baseUrl,
      apiKey: key.text.trim(),
    );

    try {
      if (!await OdinVaultApiClient(server).health()) {
        throw const OdinVaultApiException('Agent پاسخ سالم برنگرداند.');
      }
      if (mounted) Navigator.pop(context, server);
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: Text(widget.server == null ? 'افزودن OdinVault Agent' : 'ویرایش اتصال Agent'),
        content: SizedBox(
          width: 480,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: name,
                decoration: const InputDecoration(
                  labelText: 'نام نمایشی',
                  prefixIcon: Icon(Icons.badge_outlined),
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: url,
                textDirection: TextDirection.ltr,
                decoration: const InputDecoration(
                  labelText: 'آدرس Agent',
                  hintText: 'http://192.168.1.10:5188',
                  prefixIcon: Icon(Icons.link),
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: key,
                obscureText: !showKey,
                textDirection: TextDirection.ltr,
                decoration: InputDecoration(
                  labelText: 'کلید API',
                  prefixIcon: const Icon(Icons.key),
                  suffixIcon: IconButton(
                    tooltip: showKey ? 'پنهان کردن' : 'نمایش API Key',
                    onPressed: () => setState(() => showKey = !showKey),
                    icon: Icon(showKey ? Icons.visibility_off : Icons.visibility),
                  ),
                ),
              ),
              if (error != null) ...[
                const SizedBox(height: 12),
                Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
              ],
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: busy ? null : () => Navigator.pop(context),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: busy ? null : save,
            child: Text(busy ? 'در حال تست...' : (widget.server == null ? 'تست و ذخیره' : 'تست و بروزرسانی')),
          ),
        ],
      );
}

class AgentPage extends StatefulWidget {
  const AgentPage({super.key, required this.server});

  final OdinVaultServer server;

  @override
  State<AgentPage> createState() => _AgentPageState();
}

class _AgentPageState extends State<AgentPage> {
  late final OdinVaultApiClient api = OdinVaultApiClient(widget.server);

  List<OdinVaultDatabase>? databases;
  List<OdinVaultStorageTarget>? targets;
  OdinVaultDashboard? dashboard;
  OdinVaultAlerts? alerts;
  final Map<String, OdinVaultDatabaseOverview> databaseOverviews = {};
  final Set<String> selectedDatabaseIds = {};
  String? error;
  bool refreshing = false;
  bool runningBatch = false;
  String? batchProgressText;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    if (refreshing) return;
    setState(() => refreshing = true);
    try {
      final dbs = await api.databases();

      var storageTargets = targets ?? const <OdinVaultStorageTarget>[];
      var dashboardValue = dashboard;
      var alertsValue = alerts;
      var overviewValues = databaseOverviews.values.toList();

      await Future.wait([
        (() async {
          try {
            storageTargets = await api.storageTargets();
          } catch (_) {}
        })(),
        (() async {
          try {
            dashboardValue = await api.dashboard();
          } catch (_) {}
        })(),
        (() async {
          try {
            alertsValue = await api.alerts();
          } catch (_) {}
        })(),
        (() async {
          try {
            overviewValues = await api.databaseOverviews();
          } catch (_) {}
        })(),
      ]);

      if (!mounted) return;
      setState(() {
        databases = dbs;
        targets = storageTargets;
        dashboard = dashboardValue;
        alerts = alertsValue;
        error = null;
        databaseOverviews
          ..clear()
          ..addEntries(overviewValues.map((x) => MapEntry(x.id, x)));
        selectedDatabaseIds.removeWhere((id) => !dbs.any((db) => db.id == id));
      });
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => refreshing = false);
    }
  }

  Future<void> addDatabase() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => DatabaseDialog(api: api),
    );
    if (ok == true) await load();
  }

  Future<void> discoverDatabases() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => DiscoverDatabasesDialog(api: api),
    );
    if (ok == true) await load();
  }

  Future<void> manageTarget(OdinVaultStorageTarget target) async {
    final changed = await showDialog<bool>(
      context: context,
      builder: (_) => StorageTargetDialog(api: api, target: target),
    );
    if (changed == true) await load();
  }

  Future<void> backupSelected() async {
    final dbs = databases ?? const <OdinVaultDatabase>[];
    final selected = dbs.where((db) => selectedDatabaseIds.contains(db.id)).toList();
    if (selected.isEmpty) {
      _snack('حداقل یک دیتابیس را انتخاب کنید.');
      return;
    }

    final answer = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('بکاپ گروهی'),
        content: Text('برای ${selected.length} دیتابیس انتخاب‌شده بکاپ گرفته شود؟'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('انصراف')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('شروع بکاپ')),
        ],
      ),
    );
    if (answer != true) return;

    setState(() {
      runningBatch = true;
      batchProgressText = 'آماده‌سازی بکاپ گروهی...';
    });
    var success = 0;
    final failures = <String>[];
    for (var index = 0; index < selected.length; index++) {
      final db = selected[index];
      try {
        await api.backupNow(
          db.id,
          onProgress: (stage, percent) {
            if (!mounted) return;
            setState(() {
              batchProgressText =
                  '${index + 1}/${selected.length} • ${db.name} • ${_backupStageText(stage, percent)}';
            });
          },
        );
        success++;
      } catch (e) {
        failures.add('${db.name}: ${OdinVaultApiException.from(e).message}');
      }
    }
    if (!mounted) return;
    setState(() {
      runningBatch = false;
      batchProgressText = null;
    });
    await load();

    final message = failures.isEmpty
        ? '$success بکاپ با موفقیت انجام شد.'
        : '$success بکاپ موفق بود.\n${failures.join('\n')}';
    _snack(message);
  }

  Future<void> addReplica() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => ReplicaTargetDialog(api: api),
    );
    if (ok == true) await load();
  }

  Future<void> addGoogle() async {
    final result = await showDialog<_GoogleRequest>(
      context: context,
      builder: (_) => const GoogleDriveDialog(),
    );
    if (result == null) return;

    try {
      final pairing = await api.startGoogleDrivePairing(
        targetName: result.name,
        folderId: result.folderId,
      );
      if (!await launchUrl(
        Uri.parse(pairing.authorizationUrl),
        mode: LaunchMode.externalApplication,
      )) {
        throw const OdinVaultApiException('مرورگر برای اتصال Google Drive باز نشد.');
      }
      if (!mounted) return;

      showDialog<void>(
        context: context,
        barrierDismissible: false,
        builder: (_) => const _PairingProgressDialog(),
      );

      for (var i = 0; i < 60; i++) {
        await Future<void>.delayed(const Duration(seconds: 2));
        final status = await api.googleDrivePairingStatus(pairing.state);
        if (status.status == 'succeeded') {
          if (mounted) Navigator.of(context, rootNavigator: true).pop();
          await load();
          _snack('Google Drive با موفقیت متصل شد.');
          return;
        }
        if (status.status == 'failed') {
          if (mounted) Navigator.of(context, rootNavigator: true).pop();
          throw OdinVaultApiException(status.error ?? 'اتصال Google Drive ناموفق بود.');
        }
      }

      if (mounted) Navigator.of(context, rootNavigator: true).pop();
      throw const OdinVaultApiException('زمان اتصال Google Drive به پایان رسید.');
    } catch (e) {
      _snack(OdinVaultApiException.from(e).message);
    }
  }

  void _snack(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(text)));
  }

  Future<void> _showAlerts() async {
    final current = alerts;
    if (current == null) return;

    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (sheetContext) => SafeArea(
        child: SizedBox(
          height: MediaQuery.sizeOf(sheetContext).height * .72,
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        'هشدارها',
                        style: Theme.of(sheetContext).textTheme.titleLarge,
                      ),
                    ),
                    if (current.unreadCount > 0)
                      TextButton(
                        onPressed: () async {
                          final unread = current.alerts
                              .where((x) => !x.isRead && x.key.isNotEmpty)
                              .map((x) => x.key)
                              .toList();
                          try {
                            await api.markAlertsRead(unread);
                            if (sheetContext.mounted) Navigator.pop(sheetContext);
                            await load();
                          } catch (e) {
                            _snack(OdinVaultApiException.from(e).message);
                          }
                        },
                        child: const Text('خواندن همه'),
                      ),
                  ],
                ),
              ),
              const Divider(height: 1),
              Expanded(
                child: current.alerts.isEmpty
                    ? const _EmptyState(
                        icon: Icons.notifications_none,
                        title: 'هشداری ندارید',
                        subtitle: 'وضعیت فعلی Agent نیاز به اقدام فوری ندارد.',
                      )
                    : ListView.separated(
                        padding: const EdgeInsets.all(12),
                        itemCount: current.alerts.length,
                        separatorBuilder: (_, _) => const SizedBox(height: 6),
                        itemBuilder: (_, index) {
                          final item = current.alerts[index];
                          final critical = item.severity.toLowerCase() == 'critical' ||
                              item.severity.toLowerCase() == 'error';
                          return Card(
                            child: ListTile(
                              leading: Icon(
                                critical
                                    ? Icons.error_outline
                                    : Icons.warning_amber_outlined,
                                color: critical
                                    ? Theme.of(sheetContext).colorScheme.error
                                    : null,
                              ),
                              title: Text(item.title),
                              subtitle: Text(
                                [
                                  if (item.databaseName.isNotEmpty) item.databaseName,
                                  item.message,
                                  _formatDate(item.occurredAtUtc),
                                ].join('\n'),
                              ),
                              trailing: item.isRead
                                  ? null
                                  : const Icon(Icons.circle, size: 10),
                            ),
                          );
                        },
                      ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final dbs = databases;
    final storageTargets = targets;
    final googleCount = storageTargets?.where((x) => x.type == 2 && x.isEnabled).length ?? 0;

    return Scaffold(
      appBar: AppBar(
        title: Text(widget.server.name),
        actions: [
          PopupMenuButton<String>(
            tooltip: 'بخش‌های مدیریت',
            icon: const Icon(Icons.apps_outlined),
            onSelected: (value) {
              if (value == 'reports') {
                Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) => ReportsPage(server: widget.server),
                  ),
                );
              }
              if (value == 'storage') {
                Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) => StorageOverviewPage(server: widget.server),
                  ),
                ).then((_) => load());
              }
              if (value == 'backups') {
                Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) => BackupOverviewPage(server: widget.server),
                  ),
                ).then((_) => load());
              }
            },
            itemBuilder: (_) => const [
              PopupMenuItem(
                value: 'backups',
                child: ListTile(
                  leading: Icon(Icons.restore_page_outlined),
                  title: Text('بکاپ‌ها و Restore'),
                ),
              ),
              PopupMenuItem(
                value: 'storage',
                child: ListTile(
                  leading: Icon(Icons.cloud_queue_outlined),
                  title: Text('فضای ذخیره‌سازی'),
                ),
              ),
              PopupMenuItem(
                value: 'reports',
                child: ListTile(
                  leading: Icon(Icons.analytics_outlined),
                  title: Text('گزارش‌ها'),
                ),
              ),
            ],
          ),
          IconButton(
            tooltip: alerts == null ? 'هشدارها' : 'هشدارها (${alerts!.unreadCount})',
            onPressed: alerts == null ? null : _showAlerts,
            icon: Stack(
              clipBehavior: Clip.none,
              children: [
                const Icon(Icons.notifications_outlined),
                if ((alerts?.unreadCount ?? 0) > 0)
                  Positioned(
                    top: -4,
                    right: -6,
                    child: Container(
                      constraints: const BoxConstraints(minWidth: 16, minHeight: 16),
                      padding: const EdgeInsets.symmetric(horizontal: 4),
                      decoration: BoxDecoration(
                        color: Theme.of(context).colorScheme.error,
                        borderRadius: BorderRadius.circular(10),
                      ),
                      alignment: Alignment.center,
                      child: Text(
                        '${alerts!.unreadCount}',
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.onError,
                          fontSize: 9,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
          IconButton(
            tooltip: 'بروزرسانی',
            onPressed: refreshing ? null : load,
            icon: const Icon(Icons.refresh),
          ),
          PopupMenuButton<String>(
            onSelected: (value) {
              if (value == 'db') addDatabase();
              if (value == 'discover') discoverDatabases();
              if (value == 'replica') addReplica();
              if (value == 'google') addGoogle();
            },
            itemBuilder: (_) => const [
              PopupMenuItem(value: 'db', child: Text('افزودن دستی دیتابیس')),
              PopupMenuItem(value: 'discover', child: Text('شناسایی دیتابیس‌های SQL Server')),
              PopupMenuItem(value: 'google', child: Text('اتصال Google Drive')),
              PopupMenuItem(value: 'replica', child: Text('افزودن Agent ثانویه')),
            ],
          ),
        ],
      ),
      bottomNavigationBar: selectedDatabaseIds.isEmpty
          ? null
          : SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: FilledButton.icon(
                  onPressed: runningBatch ? null : backupSelected,
                  icon: runningBatch
                      ? const SizedBox(
                          width: 18,
                          height: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.backup),
                  label: Text(
                    runningBatch
                        ? (batchProgressText ?? 'در حال بکاپ...')
                        : 'بکاپ ${selectedDatabaseIds.length} دیتابیس انتخاب‌شده',
                    maxLines: 2,
                    textAlign: TextAlign.center,
                  ),
                ),
              ),
            ),
      body: RefreshIndicator(
        onRefresh: load,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.all(16),
          children: [
            if (error != null)
              Card(
                color: Theme.of(context).colorScheme.errorContainer,
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text(error!),
                ),
              ),
            _DashboardOverviewCard(
              dashboard: dashboard,
              alerts: alerts,
            ),
            const SizedBox(height: 12),
            _AgentQuickActions(
              onBackups: () => Navigator.push(
                context,
                MaterialPageRoute(
                  builder: (_) => BackupOverviewPage(server: widget.server),
                ),
              ).then((_) => load()),
              onStorage: () => Navigator.push(
                context,
                MaterialPageRoute(
                  builder: (_) => StorageOverviewPage(server: widget.server),
                ),
              ).then((_) => load()),
              onReports: () => Navigator.push(
                context,
                MaterialPageRoute(
                  builder: (_) => ReportsPage(server: widget.server),
                ),
              ),
            ),
            const SizedBox(height: 12),
            _AgentSummaryCard(
              databaseCount: dbs?.length ?? 0,
              googleDriveCount: googleCount,
              selectedCount: selectedDatabaseIds.length,
              loading: dbs == null,
            ),
            const SizedBox(height: 20),
            Row(
              children: [
                Expanded(
                  child: Text('دیتابیس‌ها', style: Theme.of(context).textTheme.titleLarge),
                ),
                if (dbs != null && dbs.isNotEmpty)
                  TextButton(
                    onPressed: () {
                      setState(() {
                        if (selectedDatabaseIds.length == dbs.length) {
                          selectedDatabaseIds.clear();
                        } else {
                          selectedDatabaseIds
                            ..clear()
                            ..addAll(dbs.map((x) => x.id));
                        }
                      });
                    },
                    child: Text(
                      selectedDatabaseIds.length == dbs.length ? 'لغو انتخاب همه' : 'انتخاب همه',
                    ),
                  ),
                TextButton.icon(
                  onPressed: addDatabase,
                  icon: const Icon(Icons.add),
                  label: const Text('افزودن'),
                ),
              ],
            ),
            if (dbs == null)
              const LinearProgressIndicator()
            else if (dbs.isEmpty)
              const _EmptyState(
                icon: Icons.storage_outlined,
                title: 'دیتابیسی ثبت نشده',
                subtitle: 'اولین دیتابیس SQL Server را اضافه کنید.',
              )
            else
              ...dbs.map(
                (db) => _DatabaseStatusCard(
                  database: db,
                  overview: databaseOverviews[db.id],
                  selected: selectedDatabaseIds.contains(db.id),
                  onSelected: (selected) {
                    setState(() {
                      if (selected) {
                        selectedDatabaseIds.add(db.id);
                      } else {
                        selectedDatabaseIds.remove(db.id);
                      }
                    });
                  },
                  onOpen: () => Navigator.push(
                    context,
                    MaterialPageRoute(
                      builder: (_) => DatabasePage(
                        server: widget.server,
                        databaseId: db.id,
                      ),
                    ),
                  ).then((_) => load()),
                  onEdit: () async {
                    final ok = await showDialog<bool>(
                      context: context,
                      builder: (_) => DatabaseDialog(api: api, database: db),
                    );
                    if (ok == true) await load();
                  },
                ),
              ),
            const SizedBox(height: 24),
            Row(
              children: [
                Expanded(
                  child: Text('فضای پشتیبان', style: Theme.of(context).textTheme.titleLarge),
                ),
                PopupMenuButton<String>(
                  tooltip: 'افزودن مقصد',
                  onSelected: (value) => value == 'google' ? addGoogle() : addReplica(),
                  itemBuilder: (_) => const [
                    PopupMenuItem(value: 'google', child: Text('Google Drive')),
                    PopupMenuItem(value: 'replica', child: Text('Agent ثانویه')),
                  ],
                ),
              ],
            ),
            if (storageTargets == null)
              const LinearProgressIndicator()
            else if (storageTargets.isEmpty)
              const _EmptyState(
                icon: Icons.cloud_outlined,
                title: 'مقصد پشتیبان ندارید',
                subtitle: 'Google Drive یا یک OdinVault Agent ثانویه متصل کنید.',
              )
            else
              ...storageTargets.map(
                (target) => Card(
                  child: ListTile(
                    leading: Icon(
                      target.type == 2 ? Icons.cloud_outlined : Icons.dns_outlined,
                    ),
                    title: Text(target.name),
                    subtitle: Text(
                      target.type == 2
                          ? (target.accountEmail ?? 'Google Drive')
                          : (target.baseUrl ?? 'OdinVault Replica'),
                    ),
                    trailing: Icon(
                      target.isEnabled && target.isConnected
                          ? Icons.check_circle
                          : Icons.pause_circle_outline,
                    ),
                    onTap: () => manageTarget(target),
                  ),
                ),
              ),
            const SizedBox(height: 96),
          ],
        ),
      ),
    );
  }
}

class ReportsPage extends StatefulWidget {
  const ReportsPage({super.key, required this.server});

  final OdinVaultServer server;

  @override
  State<ReportsPage> createState() => _ReportsPageState();
}

class _ReportsPageState extends State<ReportsPage> {
  late final OdinVaultApiClient api = OdinVaultApiClient(widget.server);
  OdinVaultBackupReport? report;
  int days = 30;
  bool loading = false;
  String? error;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    if (loading) return;
    setState(() => loading = true);
    try {
      final value = await api.backupReport(days: days);
      if (!mounted) return;
      setState(() {
        report = value;
        error = null;
      });
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = report;
    final summary = value?.summary;

    return Scaffold(
      appBar: AppBar(
        title: const Text('گزارش بکاپ'),
        actions: [
          PopupMenuButton<int>(
            tooltip: 'بازه گزارش',
            initialValue: days,
            onSelected: (value) {
              if (value == days) return;
              setState(() => days = value);
              load();
            },
            itemBuilder: (_) => const [
              PopupMenuItem(value: 7, child: Text('۷ روز اخیر')),
              PopupMenuItem(value: 30, child: Text('۳۰ روز اخیر')),
              PopupMenuItem(value: 90, child: Text('۹۰ روز اخیر')),
            ],
          ),
          IconButton(
            tooltip: 'بروزرسانی',
            onPressed: loading ? null : load,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: load,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.all(16),
          children: [
            if (loading && value == null) const LinearProgressIndicator(),
            if (error != null)
              Card(
                color: Theme.of(context).colorScheme.errorContainer,
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text(error!),
                ),
              ),
            if (summary != null) ...[
              Wrap(
                spacing: 10,
                runSpacing: 10,
                children: [
                  _ReportMetricCard(
                    title: 'کل بکاپ',
                    value: '${summary.totalBackups}',
                    icon: Icons.inventory_2_outlined,
                  ),
                  _ReportMetricCard(
                    title: 'موفق',
                    value: '${summary.succeeded}',
                    icon: Icons.check_circle_outline,
                  ),
                  _ReportMetricCard(
                    title: 'ناموفق',
                    value: '${summary.failed}',
                    icon: Icons.error_outline,
                  ),
                  _ReportMetricCard(
                    title: 'نرخ موفقیت',
                    value: summary.successRate == null
                        ? '-'
                        : '${summary.successRate!.toStringAsFixed(1)}٪',
                    icon: Icons.percent,
                  ),
                  _ReportMetricCard(
                    title: 'Verify ناموفق',
                    value: '${summary.verifyFailed}',
                    icon: Icons.fact_check_outlined,
                  ),
                  _ReportMetricCard(
                    title: 'Replica ناموفق',
                    value: '${summary.replicaFailed}',
                    icon: Icons.cloud_off_outlined,
                  ),
                  _ReportMetricCard(
                    title: 'حجم موفق',
                    value: _bytes(summary.totalSuccessfulBytes),
                    icon: Icons.data_usage_outlined,
                  ),
                  _ReportMetricCard(
                    title: 'فضای آزاد',
                    value: _bytes(summary.storageFreeBytes),
                    icon: Icons.sd_storage_outlined,
                  ),
                ],
              ),
              const SizedBox(height: 24),
              Text(
                'وضعیت دیتابیس‌ها',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 8),
              if (value!.databases.isEmpty)
                const Text('برای این بازه اطلاعاتی ثبت نشده است.')
              else
                ...value.databases.map(
                  (item) => Card(
                    child: ListTile(
                      leading: Icon(
                        item.failed > 0 || item.verifyFailed > 0 || item.replicaFailed > 0
                            ? Icons.warning_amber_outlined
                            : Icons.check_circle_outline,
                      ),
                      title: Text(item.databaseName),
                      subtitle: Text(
                        'موفق ${item.succeeded} از ${item.totalBackups}'
                        ' • Verify خطا ${item.verifyFailed}'
                        ' • Replica خطا ${item.replicaFailed}\n'
                        'آخرین بکاپ: ${_formatDate(item.latestBackupAtUtc)}'
                        ' • ${_bytes(item.latestSizeBytes)}',
                      ),
                      trailing: item.successRate == null
                          ? null
                          : Text('${item.successRate!.toStringAsFixed(0)}٪'),
                    ),
                  ),
                ),
              const SizedBox(height: 24),
              Text(
                'روزهای اخیر',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 8),
              ...value.daily.reversed.take(10).map(
                    (day) => ListTile(
                      dense: true,
                      leading: const Icon(Icons.calendar_today_outlined, size: 20),
                      title: Text(_formatDateOnly(day.dateUtc)),
                      subtitle: Text(
                        'موفق ${day.succeeded} • ناموفق ${day.failed}'
                        ' • Verify خطا ${day.verifyFailed}',
                      ),
                      trailing: Text(_bytes(day.totalSizeBytes)),
                    ),
                  ),
            ],
            const SizedBox(height: 48),
          ],
        ),
      ),
    );
  }
}

class StorageOverviewPage extends StatefulWidget {
  const StorageOverviewPage({super.key, required this.server});

  final OdinVaultServer server;

  @override
  State<StorageOverviewPage> createState() => _StorageOverviewPageState();
}

class _StorageOverviewPageState extends State<StorageOverviewPage> {
  late final OdinVaultApiClient api = OdinVaultApiClient(widget.server);
  OdinVaultStorageOverview? overview;
  bool loading = false;
  String? error;
  String? testingId;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    if (loading) return;
    setState(() => loading = true);
    try {
      final value = await api.storageOverview();
      if (!mounted) return;
      setState(() {
        overview = value;
        error = null;
      });
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => loading = false);
    }
  }

  Future<void> test(OdinVaultStorageTargetOverview target) async {
    setState(() => testingId = target.id);
    try {
      final result = await api.testStorageTargetConnection(target.id);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            result.message.isNotEmpty
                ? result.message
                : result.success
                    ? 'اتصال مقصد سالم است.'
                    : 'تست اتصال مقصد ناموفق بود.',
          ),
        ),
      );
      await load();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(OdinVaultApiException.from(e).message)),
        );
      }
    } finally {
      if (mounted) setState(() => testingId = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = overview;
    final local = value?.local;

    return Scaffold(
      appBar: AppBar(
        title: const Text('فضای ذخیره‌سازی'),
        actions: [
          IconButton(
            tooltip: 'بروزرسانی',
            onPressed: loading ? null : load,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: load,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.all(16),
          children: [
            if (loading && value == null) const LinearProgressIndicator(),
            if (error != null)
              Card(
                color: Theme.of(context).colorScheme.errorContainer,
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text(error!),
                ),
              ),
            if (local != null)
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        'فضای محلی Agent',
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      const SizedBox(height: 10),
                      _InfoRow(label: 'نام', value: local.name),
                      _InfoRow(label: 'فضای آزاد', value: _bytes(local.freeBytes)),
                      _InfoRow(
                        label: 'وضعیت مسیر',
                        value: local.exists && local.writable ? 'آماده' : 'نیازمند بررسی',
                      ),
                      const SizedBox(height: 8),
                      Directionality(
                        textDirection: TextDirection.ltr,
                        child: SelectableText(
                          local.directory,
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            const SizedBox(height: 20),
            Text(
              'مقصدهای پشتیبان',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 8),
            if (value == null && !loading)
              const Text('اطلاعاتی دریافت نشده است.')
            else if (value?.targets.isEmpty ?? false)
              const Text('مقصد ذخیره‌سازی تعریف نشده است.')
            else
              ...(value?.targets ?? const <OdinVaultStorageTargetOverview>[]).map(
                (target) => Card(
                  child: ExpansionTile(
                    leading: Icon(
                      target.type == 2 ? Icons.cloud_outlined : Icons.dns_outlined,
                    ),
                    title: Text(target.name),
                    subtitle: Text(
                      target.isConnected ? 'متصل' : 'قطع / نیازمند بررسی',
                    ),
                    trailing: testingId == target.id
                        ? const SizedBox(
                            width: 20,
                            height: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : null,
                    children: [
                      _InfoRow(
                        label: 'دیتابیس‌های متصل',
                        value: '${target.linkedDatabases}',
                      ),
                      _InfoRow(
                        label: 'Replica موفق',
                        value: '${target.succeededReplicas}',
                      ),
                      _InfoRow(
                        label: 'Replica ناموفق',
                        value: '${target.failedReplicas}',
                      ),
                      _InfoRow(
                        label: 'آخرین فعالیت',
                        value: _formatDate(target.lastActivityAtUtc),
                      ),
                      if (target.lastError != null && target.lastError!.isNotEmpty)
                        Padding(
                          padding: const EdgeInsets.all(12),
                          child: Text(
                            target.lastError!,
                            style: TextStyle(
                              color: Theme.of(context).colorScheme.error,
                            ),
                          ),
                        ),
                      Padding(
                        padding: const EdgeInsets.all(12),
                        child: SizedBox(
                          width: double.infinity,
                          child: OutlinedButton.icon(
                            onPressed: testingId == null ? () => test(target) : null,
                            icon: const Icon(Icons.cable),
                            label: const Text('تست اتصال'),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            const SizedBox(height: 48),
          ],
        ),
      ),
    );
  }
}

class BackupOverviewPage extends StatefulWidget {
  const BackupOverviewPage({super.key, required this.server});

  final OdinVaultServer server;

  @override
  State<BackupOverviewPage> createState() => _BackupOverviewPageState();
}

class _BackupOverviewPageState extends State<BackupOverviewPage> {
  late final OdinVaultApiClient api = OdinVaultApiClient(widget.server);
  OdinVaultBackupOverview? overview;
  bool loading = false;
  String? error;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    if (loading) return;
    setState(() => loading = true);
    try {
      final value = await api.backupOverview();
      if (!mounted) return;
      setState(() {
        overview = value;
        error = null;
      });
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => loading = false);
    }
  }

  Future<void> restoreBackup(OdinVaultBackupHistoryItem backup) async {
    final changed = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (_) => RestoreBackupDialog(
        api: api,
        backup: backup,
      ),
    );
    if (changed == true) await load();
  }

  @override
  Widget build(BuildContext context) {
    final value = overview;
    final activeJobs = value?.jobs.where((x) => x.completedAtUtc == null).toList() ??
        const <OdinVaultBackupJob>[];
    final recentProblemJobs = value?.jobs
            .where(
              (x) =>
                  x.completedAtUtc != null &&
                  (x.stage.toLowerCase() == 'failed' ||
                      x.stage.toLowerCase() == 'interrupted' ||
                      (x.errorMessage?.isNotEmpty ?? false)),
            )
            .take(10)
            .toList() ??
        const <OdinVaultBackupJob>[];

    return Scaffold(
      appBar: AppBar(
        title: const Text('بکاپ‌ها و بازیابی'),
        actions: [
          IconButton(
            tooltip: 'بروزرسانی',
            onPressed: loading ? null : load,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: load,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.all(16),
          children: [
            if (loading && value == null) const LinearProgressIndicator(),
            if (error != null)
              Card(
                color: Theme.of(context).colorScheme.errorContainer,
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text(error!),
                ),
              ),
            Text('عملیات فعال', style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 8),
            if (activeJobs.isEmpty)
              const Text('در حال حاضر Job فعالی وجود ندارد.')
            else
              ...activeJobs.map(
                (job) => Card(
                  child: ListTile(
                    leading: const SizedBox(
                      width: 24,
                      height: 24,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                    title: Text(job.databaseName),
                    subtitle: Text(_backupStageText(job.stage, job.percent)),
                    trailing: job.percent == null ? null : Text(_toPersianDigits('${job.percent}٪')),
                  ),
                ),
              ),
            if (recentProblemJobs.isNotEmpty) ...[
              const SizedBox(height: 24),
              Text(
                'عملیات ناموفق / متوقف‌شده اخیر',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 8),
              ...recentProblemJobs.map(
                (job) => Card(
                  child: ListTile(
                    leading: Icon(
                      job.stage.toLowerCase() == 'interrupted'
                          ? Icons.pause_circle_outline
                          : Icons.error_outline,
                    ),
                    title: Text(job.databaseName),
                    subtitle: Text(
                      [
                        _backupStageText(job.stage, job.percent),
                        if (job.errorMessage?.isNotEmpty ?? false)
                          job.errorMessage!,
                        _formatDate(job.completedAtUtc ?? job.updatedAtUtc),
                      ].join('\n'),
                    ),
                  ),
                ),
              ),
            ],
            const SizedBox(height: 24),
            Text('تاریخچه سراسری بکاپ', style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 8),
            if (value == null && !loading)
              const Text('اطلاعاتی دریافت نشده است.')
            else if (value?.backups.isEmpty ?? false)
              const Text('هنوز بکاپی ثبت نشده است.')
            else
              ...(value?.backups ?? const <OdinVaultBackupHistoryItem>[]).map(
                (backup) {
                  final canRestore = backup.status == 2 && backup.localFileAvailable;
                  return Card(
                    child: ListTile(
                      leading: Icon(
                        backup.status == 2
                            ? Icons.check_circle_outline
                            : Icons.error_outline,
                      ),
                      title: Text(backup.databaseName),
                      subtitle: Text(
                        [
                          '${_formatDate(backup.completedAtUtc ?? backup.startedAtUtc)}'
                              ' • ${_bytes(backup.sizeBytes)}'
                              ' • Verify: ${_verificationText(backup.verificationStatus)}',
                          if (backup.error?.isNotEmpty ?? false) backup.error!,
                        ].join('\n'),
                      ),
                      trailing: canRestore
                          ? IconButton(
                              tooltip: 'بازیابی به دیتابیس جدید',
                              onPressed: () => restoreBackup(backup),
                              icon: const Icon(Icons.restore),
                            )
                          : Tooltip(
                              message: _restoreUnavailableReason(backup),
                              child: const Icon(Icons.lock_outline),
                            ),
                      onTap: canRestore ? () => restoreBackup(backup) : null,
                    ),
                  );
                },
              ),
            const SizedBox(height: 48),
          ],
        ),
      ),
    );
  }
}

class RestoreBackupDialog extends StatefulWidget {
  const RestoreBackupDialog({
    super.key,
    required this.api,
    required this.backup,
  });

  final OdinVaultApiClient api;
  final OdinVaultBackupHistoryItem backup;

  @override
  State<RestoreBackupDialog> createState() => _RestoreBackupDialogState();
}

class _RestoreBackupDialogState extends State<RestoreBackupDialog> {
  late final target = TextEditingController(
    text: '${widget.backup.databaseName}_Restore',
  );

  OdinVaultRestorePreflight? preflight;
  String? preflightTarget;
  bool checking = false;
  bool restoring = false;
  String? error;

  @override
  void initState() {
    super.initState();
    target.addListener(_invalidatePreflight);
  }

  @override
  void dispose() {
    target.removeListener(_invalidatePreflight);
    target.dispose();
    super.dispose();
  }

  void _invalidatePreflight() {
    final normalized = target.text.trim();
    if (preflight != null && normalized != preflightTarget) {
      setState(() {
        preflight = null;
        preflightTarget = null;
      });
    }
  }

  Future<void> runPreflight() async {
    final targetName = target.text.trim();
    if (targetName.isEmpty) {
      setState(() => error = 'نام دیتابیس مقصد الزامی است.');
      return;
    }

    setState(() {
      checking = true;
      error = null;
      preflight = null;
      preflightTarget = null;
    });

    try {
      final value = await widget.api.preflightRestore(
        backupId: widget.backup.id,
        targetDatabaseName: targetName,
      );
      if (!mounted) return;
      setState(() {
        preflight = value;
        preflightTarget = targetName;
      });
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => checking = false);
    }
  }

  Future<void> restore() async {
    final targetName = target.text.trim();
    final ready = preflight;
    if (ready == null || preflightTarget != targetName) {
      setState(() => error = 'ابتدا پیش‌بررسی را برای مقصد فعلی اجرا کنید.');
      return;
    }

    setState(() {
      restoring = true;
      error = null;
    });

    try {
      final result = await widget.api.restore(
        backupId: widget.backup.id,
        targetDatabaseName: targetName,
      );
      if (!mounted) return;
      await showDialog<void>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: const Text('بازیابی انجام شد'),
          content: Text(
            'دیتابیس ${result.targetDatabaseName} با موفقیت Restore شد.\n'
            'مدت عملیات: ${result.durationSeconds.toStringAsFixed(1)} ثانیه',
          ),
          actions: [
            FilledButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text('باشه'),
            ),
          ],
        ),
      );
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => restoring = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final ready = preflight != null && preflightTarget == target.text.trim();

    return AlertDialog(
      title: const Text('بازیابی به دیتابیس جدید'),
      content: SizedBox(
        width: 620,
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                widget.backup.databaseName,
                style: Theme.of(context).textTheme.titleMedium,
              ),
              Text(
                '${_formatDate(widget.backup.completedAtUtc ?? widget.backup.startedAtUtc)}'
                ' • ${_bytes(widget.backup.sizeBytes)}',
              ),
              const SizedBox(height: 16),
              TextField(
                controller: target,
                enabled: !restoring,
                textDirection: TextDirection.ltr,
                decoration: const InputDecoration(
                  labelText: 'نام دیتابیس مقصد',
                  helperText: 'بازیابی فقط به دیتابیس جدید انجام می‌شود.',
                ),
              ),
              const SizedBox(height: 12),
              OutlinedButton.icon(
                onPressed: checking || restoring ? null : runPreflight,
                icon: checking
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.fact_check_outlined),
                label: Text(checking ? 'در حال بررسی...' : 'اجرای پیش‌بررسی'),
              ),
              if (preflight != null) ...[
                const SizedBox(height: 16),
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        const Text(
                          'Preflight موفق',
                          style: TextStyle(fontWeight: FontWeight.w700),
                        ),
                        const SizedBox(height: 8),
                        _InfoRow(label: 'Endpoint', value: preflight!.endpointName),
                        _InfoRow(
                          label: 'نسخه SQL Server',
                          value: preflight!.productVersion,
                        ),
                        _InfoRow(
                          label: 'Verify',
                          value: _verificationText(preflight!.verificationStatus),
                        ),
                        _InfoRow(
                          label: 'فایل بکاپ',
                          value: preflight!.backupFileName,
                        ),
                        _InfoRow(
                          label: 'حجم',
                          value: _bytes(preflight!.backupSizeBytes),
                        ),
                        const Divider(height: 20),
                        ...preflight!.files.map(
                          (file) => Padding(
                            padding: const EdgeInsets.only(bottom: 6),
                            child: Directionality(
                              textDirection: TextDirection.ltr,
                              child: Text(
                                '${file.logicalName} (${file.type})\n${file.targetPath}',
                                style: Theme.of(context).textTheme.bodySmall,
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ],
              if (error != null) ...[
                const SizedBox(height: 12),
                Text(
                  error!,
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
              ],
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: restoring ? null : () => Navigator.pop(context, false),
          child: const Text('انصراف'),
        ),
        FilledButton.icon(
          onPressed: ready && !restoring ? restore : null,
          icon: restoring
              ? const SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Icon(Icons.restore),
          label: Text(restoring ? 'در حال بازیابی...' : 'اجرای بازیابی'),
        ),
      ],
    );
  }
}

class DatabasePage extends StatefulWidget {
  const DatabasePage({
    super.key,
    required this.server,
    required this.databaseId,
  });

  final OdinVaultServer server;
  final String databaseId;

  @override
  State<DatabasePage> createState() => _DatabasePageState();
}

class _DatabasePageState extends State<DatabasePage> {
  late final api = OdinVaultApiClient(widget.server);
  OdinVaultDatabase? db;
  OdinVaultDatabaseDetails? details;
  List<OdinVaultBackup>? history;
  List<OdinVaultStorageTarget>? allTargets;
  List<DatabaseStorageLink>? linkedTargets;
  bool running = false;
  bool deleting = false;
  String? backupStage;
  int? backupPercent;
  String? error;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    try {
      final database = await api.database(widget.databaseId);

      var detailsValue = details;
      var historyValue = history ?? const <OdinVaultBackup>[];
      var targetsValue = allTargets ?? const <OdinVaultStorageTarget>[];
      var linkedValue = linkedTargets ?? const <DatabaseStorageLink>[];

      await Future.wait([
        (() async {
          try {
            detailsValue = await api.databaseDetails(widget.databaseId);
          } catch (_) {}
        })(),
        (() async {
          try {
            historyValue = await api.backups(widget.databaseId);
          } catch (_) {}
        })(),
        (() async {
          try {
            targetsValue = await api.storageTargets();
          } catch (_) {}
        })(),
        (() async {
          try {
            linkedValue = await api.databaseStorageTargets(widget.databaseId);
          } catch (_) {}
        })(),
      ]);

      if (!mounted) return;
      setState(() {
        db = database;
        details = detailsValue;
        history = historyValue;
        allTargets = targetsValue;
        linkedTargets = linkedValue;
        error = null;
      });
    } catch (e) {
      if (mounted) {
        setState(() => error = OdinVaultApiException.from(e).message);
      }
    }
  }

  Future<void> backup() async {
    setState(() {
      running = true;
      backupStage = 'queued';
      backupPercent = null;
    });
    try {
      await api.backupNow(
        widget.databaseId,
        onProgress: (stage, percent) {
          if (!mounted) return;
          setState(() {
            backupStage = stage;
            backupPercent = percent;
          });
        },
      );
      await load();
      _snack('بکاپ با موفقیت انجام شد.');
    } catch (e) {
      _snack(OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) {
        setState(() {
          running = false;
          backupStage = null;
          backupPercent = null;
        });
      }
    }
  }

  Future<void> deleteDatabase() async {
    if (deleting || running) return;
    final database = db;
    if (database == null) return;
    final answer = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('حذف دیتابیس از OdinVault'),
        content: Text(
          '«${database.name}» از تنظیمات OdinVault حذف شود؟\n'
          'History حذف می‌شود ولی فایل‌های بکاپ روی دیسک باقی می‌مانند.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('انصراف')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('حذف')),
        ],
      ),
    );
    if (answer != true) return;

    setState(() => deleting = true);
    try {
      await api.deleteDatabase(database.id, deleteHistory: true, deleteFiles: false);
      if (mounted) Navigator.pop(context);
    } catch (e) {
      _snack(OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => deleting = false);
    }
  }

  Future<void> setTarget(OdinVaultStorageTarget target, bool enabled) async {
    try {
      if (enabled) {
        await api.linkStorageTarget(widget.databaseId, target.id, enabled: true);
      } else {
        await api.unlinkStorageTarget(widget.databaseId, target.id);
      }
      await load();
    } catch (e) {
      _snack(OdinVaultApiException.from(e).message);
    }
  }

  void _snack(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(text)));
  }

  @override
  Widget build(BuildContext context) {
    final database = db;
    final linked = linkedTargets?.map((x) => x.id).toSet() ?? <String>{};

    return Scaffold(
      appBar: AppBar(
        title: Text(database?.name ?? 'دیتابیس'),
        actions: [
          if (database != null)
            IconButton(
              tooltip: 'ویرایش',
              icon: const Icon(Icons.edit_outlined),
              onPressed: () async {
                final ok = await showDialog<bool>(
                  context: context,
                  builder: (_) => DatabaseDialog(api: api, database: database),
                );
                if (ok == true) await load();
              },
            ),
          PopupMenuButton<String>(
            enabled: !deleting && !running,
            onSelected: (value) {
              if (value == 'delete') deleteDatabase();
            },
            itemBuilder: (_) => const [
              PopupMenuItem(value: 'delete', child: Text('حذف دیتابیس')),
            ],
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: load,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.all(16),
          children: [
            if (error != null) Text(error!),
            if (database == null)
              const LinearProgressIndicator()
            else ...[
              _DatabaseProtectionCard(details: details),
              const SizedBox(height: 12),
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(database.databaseName, style: Theme.of(context).textTheme.titleLarge),
                      const SizedBox(height: 4),
                      Directionality(
                        textDirection: TextDirection.ltr,
                        child: Text(
                          '${database.host}${database.port == null ? '' : ':${database.port}'}',
                          textAlign: TextAlign.right,
                        ),
                      ),
                      const Divider(height: 24),
                      _InfoRow(
                        label: 'زمان‌بندی',
                        value: ScheduleValue.displayCron(database.scheduleCron),
                      ),
                      _InfoRow(
                        label: 'نگهداری محلی',
                        value: '${database.maxLocalBackups} فایل',
                      ),
                      _InfoRow(
                        label: 'Verify',
                        value: database.verifyAfterBackup ? 'فعال' : 'غیرفعال',
                      ),
                      _InfoRow(
                        label: 'وضعیت',
                        value: database.isEnabled ? 'فعال' : 'غیرفعال',
                      ),
                      const SizedBox(height: 12),
                      Row(
                        children: [
                          Expanded(
                            child: OutlinedButton.icon(
                              onPressed: () async {
                                try {
                                  await api.testDatabase(database.id);
                                  _snack('اتصال SQL Server سالم است.');
                                } catch (e) {
                                  _snack(OdinVaultApiException.from(e).message);
                                }
                              },
                              icon: const Icon(Icons.cable),
                              label: const Text('تست اتصال'),
                            ),
                          ),
                          const SizedBox(width: 12),
                          Expanded(
                            child: FilledButton.icon(
                              onPressed: running ? null : backup,
                              icon: running
                                  ? const SizedBox(
                                      width: 18,
                                      height: 18,
                                      child: CircularProgressIndicator(strokeWidth: 2),
                                    )
                                  : const Icon(Icons.backup),
                              label: Text(running ? 'در حال بکاپ...' : 'بکاپ الان'),
                            ),
                          ),
                        ],
                      ),
                      if (running) ...[
                        const SizedBox(height: 12),
                        LinearProgressIndicator(
                          value: backupPercent == null
                              ? null
                              : (backupPercent!.clamp(0, 100) / 100),
                        ),
                        const SizedBox(height: 6),
                        Text(
                          _backupStageText(backupStage ?? '', backupPercent),
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                      const SizedBox(height: 10),
                      FilledButton.tonalIcon(
                        onPressed: running || !database.isEnabled || !database.policyEnabled
                            ? null
                            : () async {
                                setState(() => running = true);
                                await showBackupDownload(
                                  context,
                                  api,
                                  databaseId: database.id,
                                );
                                if (!mounted) return;
                                setState(() => running = false);
                                await load();
                              },
                        icon: const Icon(Icons.download_for_offline_outlined),
                        label: const Text('بکاپ جدید و دانلود روی گوشی'),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 20),
              Text('مقصدهای این دیتابیس', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 8),
              if (allTargets == null)
                const LinearProgressIndicator()
              else if (allTargets!.isEmpty)
                const Text('هنوز Google Drive یا Agent ثانویه‌ای تعریف نشده است.')
              else
                ...allTargets!.map(
                  (target) => SwitchListTile(
                    value: linked.contains(target.id),
                    onChanged: target.isEnabled ? (value) => setTarget(target, value) : null,
                    secondary: Icon(
                      target.type == 2 ? Icons.cloud_outlined : Icons.dns_outlined,
                    ),
                    title: Text(target.name),
                    subtitle: Text(
                      target.type == 2 ? 'Google Drive' : 'OdinVault Agent ثانویه',
                    ),
                  ),
                ),
              const SizedBox(height: 20),
              Text('تاریخچه بکاپ', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 8),
              if (history == null)
                const LinearProgressIndicator()
              else if (history!.isEmpty)
                const Text('هنوز بکاپی ثبت نشده است.')
              else
                ...history!.map((backup) => BackupTile(api: api, backup: backup)),
            ],
          ],
        ),
      ),
    );
  }
}

class DatabaseDialog extends StatefulWidget {
  const DatabaseDialog({
    super.key,
    required this.api,
    this.database,
  });

  final OdinVaultApiClient api;
  final OdinVaultDatabase? database;

  @override
  State<DatabaseDialog> createState() => _DatabaseDialogState();
}

class _DatabaseDialogState extends State<DatabaseDialog> {
  late final name = TextEditingController(text: widget.database?.name ?? '');
  late final host = TextEditingController(text: widget.database?.host ?? 'localhost');
  late final port = TextEditingController(text: widget.database?.port?.toString() ?? '1433');
  late final databaseName = TextEditingController(text: widget.database?.databaseName ?? '');
  late final username = TextEditingController(text: widget.database?.username ?? 'sa');
  final password = TextEditingController();
  late final backupDirectory = TextEditingController(
    text: widget.database?.backupDirectory ?? r'D:\Backups\OdinVault',
  );
  late final retention = TextEditingController(
    text: (widget.database?.maxLocalBackups ?? 7).toString(),
  );

  late bool trust = widget.database?.trustServerCertificate ?? true;
  late bool enabled = widget.database?.isEnabled ?? true;
  late bool verify = widget.database?.verifyAfterBackup ?? true;
  late ScheduleValue schedule = ScheduleValue.fromCron(widget.database?.scheduleCron);
  bool busy = false;
  String? error;

  @override
  void dispose() {
    name.dispose();
    host.dispose();
    port.dispose();
    databaseName.dispose();
    username.dispose();
    password.dispose();
    backupDirectory.dispose();
    retention.dispose();
    super.dispose();
  }

  Future<void> save() async {
    if ([name.text, host.text, databaseName.text, backupDirectory.text]
        .any((x) => x.trim().isEmpty)) {
      setState(() => error = 'نام، سرور، دیتابیس و مسیر بکاپ الزامی است.');
      return;
    }

    String? scheduleCron;
    try {
      scheduleCron = schedule.toCron();
    } on FormatException catch (e) {
      setState(() => error = e.message);
      return;
    }

    setState(() {
      busy = true;
      error = null;
    });

    try {
      final p = int.tryParse(port.text.trim());
      final keep = int.tryParse(retention.text.trim()) ?? 7;

      if (widget.database == null) {
        await widget.api.createDatabase(
          name: name.text.trim(),
          host: host.text.trim(),
          port: p,
          databaseName: databaseName.text.trim(),
          username: username.text.trim(),
          password: password.text,
          trustServerCertificate: trust,
          backupDirectory: backupDirectory.text.trim(),
          maxLocalBackups: keep,
          verifyAfterBackup: verify,
          scheduleCron: scheduleCron,
          isEnabled: enabled,
        );
      } else {
        await widget.api.updateDatabase(
          widget.database!,
          name: name.text.trim(),
          host: host.text.trim(),
          port: p,
          databaseName: databaseName.text.trim(),
          username: username.text.trim(),
          password: password.text.isEmpty ? null : password.text,
          trustServerCertificate: trust,
          isEnabled: enabled,
        );
        await widget.api.updatePolicy(
          widget.database!.id,
          backupDirectory: backupDirectory.text.trim(),
          maxLocalBackups: keep,
          verifyAfterBackup: verify,
          scheduleCron: scheduleCron,
          isEnabled: enabled,
        );
      }

      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: Text(widget.database == null ? 'افزودن دیتابیس' : 'ویرایش دیتابیس'),
        content: SizedBox(
          width: 560,
          child: SingleChildScrollView(
            child: Column(
              children: [
                TextField(controller: name, decoration: const InputDecoration(labelText: 'نام نمایشی')),
                const SizedBox(height: 12),
                TextField(
                  controller: host,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(labelText: 'آدرس SQL Server'),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: port,
                  keyboardType: TextInputType.number,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(labelText: 'پورت'),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: databaseName,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(labelText: 'نام دیتابیس'),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: username,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(labelText: 'نام کاربری'),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: password,
                  obscureText: true,
                  textDirection: TextDirection.ltr,
                  decoration: InputDecoration(
                    labelText: widget.database == null
                        ? 'رمز عبور'
                        : 'رمز عبور جدید (خالی = بدون تغییر)',
                  ),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: backupDirectory,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(labelText: 'مسیر بکاپ روی SQL Server'),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: retention,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(labelText: 'تعداد بکاپ محلی قابل نگهداری'),
                ),
                const SizedBox(height: 16),
                ScheduleEditor(
                  value: schedule,
                  onChanged: (value) => schedule = value,
                ),
                const SizedBox(height: 8),
                SwitchListTile(
                  value: trust,
                  onChanged: (value) => setState(() => trust = value),
                  title: const Text('اعتماد به گواهی سرور'),
                ),
                SwitchListTile(
                  value: verify,
                  onChanged: (value) => setState(() => verify = value),
                  title: const Text('Verify بعد از بکاپ'),
                ),
                SwitchListTile(
                  value: enabled,
                  onChanged: (value) => setState(() => enabled = value),
                  title: const Text('فعال'),
                ),
                if (error != null)
                  Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
              ],
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: busy ? null : () => Navigator.pop(context),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: busy ? null : save,
            child: Text(busy ? 'در حال ذخیره...' : 'ذخیره'),
          ),
        ],
      );
}

class DiscoverDatabasesDialog extends StatefulWidget {
  const DiscoverDatabasesDialog({super.key, required this.api});

  final OdinVaultApiClient api;

  @override
  State<DiscoverDatabasesDialog> createState() => _DiscoverDatabasesDialogState();
}

class _DiscoverDatabasesDialogState extends State<DiscoverDatabasesDialog> {
  final host = TextEditingController(text: 'localhost');
  final port = TextEditingController(text: '1433');
  final username = TextEditingController(text: 'sa');
  final password = TextEditingController();
  final backupDirectory = TextEditingController(text: r'D:\Backups\OdinVault');
  final retention = TextEditingController(text: '7');

  bool trust = true;
  bool verify = true;
  bool busy = false;
  bool adding = false;
  String? error;
  List<DiscoveredDatabase>? items;
  final Set<String> selected = {};
  ScheduleValue schedule = ScheduleValue.fromCron(null);

  @override
  void dispose() {
    host.dispose();
    port.dispose();
    username.dispose();
    password.dispose();
    backupDirectory.dispose();
    retention.dispose();
    super.dispose();
  }

  Future<void> discover() async {
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final result = await widget.api.discoverDatabases(
        host: host.text.trim(),
        port: int.tryParse(port.text.trim()),
        username: username.text.trim(),
        password: password.text,
        trustServerCertificate: trust,
      );
      if (!mounted) return;
      setState(() {
        items = result;
        selected
          ..clear()
          ..addAll(
            result
                .where((x) => x.canBackup && !x.isRegistered)
                .map((x) => x.name),
          );
      });
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> addSelected() async {
    final databases = (items ?? const <DiscoveredDatabase>[])
        .where((x) => selected.contains(x.name) && x.canBackup && !x.isRegistered)
        .toList();
    if (databases.isEmpty) {
      setState(() => error = 'حداقل یک دیتابیس آماده بکاپ را انتخاب کنید.');
      return;
    }

    String? cron;
    try {
      cron = schedule.toCron();
    } on FormatException catch (e) {
      setState(() => error = e.message);
      return;
    }

    setState(() {
      adding = true;
      error = null;
    });

    final failures = <String>[];
    for (final database in databases) {
      try {
        await widget.api.createDatabase(
          name: database.name,
          host: host.text.trim(),
          port: int.tryParse(port.text.trim()),
          databaseName: database.name,
          username: username.text.trim(),
          password: password.text,
          trustServerCertificate: trust,
          backupDirectory: backupDirectory.text.trim(),
          maxLocalBackups: int.tryParse(retention.text.trim()) ?? 7,
          verifyAfterBackup: verify,
          scheduleCron: cron,
          isEnabled: true,
        );
      } catch (e) {
        failures.add('${database.name}: ${OdinVaultApiException.from(e).message}');
      }
    }

    if (!mounted) return;
    if (failures.isEmpty) {
      Navigator.pop(context, true);
      return;
    }

    setState(() {
      adding = false;
      error = failures.join('\n');
    });
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: const Text('شناسایی دیتابیس‌های SQL Server'),
        content: SizedBox(
          width: 620,
          height: MediaQuery.sizeOf(context).height * .72,
          child: SingleChildScrollView(
            child: Column(
              children: [
                Row(
                  children: [
                    Expanded(
                      flex: 3,
                      child: TextField(
                        controller: host,
                        textDirection: TextDirection.ltr,
                        decoration: const InputDecoration(labelText: 'آدرس SQL Server'),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: TextField(
                        controller: port,
                        textDirection: TextDirection.ltr,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(labelText: 'پورت'),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: username,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(labelText: 'نام کاربری'),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: password,
                  obscureText: true,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(labelText: 'Password'),
                ),
                SwitchListTile(
                  value: trust,
                  onChanged: (value) => setState(() => trust = value),
                  title: const Text('اعتماد به گواهی سرور'),
                ),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                    onPressed: busy ? null : discover,
                    icon: busy
                        ? const SizedBox(
                            width: 18,
                            height: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.search),
                    label: const Text('شناسایی دیتابیس‌ها'),
                  ),
                ),
                if (items != null) ...[
                  const Divider(height: 28),
                  ...items!.map((database) {
                    final selectable = database.canBackup && !database.isRegistered;
                    final reason = database.isRegistered
                        ? 'قبلاً ثبت شده'
                        : database.isSystem
                            ? 'سیستمی'
                            : !database.hasAccess
                                ? 'دسترسی ندارد'
                                : database.state != 'ONLINE'
                                    ? database.state
                                    : 'آماده بکاپ';
                    return CheckboxListTile(
                      value: selected.contains(database.name),
                      onChanged: selectable
                          ? (value) => setState(() {
                                if (value == true) {
                                  selected.add(database.name);
                                } else {
                                  selected.remove(database.name);
                                }
                              })
                          : null,
                      title: Text(database.name),
                      subtitle: Text('${database.recoveryModel} • $reason'),
                    );
                  }),
                  const Divider(height: 28),
                  TextField(
                    controller: backupDirectory,
                    textDirection: TextDirection.ltr,
                    decoration: const InputDecoration(labelText: 'مسیر بکاپ روی SQL Server'),
                  ),
                  const SizedBox(height: 10),
                  TextField(
                    controller: retention,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(labelText: 'تعداد بکاپ محلی'),
                  ),
                  SwitchListTile(
                    value: verify,
                    onChanged: (value) => setState(() => verify = value),
                    title: const Text('Verify بعد از بکاپ'),
                  ),
                  ScheduleEditor(
                    value: schedule,
                    onChanged: (value) => schedule = value,
                  ),
                ],
                if (error != null) ...[
                  const SizedBox(height: 10),
                  Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
                ],
              ],
            ),
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('انصراف')),
          if (items != null)
            FilledButton(
              onPressed: adding ? null : addSelected,
              child: Text(adding ? 'در حال افزودن...' : 'افزودن انتخاب‌شده‌ها'),
            ),
        ],
      );
}

class StorageTargetDialog extends StatefulWidget {
  const StorageTargetDialog({
    super.key,
    required this.api,
    required this.target,
  });

  final OdinVaultApiClient api;
  final OdinVaultStorageTarget target;

  @override
  State<StorageTargetDialog> createState() => _StorageTargetDialogState();
}

class _StorageTargetDialogState extends State<StorageTargetDialog> {
  late final name = TextEditingController(text: widget.target.name);
  late final folder = TextEditingController(text: widget.target.folderId ?? '');
  late bool enabled = widget.target.isEnabled;
  bool busy = false;
  bool testing = false;
  String? error;

  @override
  void dispose() {
    name.dispose();
    folder.dispose();
    super.dispose();
  }

  Future<void> save() async {
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.api.updateStorageTarget(
        widget.target.id,
        name: name.text.trim(),
        folderId: widget.target.type == 2 && folder.text.trim().isNotEmpty
            ? folder.text.trim()
            : null,
        isEnabled: enabled,
      );
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> remove() async {
    if (busy || testing) return;
    final answer = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('حذف مقصد پشتیبان'),
        content: const Text(
          'اگر برای این مقصد History بکاپ وجود داشته باشد، Agent اجازه حذف نمی‌دهد و باید آن را غیرفعال کنید.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('انصراف')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('حذف')),
        ],
      ),
    );
    if (answer != true) return;

    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.api.deleteStorageTarget(widget.target.id);
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> testReplica() async {
    if (busy || testing) return;
    setState(() {
      testing = true;
      error = null;
    });
    try {
      await widget.api.testReplicaTarget(widget.target.id);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('اتصال Agent ثانویه سالم است.')),
        );
      }
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => testing = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: Text(widget.target.type == 2 ? 'تنظیمات Google Drive' : 'تنظیمات Agent ثانویه'),
        content: SizedBox(
          width: 500,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: name,
                decoration: const InputDecoration(labelText: 'نام مقصد'),
              ),
              if (widget.target.type == 2) ...[
                const SizedBox(height: 12),
                TextField(
                  controller: folder,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(labelText: 'شناسه پوشه'),
                ),
                if (widget.target.accountEmail != null) ...[
                  const SizedBox(height: 8),
                  Align(
                    alignment: Alignment.centerRight,
                    child: Text('حساب: ${widget.target.accountEmail}'),
                  ),
                ],
              ],
              SwitchListTile(
                value: enabled,
                onChanged: busy || testing
                    ? null
                    : (value) => setState(() => enabled = value),
                title: const Text('فعال'),
              ),
              if (widget.target.type == 5)
                OutlinedButton.icon(
                  onPressed: busy || testing ? null : testReplica,
                  icon: testing
                      ? const SizedBox(
                          width: 18,
                          height: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.cable),
                  label: Text(
                    testing ? 'در حال تست...' : 'تست اتصال Agent ثانویه',
                  ),
                ),
              if (error != null) ...[
                const SizedBox(height: 10),
                Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
              ],
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: busy || testing ? null : remove,
            child: const Text('حذف مقصد'),
          ),
          TextButton(
            onPressed: busy || testing ? null : () => Navigator.pop(context),
            child: const Text('انصراف'),
          ),
          FilledButton(
            onPressed: busy || testing ? null : save,
            child: Text(busy ? 'در حال ذخیره...' : 'ذخیره'),
          ),
        ],
      );
}

class ReplicaTargetDialog extends StatefulWidget {
  const ReplicaTargetDialog({super.key, required this.api});

  final OdinVaultApiClient api;

  @override
  State<ReplicaTargetDialog> createState() => _ReplicaTargetDialogState();
}

class _ReplicaTargetDialogState extends State<ReplicaTargetDialog> {
  final name = TextEditingController();
  final url = TextEditingController(text: 'http://');
  final key = TextEditingController();
  bool busy = false;
  String? error;

  @override
  void dispose() {
    name.dispose();
    url.dispose();
    key.dispose();
    super.dispose();
  }

  Future<void> save() async {
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.api.createReplicaTarget(
        name: name.text.trim(),
        baseUrl: url.text.trim(),
        apiKey: key.text.trim(),
      );
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: const Text('افزودن OdinVault Agent ثانویه'),
        content: SizedBox(
          width: 480,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(controller: name, decoration: const InputDecoration(labelText: 'نام')),
              const SizedBox(height: 12),
              TextField(
                controller: url,
                textDirection: TextDirection.ltr,
                decoration: const InputDecoration(labelText: 'آدرس Agent'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: key,
                obscureText: true,
                textDirection: TextDirection.ltr,
                decoration: const InputDecoration(labelText: 'کلید API'),
              ),
              if (error != null) ...[
                const SizedBox(height: 12),
                Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
              ],
            ],
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('انصراف')),
          FilledButton(onPressed: busy ? null : save, child: const Text('ذخیره و تست')),
        ],
      );
}

class GoogleDriveDialog extends StatefulWidget {
  const GoogleDriveDialog({super.key});

  @override
  State<GoogleDriveDialog> createState() => _GoogleDriveDialogState();
}

class _GoogleDriveDialogState extends State<GoogleDriveDialog> {
  final name = TextEditingController(text: 'Google Drive');
  final folder = TextEditingController();

  @override
  void dispose() {
    name.dispose();
    folder.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: const Text('اتصال Google Drive'),
        content: SizedBox(
          width: 480,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(controller: name, decoration: const InputDecoration(labelText: 'نام مقصد')),
              const SizedBox(height: 12),
              TextField(
                controller: folder,
                textDirection: TextDirection.ltr,
                decoration: const InputDecoration(
                  labelText: 'شناسه پوشه (اختیاری)',
                  helperText: 'اگر خالی باشد، تنظیم پیش‌فرض Agent استفاده می‌شود.',
                ),
              ),
            ],
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('انصراف')),
          FilledButton(
            onPressed: () => Navigator.pop(
              context,
              _GoogleRequest(
                name.text.trim().isEmpty ? 'Google Drive' : name.text.trim(),
                folder.text.trim().isEmpty ? null : folder.text.trim(),
              ),
            ),
            child: const Text('اتصال به Google'),
          ),
        ],
      );
}

class _GoogleRequest {
  const _GoogleRequest(this.name, this.folderId);

  final String name;
  final String? folderId;
}

class _PairingProgressDialog extends StatelessWidget {
  const _PairingProgressDialog();

  @override
  Widget build(BuildContext context) => const AlertDialog(
        title: Text('در حال اتصال Google Drive'),
        content: Row(
          children: [
            CircularProgressIndicator(),
            SizedBox(width: 16),
            Expanded(
              child: Text('ورود به حساب Google را در مرورگر کامل کنید و سپس به برنامه برگردید.'),
            ),
          ],
        ),
      );
}

class BackupTile extends StatefulWidget {
  const BackupTile({
    super.key,
    required this.api,
    required this.backup,
  });

  final OdinVaultApiClient api;
  final OdinVaultBackup backup;

  @override
  State<BackupTile> createState() => _BackupTileState();
}

class _BackupTileState extends State<BackupTile> {
  List<OdinVaultReplica>? replicas;
  bool loadingReplicas = false;
  bool retrying = false;
  String? replicaLoadError;

  Future<void> load() async {
    if (loadingReplicas) return;
    setState(() {
      loadingReplicas = true;
      replicaLoadError = null;
    });
    try {
      final value = await widget.api.replicas(widget.backup.id);
      if (mounted) setState(() => replicas = value);
    } catch (e) {
      if (mounted) {
        setState(() {
          replicaLoadError = OdinVaultApiException.from(e).message;
        });
      }
    } finally {
      if (mounted) setState(() => loadingReplicas = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final backup = widget.backup;
    return Card(
      child: ExpansionTile(
        onExpansionChanged: (expanded) {
          if (expanded && replicas == null) load();
        },
        leading: Icon(
          backup.status == 2 ? Icons.check_circle_outline : Icons.error_outline,
        ),
        title: Text(
          backup.fileName.isEmpty ? backup.id.substring(0, 8) : backup.fileName,
          textDirection: TextDirection.ltr,
        ),
        subtitle: Text(
          '${_bytes(backup.sizeBytes)} • ${_formatDate(backup.completedAtUtc ?? backup.startedAtUtc)}',
        ),
        children: [
          if (backup.status == 2 && backup.localFileAvailable)
            Padding(
              padding: const EdgeInsets.all(12),
              child: FilledButton.icon(
                onPressed: () => showBackupDownload(
                  context,
                  widget.api,
                  backup: backup,
                ),
                icon: const Icon(Icons.download_rounded),
                label: const Text('دانلود این بکاپ روی گوشی'),
              ),
            )
          else if (backup.status == 2)
            const ListTile(
              title: Text('نسخه محلی این بکاپ دیگر روی سرور موجود نیست.'),
            ),
          ListTile(
            title: const Text('Verify'),
            trailing: Text(_verificationText(backup.verificationStatus)),
          ),
          if (backup.error != null)
            Padding(
              padding: const EdgeInsets.all(12),
              child: Text(
                backup.error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          if (loadingReplicas)
            const Padding(
              padding: EdgeInsets.all(12),
              child: LinearProgressIndicator(),
            )
          else if (replicaLoadError != null)
            ListTile(
              leading: const Icon(Icons.error_outline),
              title: const Text('دریافت وضعیت Replica ناموفق بود'),
              subtitle: Text(replicaLoadError!),
              trailing: IconButton(
                tooltip: 'تلاش مجدد',
                onPressed: load,
                icon: const Icon(Icons.refresh),
              ),
            )
          else if (replicas?.isEmpty ?? false)
            const ListTile(
              leading: Icon(Icons.cloud_off_outlined),
              title: Text('کپی ثانویه‌ای برای این بکاپ ثبت نشده'),
            )
          else if (replicas != null)
            ...replicas!.map(
              (replica) => ListTile(
                leading: Icon(
                  replica.status == 2 ? Icons.cloud_done_outlined : Icons.cloud_off_outlined,
                ),
                title: Text('Replica ${replica.storageTargetId.substring(0, 8)}'),
                subtitle: Text(replica.error ?? replica.remotePath ?? 'ذخیره شده'),
              ),
            ),
          if (backup.status == 2)
            TextButton.icon(
              onPressed: retrying
                  ? null
                  : () async {
                      setState(() => retrying = true);
                      try {
                        await widget.api.retryReplication(backup.id);
                        await load();
                        if (!mounted) return;
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(
                            content: Text('ارسال مجدد به مقصدها انجام شد.'),
                          ),
                        );
                      } catch (e) {
                        if (!mounted) return;
                        ScaffoldMessenger.of(context).showSnackBar(
                          SnackBar(
                            content: Text(
                              OdinVaultApiException.from(e).message,
                            ),
                          ),
                        );
                      } finally {
                        if (mounted) setState(() => retrying = false);
                      }
                    },
              icon: retrying
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.refresh),
              label: Text(
                retrying
                    ? 'در حال ارسال مجدد...'
                    : 'تلاش مجدد برای ارسال به مقصدها',
              ),
            ),
        ],
      ),
    );
  }
}

class _DatabaseStatusCard extends StatelessWidget {
  const _DatabaseStatusCard({
    required this.database,
    required this.overview,
    required this.selected,
    required this.onSelected,
    required this.onOpen,
    required this.onEdit,
  });

  final OdinVaultDatabase database;
  final OdinVaultDatabaseOverview? overview;
  final bool selected;
  final ValueChanged<bool> onSelected;
  final VoidCallback onOpen;
  final VoidCallback onEdit;

  @override
  Widget build(BuildContext context) {
    final latestOk = overview?.latestBackupStatus == 2;
    final protected = overview?.isProtected == true;

    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onOpen,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(8, 10, 8, 10),
          child: Row(
            children: [
              Checkbox(
                value: selected,
                onChanged: (value) => onSelected(value == true),
              ),
              Icon(
                !database.isEnabled
                    ? Icons.pause_circle_outline
                    : overview?.latestBackupAtUtc == null
                        ? Icons.schedule_outlined
                        : protected && latestOk
                            ? Icons.shield_outlined
                            : Icons.warning_amber_outlined,
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(database.name, style: Theme.of(context).textTheme.titleMedium),
                    Text(
                      '${database.databaseName} • ${ScheduleValue.displayCron(database.scheduleCron)}',
                      maxLines: 2,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      overview?.latestBackupAtUtc == null
                          ? 'هنوز بکاپی ثبت نشده'
                          : 'آخرین بکاپ: ${_formatDate(overview!.latestBackupAtUtc)} • ${_bytes(overview!.latestBackupSizeBytes)}',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    if (overview != null)
                      Text(
                        protected
                            ? 'وضعیت حفاظت: محافظت‌شده'
                            : 'وضعیت حفاظت: نیازمند توجه',
                        style: TextStyle(
                          color: protected
                              ? null
                              : Theme.of(context).colorScheme.error,
                          fontSize: 12,
                        ),
                      ),
                  ],
                ),
              ),
              IconButton(
                tooltip: 'ویرایش',
                onPressed: onEdit,
                icon: const Icon(Icons.edit_outlined),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _ConnectionBadge extends StatelessWidget {
  const _ConnectionBadge({required this.health});

  final bool? health;

  @override
  Widget build(BuildContext context) {
    final text = health == true
        ? 'آنلاین'
        : health == false
            ? 'آفلاین'
            : 'در حال بررسی';
    final icon = health == true
        ? Icons.check_circle
        : health == false
            ? Icons.error_outline
            : Icons.schedule;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 16),
        const SizedBox(width: 4),
        Text(text, style: Theme.of(context).textTheme.bodySmall),
      ],
    );
  }
}

class _StateMessage extends StatelessWidget {
  const _StateMessage({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.actionText,
    required this.onAction,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final String actionText;
  final VoidCallback onAction;

  @override
  Widget build(BuildContext context) => Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(icon, size: 56),
              const SizedBox(height: 14),
              Text(title, style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 8),
              Text(
                subtitle,
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodyMedium,
              ),
              const SizedBox(height: 18),
              FilledButton.icon(
                onPressed: onAction,
                icon: const Icon(Icons.refresh),
                label: Text(actionText),
              ),
            ],
          ),
        ),
      );
}

class _AgentQuickActions extends StatelessWidget {
  const _AgentQuickActions({
    required this.onBackups,
    required this.onStorage,
    required this.onReports,
  });

  final VoidCallback onBackups;
  final VoidCallback onStorage;
  final VoidCallback onReports;

  @override
  Widget build(BuildContext context) => Row(
        children: [
          Expanded(
            child: _QuickActionButton(
              icon: Icons.restore_page_outlined,
              label: 'بکاپ‌ها',
              onTap: onBackups,
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: _QuickActionButton(
              icon: Icons.cloud_queue_outlined,
              label: 'فضای ذخیره',
              onTap: onStorage,
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: _QuickActionButton(
              icon: Icons.analytics_outlined,
              label: 'گزارش‌ها',
              onTap: onReports,
            ),
          ),
        ],
      );
}

class _QuickActionButton extends StatelessWidget {
  const _QuickActionButton({
    required this.icon,
    required this.label,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => OutlinedButton(
        onPressed: onTap,
        style: OutlinedButton.styleFrom(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon),
            const SizedBox(height: 6),
            Text(label, textAlign: TextAlign.center),
          ],
        ),
      );
}

class _ReportMetricCard extends StatelessWidget {
  const _ReportMetricCard({
    required this.title,
    required this.value,
    required this.icon,
  });

  final String title;
  final String value;
  final IconData icon;

  @override
  Widget build(BuildContext context) => SizedBox(
        width: 155,
        child: Card(
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(icon),
                const SizedBox(height: 10),
                Text(
                  value,
                  textDirection: TextDirection.ltr,
                  style: Theme.of(context).textTheme.titleLarge?.copyWith(
                        fontWeight: FontWeight.w800,
                      ),
                ),
                const SizedBox(height: 2),
                Text(title, style: Theme.of(context).textTheme.bodySmall),
              ],
            ),
          ),
        ),
      );
}

class _DatabaseProtectionCard extends StatelessWidget {
  const _DatabaseProtectionCard({required this.details});

  final OdinVaultDatabaseDetails? details;

  @override
  Widget build(BuildContext context) {
    final value = details;
    if (value == null) {
      return const Card(
        child: Padding(
          padding: EdgeInsets.all(16),
          child: LinearProgressIndicator(),
        ),
      );
    }

    final protection = value.protection;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Icon(
                  protection.isProtected
                      ? Icons.shield_outlined
                      : Icons.gpp_bad_outlined,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'وضعیت حفاظت',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                Text(
                  protection.isProtected ? 'محافظت‌شده' : 'نیازمند توجه',
                  style: TextStyle(
                    fontWeight: FontWeight.w700,
                    color: protection.isProtected
                        ? Theme.of(context).colorScheme.primary
                        : Theme.of(context).colorScheme.error,
                  ),
                ),
              ],
            ),
            const Divider(height: 24),
            _InfoRow(
              label: 'آخرین بکاپ',
              value: _formatDate(protection.latestBackupAtUtc),
            ),
            _InfoRow(
              label: 'حجم آخرین بکاپ',
              value: _bytes(protection.latestBackupSizeBytes),
            ),
            _InfoRow(
              label: 'Verify',
              value: protection.latestVerificationStatus == null
                  ? '-'
                  : _verificationText(protection.latestVerificationStatus!),
            ),
            _InfoRow(
              label: 'Replica',
              value: '${protection.latestReplicaSucceeded}/${protection.latestReplicaTotal}',
            ),
          ],
        ),
      ),
    );
  }
}

class _DashboardOverviewCard extends StatelessWidget {
  const _DashboardOverviewCard({
    required this.dashboard,
    required this.alerts,
  });

  final OdinVaultDashboard? dashboard;
  final OdinVaultAlerts? alerts;

  @override
  Widget build(BuildContext context) {
    final value = dashboard;
    if (value == null) {
      return const Card(
        child: Padding(
          padding: EdgeInsets.all(16),
          child: LinearProgressIndicator(),
        ),
      );
    }

    final protectionOk = value.enabledDatabases == 0 ||
        value.protectedDatabases >= value.enabledDatabases;
    final attention = value.attention.take(3).toList();

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Icon(
                  protectionOk ? Icons.shield_outlined : Icons.shield_moon_outlined,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'وضعیت Agent',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                Text(
                  protectionOk ? 'محافظت‌شده' : 'نیازمند توجه',
                  style: TextStyle(
                    fontWeight: FontWeight.w700,
                    color: protectionOk
                        ? Theme.of(context).colorScheme.primary
                        : Theme.of(context).colorScheme.error,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 14),
            Wrap(
              spacing: 18,
              runSpacing: 12,
              children: [
                _MiniMetric(
                  value: '${value.protectedDatabases}/${value.enabledDatabases}',
                  label: 'دیتابیس محافظت‌شده',
                ),
                _MiniMetric(
                  value: '${value.activeJobs}',
                  label: 'عملیات فعال',
                ),
                _MiniMetric(
                  value: '${value.failedJobsLast24Hours}',
                  label: 'خطای ۲۴ ساعت',
                ),
                _MiniMetric(
                  value: _bytes(value.storageFreeBytes),
                  label: 'فضای آزاد',
                ),
                _MiniMetric(
                  value: '${alerts?.unreadCount ?? 0}',
                  label: 'هشدار خوانده‌نشده',
                ),
              ],
            ),
            if (attention.isNotEmpty) ...[
              const Divider(height: 28),
              Text(
                'نیازمند توجه',
                style: Theme.of(context).textTheme.titleSmall,
              ),
              const SizedBox(height: 6),
              ...attention.map(
                (item) => Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Icon(Icons.warning_amber_rounded, size: 18),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          item.databaseName.isEmpty
                              ? item.title
                              : '${item.databaseName}: ${item.title}',
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _MiniMetric extends StatelessWidget {
  const _MiniMetric({
    required this.value,
    required this.label,
  });

  final String value;
  final String label;

  @override
  Widget build(BuildContext context) => SizedBox(
        width: 118,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              value,
              textDirection: TextDirection.ltr,
              style: Theme.of(context).textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w800,
                  ),
            ),
            Text(label, style: Theme.of(context).textTheme.bodySmall),
          ],
        ),
      );
}

class _AgentSummaryCard extends StatelessWidget {
  const _AgentSummaryCard({
    required this.databaseCount,
    required this.googleDriveCount,
    required this.selectedCount,
    required this.loading,
  });

  final int databaseCount;
  final int googleDriveCount;
  final int selectedCount;
  final bool loading;

  @override
  Widget build(BuildContext context) => Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: loading
              ? const LinearProgressIndicator()
              : Row(
                  children: [
                    Expanded(
                      child: _SummaryItem(
                        icon: Icons.storage,
                        value: '$databaseCount',
                        label: 'دیتابیس',
                      ),
                    ),
                    Expanded(
                      child: _SummaryItem(
                        icon: Icons.cloud_done_outlined,
                        value: '$googleDriveCount',
                        label: 'Google Drive',
                      ),
                    ),
                    Expanded(
                      child: _SummaryItem(
                        icon: Icons.check_box_outlined,
                        value: '$selectedCount',
                        label: 'انتخاب‌شده',
                      ),
                    ),
                  ],
                ),
        ),
      );
}

class _SummaryItem extends StatelessWidget {
  const _SummaryItem({
    required this.icon,
    required this.value,
    required this.label,
  });

  final IconData icon;
  final String value;
  final String label;

  @override
  Widget build(BuildContext context) => Column(
        children: [
          Icon(icon),
          const SizedBox(height: 4),
          Text(value, style: Theme.of(context).textTheme.titleLarge),
          Text(label, style: Theme.of(context).textTheme.bodySmall),
        ],
      );
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({
    required this.label,
    required this.value,
  });

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 3),
        child: Row(
          children: [
            Expanded(child: Text(label)),
            Text(value, style: const TextStyle(fontWeight: FontWeight.w600)),
          ],
        ),
      );
}

class _EmptyState extends StatelessWidget {
  const _EmptyState({
    required this.icon,
    required this.title,
    required this.subtitle,
  });

  final IconData icon;
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 36, horizontal: 16),
        child: Column(
          children: [
            Icon(icon, size: 48),
            const SizedBox(height: 12),
            Text(title, style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 4),
            Text(subtitle, textAlign: TextAlign.center),
          ],
        ),
      );
}

String _backupStageText(String stage, int? percent) {
  final suffix = percent == null ? '' : ' • $percent٪';
  return switch (stage.toLowerCase()) {
    'queued' => 'در صف بکاپ$suffix',
    'backup' => 'در حال ساخت بکاپ$suffix',
    'verify' => 'در حال بررسی سلامت فایل$suffix',
    'replicating' => 'در حال ارسال به مقصدهای پشتیبان$suffix',
    'complete' => 'بکاپ تکمیل شد',
    'failed' => 'بکاپ ناموفق',
    'interrupted' => 'عملیات متوقف شد',
    _ => stage.isEmpty ? 'در حال انجام...' : '$stage$suffix',
  };
}

String _restoreUnavailableReason(OdinVaultBackupHistoryItem backup) {
  if (backup.status != 2) return 'فقط بکاپ موفق قابل Restore است.';
  if (!backup.localFileAvailable) {
    return 'فایل محلی این بکاپ روی Agent در دسترس نیست.';
  }
  return 'Restore برای این بکاپ در دسترس نیست.';
}

String _verificationText(int status) {
  return switch (status) {
    2 => 'موفق',
    3 => 'ناموفق',
    1 => 'در حال بررسی',
    _ => 'انجام نشده',
  };
}

String _bytes(int? value) {
  if (value == null) return '-';
  if (value < 1024) return '$value B';
  if (value < 1024 * 1024) return '${(value / 1024).toStringAsFixed(1)} KB';
  if (value < 1024 * 1024 * 1024) {
    return '${(value / 1024 / 1024).toStringAsFixed(1)} MB';
  }
  return '${(value / 1024 / 1024 / 1024).toStringAsFixed(2)} GB';
}

String _formatDateOnly(DateTime? value) {
  if (value == null) return '-';
  final local = value.toLocal();
  final jalali = _toJalali(local.year, local.month, local.day);
  String two(int x) => x.toString().padLeft(2, '0');
  return _toPersianDigits(
    '${jalali.$1}/${two(jalali.$2)}/${two(jalali.$3)}',
  );
}

String _formatDate(DateTime? value) {
  if (value == null) return '-';
  final local = value.toLocal();
  final jalali = _toJalali(local.year, local.month, local.day);
  String two(int x) => x.toString().padLeft(2, '0');
  return _toPersianDigits(
    '${jalali.$1}/${two(jalali.$2)}/${two(jalali.$3)}  '
    '${two(local.hour)}:${two(local.minute)}',
  );
}

(int, int, int) _toJalali(int year, int month, int day) {
  var gy = year - 1600;
  final gm = month - 1;
  final gd = day - 1;

  var gDayNo = 365 * gy +
      ((gy + 3) ~/ 4) -
      ((gy + 99) ~/ 100) +
      ((gy + 399) ~/ 400);

  const gDays = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  for (var i = 0; i < gm; i++) {
    gDayNo += gDays[i];
  }

  final leap = (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
  if (gm > 1 && leap) gDayNo++;
  gDayNo += gd;

  var jDayNo = gDayNo - 79;
  final jNp = jDayNo ~/ 12053;
  jDayNo %= 12053;

  var jy = 979 + 33 * jNp + 4 * (jDayNo ~/ 1461);
  jDayNo %= 1461;

  if (jDayNo >= 366) {
    jy += (jDayNo - 1) ~/ 365;
    jDayNo = (jDayNo - 1) % 365;
  }

  const jDays = [31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 29];
  var jm = 0;
  while (jm < 11 && jDayNo >= jDays[jm]) {
    jDayNo -= jDays[jm];
    jm++;
  }

  return (jy, jm + 1, jDayNo + 1);
}

String _toPersianDigits(String value) {
  const latin = '0123456789';
  const persian = '۰۱۲۳۴۵۶۷۸۹';
  var result = value;
  for (var i = 0; i < latin.length; i++) {
    result = result.replaceAll(latin[i], persian[i]);
  }
  return result;
}
