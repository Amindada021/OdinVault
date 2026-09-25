using System.ComponentModel;
using System.Globalization;

namespace OdinVault.Manager;

internal sealed class ScheduleEditor : UserControl
{
    private readonly ComboBox _mode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 170
    };

    private readonly DateTimePicker _time1 = CreateTimePicker();
    private readonly DateTimePicker _time2 = CreateTimePicker();
    private readonly TextBox _advancedCron = new() { Width = 220 };
    private readonly Label _summary = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText
    };

    public ScheduleEditor()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        RightToLeft = RightToLeft.Yes;

        _mode.Items.AddRange([
            "دستی",
            "روزانه یک‌بار",
            "روزانه دو بار",
            "پیشرفته (UTC)"
        ]);
        _mode.SelectedIndex = 0;
        _mode.SelectedIndexChanged += (_, _) => RefreshModeUi();

        _time1.ValueChanged += (_, _) => RefreshSummary();
        _time2.ValueChanged += (_, _) => RefreshSummary();
        _advancedCron.TextChanged += (_, _) => RefreshSummary();

        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Top
        };

        row.Controls.Add(_mode);
        row.Controls.Add(_time1);
        row.Controls.Add(_time2);
        row.Controls.Add(_advancedCron);

        var root = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill
        };
        root.Controls.Add(row);
        root.Controls.Add(_summary);

        Controls.Add(root);
        RefreshModeUi();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? ScheduleCron
    {
        get => ShiftCron(BuildCron(), -210);
        set => LoadCron(ShiftCron(value, 210));
    }

    public string DisplayText => FormatCron(ScheduleCron);

    public static string FormatCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
            return "دستی";

        cron = ShiftCron(cron, 210);
        var parts = cron!.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return $"پیشرفته: {cron}";

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute))
            return $"پیشرفته: {cron}";

        if (parts[2] == "*" && parts[3] == "*" && parts[4] == "*")
        {
            var hours = parts[1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ? h : -1)
                .Where(h => h is >= 0 and <= 23)
                .ToArray();

            if (hours.Length == 1)
                return $"هر روز ساعت {hours[0]:00}:{minute:00}";

            if (hours.Length > 1)
                return $"هر روز {string.Join(" و ", hours.Select(h => $"{h:00}:{minute:00}"))}";
        }

        return $"پیشرفته: {cron}";
    }

    private static string? ShiftCron(string? cron, int offset)
    {
        if (string.IsNullOrWhiteSpace(cron)) return cron;
        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[2] != "*" || parts[3] != "*" || parts[4] != "*" ||
            !int.TryParse(parts[0], out var minute) || minute is < 0 or > 59) return cron;
        var hours = parts[1].Split(',');
        if (hours.Length is < 1 or > 2) return cron;
        var shifted = new List<int>();
        foreach (var text in hours)
        {
            if (!int.TryParse(text, out var hour) || hour is < 0 or > 23) return cron;
            shifted.Add((hour * 60 + minute + offset + 1440) % 1440);
        }
        return $"{shifted[0] % 60} {string.Join(",", shifted.Select(x => x / 60).OrderBy(x => x))} * * *";
    }

    private static DateTimePicker CreateTimePicker() => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "HH:mm",
        ShowUpDown = true,
        Width = 90,
        Value = DateTime.Today.AddHours(2)
    };

    private void LoadCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            _mode.SelectedIndex = 0;
            RefreshModeUi();
            return;
        }

        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 5 &&
            int.TryParse(parts[0], out var minute) &&
            parts[2] == "*" &&
            parts[3] == "*" &&
            parts[4] == "*")
        {
            var hours = parts[1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var h) ? h : -1)
                .Where(h => h is >= 0 and <= 23)
                .ToArray();

            if (hours.Length == 1)
            {
                _time1.Value = DateTime.Today.AddHours(hours[0]).AddMinutes(Math.Clamp(minute, 0, 59));
                _mode.SelectedIndex = 1;
                RefreshModeUi();
                return;
            }

            if (hours.Length == 2)
            {
                _time1.Value = DateTime.Today.AddHours(hours[0]).AddMinutes(Math.Clamp(minute, 0, 59));
                _time2.Value = DateTime.Today.AddHours(hours[1]).AddMinutes(Math.Clamp(minute, 0, 59));
                _mode.SelectedIndex = 2;
                RefreshModeUi();
                return;
            }
        }

        _advancedCron.Text = ShiftCron(cron.Trim(), -210);
        _mode.SelectedIndex = 3;
        RefreshModeUi();
    }

    private string? BuildCron()
    {
        return _mode.SelectedIndex switch
        {
            0 => null,
            1 => $"{_time1.Value.Minute} {_time1.Value.Hour} * * *",
            2 => BuildTwiceDailyCron(),
            3 => string.IsNullOrWhiteSpace(_advancedCron.Text) ? null : ShiftCron(_advancedCron.Text.Trim(), 210),
            _ => null
        };
    }

    private string BuildTwiceDailyCron()
    {
        if (_time1.Value.Minute != _time2.Value.Minute)
            throw new InvalidOperationException(
                "برای زمان‌بندی دو بار در روز، دقیقه دو زمان باید یکسان باشد. مثلاً 02:00 و 14:00.");

        var hours = new[] { _time1.Value.Hour, _time2.Value.Hour }
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (hours.Length != 2)
            throw new InvalidOperationException("دو ساعت متفاوت برای زمان‌بندی انتخاب کنید.");

        return $"{_time1.Value.Minute} {string.Join(",", hours)} * * *";
    }

    private void RefreshModeUi()
    {
        _time1.Visible = _mode.SelectedIndex is 1 or 2;
        _time2.Visible = _mode.SelectedIndex == 2;
        _advancedCron.Visible = _mode.SelectedIndex == 3;
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        try
        {
            _summary.Text = $"زمان‌بندی: {FormatCron(ScheduleCron)} — ساعت تهران (پیشرفته: UTC)";
        }
        catch (Exception ex)
        {
            _summary.Text = ex.Message;
        }
    }
}
