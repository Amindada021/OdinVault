package com.odinvault.odinvault_mobile

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.view.WindowManager
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.io.File
import java.util.UUID

class MainActivity : FlutterActivity() {
    private var pendingResult: MethodChannel.Result? = null
    private var pendingFile: File? = null

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "odinvault/files").setMethodCallHandler { call, result ->
            when (call.method) {
                "temporaryFile" -> {
                    val folder = File(cacheDir, "backups").apply { mkdirs() }
                    folder.listFiles()?.filter { System.currentTimeMillis() - it.lastModified() > 86400000L }?.forEach { it.delete() }
                    result.success(File(folder, UUID.randomUUID().toString() + ".bak").absolutePath)
                }
                "keepAwake" -> {
                    if (call.argument<Boolean>("enabled") == true) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                    else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                    result.success(null)
                }
                "saveBackup" -> {
                    val path = call.argument<String>("path")
                    val file = path?.let { File(it).canonicalFile }
                    val root = File(cacheDir, "backups").canonicalPath + File.separator
                    if (file == null || !file.path.startsWith(root) || !file.isFile) {
                        result.error("missing_file", "فایل دانلودشده پیدا نشد.", null)
                    } else if (pendingResult != null) {
                        result.error("busy", "پنجره ذخیره فایل از قبل باز است.", null)
                    } else {
                        pendingResult = result
                        pendingFile = file
                        try {
                            val intent = Intent(Intent.ACTION_CREATE_DOCUMENT).apply {
                                addCategory(Intent.CATEGORY_OPENABLE)
                                type = "application/octet-stream"
                                putExtra(Intent.EXTRA_TITLE, call.argument<String>("name") ?: "backup.bak")
                            }
                            startActivityForResult(intent, 701)
                        } catch (e: Exception) {
                            pendingResult = null
                            pendingFile = null
                            result.error("save_failed", "پنجره ذخیره فایل باز نشد.", null)
                        }
                    }
                }
                else -> result.notImplemented()
            }
        }
    }

    @Deprecated("Android activity result bridge")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != 701) return
        val result = pendingResult ?: return
        val source = pendingFile
        val uri: Uri? = data?.data
        if (resultCode != Activity.RESULT_OK || uri == null || source == null) {
            pendingResult = null
            pendingFile = null
            result.success(null)
            return
        }
        Thread {
            try {
                contentResolver.openOutputStream(uri, "w").use { output ->
                    requireNotNull(output)
                    source.inputStream().use { it.copyTo(output, 1024 * 1024) }
                }
                runOnUiThread {
                    pendingResult = null
                    pendingFile = null
                    result.success(uri.toString())
                }
            } catch (e: Exception) {
                try { android.provider.DocumentsContract.deleteDocument(contentResolver, uri) } catch (_: Exception) { }
                runOnUiThread {
                    pendingResult = null
                    pendingFile = null
                    result.error("save_failed", "ذخیره فایل انجام نشد؛ فضای خالی و دسترسی مقصد را بررسی کنید.", null)
                }
            }
        }.start()
    }
}
