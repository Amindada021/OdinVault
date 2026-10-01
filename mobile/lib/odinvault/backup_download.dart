import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'api_client.dart';
import 'models.dart';

Future<void> showBackupDownload(BuildContext context, OdinVaultApiClient api,
    {required String databaseName, OdinVaultBackup? backup, String? databaseId}) => showDialog<void>(
  context: context,
  barrierDismissible: false,
  builder: (_) => _BackupDownload(api: api, backup: backup, databaseId: databaseId, databaseName: databaseName),
);

class _BackupDownload extends StatefulWidget {
  const _BackupDownload({required this.api, required this.databaseName, this.backup, this.databaseId});
  final OdinVaultApiClient api;
  final OdinVaultBackup? backup;
  final String? databaseId;
  final String databaseName;
  @override
  State<_BackupDownload> createState() => _BackupDownloadState();
}

class _BackupDownloadState extends State<_BackupDownload> {
  static const files = MethodChannel('odinvault/files');
  OdinVaultBackup? backup;
  String? jobId;
  String? requestId;
  String stage = 'queued';
  String? error;
  double? progress;
  int received = 0;
  int total = 0;
  bool busy = true;
  bool saved = false;
  bool closed = false;

  @override
  void initState() { super.initState(); backup = widget.backup; run(); }

  Future<void> run() async {
    if (mounted) setState(() { busy = true; error = null; });
    try {
      if (backup == null) await waitForBackup();
      if (closed || backup == null) return;
      if (backup!.status != 2) throw const OdinVaultApiException('بکاپ آماده دانلود نیست.');
      final folder = await files.invokeMethod<String>('ensureBackupFolder');
      if (folder == null) throw const OdinVaultApiException('برای ذخیره بکاپ، پوشه OdinVault را بسازید و انتخاب کنید.');
      final current = await downloadStatus();
      final sameActive = current['backupId']?.toString() == backup!.id &&
          const {'starting', 'downloading', 'saving'}.contains(current['state']?.toString());
      if (!sameActive) {
        await files.invokeMethod<void>('startBackupDownload', {
          'baseUrl': widget.api.baseUrl, 'apiKey': widget.api.apiKey, 'backupId': backup!.id,
          'fileName': backup!.fileName, 'databaseName': widget.databaseName, 'expectedSize': backup!.sizeBytes ?? 0,
        });
      }
      while (!closed) {
        final status = await downloadStatus();
        if (status['backupId']?.toString() != backup!.id) throw const OdinVaultApiException('وضعیت دانلود دیگری روی گوشی ثبت شده است.');
        final state = status['state']?.toString() ?? 'starting';
        received = (status['received'] as num?)?.toInt() ?? 0;
        total = (status['total'] as num?)?.toInt() ?? (backup!.sizeBytes ?? 0);
        if (!mounted) return;
        setState(() {
          stage = state; progress = total > 0 ? (received / total).clamp(0.0, 1.0) : null;
          saved = state == 'complete'; error = (state == 'failed' || state == 'cancelled') ? status['error']?.toString() : null;
          busy = const {'starting', 'downloading', 'saving'}.contains(state);
        });
        if (!busy) return;
        await Future<void>.delayed(const Duration(milliseconds: 700));
      }
    } catch (e) {
      if (mounted && !closed) setState(() { error = e is PlatformException ? e.message : OdinVaultApiException.from(e).message; busy = false; });
    }
  }

  Future<void> waitForBackup() async {
    if (jobId == null) {
      requestId ??= widget.api.createRequestId();
      final job = await widget.api.startBackupJob(widget.databaseId!, requestId: requestId!);
      jobId = job['id'].toString();
    }
    while (!closed) {
      final job = await widget.api.backupJob(jobId!);
      if (job['stage'] == 'failed' || job['stage'] == 'interrupted' || job['status'] == 3 || job['status'] == 4) {
        throw OdinVaultApiException(job['error']?.toString() ?? job['errorMessage']?.toString() ?? 'بکاپ ناموفق بود.');
      }
      if (job['stage'] == 'complete' && job['backup'] is Map) {
        backup = OdinVaultBackup.fromJson(Map<String, dynamic>.from(job['backup'] as Map)); return;
      }
      if (mounted) setState(() {
        stage = job['stage']?.toString() ?? 'backup'; progress = (job['percent'] as num?)?.toDouble();
        if (progress != null) progress = progress! / 100;
      });
      await Future<void>.delayed(const Duration(seconds: 2));
    }
  }

  Future<Map<dynamic, dynamic>> downloadStatus() async =>
      await files.invokeMethod<Map<dynamic, dynamic>>('backupDownloadStatus') ?? <dynamic, dynamic>{};

  Future<void> cancelDownload() async {
    await files.invokeMethod<void>('cancelBackupDownload');
    if (!closed) await run();
  }

  @override
  void dispose() { closed = true; super.dispose(); }

  String get title => switch (stage) {
    'queued' => 'در صف ساخت بکاپ',
    'backup' => 'در حال ساخت بکاپ',
    'verify' => 'بررسی سلامت بکاپ',
    'starting' => 'آماده‌سازی دانلود',
    'downloading' => 'دریافت روی گوشی',
    'saving' => 'ذخیره روی گوشی',
    'complete' => 'فایل ذخیره شد',
    'cancelled' => 'دانلود لغو شد',
    'failed' => 'دانلود ناموفق',
    _ => 'آماده‌سازی فایل',
  };
  String bytes(int value) => value >= 1073741824 ? '${(value / 1073741824).toStringAsFixed(2)} GB' : '${(value / 1048576).toStringAsFixed(1)} MB';

  @override
  Widget build(BuildContext context) => AlertDialog(
      title: Text(title),
      content: SizedBox(width: 360, child: Column(mainAxisSize: MainAxisSize.min, children: [
        const SizedBox(height: 16),
        SizedBox(width: 112, height: 112, child: Stack(alignment: Alignment.center, children: [
          SizedBox.expand(child: CircularProgressIndicator(value: progress, strokeWidth: 9,
              backgroundColor: Theme.of(context).colorScheme.surfaceContainerHighest)),
          Text(progress == null ? '•••' : '${(progress! * 100).floor()}٪', style: Theme.of(context).textTheme.headlineMedium),
        ])),
        const SizedBox(height: 24),
        if (backup != null) Text(backup!.fileName, textDirection: TextDirection.ltr, textAlign: TextAlign.center),
        if (const {'starting', 'downloading', 'saving'}.contains(stage)) ...[
          const SizedBox(height: 12),
          Text('${bytes(received)} / ${total > 0 ? bytes(total) : 'نامشخص'}', textDirection: TextDirection.ltr),
        ],
        const SizedBox(height: 12),
        Text(saved ? 'بکاپ در پوشه OdinVault، داخل پوشه ${widget.databaseName} ذخیره شد.' : busy && const {'starting', 'downloading', 'saving'}.contains(stage) ? 'دانلود مستقل از این پنجره ادامه پیدا می‌کند. پیشرفت و لغو از اعلان اندروید هم در دسترس است.' : 'دانلود ناقص در تلاش بعدی با HTTP Range ادامه پیدا می‌کند.', textAlign: TextAlign.center),
        if (error != null) Padding(padding: const EdgeInsets.only(top: 12), child: Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
      ])),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('بستن')),
        if (busy && const {'starting', 'downloading', 'saving'}.contains(stage))
          TextButton(onPressed: cancelDownload, child: const Text('لغو دانلود گوشی')),
        if (!busy && error != null && stage != 'cancelled') FilledButton(onPressed: run, child: const Text('ادامه / تلاش مجدد')),
      ],

  );
}
