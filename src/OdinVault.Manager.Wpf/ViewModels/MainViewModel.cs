using System.Collections.ObjectModel;
using System.Windows.Input;
using OdinVault.Manager.Wpf.Infrastructure;
using OdinVault.Manager.Wpf.Services;
namespace OdinVault.Manager.Wpf.ViewModels;
internal sealed class MainViewModel : ObservableObject, IDisposable
{
 readonly AgentDashboardClient client=new();
 public AgentDashboardClient Client=>client;
 string agentStatus="در حال بررسی…",lastRefresh="—",protectedValue="—",failedValue="—",activeValue="—",storageValue="—";
 public string AgentStatus{get=>agentStatus;private set=>Set(ref agentStatus,value);}
 public string LastRefresh{get=>lastRefresh;private set=>Set(ref lastRefresh,value);}
 public string ProtectedValue{get=>protectedValue;private set=>Set(ref protectedValue,value);}
 public string FailedValue{get=>failedValue;private set=>Set(ref failedValue,value);}
 public string ActiveValue{get=>activeValue;private set=>Set(ref activeValue,value);}
 public string StorageValue{get=>storageValue;private set=>Set(ref storageValue,value);}
 public ObservableCollection<ActivityItem> Activities{get;}=[];
 public ObservableCollection<AttentionItem> Attention{get;}=[];
 public ICommand RefreshCommand{get;}
 public MainViewModel(){RefreshCommand=new AsyncCommand(RefreshAsync);}
 public async Task RefreshAsync(){try{var health=await client.HealthAsync(); AgentStatus=health?.Status?.Equals("ok",StringComparison.OrdinalIgnoreCase)==true?"Agent متصل":"Agent در دسترس نیست"; var d=await client.DashboardAsync(); if(d is null)return; ProtectedValue=$"{d.ProtectedDatabases} / {d.EnabledDatabases}";FailedValue=d.FailedJobsLast24Hours.ToString("N0");ActiveValue=d.ActiveJobs.ToString("N0");StorageValue=FormatBytes(d.StorageFreeBytes);LastRefresh=DateTime.Now.ToString("HH:mm"); Activities.Clear();foreach(var x in d.RecentActivity.Take(8))Activities.Add(new(x.DatabaseName,Status(x.Status),PersianDateFormatter.Format(x.StartedAtUtc))); Attention.Clear();foreach(var x in d.Attention.Take(6))Attention.Add(new(x.Title,x.DatabaseName,x.Severity));}catch(Exception ex){AgentStatus="خطا در اتصال";LastRefresh=ex.Message;}}
 static string Status(int s)=>s switch{2=>"موفق",3=>"ناموفق",1=>"در حال اجرا",_=>"در صف"};
 static string FormatBytes(long? v){if(v is null)return "—";double n=v.Value;string[] u=["B","KB","MB","GB","TB"];int i=0;while(n>=1024&&i<u.Length-1){n/=1024;i++;}return $"{n:0.#} {u[i]}";}
 public void Dispose()=>client.Dispose();
}
internal sealed record ActivityItem(string Database,string Status,string Time);
internal sealed record AttentionItem(string Title,string Database,string Severity);
