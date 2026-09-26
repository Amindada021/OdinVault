using System.Net.Http.Json;
using System.Text.Json;
namespace OdinVault.Manager.Wpf.Services;
internal sealed class AgentDashboardClient : IDisposable
{
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=true};
 readonly HttpClient http=new(){BaseAddress=new Uri("http://127.0.0.1:5188/"),Timeout=TimeSpan.FromSeconds(30)};
 public Task<HealthResponse?> HealthAsync()=>http.GetFromJsonAsync<HealthResponse>("api/health",Json);
 public async Task<IReadOnlyList<DatabaseOverviewResponse>> DatabasesAsync(){
  using var request=new HttpRequestMessage(HttpMethod.Get,"api/databases/overview"); request.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey()); using var response=await http.SendAsync(request); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<List<DatabaseOverviewResponse>>(Json)??[];
 }
 public async Task<DatabaseDetailsResponse?> DatabaseDetailsAsync(Guid id){
  using var request=new HttpRequestMessage(HttpMethod.Get,$"api/databases/{id}/details"); request.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey()); using var response=await http.SendAsync(request); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<DatabaseDetailsResponse>(Json);
 }
 public async Task<DashboardResponse?> DashboardAsync(){
  using var request=new HttpRequestMessage(HttpMethod.Get,"api/dashboard");
  request.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey());
  using var response=await http.SendAsync(request); response.EnsureSuccessStatusCode();
  return await response.Content.ReadFromJsonAsync<DashboardResponse>(Json);
 }
 static string LoadApiKey(){var configured=Environment.GetEnvironmentVariable("ODINVAULT_API_KEY");if(!string.IsNullOrWhiteSpace(configured))return configured.Trim();var p=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"OdinVault","agent-api-key.txt");if(!File.Exists(p))throw new InvalidOperationException($"کلید Agent پیدا نشد: {p}");var key=File.ReadAllText(p).Trim();if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException("فایل کلید Agent خالی است.");return key;}
 public void Dispose()=>http.Dispose();
}
internal sealed record HealthResponse(string? Service,string? Status,DateTime Utc);
internal sealed record DashboardResponse(DateTime Utc,string Status,string ProtectionStatus,int EnabledDatabases,int ProtectedDatabases,int ActiveJobs,int FailedJobsLast24Hours,long? StorageFreeBytes,IReadOnlyList<DashboardAttentionResponse> Attention,IReadOnlyList<DashboardActivityResponse> RecentActivity);
internal sealed record DashboardAttentionResponse(string Severity,Guid DatabaseId,string DatabaseName,string Title,string Message,DateTime? OccurredAtUtc);
internal sealed record DashboardActivityResponse(Guid DatabaseId,string DatabaseName,Guid BackupId,int Status,int VerificationStatus,long? SizeBytes,DateTime StartedAtUtc,DateTime? CompletedAtUtc,string? Error);

internal sealed record DatabaseOverviewResponse(Guid Id,string Name,string Host,int? Port,string DatabaseName,bool IsEnabled,bool IsProtected,DateTime? LatestBackupAtUtc,int? LatestBackupStatus,int? LatestVerificationStatus,long? LatestBackupSizeBytes);
internal sealed record DatabaseDetailsResponse(DatabaseDetailsDatabaseResponse Database,DatabaseProtectionResponse Protection,IReadOnlyList<DatabaseBackupHistoryResponse> Backups,IReadOnlyList<DatabaseReplicaHistoryResponse> Replicas);
internal sealed record DatabaseDetailsDatabaseResponse(Guid Id,string Name,string Host,int? Port,string DatabaseName,bool IsEnabled,DatabaseDetailsPolicyResponse? Policy);
internal sealed record DatabaseDetailsPolicyResponse(string? ScheduleCron,int MaxLocalBackups,bool VerifyAfterBackup,string BackupDirectory,bool IsEnabled);
internal sealed record DatabaseProtectionResponse(bool IsProtected,DateTime? LatestBackupAtUtc,int? LatestBackupStatus,int? LatestVerificationStatus,long? LatestBackupSizeBytes,int LatestReplicaSucceeded,int LatestReplicaTotal);
internal sealed record DatabaseBackupHistoryResponse(Guid Id,int Status,int VerificationStatus,long? SizeBytes,DateTime StartedAtUtc,DateTime? CompletedAtUtc,bool LocalFileAvailable,string? Error);
internal sealed record DatabaseReplicaHistoryResponse(Guid BackupRecordId,Guid StorageTargetId,string Name,int Type,int Status,long? SizeBytes,string? ContentHashSha256,string? Error,DateTime StartedAtUtc,DateTime? CompletedAtUtc);
