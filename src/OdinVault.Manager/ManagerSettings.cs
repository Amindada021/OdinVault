using System.Text.Json;

namespace OdinVault.Manager;

internal sealed record ManagerSettings(
    bool StartMinimizedToTray = false,
    bool MinimizeToTray = true,
    bool ShowTrayNotifications = true,
    bool CheckForUpdatesOnStart = true,
    string Theme = "Light");

internal sealed class ManagerSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string path;

    public ManagerSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OdinVault");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "manager-settings.json");
    }

    public ManagerSettings Load()
    {
        try
        {
            if (!File.Exists(path))
                return new ManagerSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ManagerSettings>(json, JsonOptions)
                   ?? new ManagerSettings();
        }
        catch
        {
            return new ManagerSettings();
        }
    }

    public void Save(ManagerSettings settings)
    {
        var normalizedTheme = settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? "Dark"
            : "Light";

        var normalized = settings with { Theme = normalizedTheme };
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}
