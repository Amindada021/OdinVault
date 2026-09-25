import 'dart:async';
import 'dart:math';

import 'package:odinvault_mobile/odinvault/api_client.dart';
import 'package:odinvault_mobile/odinvault/models.dart';
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
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  Future<void> _reload() async {
    final servers = await _store.load();
    if (!mounted) return;
    setState(() {
      _servers = servers;
      _loading = false;
    });
  }

  Future<void> _add() async {
    final server = await showDialog<OdinVaultServer>(
      context: context,
      builder: (_) => const AddServerDialog(),
    );
    if (server == null) return;

    final next = [..._servers, server];
    await _store.save(next);
    if (mounted) setState(() => _servers = next);
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
    await _store.save(next);
    if (mounted) setState(() => _servers = next);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          title: const Text('OdinVault'),
          actions: [
            IconButton(
              tooltip: 'بروزرسانی',
              onPressed: _reload,
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
            : _servers.isEmpty
                ? const _EmptyState(
                    icon: Icons.dns_outlined,
                    title: 'هنوز Agent اضافه نشده',
                    subtitle: 'برای مدیریت بکاپ‌ها، OdinVault Agent سرور را اضافه کنید.',
                  )
                : ListView.separated(
                    padding: const EdgeInsets.all(16),
                    itemCount: _servers.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 8),
                    itemBuilder: (_, i) {
                      final server = _servers[i];
                      return Card(
                        child: ListTile(
                          leading: const CircleAvatar(child: Icon(Icons.dns)),
                          title: Text(server.name),
                          subtitle: Directionality(
                            textDirection: TextDirection.ltr,
                            child: Text(server.baseUrl, textAlign: TextAlign.right),
                          ),
                          trailing: IconButton(
                            tooltip: 'حذف اتصال',
                            icon: const Icon(Icons.delete_outline),
                            onPressed: () => _remove(server),
                          ),
                          onTap: () => Navigator.push(
                            context,
                            MaterialPageRoute(builder: (_) => AgentPage(server: server)),
                          ),
                        ),
                      );
                    },
                  ),
      );
}

class AddServerDialog extends StatefulWidget {
  const AddServerDialog({super.key});

  @override
  State<AddServerDialog> createState() => _AddServerDialogState();
}

