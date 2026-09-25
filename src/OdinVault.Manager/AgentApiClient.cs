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

    public AgentApiClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5188/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<HealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/health", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<DatabaseResponse>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/databases");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<DatabaseResponse>>(JsonOptions, cancellationToken)
               ?? [];
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

    public void Dispose() => _httpClient.Dispose();
}

internal sealed record HealthResponse(string? Service, string? Status, DateTime Utc);

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
