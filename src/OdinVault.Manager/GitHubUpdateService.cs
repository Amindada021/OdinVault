using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

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
            .FirstOrDefault(x =>
                x.Name.StartsWith("OdinVault-Setup-v", StringComparison.OrdinalIgnoreCase) &&
                x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        var checksum = release.Assets?
            .FirstOrDefault(x =>
                installer is not null &&
                string.Equals(x.Name, installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));

        var currentVersion = GetCurrentVersion();

        return new UpdateCheckResult(
            currentVersion,
            latestVersion,
            latestVersion > currentVersion,
            release.TagName,
            release.Name,
            release.HtmlUrl,
            installer?.BrowserDownloadUrl,
            checksum?.BrowserDownloadUrl);
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateCheckResult update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.InstallerDownloadUrl))
            throw new InvalidOperationException("فایل نصب OdinVault در Release پیدا نشد.");
        if (string.IsNullOrWhiteSpace(update.ChecksumDownloadUrl))
            throw new InvalidOperationException("فایل SHA256 نسخه جدید در Release پیدا نشد.");

        var directory = Path.Combine(Path.GetTempPath(), "OdinVault", "Updates");
        Directory.CreateDirectory(directory);

        var safeTag = string.Concat(update.TagName.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var destination = Path.Combine(directory, $"OdinVault-Setup-{safeTag}.exe");

        var expectedHashText = await _httpClient.GetStringAsync(update.ChecksumDownloadUrl, cancellationToken);
        var expectedHash = expectedHashText
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(expectedHash) || expectedHash.Length != 64)
            throw new InvalidOperationException("مقدار SHA256 منتشرشده برای نسخه جدید معتبر نیست.");

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

            var percent = total is > 0
                ? (int)Math.Clamp(readTotal * 100 / total.Value, 0, 100)
                : 0;
            progress?.Report(new UpdateDownloadProgress(
                percent,
                readTotal,
                total));
        }

        await output.FlushAsync(cancellationToken);
        output.Close();

        var actualHash = await CalculateSha256Async(destination, cancellationToken);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(destination); } catch { }
            throw new InvalidOperationException(
                "اعتبار فایل بروزرسانی تأیید نشد. SHA256 فایل دانلودشده با مقدار منتشرشده در GitHub برابر نیست.");
        }

        return destination;
    }

    private static async Task<string> CalculateSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static Process LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("فایل نصب دانلودشده پیدا نشد.", installerPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/CLOSEAPPLICATIONS",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory
        };

        var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("اجرای Installer شروع نشد.");

        return process;
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
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] List<GitHubReleaseAsset>? Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}

internal sealed record UpdateDownloadProgress(
    int Percent,
    long BytesReceived,
    long? TotalBytes);

internal sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    bool IsUpdateAvailable,
    string TagName,
    string? ReleaseName,
    string? ReleaseUrl,
    string? InstallerDownloadUrl,
    string? ChecksumDownloadUrl);
