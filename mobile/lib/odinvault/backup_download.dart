import 'dart:async';
import 'dart:io';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'api_client.dart';
import 'models.dart';

Future<void> showBackupDownload(BuildContext context, OdinVaultApiClient api,
    {OdinVaultBackup? backup, String? databaseId}) => showDialog<void>(
  context: context,
  barrierDismissible: false,
  builder: (_) => _BackupDownload(api: api, backup: backup, databaseId: databaseId),
);

class _BackupDownload extends StatefulWidget {
  const _BackupDownload({required this.api, this.backup, this.databaseId});
  final OdinVaultApiClient api;
  final OdinVaultBackup? backup;
  final String? databaseId;
  @override
  State<_BackupDownload> createState() => _BackupDownloadState();
}

class _BackupDownloadState extends State<_BackupDownload> {
  static const files = MethodChannel('odinvault/files');
  final token = CancelToken();
  final watch = Stopwatch();
  OdinVaultBackup? backup;
  String? jobId;
  String? path;
  String stage = 'queued';
  String? error;
  double? progress;
  int received = 0;
  int total = 0;
  bool busy = true;
  bool saved = false;
  bool closed = false;
  int lastUpdate = 0;

  @override
  void initState() { super.initState(); backup = widget.backup; run(); }

  Future<void> run() async {
    setState(() { busy = true; error = null; });
    try {
      await files.invokeMethod<void>('keepAwake', {'enabled': true});
      if (backup == null) {
        if (jobId == null) {
          final job = await widget.api.startBackupJob(widget.databaseId!);
          jobId = job['id'].toString();
        }
        while (!closed) {
          final job = await widget.api.backupJob(jobId!);
          if (closed) return;
          if (job['backup'] is Map) {
            backup = OdinVaultBackup.fromJson(Map<String, dynamic>.from(job['backup'] as Map));
            break;
          }
          if (job['stage'] == 'failed') throw OdinVaultApiException(job['error']?.toString() ?? 'بکاپ ناموفق بود.');
          setState(() {
            stage = job['stage']?.toString() ?? 'backup';
            progress = (job['percent'] as num?)?.toDouble();
            if (progress != null) progress = progress! / 100;
          });
          await Future<void>.delayed(const Duration(seconds: 2));
        }
      }
      if (closed) return;
      if (backup?.status != 2) throw const OdinVaultApiException('بکاپ آماده دانلود نیست.');
      path ??= await files.invokeMethod<String>('temporaryFile');
      if (path == null) throw const OdinVaultApiException('مسیر ذخیره گوشی در دسترس نیست.');
      setState(() { stage = 'download'; progress = null; received = 0; total = backup!.sizeBytes ?? 0; });
      watch.reset(); watch.start(); lastUpdate = 0;
      await widget.api.downloadBackup(backup!.id, path!, cancelToken: token,
        onProgress: (count, length) {
          if (!mounted || closed) return;
          if (length > 0) total = length;
          received = count;
          if (watch.elapsedMilliseconds - lastUpdate < 150 && count != total) return;
          lastUpdate = watch.elapsedMilliseconds;
          setState(() { progress = total > 0 ? (count / total).clamp(0.0, 1.0) : null; });
        });
      watch.stop();
      final expected = backup!.sizeBytes;
      if (expected != null && expected > 0 && await File(path!).length() != expected) {
        await File(path!).delete();
        throw const OdinVaultApiException('حجم فایل دریافت‌شده کامل نیست؛ دوباره دانلود کنید.');
      }
      if (!mounted || closed) return;
      setState(() { stage = 'ready'; progress = 1; busy = false; });
    } catch (e) {
      if (mounted && !closed) setState(() { error = e is PlatformException ? e.message : OdinVaultApiException.from(e).message; busy = false; });
    } finally {
      try { await files.invokeMethod<void>('keepAwake', {'enabled': false}); } catch (_) { }
    }
  }

  Future<void> save() async {
    setState(() { busy = true; error = null; });
    try {
      final uri = await files.invokeMethod<String>('saveBackup', {'path': path, 'name': backup!.fileName});
      if (mounted) setState(() { saved = uri != null; });
    } catch (e) {
      if (mounted) setState(() { error = e is PlatformException ? e.message : 'ذخیره فایل انجام نشد.'; });
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  void dispose() {
    closed = true;
    token.cancel();
    watch.stop();
    final temp = path;
    if (temp != null) unawaited(Future<void>.delayed(const Duration(seconds: 2), () async {
      try {
        if (await File(temp).exists()) {
          await File(temp).delete();
        }
      } catch (_) { }
    }));
    super.dispose();
  }

  String get title => switch (stage) {
    'queued' => 'در صف ساخت بکاپ',
    'backup' => 'در حال ساخت بکاپ',
    'verify' => 'بررسی سلامت بکاپ',
    'download' => 'دریافت روی گوشی',
    'ready' => saved ? 'فایل ذخیره شد' : 'دانلود کامل شد',
    _ => 'آماده‌سازی فایل',
  };
  String bytes(int value) => value >= 1073741824 ? '${(value / 1073741824).toStringAsFixed(2)} GB' : '${(value / 1048576).toStringAsFixed(1)} MB';

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !busy,
    child: AlertDialog(
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
        if (stage == 'download') ...[
          const SizedBox(height: 12),
          Text('${bytes(received)} / ${total > 0 ? bytes(total) : 'نامشخص'}', textDirection: TextDirection.ltr),
          if (watch.elapsedMilliseconds > 0) Text('${bytes((received * 1000 / watch.elapsedMilliseconds).round())}/s', textDirection: TextDirection.ltr),
        ],
        const SizedBox(height: 12),
        Text(stage == 'ready'
            ? saved ? 'نسخه بکاپ در محلی که انتخاب کردی ذخیره شد.' : 'برای نگهداری فایل، «ذخیره در فایل‌های گوشی» را بزن و پوشه دلخواهت را انتخاب کن.'
            : 'بکاپ سرور با بستن این پنجره متوقف نمی‌شود. برای ادامه دانلود، برنامه را باز نگه دار.', textAlign: TextAlign.center),
        if (error != null) Padding(padding: const EdgeInsets.only(top: 12), child: Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
      ])),
      actions: [
        TextButton(onPressed: stage == 'ready' && busy ? null : () => Navigator.pop(context), child: Text(busy ? 'بستن / لغو دانلود' : 'بستن')),
        if (!busy && stage == 'ready' && !saved) FilledButton.icon(onPressed: save, icon: const Icon(Icons.save_alt), label: const Text('ذخیره در فایل‌های گوشی')),
        if (!busy && error != null && stage != 'ready') FilledButton(onPressed: run, child: const Text('تلاش مجدد')),
      ],
    ),
  );
}
