using System.ComponentModel;
using System.Globalization;

namespace OdinVault.Manager;

internal sealed class ScheduleEditor : UserControl
{
    private const int TehranOffsetMinutes = 210;

    private readonly ComboBox _mode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 180
    };

    private readonly DateTimePicker _singleTime = CreateTimePicker();
    private readonly FlowLayoutPanel _multiTimesHost = new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Margin = Padding.Empty,
        Padding = Padding.Empty
    };

    private readonly Button _addTime = new()
    {
        Text = "+ افزودن زمان",
        AutoSize = true,
        Height = 34,
        Margin = new Padding(0, 6, 0, 2)
    };

    private readonly TextBox _advancedCron = new()
    {
        Width = 360,
        PlaceholderText = "مثال: 0 2 * * * یا چند Cron با ;"
    };

    private readonly Label _summary = new()
    {
        AutoSize = true,
        MaximumSize = new Size(620, 0),
        ForeColor = Color.FromArgb(100, 116, 139),
        Margin = new Padding(0, 8, 0, 0)
    };

    private readonly List<TimeRow> _multiTimes = [];

    public ScheduleEditor()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        RightToLeft = RightToLeft.Yes;

        _mode.Items.AddRange([
            "دستی",
            "روزانه یک‌بار",
            "روزانه چند زمان",
            "پیشرفته (Cron / UTC)"
        ]);
        _mode.SelectedIndex = 0;
        _mode.SelectedIndexChanged += (_, _) => RefreshModeUi();

        _singleTime.ValueChanged += (_, _) => RefreshSummary();
        _advancedCron.TextChanged += (_, _) => RefreshSummary();
        _addTime.Click += (_, _) => AddMultiTime();

        AddMultiTime(DateTime.Today.AddHours(2), refresh: false);
        AddMultiTime(DateTime.Today.AddHours(14), refresh: false);

        var firstRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        firstRow.Controls.Add(_mode);
        firstRow.Controls.Add(_singleTime);
        firstRow.Controls.Add(_advancedCron);

        var multiPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0),
            Padding = Padding.Empty
        };
        multiPanel.Controls.Add(_multiTimesHost);
        multiPanel.Controls.Add(_addTime);
        multiPanel.Tag = "multi-panel";

        var root = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.Controls.Add(firstRow);
        root.Controls.Add(multiPanel);
        root.Controls.Add(_summary);

        Controls.Add(root);
        RefreshModeUi();
    
        UiLayout.Apply(this);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? ScheduleCron
    {
        get => BuildCron();
        set => LoadCron(value);
    }

    public string DisplayText => FormatCron(ScheduleCron);

    public static string FormatCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
            return "دستی";

        if (TryParseDailyUtcTimes(cron, out var utcTimes))
        {
            var localTimes = utcTimes
                .Select(UtcToTehran)
                .Distinct()
                .OrderBy(x => x.Hour)
                .ThenBy(x => x.Minute)
                .ToArray();

            if (localTimes.Length == 1)
                return $"هر روز ساعت {FormatTime(localTimes[0])}";

            if (localTimes.Length > 1)
                return $"هر روز: {string.Join("، ", localTimes.Select(FormatTime))}";
        }

        return $"پیشرفته: {cron}";
    }

    private void LoadCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            _mode.SelectedIndex = 0;
            RefreshModeUi();
            return;
        }

        if (TryParseDailyUtcTimes(cron, out var utcTimes))
        {
            var localTimes = utcTimes
                .Select(UtcToTehran)
                .Distinct()
                .OrderBy(x => x.Hour)
                .ThenBy(x => x.Minute)
                .ToArray();

            if (localTimes.Length == 1)
            {
                _singleTime.Value = ToPickerValue(localTimes[0]);
                _mode.SelectedIndex = 1;
                RefreshModeUi();
                return;
            }

            if (localTimes.Length > 1)
            {
                ReplaceMultiTimes(localTimes);
                _mode.SelectedIndex = 2;
                RefreshModeUi();
                return;
            }
        }

        _advancedCron.Text = cron.Trim();
        _mode.SelectedIndex = 3;
        RefreshModeUi();
    }

    private string? BuildCron()
    {
        return _mode.SelectedIndex switch
        {
            0 => null,
            1 => BuildDailySchedule([ToTimeOnly(_singleTime.Value)]),
            2 => BuildDailySchedule(GetMultiTimes()),
            3 => NormalizeAdvancedCron(_advancedCron.Text),
            _ => null
        };
    }

    private string BuildDailySchedule(IReadOnlyList<TimeOnly> localTimes)
    {
        if (localTimes.Count == 0)
            throw new InvalidOperationException("حداقل یک زمان برای بکاپ انتخاب کنید.");

        var unique = localTimes
            .Distinct()
            .OrderBy(x => x.Hour)
            .ThenBy(x => x.Minute)
            .ToArray();

        if (unique.Length != localTimes.Count)
            throw new InvalidOperationException("زمان‌های تکراری را حذف کنید.");

        return string.Join(
            ';',
            unique.Select(local =>
            {
                var utc = TehranToUtc(local);
                return $"{utc.Minute} {utc.Hour} * * *";
            }));
    }

    private IReadOnlyList<TimeOnly> GetMultiTimes() =>
        _multiTimes
            .Select(x => ToTimeOnly(x.Picker.Value))
            .ToArray();

    private void AddMultiTime(DateTime? value = null, bool refresh = true)
    {
        var rowPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 2, 0, 2),
            Padding = Padding.Empty
        };

        var picker = CreateTimePicker();
        picker.Value = value ?? GetSuggestedNextTime();
        picker.ValueChanged += (_, _) => RefreshSummary();

        var remove = new Button
        {
            Text = "حذف",
            AutoSize = true,
            Height = 30,
            Margin = new Padding(6, 1, 0, 0),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(185, 28, 28),
            BackColor = Color.White
        };
        remove.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);

        var row = new TimeRow(rowPanel, picker, remove);
        remove.Click += (_, _) => RemoveMultiTime(row);

        rowPanel.Controls.Add(remove);
        rowPanel.Controls.Add(picker);

        _multiTimes.Add(row);
        _multiTimesHost.Controls.Add(rowPanel);

        if (refresh)
            RefreshSummary();
    }

    private void RemoveMultiTime(TimeRow row)
    {
        if (_multiTimes.Count <= 1)
        {
            OdinDialog.Show(
                this,
                "برای حالت چند زمان، حداقل یک زمان باید باقی بماند.",
                "زمان‌بندی",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _multiTimes.Remove(row);
        _multiTimesHost.Controls.Remove(row.Container);
        row.Container.Dispose();
        RefreshSummary();
    }

    private void ReplaceMultiTimes(IReadOnlyList<TimeOnly> times)
    {
        foreach (var row in _multiTimes.ToArray())
        {
            _multiTimesHost.Controls.Remove(row.Container);
            row.Container.Dispose();
        }
        _multiTimes.Clear();

        foreach (var time in times)
            AddMultiTime(ToPickerValue(time), refresh: false);

        if (_multiTimes.Count == 0)
            AddMultiTime(refresh: false);
    }

    private DateTime GetSuggestedNextTime()
    {
        if (_multiTimes.Count == 0)
            return DateTime.Today.AddHours(2);

        var latest = _multiTimes
            .Select(x => x.Picker.Value)
            .OrderBy(x => x.TimeOfDay)
            .Last();

        var minutes = ((int)latest.TimeOfDay.TotalMinutes + 240) % 1440;
        return DateTime.Today.AddMinutes(minutes);
    }

    private void RefreshModeUi()
    {
        _singleTime.Visible = _mode.SelectedIndex == 1;
        _advancedCron.Visible = _mode.SelectedIndex == 3;

        var root = Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        var multiPanel = root?.Controls
            .Cast<Control>()
            .FirstOrDefault(x => Equals(x.Tag, "multi-panel"));

        if (multiPanel is not null)
            multiPanel.Visible = _mode.SelectedIndex == 2;

        RefreshSummary();
    }

    private void RefreshSummary()
    {
        try
        {
            _summary.ForeColor = Color.FromArgb(100, 116, 139);
            _summary.Text = _mode.SelectedIndex == 3
                ? $"زمان‌بندی: {FormatCron(ScheduleCron)} — Cron پیشرفته با UTC"
                : $"زمان‌بندی: {FormatCron(ScheduleCron)} — ساعت تهران";
        }
        catch (Exception ex)
        {
            _summary.ForeColor = Color.FromArgb(185, 28, 28);
            _summary.Text = ex.Message;
        }
    }

    private static string? NormalizeAdvancedCron(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var expressions = text
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return expressions.Length == 0
            ? null
            : string.Join(';', expressions);
    }

    private static bool TryParseDailyUtcTimes(string cron, out IReadOnlyList<TimeOnly> times)
    {
        var result = new List<TimeOnly>();
        var expressions = cron
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (expressions.Length == 0)
        {
            times = [];
            return false;
        }

        foreach (var expression in expressions)
        {
            var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5 ||
                parts[2] != "*" ||
                parts[3] != "*" ||
                parts[4] != "*" ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute) ||
                minute is < 0 or > 59)
            {
                times = [];
                return false;
            }

            var hours = parts[1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (hours.Length == 0)
            {
                times = [];
                return false;
            }

            foreach (var hourText in hours)
            {
                if (!int.TryParse(hourText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) ||
                    hour is < 0 or > 23)
                {
                    times = [];
                    return false;
                }

                result.Add(new TimeOnly(hour, minute));
            }
        }

        times = result;
        return result.Count > 0;
    }

    private static TimeOnly TehranToUtc(TimeOnly local)
    {
        var minutes = (local.Hour * 60 + local.Minute - TehranOffsetMinutes + 1440) % 1440;
        return new TimeOnly(minutes / 60, minutes % 60);
    }

    private static TimeOnly UtcToTehran(TimeOnly utc)
    {
        var minutes = (utc.Hour * 60 + utc.Minute + TehranOffsetMinutes) % 1440;
        return new TimeOnly(minutes / 60, minutes % 60);
    }

    private static TimeOnly ToTimeOnly(DateTime value) => new(value.Hour, value.Minute);

    private static DateTime ToPickerValue(TimeOnly value) =>
        DateTime.Today.AddHours(value.Hour).AddMinutes(value.Minute);

    private static string FormatTime(TimeOnly value) => $"{value.Hour:00}:{value.Minute:00}";

    private static DateTimePicker CreateTimePicker() => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "HH:mm",
        ShowUpDown = true,
        Width = 96,
        Value = DateTime.Today.AddHours(2)
    };

    private sealed record TimeRow(
        FlowLayoutPanel Container,
        DateTimePicker Picker,
        Button RemoveButton);
}
