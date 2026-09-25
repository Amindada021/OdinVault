using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;

namespace OdinVault.Manager;

internal sealed class AgentApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly HttpClient _longRunningHttpClient;

    public AgentApiClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5188/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _longRunningHttpClient = new HttpClient
        {
            BaseAddress = _httpClient.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<HealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/health", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions, cancellationToken);
    }

    public async Task<DashboardStatsResponse?> GetDashboardStatsAsync(
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        var normalizedDays = days == 7 ? 7 : 30;
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"api/dashboard/stats?days={normalizedDays}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DashboardStatsResponse>(
            JsonOptions,
            cancellationToken);
    }

    public async Task<DashboardResponse?> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/dashboard");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions, cancellationToken);
    }

    public async Task<BackupReportResponse?> GetBackupReportAsync(
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        var normalizedDays = days switch
        {
            7 => 7,
            90 => 90,
            _ => 30
        };

        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"api/reports/backup?days={normalizedDays}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<BackupReportResponse>(
            JsonOptions,
            cancellationToken);
    }

    public async Task<AlertsOverviewResponse?> GetAlertsAsync(
        bool includeRead = true,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"api/alerts?includeRead={includeRead.ToString().ToLowerInvariant()}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AlertsOverviewResponse>(
            JsonOptions,
            cancellationToken);
    }

    public async Task MarkAlertsReadAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            "api/alerts/mark-read");
        request.Content = JsonContent.Create(
            new MarkAlertsReadClientRequest(keys),
            options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<RestorePreflightClientResponse?> PreflightRestoreAsync(
        Guid backupId,
        string targetDatabaseName,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/restores/preflight");
        request.Content = JsonContent.Create(
            new RestoreClientRequest(backupId, targetDatabaseName),
            options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RestorePreflightClientResponse>(
            JsonOptions,
            cancellationToken);
    }

    public async Task<RestoreExecutionClientResponse?> RestoreToNewDatabaseAsync(
        Guid backupId,
        string targetDatabaseName,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/restores");
        request.Content = JsonContent.Create(
            new RestoreClientRequest(backupId, targetDatabaseName),
            options: JsonOptions);
        using var response = await _longRunningHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RestoreExecutionClientResponse>(
            JsonOptions,
            cancellationToken);
    }

    public async Task<StorageOverviewResponse?> GetStorageOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/storage/overview");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<StorageOverviewResponse>(
            JsonOptions,
            cancellationToken);
    }

    public async Task<StorageConnectionTestResponse?> TestStorageTargetAsync(
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"api/storage-targets/{targetId}/connection-test");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<StorageConnectionTestResponse>(
            JsonOptions,
            cancellationToken);
    }

    public async Task UpdateStorageTargetAsync(
        Guid targetId,
        UpdateStorageTargetClientRequest body,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"api/storage-targets/{targetId}");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<BackupOverviewResponse?> GetBackupOverviewAsync(
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        var normalizedTake = Math.Clamp(take, 20, 500);
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"api/backups/overview?take={normalizedTake}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<BackupOverviewResponse>(
            JsonOptions,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DatabaseResponse>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/databases");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<DatabaseResponse>>(JsonOptions, cancellationToken)
               ?? [];
    }

    public async Task<IReadOnlyList<DatabaseOverviewResponse>> GetDatabaseOverviewsAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/databases/overview");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<DatabaseOverviewResponse>>(
                   JsonOptions,
                   cancellationToken)
               ?? [];
    }

    public async Task<DatabaseDetailsResponse?> GetDatabaseDetailsAsync(
        Guid databaseId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"api/databases/{databaseId}/details");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DatabaseDetailsResponse>(
            JsonOptions,
            cancellationToken);
    }

    public async Task<DatabaseResponse?> GetDatabaseAsync(Guid databaseId, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, $"api/databases/{databaseId}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DatabaseResponse>(JsonOptions, cancellationToken);
    }

    public async Task UpdateDatabaseAsync(Guid databaseId, UpdateDatabaseRequest body, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Put, $"api/databases/{databaseId}");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task UpdateBackupPolicyAsync(Guid databaseId, UpdateBackupPolicyRequest body, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Put, $"api/databases/{databaseId}/policy");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteDatabaseAsync(
        Guid databaseId,
        bool deleteHistory,
        bool deleteFiles,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"api/databases/{databaseId}?deleteHistory={deleteHistory.ToString().ToLowerInvariant()}&deleteFiles={deleteFiles.ToString().ToLowerInvariant()}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<DiscoveredDatabaseResponse>> DiscoverDatabasesAsync(
        DiscoverSqlServerRequest body,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/sql-server/discover");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<DiscoveredDatabaseResponse>>(JsonOptions, cancellationToken)
               ?? [];
    }

    public async Task TestDatabaseAsync(Guid databaseId, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"api/databases/{databaseId}/test");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RunBackupAsync(Guid databaseId, CancellationToken cancellationToken = default, IProgress<string>? progress = null)
    {
        var requestId = Guid.NewGuid();
        var job = await SendAsync<BackupJobResponse>(
            HttpMethod.Post,
            $"api/databases/{databaseId}/backup-jobs",
            null,
            cancellationToken,
            requestId);
        while (true)
        {
            var status = job.Stage switch
            {
                "queued" => "در صف بکاپ",
                "backup" => $"ساخت بکاپ {job.Percent?.ToString() ?? "…"}٪",
                "verify" => "بررسی سلامت فایل",
                "replicating" => "بکاپ آماده است؛ ارسال به پشتیبان",
                _ => "در حال انجام"
            };
            progress?.Report(status);
            if (job.Backup is not null) return;
            if (job.Stage == "failed") throw new InvalidOperationException(job.Error);
            await Task.Delay(1500, cancellationToken);
            job = await SendAsync<BackupJobResponse>(HttpMethod.Get, $"api/backup-jobs/{job.Id}", null, cancellationToken);
        }
    }

    public async Task<T> SendAsync<T>(
        HttpMethod method,
        string url,
        object? body = null,
        CancellationToken ct = default,
        Guid? requestId = null)
    {
        using var request = CreateAuthorizedRequest(method, url);
        if (requestId.HasValue)
            request.Headers.TryAddWithoutValidation("X-OdinVault-Request-Id", requestId.Value.ToString());
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default!;
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    public async Task<DatabaseResponse?> CreateDatabaseAsync(CreateDatabaseRequest body, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/databases");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DatabaseResponse>(JsonOptions, cancellationToken);
    }

    public string GetApiKey() => LoadApiKey();

    public string GetMobileBaseUrl()
    {
        var configured = LoadMobileBaseUrl();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var host = Environment.MachineName;
        return $"http://{host}:5188";
    }

    public void SaveMobileBaseUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("آدرس اتصال موبایل باید یک URL معتبر HTTP یا HTTPS باشد.");

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OdinVault");
        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, "mobile-base-url.txt"),
            value.Trim());
    }

    public async Task<PortProbeResult> TestPortAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Base / Host الزامی است.");
        if (port is < 1 or > 65535)
            throw new InvalidOperationException("Port باید بین 1 تا 65535 باشد.");

        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        var started = DateTime.UtcNow;
        try
        {
            await client.ConnectAsync(host.Trim(), port, timeout.Token);
            var elapsed = DateTime.UtcNow - started;
            return new PortProbeResult(true, elapsed.TotalMilliseconds, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PortProbeResult(false, null, "مهلت اتصال ۵ ثانیه‌ای تمام شد.");
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return new PortProbeResult(false, null, ex.Message);
        }
    }

    public async Task<AgentEndpointProbeResult> TestAgentEndpointAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            throw new InvalidOperationException("آدرس Agent معتبر نیست.");
        }

        using var client = new HttpClient
        {
            BaseAddress = new Uri(uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(8)
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/health");
        request.Headers.TryAddWithoutValidation("X-OdinVault-Key", LoadApiKey());

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new AgentEndpointProbeResult(
                    false,
                    $"Agent پاسخ HTTP {(int)response.StatusCode} برگرداند.");
            }

            var health = await response.Content.ReadFromJsonAsync<HealthResponse>(
                JsonOptions,
                cancellationToken);

            return new AgentEndpointProbeResult(
                health?.Status?.Equals("healthy", StringComparison.OrdinalIgnoreCase) == true,
                health?.Status ?? "Agent پاسخ داد.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AgentEndpointProbeResult(false, "مهلت اتصال به Agent تمام شد.");
        }
        catch (HttpRequestException ex)
        {
            return new AgentEndpointProbeResult(false, ex.Message);
        }
    }

    private static string? LoadMobileBaseUrl()
    {
        var configured = Environment.GetEnvironmentVariable("ODINVAULT_MOBILE_BASE_URL");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().TrimEnd('/');

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OdinVault",
            "mobile-base-url.txt");

        if (!File.Exists(path))
            return null;

        var value = File.ReadAllText(path).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string uri)
    {
        var apiKey = LoadApiKey();
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-OdinVault-Key", apiKey);
        return request;
    }

    private static string LoadApiKey()
    {
        var configured = Environment.GetEnvironmentVariable("ODINVAULT_API_KEY");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OdinVault",
            "agent-api-key.txt");

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"کلید Agent پیدا نشد. ابتدا سرویس OdinVault Agent را اجرا کنید. مسیر مورد انتظار: {path}");

        var key = File.ReadAllText(path).Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("فایل کلید Agent خالی است.");

        return key;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(error?.Message))
                throw new InvalidOperationException(error.Message);
        }
        catch (JsonException)
        {
        }

        throw new InvalidOperationException(
            $"Agent خطای {(int)response.StatusCode} ({response.ReasonPhrase}) برگرداند.{Environment.NewLine}{body}");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _longRunningHttpClient.Dispose();
    }
}

