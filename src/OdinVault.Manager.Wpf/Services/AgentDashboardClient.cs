using System.Net.Http.Json;
using System.Text.Json;
namespace OdinVault.Manager.Wpf.Services;
internal sealed class AgentDashboardClient : IDisposable
{
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=true};
 readonly HttpClient http=new(){BaseAddress=new Uri("http://127.0.0.1:5188/"),Timeout=TimeSpan.FromSeconds(30)};
 public Task<HealthResponse?> HealthAsync()=>http.GetFromJsonAsync<HealthResponse>("api/health",Json);
 public async Task<DashboardResponse?> DashboardAsync(){
  using var request=new HttpRequestMessage(HttpMethod.Get,"api/dashboard");
  request.Headers.TryAddWithoutValidation("X-OdinVault-Key",LoadApiKey());
  using var response=await http.SendAsync(request); response.EnsureSuccessStatusCode();
  return await response.Content.ReadFromJsonAsync<DashboardResponse>(Json);
 }
 static string LoadApiKey(){var p=Path.Combine(AppContext.BaseDirectory,"data","agent-api-key.txt"); if(!File.Exists(p)) p=Path.Combine(AppContext.BaseDirectory,"..","OdinVault.Agent","data","agent-api-key.txt"); return File.Exists(p)?File.ReadAllText(p).Trim():string.Empty;}
 public void Dispose()=>http.Dispose();
}
internal sealed record HealthResponse(string? Service,string? Status,DateTime Utc);
internal sealed record DashboardResponse(DateTime Utc,string Status,string ProtectionStatus,int EnabledDatabases,int ProtectedDatabases,int ActiveJobs,int FailedJobsLast24Hours,long? StorageFreeBytes,IReadOnlyList<DashboardAttentionResponse> Attention,IReadOnlyList<DashboardActivityResponse> RecentActivity);
internal sealed record DashboardAttentionResponse(string Severity,Guid DatabaseId,string DatabaseName,string Title,string Message,DateTime? OccurredAtUtc);
internal sealed record DashboardActivityResponse(Guid DatabaseId,string DatabaseName,Guid BackupId,int Status,int VerificationStatus,long? SizeBytes,DateTime StartedAtUtc,DateTime? CompletedAtUtc,string? Error);
