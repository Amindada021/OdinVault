using System.Collections.ObjectModel;
using System.Windows.Input;
using OdinVault.Manager.Wpf.Infrastructure;
using OdinVault.Manager.Wpf.Services;
namespace OdinVault.Manager.Wpf.ViewModels;
internal sealed class DatabasesViewModel : ObservableObject
{
 readonly AgentDashboardClient client; bool loading, detailsLoading; string? error, detailsError; DatabaseRow? selected; DatabaseDetailsCard? details;
 public ObservableCollection<DatabaseRow> Items{get;}=[];
 public bool IsLoading{get=>loading;private set{if(Set(ref loading,value))Raise(nameof(HasNoItems));}}
 public string? Error{get=>error;private set{if(Set(ref error,value)){Raise(nameof(HasError));Raise(nameof(HasNoItems));}}}
 public bool IsDetailsLoading{get=>detailsLoading;private set=>Set(ref detailsLoading,value);}
 public string? DetailsError{get=>detailsError;private set{if(Set(ref detailsError,value))Raise(nameof(HasDetailsError));}}
 public bool HasDetailsError=>!string.IsNullOrWhiteSpace(DetailsError);
 public DatabaseRow? Selected{get=>selected;set{if(Set(ref selected,value)&&value is not null)_=LoadDetailsAsync(value.Id);}}
 public DatabaseDetailsCard? Details{get=>details;private set{if(Set(ref details,value))Raise(nameof(HasDetails));}}
 public bool HasDetails=>Details is not null;
 public bool HasError=>!string.IsNullOrWhiteSpace(Error);
 public bool HasNoItems=>!IsLoading&&!HasError&&Items.Count==0;
 public ICommand RefreshCommand{get;}
 public DatabasesViewModel(AgentDashboardClient client){this.client=client;RefreshCommand=new AsyncCommand(LoadAsync);}
 public async Task LoadAsync(){IsLoading=true;Error=null;try{var rows=await client.DatabasesAsync();Items.Clear();foreach(var x in rows)Items.Add(new(x.Id,x.Name,x.DatabaseName,Host(x),x.IsEnabled?"فعال":"غیرفعال",x.IsEnabled,x.IsProtected?"محافظت‌شده":"نیازمند تنظیم",x.IsProtected,x.LatestBackupAtUtc?.ToLocalTime().ToString("yyyy/MM/dd HH:mm")??"—",FormatBytes(x.LatestBackupSizeBytes)));Raise(nameof(HasNoItems));}catch(Exception ex){Error=ex.Message;}finally{IsLoading=false;}}
 async Task LoadDetailsAsync(Guid id){IsDetailsLoading=true;DetailsError=null;Details=null;try{var d=await client.DatabaseDetailsAsync(id);if(d is null)return;Details=new(d.Database.Name,d.Database.DatabaseName,Host(d.Database.Host,d.Database.Port),d.Protection.IsProtected?"محافظت‌شده":"نیازمند تنظیم",d.Protection.LatestBackupAtUtc?.ToLocalTime().ToString("yyyy/MM/dd HH:mm")??"—",Status(d.Protection.LatestBackupStatus),Verification(d.Protection.LatestVerificationStatus),FormatBytes(d.Protection.LatestBackupSizeBytes),$"{d.Protection.LatestReplicaSucceeded} / {d.Protection.LatestReplicaTotal}",d.Database.Policy?.ScheduleCron??"—",d.Database.Policy?.BackupDirectory??"—",d.Backups.Count,d.Replicas.Count);}catch(Exception ex){DetailsError=ex.Message;}finally{IsDetailsLoading=false;}}
 static string Host(DatabaseOverviewResponse x)=>Host(x.Host,x.Port); static string Host(string h,int? p)=>p is null?h:$"{h}:{p}";
 static string Status(int? s)=>s switch{2=>"موفق",3=>"ناموفق",1=>"در حال اجرا",0=>"در صف",_=>"—"};
 static string Verification(int? s)=>s switch{2=>"تأیید شده",3=>"ناموفق",1=>"در حال بررسی",_=>"—"};
 static string FormatBytes(long? v){if(v is null)return "—";double n=v.Value;string[] u=["B","KB","MB","GB","TB"];int i=0;while(n>=1024&&i<u.Length-1){n/=1024;i++;}return $"{n:0.#} {u[i]}";}
}
internal sealed record DatabaseRow(Guid Id,string Name,string DatabaseName,string Host,string State,bool IsEnabled,string Protection,bool IsProtected,string LastBackup,string Size);
internal sealed record DatabaseDetailsCard(string Name,string DatabaseName,string Host,string Protection,string LastBackup,string BackupStatus,string Verification,string Size,string Replica,string Schedule,string BackupPath,int BackupCount,int ReplicaCount);