internal sealed record PortProbeResult(
    bool IsOpen,
    double? LatencyMilliseconds,
    string? Error);

internal sealed record AgentEndpointProbeResult(
    bool Success,
    string Message);

internal sealed record HealthResponse(string? Service, string? Status, DateTime Utc);

internal sealed record DashboardStatsResponse(
    int RangeDays,
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<DashboardDailyStatsResponse> Daily,
    IReadOnlyList<DashboardDatabaseSizeResponse> DatabaseSizes);

internal sealed record DashboardDailyStatsResponse(
    DateTime DateUtc,
    int Succeeded,
    int Failed,
    long TotalSizeBytes,
    double? AverageDurationSeconds);

internal sealed record DashboardDatabaseSizeResponse(
    Guid DatabaseId,
    string DatabaseName,
    long? SizeBytes);

internal sealed record DashboardResponse(
    DateTime Utc,
    string Status,
    string ProtectionStatus,
    int EnabledDatabases,
    int ProtectedDatabases,
    int ActiveJobs,
    int FailedJobsLast24Hours,
    long? StorageFreeBytes,
    IReadOnlyList<DashboardAttentionResponse> Attention,
    IReadOnlyList<DashboardActivityResponse> RecentActivity);

internal sealed record DashboardAttentionResponse(
    string Severity,
    Guid DatabaseId,
    string DatabaseName,
    string Title,
    string Message,
    DateTime? OccurredAtUtc);

