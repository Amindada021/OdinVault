using System.Collections.ObjectModel;
using System.Windows.Input;
using OdinVault.Manager.Wpf.Infrastructure;
using OdinVault.Manager.Wpf.Services;
namespace OdinVault.Manager.Wpf.ViewModels;
internal sealed class DatabasesViewModel : ObservableObject
{
 readonly AgentDashboardClient client; bool loading; string? error;
 public ObservableCollection<DatabaseRow> Items{get;}=[];
 public bool IsLoading{get=>loading;private set{if(Set(ref loading,value))Raise(nameof(HasNoItems));}}
 public string? Error{get=>error;private set{if(Set(ref error,value)){Raise(nameof(HasError));Raise(nameof(HasNoItems));}}}
 public bool HasError=>!string.IsNullOrWhiteSpace(Error);
 public bool HasNoItems=>!IsLoading&&!HasError&&Items.Count==0;
 public ICommand RefreshCommand{get;}
 public DatabasesViewModel(AgentDashboardClient client){this.client=client;RefreshCommand=new AsyncCommand(LoadAsync);}
 public async Task LoadAsync(){IsLoading=true;Error=null;try{var rows=await client.DatabasesAsync();Items.Clear();foreach(var x in rows)Items.Add(new(x.Name,x.DatabaseName,Host(x),x.IsEnabled?"فعال":"غیرفعال",x.IsProtected?"محافظت‌شده":"نیازمند تنظیم",x.LatestBackupAtUtc?.ToLocalTime().ToString("yyyy/MM/dd HH:mm")??"—",FormatBytes(x.LatestBackupSizeBytes)));Raise(nameof(HasNoItems));}catch(Exception ex){Error=ex.Message;}finally{IsLoading=false;}}
 static string Host(DatabaseOverviewResponse x)=>x.Port is null?x.Host:$"{x.Host}:{x.Port}";
 static string FormatBytes(long? v){if(v is null)return "—";double n=v.Value;string[] u=["B","KB","MB","GB","TB"];int i=0;while(n>=1024&&i<u.Length-1){n/=1024;i++;}return $"{n:0.#} {u[i]}";}
}
internal sealed record DatabaseRow(string Name,string DatabaseName,string Host,string State,string Protection,string LastBackup,string Size);
