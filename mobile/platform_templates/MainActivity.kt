package com.odinvault.odinvault_mobile

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.DocumentsContract
import android.view.WindowManager
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.io.File
import java.util.UUID

class MainActivity : FlutterActivity() {
    private var pendingResult: MethodChannel.Result? = null
    private val preferences by lazy { getSharedPreferences("odinvault_files", MODE_PRIVATE) }
    private var saving = false

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "odinvault/files").setMethodCallHandler { call, result ->
            when (call.method) {
                "startBackupDownload" -> {
                    val p = getSharedPreferences("odinvault_download", MODE_PRIVATE)
                    p.edit().putString("state", "starting").putLong("received", 0).putLong("total", call.argument<Number>("expectedSize")?.toLong() ?: 0L).remove("error").apply()
                    val intent = Intent(this, BackupDownloadService::class.java).setAction(BackupDownloadService.ACTION_START).apply {
                        putExtra("baseUrl", call.argument<String>("baseUrl")); putExtra("apiKey", call.argument<String>("apiKey") ?: "")
                        putExtra("backupId", call.argument<String>("backupId")); putExtra("fileName", call.argument<String>("fileName"))
                        putExtra("databaseName", call.argument<String>("databaseName")); putExtra("expectedSize", call.argument<Number>("expectedSize")?.toLong() ?: 0L)
                    }
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) startForegroundService(intent) else startService(intent)
                    result.success(null)
                }
                "backupDownloadStatus" -> {
                    val p = getSharedPreferences("odinvault_download", MODE_PRIVATE)
                    result.success(mapOf("state" to (p.getString("state", "idle") ?: "idle"), "received" to p.getLong("received", 0),
                        "total" to p.getLong("total", 0), "error" to p.getString("error", null), "backupId" to p.getString("backup_id", null),
                        "fileName" to p.getString("file_name", null), "databaseName" to p.getString("database_name", null),
                        "savedUri" to p.getString("saved_uri", null), "updatedAt" to p.getLong("updated_at", 0)))
                }
                "cancelBackupDownload" -> {
                    startService(Intent(this, BackupDownloadService::class.java).setAction(BackupDownloadService.ACTION_CANCEL))
                    result.success(null)
                }
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
                "ensureBackupFolder" -> {
                    val stored = preferences.getString("backup_tree", null)
                    val uri = stored?.let { Uri.parse(it) }
                    val granted = uri != null && contentResolver.persistedUriPermissions.any {
                        it.uri == uri && it.isReadPermission && it.isWritePermission
                    }
                    if (granted) {
                        result.success(stored)
                    } else if (pendingResult != null || saving) {
                        result.error("busy", "انتخاب یا ذخیره فایل در حال انجام است.", null)
                    } else {
                        pendingResult = result
                        try {
                            val intent = Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).apply {
                                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or
                                    Intent.FLAG_GRANT_WRITE_URI_PERMISSION or
                                    Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION or
                                    Intent.FLAG_GRANT_PREFIX_URI_PERMISSION)
                                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                                    putExtra(DocumentsContract.EXTRA_INITIAL_URI,
                                        Uri.parse("content://com.android.externalstorage.documents/document/primary%3A"))
                                }
                            }
                            startActivityForResult(intent, 701)
                        } catch (_: Exception) {
                            pendingResult = null
                            result.error("folder_failed", "پنجره انتخاب پوشه باز نشد.", null)
                        }
                    }
                }
                "saveBackup" -> {
                    val file = call.argument<String>("path")?.let { File(it).canonicalFile }
                    val root = File(cacheDir, "backups").canonicalPath + File.separator
                    val tree = preferences.getString("backup_tree", null)
                    if (file == null || !file.path.startsWith(root) || !file.isFile || tree == null) {
                        result.error("missing_file", "فایل یا پوشه مقصد در دسترس نیست.", null)
                    } else if (saving) {
                        result.error("busy", "ذخیره فایل در حال انجام است.", null)
                    } else {
                        saving = true
                        val database = safeName(call.argument<String>("databaseName") ?: "Database")
                        val name = safeName(call.argument<String>("name") ?: "backup.bak")
                        Thread {
                            var destination: Uri? = null
                            try {
                                val treeUri = Uri.parse(tree)
                                val selected = DocumentsContract.buildDocumentUriUsingTree(treeUri, DocumentsContract.getTreeDocumentId(treeUri))
                                val selectedName = contentResolver.query(selected,
                                    arrayOf(DocumentsContract.Document.COLUMN_DISPLAY_NAME), null, null, null)?.use {
                                    if (it.moveToFirst()) it.getString(0) else null
                                }
                                val appFolder = if (selectedName == "OdinVault") selected else directory(treeUri, selected, "OdinVault")
                                val databaseFolder = directory(treeUri, appFolder, database)
                                // Always create a new document; never replace a previous backup.
                                val target = requireNotNull(DocumentsContract.createDocument(contentResolver,
                                    databaseFolder, "application/octet-stream", name))
                                destination = target
                                contentResolver.openOutputStream(target, "w").use { output ->
                                    requireNotNull(output)
                                    file.inputStream().use { it.copyTo(output, 1024 * 1024) }
                                }
                                val savedUri = destination.toString()
                                runOnUiThread { saving = false; result.success(savedUri) }
                            } catch (e: Exception) {
                                destination?.let { try { DocumentsContract.deleteDocument(contentResolver, it) } catch (_: Exception) { } }
                                if (e is SecurityException || e is java.io.FileNotFoundException) {
                                    preferences.edit().remove("backup_tree").apply()
                                }
                                runOnUiThread {
                                    saving = false
                                    result.error("save_failed", "ذخیره فایل انجام نشد؛ فضای خالی و دسترسی پوشه را بررسی کنید و دوباره تلاش کنید.", null)
                                }
                            }
                        }.start()
                    }
                }
                else -> result.notImplemented()
            }
        }
    }

    private fun safeName(value: String): String = value
        .map { if (it < ' ' || it in "\\/:*?\"<>|") '_' else it }
        .joinToString("").trim().trim('.').take(120).ifEmpty { "backup" }

    private fun directory(tree: Uri, parent: Uri, name: String): Uri {
        val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, DocumentsContract.getDocumentId(parent))
        contentResolver.query(children, arrayOf(DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME, DocumentsContract.Document.COLUMN_MIME_TYPE), null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                if (cursor.getString(1) == name && cursor.getString(2) == DocumentsContract.Document.MIME_TYPE_DIR) {
                    return DocumentsContract.buildDocumentUriUsingTree(tree, cursor.getString(0))
                }
            }
        }
        return requireNotNull(DocumentsContract.createDocument(contentResolver, parent,
            DocumentsContract.Document.MIME_TYPE_DIR, name))
    }

    @Deprecated("Android activity result bridge")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != 701) return
        val result = pendingResult ?: return
        pendingResult = null
        val uri = data?.data
        if (resultCode != Activity.RESULT_OK || uri == null) {
            result.success(null)
            return
        }
        try {
            val flags = (data?.flags ?: 0) and (Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
            contentResolver.takePersistableUriPermission(uri, flags)
            preferences.edit().putString("backup_tree", uri.toString()).apply()
            result.success(uri.toString())
        } catch (_: Exception) {
            result.error("folder_failed", "اجازه دسترسی دائمی به پوشه داده نشد؛ پوشه دیگری انتخاب کنید.", null)
        }
    }
}