internal sealed record DashboardActivityResponse(
    Guid DatabaseId,
    string DatabaseName,
    Guid BackupId,
    int Status,
    int VerificationStatus,
    long? SizeBytes,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? Error);

internal sealed record BackupReportResponse(
    int RangeDays,
    DateTime FromUtc,
    DateTime ToUtc,
    BackupReportSummaryResponse Summary,
    IReadOnlyList<BackupReportDailyResponse> Daily,
    IReadOnlyList<BackupReportDatabaseResponse> Databases);

internal sealed record BackupReportSummaryResponse(
    int TotalBackups,
    int Succeeded,
    int Failed,
    int VerifyFailed,
    int ReplicaFailed,
    double? SuccessRate,
    double? AverageDurationSeconds,
    long TotalSuccessfulBytes,
    int EnabledDatabases,
    int ProtectedDatabases,
    long? StorageFreeBytes);

internal sealed record BackupReportDailyResponse(
    DateTime DateUtc,
    int Succeeded,
    int Failed,
    int VerifyFailed,
    long TotalSizeBytes,
    double? AverageDurationSeconds);

internal sealed record BackupReportDatabaseResponse(
    Guid DatabaseId,
    string DatabaseName,
    int TotalBackups,
    int Succeeded,
    int Failed,
    int VerifyFailed,
    int ReplicaFailed,
    double? SuccessRate,
    double? AverageDurationSeconds,
    DateTime? LatestBackupAtUtc,
    int? LatestBackupStatus,
    long? LatestSizeBytes,
    long? PreviousSizeBytes,
    double? SizeGrowthPercent,
    string Severity);

