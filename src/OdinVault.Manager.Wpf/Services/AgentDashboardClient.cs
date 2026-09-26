using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
namespace OdinVault.Manager.Wpf.Services;
internal sealed class AgentDashboardClient : IDisposable
{
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=true};
 readonly HttpClient http=new(){BaseAddress=new Uri("http://127.0.0.1:5188/"),Timeout=TimeSpan.FromSeconds(30)};
 readonly HttpClient longRunning=new(){BaseAddress=new Uri("http://127.0.0.1:5188/"),Timeout=Timeout.InfiniteTimeSpan};
 public Task<HealthResponse?> HealthAsync()=>http.GetFromJsonAsync<HealthResponse>("api/health",Json);
 public async Task DeleteDatabaseAsync(Guid id,bool deleteHistory=true,bool deleteFiles=false){using var r=Authorized(HttpMethod.Delete,$"api/databases/{id}?deleteHistory={deleteHistory.ToString().ToLowerInvariant()}&deleteFiles={deleteFiles.ToString().ToLowerInvariant()}");using var x=await client.SendAsync(r);x.EnsureSuccessStatusCode();}public async Task<IReadOnlyList<DatabaseOverviewResponse>> DatabasesAsync(){
  using var request=new HttpRequestMessage(HttpMethod.Get,"api/databases/overview"); request.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey()); using var response=await http.SendAsync(request); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<List<DatabaseOverviewResponse>>(Json)??[];
 }
 public async Task<DatabaseDetailsResponse?> DatabaseDetailsAsync(Guid id){
  using var request=new HttpRequestMessage(HttpMethod.Get,$"api/databases/{id}/details"); request.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey()); using var response=await http.SendAsync(request); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<DatabaseDetailsResponse>(Json);
 }
 public Task<List<DiscoveredDatabaseResponse>?> DiscoverAsync(DiscoverSqlServerRequest body)=>SendAsync<List<DiscoveredDatabaseResponse>>(HttpMethod.Post,"api/sql-server/discover",body);
 public Task<DatabaseResponse?> CreateDatabaseAsync(CreateDatabaseRequest body)=>SendAsync<DatabaseResponse>(HttpMethod.Post,"api/databases",body);
 public Task<DatabaseResponse?> GetDatabaseAsync(Guid id)=>GetAuthorizedAsync<DatabaseResponse>($"api/databases/{id}");
 public async Task UpdateDatabaseAsync(Guid id,UpdateDatabaseRequest body){await SendAsync<System.Text.Json.JsonElement>(HttpMethod.Put,$"api/databases/{id}",body);}
 public async Task UpdatePolicyAsync(Guid id,UpdateBackupPolicyRequest body){await SendAsync<System.Text.Json.JsonElement>(HttpMethod.Put,$"api/databases/{id}/policy",body);}
 public async Task TestDatabaseAsync(Guid id){using var request=Authorized(HttpMethod.Post,$"api/databases/{id}/test");using var response=await http.SendAsync(request);response.EnsureSuccessStatusCode();}
 public async Task RunBackupAsync(Guid id,IProgress<string>? progress=null){var requestId=Guid.NewGuid();using var start=Authorized(HttpMethod.Post,$"api/databases/{id}/backup-jobs");start.Headers.TryAddWithoutValidation("X-OdinVault-Request-Id",requestId.ToString());using var sr=await http.SendAsync(start);sr.EnsureSuccessStatusCode();var job=(await sr.Content.ReadFromJsonAsync<BackupJobResponse>(Json))!;while(true){progress?.Report(job.Stage switch{"queued"=>"در صف بکاپ","backup"=>$"ساخت بکاپ {job.Percent?.ToString()??"…"}٪","verify"=>"بررسی سلامت فایل","replicating"=>"ارسال به پشتیبان","failed"=>"ناموفق",_=>"در حال انجام"});if(job.Backup is not null)return;if(job.Stage=="failed")throw new InvalidOperationException(job.Error);await Task.Delay(1500);using var poll=Authorized(HttpMethod.Get,$"api/backup-jobs/{job.Id}");using var pr=await http.SendAsync(poll);pr.EnsureSuccessStatusCode();job=(await pr.Content.ReadFromJsonAsync<BackupJobResponse>(Json))!;}}
 HttpRequestMessage Authorized(HttpMethod method,string uri){var r=new HttpRequestMessage(method,uri);r.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey());return r;}
 public async Task<RestorePreflightClientResponse?> RestorePreflightAsync(Guid backupId,string target){using var r=Authorized(HttpMethod.Post,"api/restores/preflight");r.Content=JsonContent.Create(new RestoreClientRequest(backupId,target),options:Json);using var response=await http.SendAsync(r);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<RestorePreflightClientResponse>(Json);}
 public async Task<RestoreExecutionClientResponse?> RestoreAsync(Guid backupId,string target){using var r=Authorized(HttpMethod.Post,"api/restores");r.Content=JsonContent.Create(new RestoreClientRequest(backupId,target),options:Json);using var response=await longRunning.SendAsync(r);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<RestoreExecutionClientResponse>(Json);}
 public async Task<StorageOverviewResponse?> StorageAsync()=>await GetAuthorizedAsync<StorageOverviewResponse>("api/storage/overview");
 public async Task<StorageConnectionTestResponse?> TestStorageAsync(Guid id){using var r=Authorized(HttpMethod.Post,$"api/storage-targets/{id}/connection-test");using var response=await http.SendAsync(r);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<StorageConnectionTestResponse>(Json);}
 public async Task UpdateStorageAsync(Guid id,UpdateStorageTargetClientRequest body){using var r=Authorized(HttpMethod.Put,$"api/storage-targets/{id}");r.Content=JsonContent.Create(body,options:Json);using var response=await http.SendAsync(r);response.EnsureSuccessStatusCode();}
 public string GetApiKey()=>LoadApiKey();
 public string GetMobileBaseUrl(){var p=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"OdinVault","mobile-base-url.txt");var env=Environment.GetEnvironmentVariable("ODINVAULT_MOBILE_BASE_URL");if(!string.IsNullOrWhiteSpace(env))return env.Trim();return File.Exists(p)?File.ReadAllText(p).Trim():$"http://{Environment.MachineName}:5188";}
 public void SaveMobileBaseUrl(string value){if(!Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri)||(uri.Scheme!="http"&&uri.Scheme!="https"))throw new InvalidOperationException("آدرس اتصال موبایل معتبر نیست.");var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"OdinVault");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"mobile-base-url.txt"),value.Trim());}
 public async Task<AgentEndpointProbeResult> TestAgentEndpointAsync(string baseUrl){using var c=new HttpClient{BaseAddress=new Uri(baseUrl.Trim().TrimEnd('/')+"/"),Timeout=TimeSpan.FromSeconds(8)};using var r=new HttpRequestMessage(HttpMethod.Get,"api/health");r.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey());using var response=await c.SendAsync(r);return new(response.IsSuccessStatusCode,$"{(int)response.StatusCode} {response.ReasonPhrase}");}
 public async Task<PortProbeResult> TestPortAsync(string host,int port){using var client=new System.Net.Sockets.TcpClient();var started=DateTime.UtcNow;try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));await client.ConnectAsync(host,port,timeout.Token);return new(true,(DateTime.UtcNow-started).TotalMilliseconds,null);}catch(Exception e){return new(false,null,e.Message);}}
 public async Task<T?> SendAsync<T>(HttpMethod method,string uri,object? body=null){using var r=Authorized(method,uri);if(body is not null)r.Content=JsonContent.Create(body,options:Json);using var response=await http.SendAsync(r);response.EnsureSuccessStatusCode();if(response.StatusCode==System.Net.HttpStatusCode.NoContent)return default;return await response.Content.ReadFromJsonAsync<T>(Json);}
 public Task<List<DatabaseResponse>?> DatabasesFullAsync()=>GetAuthorizedAsync<List<DatabaseResponse>>("api/databases");
 public Task<List<ReplicaTargetResponse>?> ReplicaTargetsAsync()=>GetAuthorizedAsync<List<ReplicaTargetResponse>>("api/storage-targets");
 public Task<ReplicaSettingsResponse?> ReplicaSettingsAsync()=>GetAuthorizedAsync<ReplicaSettingsResponse>("api/replica/settings");
 public Task<List<ReceivedBackupResponse>?> ReceivedReplicasAsync()=>GetAuthorizedAsync<List<ReceivedBackupResponse>>("api/replica/received");
 public async Task MarkAlertsReadAsync(IReadOnlyList<string> keys){using var r=Authorized(HttpMethod.Post,"api/alerts/mark-read");r.Content=JsonContent.Create(new MarkAlertsReadClientRequest(keys),options:Json);using var response=await http.SendAsync(r);response.EnsureSuccessStatusCode();}
 public async Task<AlertsOverviewResponse?> AlertsAsync()=>await GetAuthorizedAsync<AlertsOverviewResponse>("api/alerts?includeRead=true");
 public async Task<BackupReportResponse?> ReportAsync(int days=30)=>await GetAuthorizedAsync<BackupReportResponse>($"api/reports/backup?days={(days==7?7:days==90?90:30)}");
 async Task<T?> GetAuthorizedAsync<T>(string uri){using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey());using var response=await http.SendAsync(request);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<T>(Json);}
 public async Task<BackupOverviewResponse?> BackupsAsync(int take=200){using var request=new HttpRequestMessage(HttpMethod.Get,$"api/backups/overview?take={Math.Clamp(take,20,500)}");request.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey());using var response=await http.SendAsync(request);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<BackupOverviewResponse>(Json);}
 public async Task<DashboardResponse?> DashboardAsync(){
  using var request=new HttpRequestMessage(HttpMethod.Get,"api/dashboard");
  request.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey());
  using var response=await http.SendAsync(request); response.EnsureSuccessStatusCode();
  return await response.Content.ReadFromJsonAsync<DashboardResponse>(Json);
 }
 static string LoadApiKey(){var configured=Environment.GetEnvironmentVariable("ODINVAULT_API_KEY");if(!string.IsNullOrWhiteSpace(configured))return configured.Trim();var p=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"OdinVault","agent-api-key.txt");if(!File.Exists(p))throw new InvalidOperationException($"کلید Agent پیدا نشد: {p}");var key=File.ReadAllText(p).Trim();if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException("فایل کلید Agent خالی است.");return key;}
 public void Dispose(){http.Dispose();longRunning.Dispose();}
}
internal sealed record DiscoverSqlServerRequest(string Host,int? Port,string? Username,string? Password,bool TrustServerCertificate);
internal sealed record DiscoveredDatabaseResponse(string Name,int DatabaseId,string State,string RecoveryModel,bool IsSystem,bool HasAccess,bool IsRegistered,bool CanBackup);
internal sealed record CreateDatabaseRequest(string Name,string Host,int? Port,string DatabaseName,string? Username,string? Password,bool TrustServerCertificate,string BackupDirectory,int MaxLocalBackups,bool VerifyAfterBackup,string? ScheduleCron,bool IsEnabled);
internal sealed record UpdateDatabaseRequest(string Name,string Host,int? Port,string DatabaseName,string? Username,string? Password,bool ClearPassword,bool TrustServerCertificate,bool IsEnabled);
internal sealed record UpdateBackupPolicyRequest(string BackupDirectory,int MaxLocalBackups,bool VerifyAfterBackup,string? ScheduleCron,bool IsEnabled);
internal sealed record DatabaseResponse(Guid Id,string Name,string Host,int? Port,string DatabaseName,string Username,bool HasPassword,bool TrustServerCertificate,bool IsEnabled,DateTime CreatedAtUtc,BackupPolicyResponse? Policy);
internal sealed record BackupPolicyResponse(string BackupDirectory,string? ScheduleCron,int MaxLocalBackups,bool VerifyAfterBackup,bool IsEnabled,DateTime? LastScheduledRunUtc);
internal sealed record ReplicaSettingsResponse(string Directory);
internal sealed record ReplicaTargetResponse(Guid Id,string Name,int Type,bool IsEnabled);
internal sealed record ReceivedBackupResponse(string FileName,string RelativePath,long SizeBytes,DateTime ReceivedAtUtc);
internal sealed record UpdateStorageTargetClientRequest(string Name,string? FolderId,bool IsEnabled,string? BaseUrl,string? ApiKey);
internal sealed record AgentEndpointProbeResult(bool Success,string Message);
internal sealed record PortProbeResult(bool IsOpen,double? LatencyMilliseconds,string? Error);
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

