using System.Globalization;

namespace OdinVault.Core;

public static class BackupNaming
{
    public static string SafeSegment(string value)
    {
        var safe = string.Concat(value.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(safe)) safe = "database";
        var stem = safe.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) safe = "_" + safe;
        return safe.Length > 100 ? safe[..100] : safe;
    }

    public static string Create(string databaseName)
    {
        var tehran = DateTime.UtcNow.AddMinutes(210);
        var calendar = new PersianCalendar();
        return FormattableString.Invariant($"{SafeSegment(databaseName)}_{calendar.GetYear(tehran):0000}-{calendar.GetMonth(tehran):00}-{calendar.GetDayOfMonth(tehran):00}_{tehran:HH-mm-ss-fff}_{Guid.NewGuid().ToString("N")[..8]}.bak");
    }
}