internal sealed record AlertsOverviewResponse(
    DateTime Utc,
    int UnreadCount,
    IReadOnlyList<AlertClientResponse> Alerts);

internal sealed record AlertClientResponse(
    string Key,
    string Severity,
    string Category,
    Guid? DatabaseId,
    string DatabaseName,
    string Title,
    string Message,
    DateTime OccurredAtUtc,
    bool IsRead);

internal sealed record MarkAlertsReadClientRequest(
    IReadOnlyList<string> Keys);

internal sealed record RestoreClientRequest(
    Guid BackupId,
    string TargetDatabaseName);

internal sealed record RestorePreflightClientResponse(
    Guid BackupId,
    Guid DatabaseEndpointId,
    string EndpointName,
    string SourceDatabaseName,
    string TargetDatabaseName,
    string BackupFileName,
    long? BackupSizeBytes,
    int VerificationStatus,
    string ProductVersion,
    string DataDirectory,
    string LogDirectory,
    IReadOnlyList<RestoreFilePlanClientResponse> Files);

internal sealed record RestoreFilePlanClientResponse(
    string LogicalName,
    string Type,
    string TargetPath);

internal sealed record RestoreExecutionClientResponse(
    Guid BackupId,
    Guid DatabaseEndpointId,
    string TargetDatabaseName,
    DateTime CompletedAtUtc,
    double DurationSeconds);

internal sealed record StorageOverviewResponse(
    StorageLocalOverviewResponse Local,
    IReadOnlyList<StorageTargetOverviewResponse> Targets);

internal sealed record StorageLocalOverviewResponse(
    string Name,
    string Directory,
    long? FreeBytes,
    bool Exists,
    bool Writable);

internal sealed record StorageTargetOverviewResponse(
    Guid Id,
    string Name,
    int Type,
    bool IsEnabled,
    string? FolderId,
    string? AccountEmail,
    string? BaseUrl,
    bool IsConnected,
    int LinkedDatabases,
    int SucceededReplicas,
    int FailedReplicas,
    DateTime? LastActivityAtUtc,
    DateTime? LastSuccessAtUtc,
    DateTime? LastFailureAtUtc,
    string? LastError);

internal sealed record StorageConnectionTestResponse(
    bool Success,
    string? Message);

internal sealed record UpdateStorageTargetClientRequest(
    string Name,
    string? FolderId,
    bool IsEnabled,
    string? BaseUrl,
    string? ApiKey);

internal sealed record BackupOverviewResponse(
    DateTime Utc,
    IReadOnlyList<BackupJobOverviewResponse> Jobs,
    IReadOnlyList<BackupHistoryOverviewResponse> Backups);

internal sealed record BackupJobOverviewResponse(
    Guid Id,
    Guid DatabaseEndpointId,
    string DatabaseName,
    int Status,
    string Stage,
    int? Percent,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    Guid? BackupRecordId,
    string? ErrorCode,
    string? ErrorMessage);

internal sealed record BackupHistoryOverviewResponse(
    Guid Id,
    Guid DatabaseEndpointId,
    string DatabaseName,
    int Status,
    int VerificationStatus,
    long? SizeBytes,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    bool LocalFileAvailable,
    string? Error);