internal sealed record BackupOverviewResponse(DateTime Utc,IReadOnlyList<BackupJobOverviewResponse> Jobs,IReadOnlyList<BackupHistoryOverviewResponse> Backups);
internal sealed record BackupJobOverviewResponse(Guid Id,Guid DatabaseEndpointId,string DatabaseName,int Status,string Stage,int? Percent,DateTime CreatedAtUtc,DateTime? StartedAtUtc,DateTime UpdatedAtUtc,DateTime? CompletedAtUtc,Guid? BackupRecordId,string? ErrorCode,string? ErrorMessage);
internal sealed record BackupHistoryOverviewResponse(Guid Id,Guid DatabaseEndpointId,string DatabaseName,int Status,int VerificationStatus,long? SizeBytes,DateTime StartedAtUtc,DateTime? CompletedAtUtc,bool LocalFileAvailable,string? Error);

internal sealed record StorageOverviewResponse(StorageLocalOverviewResponse Local,IReadOnlyList<StorageTargetOverviewResponse> Targets);internal sealed record StorageLocalOverviewResponse(string Name,string Directory,long? FreeBytes,bool Exists,bool Writable);internal sealed record StorageTargetOverviewResponse(Guid Id,string Name,int Type,bool IsEnabled,string? FolderId,string? AccountEmail,string? BaseUrl,bool IsConnected,int LinkedDatabases,int SucceededReplicas,int FailedReplicas,DateTime? LastActivityAtUtc,DateTime? LastSuccessAtUtc,DateTime? LastFailureAtUtc,string? LastError);internal sealed record AlertsOverviewResponse(DateTime Utc,int UnreadCount,IReadOnlyList<AlertClientResponse> Alerts);internal sealed record AlertClientResponse(string Key,string Severity,string Category,Guid? DatabaseId,string DatabaseName,string Title,string Message,DateTime OccurredAtUtc,bool IsRead);internal sealed record BackupReportResponse(int RangeDays,DateTime FromUtc,DateTime ToUtc,BackupReportSummaryResponse Summary,IReadOnlyList<BackupReportDailyResponse> Daily,IReadOnlyList<BackupReportDatabaseResponse> Databases);internal sealed record BackupReportSummaryResponse(int TotalBackups,int Succeeded,int Failed,int VerifyFailed,int ReplicaFailed,double? SuccessRate,double? AverageDurationSeconds,long TotalSuccessfulBytes,int EnabledDatabases,int ProtectedDatabases,long? StorageFreeBytes);internal sealed record BackupReportDailyResponse(DateTime DateUtc,int Succeeded,int Failed,int VerifyFailed,long TotalSizeBytes,double? AverageDurationSeconds);internal sealed record BackupReportDatabaseResponse(Guid DatabaseId,string DatabaseName,int TotalBackups,int Succeeded,int Failed,int VerifyFailed,int ReplicaFailed,double? SuccessRate,double? AverageDurationSeconds,DateTime? LatestBackupAtUtc,int? LatestBackupStatus,long? LatestSizeBytes,long? PreviousSizeBytes,double? SizeGrowthPercent,string Severity);

internal sealed record BackupJobResponse(Guid Id,string Stage,int? Percent,JsonElement? Backup,string? Error);

internal sealed record RestoreClientRequest(Guid BackupId,string TargetDatabaseName);internal sealed record RestorePreflightClientResponse(Guid BackupId,Guid DatabaseEndpointId,string EndpointName,string SourceDatabaseName,string TargetDatabaseName,string BackupFileName,long? BackupSizeBytes,int VerificationStatus,string ProductVersion,string DataDirectory,string LogDirectory,IReadOnlyList<RestoreFilePlanClientResponse> Files);internal sealed record RestoreFilePlanClientResponse(string LogicalName,string Type,string TargetPath);internal sealed record RestoreExecutionClientResponse(Guid BackupId,Guid DatabaseEndpointId,string TargetDatabaseName,DateTime CompletedAtUtc,double DurationSeconds);

internal sealed record StorageConnectionTestResponse(bool Success,string? Message);
internal sealed record MarkAlertsReadClientRequest(IReadOnlyList<string> Keys);
