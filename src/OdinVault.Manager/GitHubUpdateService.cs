using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace OdinVault.Manager;

internal sealed class GitHubUpdateService : IDisposable
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Amindada021/OdinVault/releases/latest";
    private readonly HttpClient _httpClient;

    public GitHubUpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("OdinVault-Manager", GetCurrentVersion().ToString()));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public Version GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken);

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            throw new InvalidOperationException("اطلاعات نسخه جدید از GitHub دریافت نشد.");

        if (!TryParseVersion(release.TagName, out var latestVersion))
            throw new InvalidOperationException($"نسخه Release نامعتبر است: {release.TagName}");

        var installer = release.Assets?
            .FirstOrDefault(x => string.Equals(x.Name, "OdinVault-Setup.exe", StringComparison.OrdinalIgnoreCase));

        var currentVersion = GetCurrentVersion();

        return new UpdateCheckResult(
            currentVersion,
            latestVersion,
            latestVersion > currentVersion,
            release.TagName,
            release.Name,
            release.HtmlUrl,
            installer?.BrowserDownloadUrl);
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateCheckResult update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.InstallerDownloadUrl))
            throw new InvalidOperationException("فایل OdinVault-Setup.exe در Release پیدا نشد.");

        var directory = Path.Combine(Path.GetTempPath(), "OdinVault", "Updates");
        Directory.CreateDirectory(directory);

        var safeTag = string.Concat(update.TagName.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var destination = Path.Combine(directory, $"OdinVault-Setup-{safeTag}.exe");

        using var response = await _httpClient.GetAsync(
            update.InstallerDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            useAsync: true);

        var buffer = new byte[1024 * 128];
        long readTotal = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            readTotal += read;

            if (total is > 0)
                progress?.Report((int)Math.Clamp(readTotal * 100 / total.Value, 0, 100));
        }

        return destination;
    }

    public static void LaunchInstallerAfterExit(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("فایل نصب دانلودشده پیدا نشد.", installerPath);

        var command = $"timeout /t 2 /nobreak >nul & start \"\" \"{installerPath}\" /CLOSEAPPLICATIONS";
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);

        Process.Start(startInfo);
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        var dash = value.IndexOf('-');
        if (dash >= 0)
            value = value[..dash];

        return Version.TryParse(value, out version!);
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record GitHubRelease(
        string TagName,
        string? Name,
        string? HtmlUrl,
        List<GitHubReleaseAsset>? Assets);

    private sealed record GitHubReleaseAsset(
        string Name,
        string BrowserDownloadUrl);
}

internal sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    bool IsUpdateAvailable,
    string TagName,
    string? ReleaseName,
    string? ReleaseUrl,
    string? InstallerDownloadUrl);
