package com.odinvault.odinvault_mobile

import android.app.*
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.IBinder
import android.provider.DocumentsContract
import java.io.File
import java.io.RandomAccessFile
import java.net.HttpURLConnection
import java.net.URL
import kotlin.math.max

class BackupDownloadService : Service() {
    companion object { const val CHANNEL_ID="odinvault_downloads"; const val NOTIFICATION_ID=4102; const val ACTION_START="odinvault.download.START"; const val ACTION_CANCEL="odinvault.download.CANCEL"; private const val PREFS="odinvault_download" }
    @Volatile private var cancelled=false
    private val prefs by lazy { getSharedPreferences(PREFS,MODE_PRIVATE) }
    override fun onBind(intent:Intent?):IBinder?=null
    override fun onStartCommand(intent:Intent?,flags:Int,startId:Int):Int{
        if(intent?.action==ACTION_CANCEL){cancelled=true;update("cancelled",error="دانلود روی گوشی لغو شد.");stopForeground(STOP_FOREGROUND_REMOVE);stopSelf();return START_NOT_STICKY}
        if(intent?.action!=ACTION_START)return START_NOT_STICKY
        createChannel();cancelled=false;startForeground(NOTIFICATION_ID,notification("در حال آماده‌سازی دانلود",0,0,true));Thread{download(intent)}.start();return START_REDELIVER_INTENT
    }
    private fun download(intent:Intent){
        val base=intent.getStringExtra("baseUrl")?:return fail("آدرس Agent در دسترس نیست.");val key=intent.getStringExtra("apiKey")?:"";val id=intent.getStringExtra("backupId")?:return fail("شناسه بکاپ در دسترس نیست.")
        val originalName=safeName(intent.getStringExtra("fileName")?:"backup.bak");val agent=safeName(intent.getStringExtra("agentName")?:"Agent");val fileName=safeName(agent+"__"+originalName);val db=safeName(intent.getStringExtra("databaseName")?:"Database");val expected=intent.getLongExtra("expectedSize",0)
        val partial=File(filesDir,"downloads").apply{mkdirs()}.resolve("$id.part")
        prefs.edit().putString("backup_id",id).putString("file_name",fileName).putString("database_name",db).remove("saved_uri").apply()
        try{
            var existing=if(partial.isFile)partial.length()else 0L;var c=open(base,id,key,existing);var code=c.responseCode
            if(code==416){val remote=parse416(c.getHeaderField("Content-Range"));c.disconnect();if(remote>0&&existing==remote&&(expected<=0||remote==expected)){finishFile(partial,fileName,db,remote);return};partial.delete();existing=0;c=open(base,id,key,0);code=c.responseCode}
            if(code!=200&&code!=206)throw IllegalStateException("HTTP $code")
            var range=c.getHeaderField("Content-Range");var etag=c.getHeaderField("ETag");var modified=c.getHeaderField("Last-Modified")
            val changed=existing>0&&((prefs.getString("etag",null)!=null&&etag!=null&&prefs.getString("etag",null)!=etag)||(prefs.getString("last_modified",null)!=null&&modified!=null&&prefs.getString("last_modified",null)!=modified))
            val valid=code==206&&range?.startsWith("bytes $existing-")==true&&!changed
            if(existing>0&&!valid){c.disconnect();partial.delete();existing=0;c=open(base,id,key,0);code=c.responseCode;if(code!=200)throw IllegalStateException("HTTP $code while restarting download");range=c.getHeaderField("Content-Range");etag=c.getHeaderField("ETag");modified=c.getHeaderField("Last-Modified")}
            prefs.edit().putString("etag",etag).putString("last_modified",modified).apply()
            val total=if(code==206)parseTotal(range)else c.contentLengthLong.coerceAtLeast(0)
            if(expected>0&&total>0&&expected!=total){c.disconnect();partial.delete();throw IllegalStateException("فایل روی Agent نسبت به نسخه انتخاب‌شده تغییر کرده است.")}
            update("downloading",existing,total)
            RandomAccessFile(partial,"rw").use{out->if(existing==0L)out.setLength(0)else out.seek(existing);c.inputStream.use{input->val buf=ByteArray(1024*1024);var received=existing;var last=0L
                while(true){if(cancelled)throw InterruptedException();val n=input.read(buf);if(n<0)break;out.write(buf,0,n);received+=n;val now=System.currentTimeMillis();if(now-last>=500){update("downloading",received,total);last=now}}
                if(total>0&&received!=total)throw IllegalStateException("دانلود ناقص ماند؛ ادامه آن در اجرای بعدی انجام می‌شود.");if(expected>0&&received!=expected)throw IllegalStateException("حجم فایل دانلودشده با بکاپ انتخاب‌شده یکسان نیست.");finishFile(partial,fileName,db,received)}}
            c.disconnect()
        }catch(_:InterruptedException){update("cancelled",partial.length(),max(expected,0),"دانلود روی گوشی لغو شد.");notifyFinal("دانلود روی گوشی لغو شد")}
        catch(e:Exception){fail(e.message?:"دانلود بکاپ ناموفق بود.",partial.length(),expected)}
    }
    private fun finishFile(partial:File,fileName:String,db:String,total:Long){
        update("saving",total,total);val treeText=getSharedPreferences("odinvault_files",MODE_PRIVATE).getString("backup_tree",null)?:throw IllegalStateException("پوشه مقصد OdinVault در دسترس نیست.");val tree=Uri.parse(treeText)
        val selected=DocumentsContract.buildDocumentUriUsingTree(tree,DocumentsContract.getTreeDocumentId(tree));val selectedName=contentResolver.query(selected,arrayOf(DocumentsContract.Document.COLUMN_DISPLAY_NAME),null,null,null)?.use{if(it.moveToFirst())it.getString(0)else null}
        val app=if(selectedName=="OdinVault")selected else directory(tree,selected,"OdinVault");val folder=directory(tree,app,db);val name=uniqueName(tree,folder,fileName);val target=requireNotNull(DocumentsContract.createDocument(contentResolver,folder,"application/octet-stream",name))
        try{contentResolver.openOutputStream(target,"w").use{o->requireNotNull(o);partial.inputStream().use{it.copyTo(o,1024*1024)}}}catch(e:Exception){try{DocumentsContract.deleteDocument(contentResolver,target)}catch(_:Exception){};throw e}
        partial.delete();prefs.edit().putString("saved_uri",target.toString()).apply();update("complete",total,total);notifyFinal("بکاپ روی گوشی ذخیره شد");stopForeground(STOP_FOREGROUND_REMOVE);stopSelf()
    }
    private fun open(base:String,id:String,key:String,offset:Long)=(URL(base.trimEnd('/')+"/api/backups/$id/download").openConnection() as HttpURLConnection).apply{connectTimeout=15000;readTimeout=30000;instanceFollowRedirects=false;setRequestProperty("Accept","application/octet-stream");if(key.isNotEmpty())setRequestProperty("X-OdinVault-Key",key);if(offset>0)setRequestProperty("Range","bytes=$offset-")}
    private fun update(state:String,received:Long=0,total:Long=0,error:String?=null){prefs.edit().putString("state",state).putLong("received",received).putLong("total",total).putString("error",error).putLong("updated_at",System.currentTimeMillis()).apply();if(state=="downloading"||state=="saving")(getSystemService(NOTIFICATION_SERVICE) as NotificationManager).notify(NOTIFICATION_ID,notification(if(state=="saving")"در حال ذخیره بکاپ" else "در حال دانلود بکاپ",received,total,total<=0))}
    private fun fail(message:String,received:Long=0,total:Long=0){update("failed",received,total,message);notifyFinal("دانلود بکاپ ناموفق بود");stopForeground(STOP_FOREGROUND_REMOVE);stopSelf()}
    private fun notification(title:String,received:Long,total:Long,indeterminate:Boolean):Notification{val cancel=Intent(this,BackupDownloadService::class.java).setAction(ACTION_CANCEL);val pi=PendingIntent.getService(this,4103,cancel,PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE);val p=if(total>0)((received*100/total).coerceIn(0,100)).toInt()else 0;return Notification.Builder(this,CHANNEL_ID).setSmallIcon(android.R.drawable.stat_sys_download).setContentTitle(title).setContentText(if(total>0)"$p٪" else "در حال دریافت...").setOnlyAlertOnce(true).setOngoing(true).setProgress(100,p,indeterminate).addAction(Notification.Action.Builder(null,"لغو دانلود",pi).build()).build()}
    private fun notifyFinal(title:String){(getSystemService(NOTIFICATION_SERVICE) as NotificationManager).notify(NOTIFICATION_ID,Notification.Builder(this,CHANNEL_ID).setSmallIcon(android.R.drawable.stat_sys_download_done).setContentTitle(title).setAutoCancel(true).build())}
    private fun createChannel(){if(Build.VERSION.SDK_INT>=26)(getSystemService(NOTIFICATION_SERVICE) as NotificationManager).createNotificationChannel(NotificationChannel(CHANNEL_ID,"دانلود بکاپ",NotificationManager.IMPORTANCE_LOW))}
    private fun parseTotal(v:String?)=v?.substringAfter('/')?.toLongOrNull()?:0L
    private fun parse416(v:String?)=if(v?.startsWith("bytes */")==true)v.substringAfter('/').toLongOrNull()?:0L else 0L
    private fun safeName(v:String)=v.map{if(it<' '||it in "\\/:*?\"<>|")'_' else it}.joinToString("").trim().trim('.').take(120).ifEmpty{"backup"}
    private fun directory(tree:Uri,parent:Uri,name:String):Uri{val children=DocumentsContract.buildChildDocumentsUriUsingTree(tree,DocumentsContract.getDocumentId(parent));contentResolver.query(children,arrayOf(DocumentsContract.Document.COLUMN_DOCUMENT_ID,DocumentsContract.Document.COLUMN_DISPLAY_NAME,DocumentsContract.Document.COLUMN_MIME_TYPE),null,null,null)?.use{c->while(c.moveToNext())if(c.getString(1)==name&&c.getString(2)==DocumentsContract.Document.MIME_TYPE_DIR)return DocumentsContract.buildDocumentUriUsingTree(tree,c.getString(0))};return requireNotNull(DocumentsContract.createDocument(contentResolver,parent,DocumentsContract.Document.MIME_TYPE_DIR,name))}
    private fun uniqueName(tree:Uri,parent:Uri,name:String):String{val children=DocumentsContract.buildChildDocumentsUriUsingTree(tree,DocumentsContract.getDocumentId(parent));val names=mutableSetOf<String>();contentResolver.query(children,arrayOf(DocumentsContract.Document.COLUMN_DISPLAY_NAME),null,null,null)?.use{c->while(c.moveToNext())names.add(c.getString(0))};if(!names.contains(name))return name;val dot=name.lastIndexOf('.');val stem=if(dot>0)name.substring(0,dot)else name;val ext=if(dot>0)name.substring(dot)else "";var i=2;while(names.contains("$stem ($i)$ext"))i++;return "$stem ($i)$ext"}
}