internal sealed record DatabaseOverviewResponse(
    Guid Id,
    string Name,
    string Host,
    int? Port,
    string DatabaseName,
    bool IsEnabled,
    bool IsProtected,
    DateTime? LatestBackupAtUtc,
    int? LatestBackupStatus,
    int? LatestVerificationStatus,
    long? LatestBackupSizeBytes);

internal sealed record DatabaseDetailsResponse(
    DatabaseDetailsDatabaseResponse Database,
    DatabaseProtectionResponse Protection,
    IReadOnlyList<DatabaseBackupHistoryResponse> Backups,
    IReadOnlyList<DatabaseReplicaHistoryResponse> Replicas);

internal sealed record DatabaseDetailsDatabaseResponse(
    Guid Id,
    string Name,
    string Host,
    int? Port,
    string DatabaseName,
    bool IsEnabled,
    DatabaseDetailsPolicyResponse? Policy);

internal sealed record DatabaseDetailsPolicyResponse(
    string? ScheduleCron,
    int MaxLocalBackups,
    bool VerifyAfterBackup,
    string BackupDirectory,
    bool IsEnabled);

internal sealed record DatabaseProtectionResponse(
    bool IsProtected,
    DateTime? LatestBackupAtUtc,
    int? LatestBackupStatus,
    int? LatestVerificationStatus,
    long? LatestBackupSizeBytes,
    int LatestReplicaSucceeded,
    int LatestReplicaTotal);

internal sealed record DatabaseBackupHistoryResponse(
    Guid Id,
    int Status,
    int VerificationStatus,
    long? SizeBytes,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    bool LocalFileAvailable,
    string? Error);

internal sealed record DatabaseReplicaHistoryResponse(
    Guid BackupRecordId,
    Guid StorageTargetId,
    string Name,
    int Type,
    int Status,
    long? SizeBytes,
    string? ContentHashSha256,
    string? Error,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc);

internal sealed record DatabaseResponse(
    Guid Id,
    string Name,
    string Host,
    int? Port,
    string DatabaseName,
    string Username,
    bool HasPassword,
    bool TrustServerCertificate,
    bool IsEnabled,
    DateTime CreatedAtUtc,
    BackupPolicyResponse? Policy);

internal sealed record BackupPolicyResponse(
    string BackupDirectory,
    string? ScheduleCron,
    int MaxLocalBackups,
    bool VerifyAfterBackup,
    bool IsEnabled,
    DateTime? LastScheduledRunUtc);

internal sealed record DiscoverSqlServerRequest(
    string Host,
    int? Port,
    string? Username,
    string? Password,
    bool TrustServerCertificate);

internal sealed record DiscoveredDatabaseResponse(
    string Name,
    int DatabaseId,
    string State,
    string RecoveryModel,
    bool IsSystem,
    bool HasAccess,
    bool IsRegistered,
    bool CanBackup);

internal sealed record CreateDatabaseRequest(
    string Name,
    string Host,
    int? Port,
    string DatabaseName,
    string? Username,
    string? Password,
    bool TrustServerCertificate,
    string BackupDirectory,
    int MaxLocalBackups,
    bool VerifyAfterBackup,
    string? ScheduleCron,
    bool IsEnabled);

internal sealed record UpdateDatabaseRequest(
    string Name,
    string Host,
    int? Port,
    string DatabaseName,
    string? Username,
    string? Password,
    bool ClearPassword,
    bool TrustServerCertificate,
    bool IsEnabled);

internal sealed record UpdateBackupPolicyRequest(
    string BackupDirectory,
    int MaxLocalBackups,
    bool VerifyAfterBackup,
    string? ScheduleCron,
    bool IsEnabled);

internal sealed record ErrorResponse(string? Message);

internal sealed record BackupJobResponse(Guid Id, string Stage, int? Percent, JsonElement? Backup, string? Error);
internal sealed record ReplicaSettingsResponse(string Directory);
internal sealed record ReplicaTargetResponse(Guid Id, string Name, int Type, bool IsEnabled);
internal sealed record ReceivedBackupResponse(string FileName, string RelativePath, long SizeBytes, DateTime ReceivedAtUtc);
