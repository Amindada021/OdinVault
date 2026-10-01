import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

class PhoneBackupsPage extends StatefulWidget {
  const PhoneBackupsPage({super.key});
  @override State<PhoneBackupsPage> createState() => _PhoneBackupsPageState();
}
class _PhoneBackupsPageState extends State<PhoneBackupsPage> {
  static const files=MethodChannel('odinvault/files');
  List<Map<String,dynamic>> items=[]; String? error; bool loading=true;
  @override void initState(){super.initState();load();}
  Future<void> load() async {
    try{
      final raw=await files.invokeMethod<List<dynamic>>('listStoredBackups')??[];
      if(!mounted)return;setState((){items=raw.whereType<Map>().map((x)=>Map<String,dynamic>.from(x)).toList();error=null;loading=false;});
    }catch(e){if(mounted)setState((){error=e is PlatformException?(e.message??'خطا'):'خواندن فایل‌ها ناموفق بود.';loading=false;});}
  }
  Future<void> changeFolder() async {
    await files.invokeMethod<void>('clearBackupFolder');
    try{await files.invokeMethod<String>('ensureBackupFolder');}catch(_){}
    await load();
  }
  Future<void> remove(Map<String,dynamic> item) async {
    final ok=await showDialog<bool>(context:context,builder:(_)=>AlertDialog(title:const Text('حذف بکاپ گوشی'),content:Text('فایل «${item['name']}» از گوشی حذف شود؟'),actions:[TextButton(onPressed:()=>Navigator.pop(context,false),child:const Text('انصراف')),FilledButton(onPressed:()=>Navigator.pop(context,true),child:const Text('حذف'))]))??false;
    if(!ok)return;await files.invokeMethod<bool>('deleteStoredBackup',{'uri':item['uri']});await load();
  }
  String size(num? v){final n=v?.toInt()??0;if(n<1048576)return '${(n/1024).toStringAsFixed(1)} KB';if(n<1073741824)return '${(n/1048576).toStringAsFixed(1)} MB';return '${(n/1073741824).toStringAsFixed(2)} GB';}
  String date(num? v){if(v==null||v.toInt()<=0)return '-';final d=DateTime.fromMillisecondsSinceEpoch(v.toInt());return '${d.year}/${d.month.toString().padLeft(2,'0')}/${d.day.toString().padLeft(2,'0')}  ${d.hour.toString().padLeft(2,'0')}:${d.minute.toString().padLeft(2,'0')}';}
  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          title: const Text('بکاپ‌های ذخیره‌شده روی گوشی'),
          actions: [
            IconButton(
              tooltip: 'تغییر پوشه مقصد',
              onPressed: changeFolder,
              icon: const Icon(Icons.drive_file_move_outline),
            ),
            IconButton(onPressed: load, icon: const Icon(Icons.refresh)),
          ],
        ),
        body: loading
            ? const Center(child: CircularProgressIndicator())
            : error != null
                ? Center(child: Text(error!))
                : items.isEmpty
                    ? const Center(child: Text('بکاپی در پوشه انتخاب‌شده پیدا نشد.'))
                    : RefreshIndicator(
                        onRefresh: load,
                        child: ListView.separated(
                          padding: const EdgeInsets.all(12),
                          itemCount: items.length,
                          separatorBuilder: (_, index) => const SizedBox(height: 8),
                          itemBuilder: (context, index) {
                            final x = items[index];
                            return Card(
                              child: ListTile(
                                title: Text(
                                  x['name']?.toString() ?? 'backup.bak',
                                  textDirection: TextDirection.ltr,
                                ),
                                subtitle: Text(
                                  'دیتابیس: ${x['database'] ?? '-'}\n'
                                  'Agent: ${x['agent'] ?? 'نامشخص'}\n'
                                  '${date(x['modified'] as num?)} • ${size(x['size'] as num?)}',
                                ),
                                isThreeLine: true,
                                trailing: PopupMenuButton<String>(
                                  onSelected: (v) async {
                                    if (v == 'share') {
                                      await files.invokeMethod<void>(
                                        'shareStoredBackup',
                                        {'uri': x['uri']},
                                      );
                                    }
                                    if (v == 'delete') await remove(x);
                                  },
                                  itemBuilder: (_) => const [
                                    PopupMenuItem(
                                      value: 'share',
                                      child: Text('اشتراک‌گذاری'),
                                    ),
                                    PopupMenuItem(
                                      value: 'delete',
                                      child: Text('حذف'),
                                    ),
                                  ],
                                ),
                              ),
                            );
                          },
                        ),
                      ),
      );
}