class _AddServerDialogState extends State<AddServerDialog> {
  final name = TextEditingController();
  final url = TextEditingController(text: 'http://');
  final key = TextEditingController();
  String? error;
  bool busy = false;

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
      setState(() => error = 'نام نمایشی و API Key الزامی است.');
      return;
    }

    url.text = baseUrl;
    url.selection = TextSelection.collapsed(offset: url.text.length);

    setState(() {
      busy = true;
      error = null;
    });

    final server = OdinVaultServer(
      id: '${DateTime.now().microsecondsSinceEpoch}-${Random().nextInt(1 << 20)}',
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
        title: const Text('افزودن OdinVault Agent'),
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
                textAlign: TextAlign.left,
                keyboardType: TextInputType.url,
                autocorrect: false,
                enableSuggestions: false,
                decoration: const InputDecoration(
                  labelText: 'آدرس Agent',
                  hintText: 'http://192.168.1.10:5188',
                  prefixIcon: Icon(Icons.link),
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: key,
                obscureText: true,
                textDirection: TextDirection.ltr,
                decoration: const InputDecoration(
                  labelText: 'API Key',
                  prefixIcon: Icon(Icons.key),
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
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('انصراف')),
          FilledButton(
            onPressed: busy ? null : save,
            child: Text(busy ? 'در حال تست...' : 'تست و ذخیره'),
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
  final Map<String, OdinVaultBackup?> latestBackups = {};
  final Map<String, List<OdinVaultReplica>> latestReplicas = {};
  final Set<String> selectedDatabaseIds = {};
  String? error;
  bool refreshing = false;
  bool runningBatch = false;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    if (refreshing) return;
    setState(() => refreshing = true);
    try {
      final values = await Future.wait([
        api.databases(),
        api.storageTargets(),
      ]);
      final dbs = values[0] as List<OdinVaultDatabase>;
      final storageTargets = values[1] as List<OdinVaultStorageTarget>;

      final summaries = await Future.wait(
        dbs.map((db) async {
          try {
            final history = await api.backups(db.id, take: 1);
            final latest = history.isEmpty ? null : history.first;
            var replicas = <OdinVaultReplica>[];
            if (latest != null) {
              try {
                replicas = await api.replicas(latest.id);
              } catch (_) {}
            }
            return _DatabaseSnapshot(db.id, latest, replicas);
          } catch (_) {
            return _DatabaseSnapshot(db.id, null, const []);
          }
        }),
      );

      if (!mounted) return;
      setState(() {
        databases = dbs;
        targets = storageTargets;
        error = null;
        latestBackups
          ..clear()
          ..addEntries(summaries.map((x) => MapEntry(x.databaseId, x.backup)));
        latestReplicas
          ..clear()
          ..addEntries(summaries.map((x) => MapEntry(x.databaseId, x.replicas)));
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

    setState(() => runningBatch = true);
    var success = 0;
    final failures = <String>[];
    for (final db in selected) {
      try {
        await api.backupNow(db.id);
        success++;
      } catch (e) {
        failures.add('${db.name}: ${OdinVaultApiException.from(e).message}');
      }
    }
    if (!mounted) return;
    setState(() => runningBatch = false);
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

  @override
  Widget build(BuildContext context) {
    final dbs = databases;
    final storageTargets = targets;
    final googleCount = storageTargets?.where((x) => x.type == 2 && x.isEnabled).length ?? 0;

    return Scaffold(
      appBar: AppBar(
        title: Text(widget.server.name),
        actions: [
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
                        ? 'در حال بکاپ...'
                        : 'بکاپ ${selectedDatabaseIds.length} دیتابیس انتخاب‌شده',
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
                  latest: latestBackups[db.id],
                  replicas: latestReplicas[db.id] ?? const [],
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
  List<OdinVaultBackup>? history;
  List<OdinVaultStorageTarget>? allTargets;
  List<DatabaseStorageLink>? linkedTargets;
  bool running = false;
  String? error;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    try {
      final values = await Future.wait([
        api.database(widget.databaseId),
        api.backups(widget.databaseId),
        api.storageTargets(),
        api.databaseStorageTargets(widget.databaseId),
      ]);
      if (!mounted) return;
      setState(() {
        db = values[0] as OdinVaultDatabase;
        history = values[1] as List<OdinVaultBackup>;
        allTargets = values[2] as List<OdinVaultStorageTarget>;
        linkedTargets = values[3] as List<DatabaseStorageLink>;
        error = null;
      });
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    }
  }

  Future<void> backup() async {
    setState(() => running = true);
    try {
      await api.backupNow(widget.databaseId);
      await load();
      _snack('بکاپ با موفقیت انجام شد.');
    } catch (e) {
      _snack(OdinVaultApiException.from(e).message);
    } finally {
      if (mounted) setState(() => running = false);
    }
  }

  Future<void> deleteDatabase() async {
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

    try {
      await api.deleteDatabase(database.id, deleteHistory: true, deleteFiles: false);
      if (mounted) Navigator.pop(context);
    } catch (e) {
      _snack(OdinVaultApiException.from(e).message);
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
                  decoration: const InputDecoration(labelText: 'SQL Server Host'),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: port,
                  keyboardType: TextInputType.number,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(labelText: 'Port'),
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
                  decoration: const InputDecoration(labelText: 'Username'),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: password,
                  obscureText: true,
                  textDirection: TextDirection.ltr,
                  decoration: InputDecoration(
                    labelText: widget.database == null
                        ? 'Password'
                        : 'Password جدید (خالی = بدون تغییر)',
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
                  title: const Text('Trust Server Certificate'),
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
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('انصراف')),
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
                        decoration: const InputDecoration(labelText: 'SQL Server Host'),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: TextField(
                        controller: port,
                        textDirection: TextDirection.ltr,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(labelText: 'Port'),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: username,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(labelText: 'Username'),
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
                  title: const Text('Trust Server Certificate'),
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

    try {
      await widget.api.deleteStorageTarget(widget.target.id);
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
    }
  }

  Future<void> testReplica() async {
    try {
      await widget.api.testReplicaTarget(widget.target.id);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('اتصال Agent ثانویه سالم است.')),
        );
      }
    } catch (e) {
      if (mounted) setState(() => error = OdinVaultApiException.from(e).message);
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
                  decoration: const InputDecoration(labelText: 'Folder ID'),
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
                onChanged: (value) => setState(() => enabled = value),
                title: const Text('فعال'),
              ),
              if (widget.target.type == 5)
                OutlinedButton.icon(
                  onPressed: testReplica,
                  icon: const Icon(Icons.cable),
                  label: const Text('تست اتصال Agent ثانویه'),
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
            onPressed: busy ? null : remove,
            child: const Text('حذف مقصد'),
          ),
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('انصراف')),
          FilledButton(
            onPressed: busy ? null : save,
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
                decoration: const InputDecoration(labelText: 'Base URL'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: key,
                obscureText: true,
                textDirection: TextDirection.ltr,
                decoration: const InputDecoration(labelText: 'API Key'),
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
                  labelText: 'Folder ID (اختیاری)',
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

  Future<void> load() async {
    try {
      final value = await widget.api.replicas(widget.backup.id);
      if (mounted) setState(() => replicas = value);
    } catch (_) {}
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
          if (replicas == null)
            const Padding(
              padding: EdgeInsets.all(12),
              child: LinearProgressIndicator(),
            )
          else if (replicas!.isEmpty)
            const ListTile(
              leading: Icon(Icons.cloud_off_outlined),
              title: Text('کپی ثانویه‌ای برای این بکاپ ثبت نشده'),
            )
          else
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
              onPressed: () async {
                await widget.api.retryReplication(backup.id);
                await load();
              },
              icon: const Icon(Icons.refresh),
              label: const Text('تلاش مجدد برای ارسال به مقصدها'),
            ),
        ],
      ),
    );
  }
}

class _DatabaseStatusCard extends StatelessWidget {
  const _DatabaseStatusCard({
    required this.database,
    required this.latest,
    required this.replicas,
    required this.selected,
    required this.onSelected,
    required this.onOpen,
    required this.onEdit,
  });

  final OdinVaultDatabase database;
  final OdinVaultBackup? latest;
  final List<OdinVaultReplica> replicas;
  final bool selected;
  final ValueChanged<bool> onSelected;
  final VoidCallback onOpen;
  final VoidCallback onEdit;

  @override
  Widget build(BuildContext context) {
    final hasReplicaError = replicas.any((x) => x.status != 2);
    final allReplicasOk = replicas.isNotEmpty && !hasReplicaError;
    final latestOk = latest?.status == 2;

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
                    : latest == null
                        ? Icons.schedule_outlined
                        : latestOk
                            ? Icons.check_circle
                            : Icons.error,
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
                      latest == null
                          ? 'هنوز بکاپی ثبت نشده'
                          : 'آخرین بکاپ: ${_formatDate(latest!.completedAtUtc ?? latest!.startedAtUtc)} • ${_bytes(latest!.sizeBytes)}',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    if (allReplicasOk)
                      Text(
                        'کپی ثانویه: سالم',
                        style: Theme.of(context).textTheme.bodySmall,
                      )
                    else if (hasReplicaError)
                      Text(
                        'کپی ثانویه: نیاز به بررسی',
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.error,
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

class _DatabaseSnapshot {
  const _DatabaseSnapshot(this.databaseId, this.backup, this.replicas);

  final String databaseId;
  final OdinVaultBackup? backup;
  final List<OdinVaultReplica> replicas;
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

String _formatDate(DateTime? value) {
  if (value == null) return '-';
  final local = value.toLocal();
  String two(int x) => x.toString().padLeft(2, '0');
  return '${local.year}/${two(local.month)}/${two(local.day)}  ${two(local.hour)}:${two(local.minute)}';
}
