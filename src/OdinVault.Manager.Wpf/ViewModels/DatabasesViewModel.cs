using System.Collections.ObjectModel;
using System.Windows.Input;
using OdinVault.Manager.Wpf.Infrastructure;
using OdinVault.Manager.Wpf.Services;
namespace OdinVault.Manager.Wpf.ViewModels;
internal sealed class DatabasesViewModel : ObservableObject
{
 readonly AgentDashboardClient client; bool loading, detailsLoading, operationRunning; string? error, detailsError; string operationStatus=""; DatabaseRow? selected; DatabaseDetailsCard? details;
 public ObservableCollection<DatabaseRow> Items{get;}=[]; public ObservableCollection<BackupHistoryRow> BackupHistory{get;}=[]; public ObservableCollection<ReplicaHistoryRow> ReplicaHistory{get;}=[];
 public bool IsLoading{get=>loading;private set{if(Set(ref loading,value))Raise(nameof(HasNoItems));}}
 public string? Error{get=>error;private set{if(Set(ref error,value)){Raise(nameof(HasError));Raise(nameof(HasNoItems));}}}
 public bool IsDetailsLoading{get=>detailsLoading;private set{if(Set(ref detailsLoading,value))Raise(nameof(ShowSelectionPrompt));}}
 public string? DetailsError{get=>detailsError;private set{if(Set(ref detailsError,value))Raise(nameof(HasDetailsError));Raise(nameof(ShowSelectionPrompt));}}
 public bool HasDetailsError=>!string.IsNullOrWhiteSpace(DetailsError);
 public DatabaseRow? Selected{get=>selected;set{if(Set(ref selected,value)&&value is not null)_=LoadDetailsAsync(value.Id);}}
 public DatabaseDetailsCard? Details{get=>details;private set{if(Set(ref details,value))Raise(nameof(HasDetails));Raise(nameof(ShowSelectionPrompt));}}
 public bool HasDetails=>Details is not null; public bool ShowSelectionPrompt=>!HasDetails&&!IsDetailsLoading&&!HasDetailsError;
 public bool HasError=>!string.IsNullOrWhiteSpace(Error);
 public bool HasNoItems=>!IsLoading&&!HasError&&Items.Count==0;
 public bool OperationRunning{get=>operationRunning;private set=>Set(ref operationRunning,value);} public string OperationStatus{get=>operationStatus;private set=>Set(ref operationStatus,value);}
 public ICommand RefreshCommand{get;} public ICommand TestCommand{get;} public ICommand BackupCommand{get;}
 public DatabasesViewModel(AgentDashboardClient client){this.client=client;RefreshCommand=new AsyncCommand(LoadAsync);TestCommand=new AsyncCommand(TestAsync);BackupCommand=new AsyncCommand(BackupAsync);}
 public async Task LoadAsync(){IsLoading=true;Error=null;try{var rows=await client.DatabasesAsync();Items.Clear();foreach(var x in rows)Items.Add(new(x.Id,x.Name,x.DatabaseName,Host(x),x.IsEnabled?"فعال":"غیرفعال",x.IsEnabled,x.IsProtected?"محافظت‌شده":"نیازمند تنظیم",x.IsProtected,PersianDateFormatter.Format(x.LatestBackupAtUtc),FormatBytes(x.LatestBackupSizeBytes)));Raise(nameof(HasNoItems));}catch(Exception ex){Error=ex.Message;}finally{IsLoading=false;}}
 async Task TestAsync(){if(Selected is null)return;OperationRunning=true;OperationStatus="در حال تست اتصال…";try{await client.TestDatabaseAsync(Selected.Id);OperationStatus="اتصال دیتابیس موفق بود.";}catch(Exception ex){OperationStatus=$"خطا: {ex.Message}";}finally{OperationRunning=false;}}
 async Task BackupAsync(){if(Selected is null)return;OperationRunning=true;OperationStatus="شروع بکاپ…";try{var progress=new Progress<string>(x=>OperationStatus=x);await client.RunBackupAsync(Selected.Id,progress);OperationStatus="بکاپ با موفقیت تکمیل شد.";await LoadDetailsAsync(Selected.Id);}catch(Exception ex){OperationStatus=$"خطا: {ex.Message}";}finally{OperationRunning=false;}}
 async Task LoadDetailsAsync(Guid id){IsDetailsLoading=true;DetailsError=null;Details=null;try{var d=await client.DatabaseDetailsAsync(id);if(d is null)return;BackupHistory.Clear();foreach(var b in d.Backups.Take(8))BackupHistory.Add(new(Status(b.Status),Verification(b.VerificationStatus),FormatBytes(b.SizeBytes),PersianDateFormatter.Format(b.StartedAtUtc),b.LocalFileAvailable?"موجود":"ناموجود"));ReplicaHistory.Clear();foreach(var r in d.Replicas.Take(8))ReplicaHistory.Add(new(r.Name,Status(r.Status),FormatBytes(r.SizeBytes),PersianDateFormatter.Format(r.StartedAtUtc)));Details=new(d.Database.Name,d.Database.DatabaseName,Host(d.Database.Host,d.Database.Port),d.Protection.IsProtected?"محافظت‌شده":"نیازمند تنظیم",PersianDateFormatter.Format(d.Protection.LatestBackupAtUtc),Status(d.Protection.LatestBackupStatus),Verification(d.Protection.LatestVerificationStatus),FormatBytes(d.Protection.LatestBackupSizeBytes),$"{d.Protection.LatestReplicaSucceeded} / {d.Protection.LatestReplicaTotal}",d.Database.Policy?.ScheduleCron??"—",d.Database.Policy?.BackupDirectory??"—",d.Backups.Count,d.Replicas.Count);}catch(Exception ex){DetailsError=ex.Message;}finally{IsDetailsLoading=false;}}
 static string Host(DatabaseOverviewResponse x)=>Host(x.Host,x.Port); static string Host(string h,int? p)=>p is null?h:$"{h}:{p}";
 static string Status(int? s)=>s switch{2=>"موفق",3=>"ناموفق",1=>"در حال اجرا",0=>"در صف",_=>"—"};
 static string Verification(int? s)=>s switch{2=>"تأیید شده",3=>"ناموفق",1=>"در حال بررسی",_=>"—"};
 static string FormatBytes(long? v){if(v is null)return "—";double n=v.Value;string[] u=["B","KB","MB","GB","TB"];int i=0;while(n>=1024&&i<u.Length-1){n/=1024;i++;}return $"{n:0.#} {u[i]}";}
}
internal sealed record DatabaseRow(Guid Id,string Name,string DatabaseName,string Host,string State,bool IsEnabled,string Protection,bool IsProtected,string LastBackup,string Size);
internal sealed record DatabaseDetailsCard(string Name,string DatabaseName,string Host,string Protection,string LastBackup,string BackupStatus,string Verification,string Size,string Replica,string Schedule,string BackupPath,int BackupCount,int ReplicaCount);

internal sealed record BackupHistoryRow(string Status,string Verification,string Size,string Time,string LocalFile);
internal sealed record ReplicaHistoryRow(string Name,string Status,string Size,string Time);
