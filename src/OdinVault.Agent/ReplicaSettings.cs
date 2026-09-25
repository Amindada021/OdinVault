using System.Text.Json;

namespace OdinVault.Agent;

public sealed class ReplicaSettings
{
    private readonly string settingsFile;
    private string directory;
    public ReplicaSettings(string dataDirectory, string defaultDirectory)
    {
        settingsFile = Path.Combine(dataDirectory, "replica-settings.json");
        directory = File.Exists(settingsFile)
            ? JsonSerializer.Deserialize<ReplicaDirectoryRequest>(File.ReadAllText(settingsFile))?.Directory ?? defaultDirectory
            : defaultDirectory;
        System.IO.Directory.CreateDirectory(directory);
    }
    public string Directory => Volatile.Read(ref directory);
    public void Save(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new ArgumentException("یک مسیر کامل برای پوشه دریافت بکاپ انتخاب کنید.");
        value = Path.GetFullPath(value);
        if (!string.Equals(value, Directory, StringComparison.OrdinalIgnoreCase) &&
            System.IO.Directory.EnumerateFiles(Directory, "*.bak", SearchOption.AllDirectories).Any())
            throw new ArgumentException("پوشه فعلی دارای بکاپ است. پیش از تغییر مسیر، فایل‌ها را به پوشه جدید منتقل کنید تا دسترسی به نسخه‌های قبلی حفظ شود.");
        System.IO.Directory.CreateDirectory(value);
        var probe = Path.Combine(value, Guid.NewGuid() + ".tmp");
        File.WriteAllText(probe, "");
        File.Delete(probe);
        File.WriteAllText(settingsFile + ".tmp", JsonSerializer.Serialize(new ReplicaDirectoryRequest(value)));
        File.Move(settingsFile + ".tmp", settingsFile, true);
        Volatile.Write(ref directory, value);
    }
}
public sealed record ReplicaDirectoryRequest(string Directory);
