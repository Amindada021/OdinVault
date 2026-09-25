namespace OdinVault.Manager;

internal sealed class MainForm : Form
{
    private readonly AgentApiClient _api = new();
    private readonly GitHubUpdateService _updates = new();
    private readonly ManagerSettingsStore _settingsStore = new();
    private ManagerSettings _settings;
    private readonly NotifyIcon _trayIcon = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 60_000 };
    private readonly HashSet<string> _knownUnreadAlertKeys = new(StringComparer.Ordinal);
    private bool _alertSnapshotInitialized;
    private bool _refreshAllInProgress;
    private bool _allowExit;
    private readonly Label _agentStatus = new();
    private readonly Label _lastRefresh = new();
    private readonly Label _sidebarAgentStatus = new();
    private readonly DataGridView _grid = new();
    private readonly Button _refreshButton = new();
    private readonly Button _addButton = new();
    private readonly Button _discoverButton = new();
    private readonly Button _editButton = new();
    private readonly Button _deleteButton = new();
    private readonly Button _testButton = new();
    private readonly Button _backupButton = new();
    private readonly Button _detailsButton = new();
    private readonly Button _mobileConnectionButton = new();
    private readonly Button _updateButton = new();
    private readonly Panel _contentHost = new();
    private readonly Label _pageTitle = new();
    private readonly Dictionary<string, Button> _navigationButtons = new(StringComparer.Ordinal);
    private readonly Label _protectedValue = new();
    private readonly Label _failedValue = new();
    private readonly Label _activeJobsValue = new();
    private readonly Label _storageValue = new();
    private readonly FlowLayoutPanel _attentionList = new();
    private readonly FlowLayoutPanel _activityList = new();
    private readonly BackupChartControl _backupSizeChart = new();
    private readonly BackupChartControl _backupStatusChart = new();
    private readonly BackupChartControl _backupDurationChart = new();
    private readonly BackupChartControl _databaseSizeChart = new();
    private readonly ComboBox _chartRange = new();
    private Control? _dashboardPage;
    private Control? _databasesPage;
    private Control? _backupsPage;
    private Control? _storagePage;
    private Control? _restorePage;
    private Control? _alertsPage;
    private Control? _reportsPage;
    private Control? _settingsPage;
    private Guid? _currentDetailsDatabaseId;
    private readonly CheckBox _settingStartMinimized = new();
    private readonly CheckBox _settingMinimizeToTray = new();
    private readonly CheckBox _settingTrayNotifications = new();
    private readonly CheckBox _settingAutoUpdate = new();
    private readonly ComboBox _settingTheme = new();
    private readonly TextBox _settingMobileUrl = new();
    private readonly Label _settingVersion = new();
    private readonly ComboBox _reportRange = new();
    private readonly Label _reportSuccessRate = new();
    private readonly Label _reportFailureCount = new();
    private readonly Label _reportAverageDuration = new();
    private readonly Label _reportProtected = new();
    private readonly BackupChartControl _reportStatusChart = new();
    private readonly BackupChartControl _reportSizeChart = new();
    private readonly DataGridView _reportDatabaseGrid = new();
    private BackupReportResponse? _backupReport;
    private readonly DataGridView _alertsGrid = new();
    private readonly ComboBox _alertReadFilter = new();
    private readonly ComboBox _alertSeverityFilter = new();
    private AlertsOverviewResponse? _alertsOverview;
    private readonly Label _localStoragePath = new();
    private readonly Label _localStorageFree = new();
    private readonly Label _localStorageStatus = new();
    private readonly DataGridView _storageGrid = new();
    private StorageOverviewResponse? _storageOverview;
    private readonly DataGridView _jobsGrid = new();
    private readonly DataGridView _backupHistoryGrid = new();
    private readonly ComboBox _backupDatabaseFilter = new();
    private readonly ComboBox _backupStatusFilter = new();
    private BackupOverviewResponse? _backupOverview;

    public MainForm()
    {
        _settings = _settingsStore.Load();

        Text = "OdinVault Manager";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(1180, 760);
        Size = new Size(1440, 900);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        BuildUi();
        ConfigureTray();
        ApplyTheme();

        _statusTimer.Tick += async (_, _) => await RefreshAllAsync(showErrors: false);
        _statusTimer.Start();

        Shown += async (_, _) =>
        {
            await RefreshAllAsync();

            if (_settings.CheckForUpdatesOnStart)
                await CheckForUpdatesAsync(silent: true);

            if (_settings.StartMinimizedToTray)
                HideToTray(showNotification: false);
        };

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
                HideToTray(showNotification: false);
        };

        FormClosing += (_, e) =>
        {
            if (!_allowExit &&
                e.CloseReason == CloseReason.UserClosing &&
                _settings.MinimizeToTray)
            {
                e.Cancel = true;
                HideToTray(showNotification: true);
            }
        };

        FormClosed += (_, _) =>
        {
            _statusTimer.Stop();
            _statusTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _api.Dispose();
            _updates.Dispose();
        };
    }

    private void BuildUi()
    {
        BackColor = Color.FromArgb(245, 247, 250);

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        Controls.Add(shell);

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(28, 22, 28, 28),
            BackColor = Color.FromArgb(245, 247, 250)
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(main, 0, 0);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        _pageTitle.Text = "داشبورد";
        _pageTitle.AutoSize = false;
        _pageTitle.Dock = DockStyle.Fill;
        _pageTitle.Font = new Font(Font.FontFamily, 20F, FontStyle.Bold);
        _pageTitle.TextAlign = ContentAlignment.BottomRight;

        _agentStatus.Text = "در حال بررسی Agent...";
        _agentStatus.AutoSize = false;
        _agentStatus.Dock = DockStyle.Fill;
        _agentStatus.TextAlign = ContentAlignment.BottomLeft;
        _agentStatus.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);

        _lastRefresh.Text = "";
        _lastRefresh.AutoSize = false;
        _lastRefresh.Dock = DockStyle.Fill;
        _lastRefresh.TextAlign = ContentAlignment.TopLeft;
        _lastRefresh.ForeColor = Color.FromArgb(105, 115, 130);

        var subtitle = new Label
        {
            Text = "مدیریت بکاپ، سلامت و ذخیره‌سازی",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopRight,
            ForeColor = Color.FromArgb(105, 115, 130)
        };

        header.Controls.Add(_pageTitle, 0, 0);
        header.Controls.Add(_agentStatus, 1, 0);
        header.Controls.Add(subtitle, 0, 1);
        header.Controls.Add(_lastRefresh, 1, 1);
        main.Controls.Add(header, 0, 0);

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.Margin = Padding.Empty;
        _contentHost.Padding = Padding.Empty;
        _contentHost.BackColor = Color.FromArgb(245, 247, 250);
        main.Controls.Add(_contentHost, 0, 1);

        var sidebar = BuildSidebar();
        shell.Controls.Add(sidebar, 1, 0);

        ConfigureDatabaseGrid();
        ShowPage("dashboard");
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 18, 14, 14),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(27, 35, 48)
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));

        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        brand.Controls.Add(new Label
        {
            Text = "OdinVault",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomRight,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold)
        }, 0, 0);
        brand.Controls.Add(new Label
        {
            Text = "Backup Management",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopRight,
            ForeColor = Color.FromArgb(150, 165, 185),
            Font = new Font(Font.FontFamily, 9F)
        }, 0, 1);
        sidebar.Controls.Add(brand, 0, 0);

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Margin = new Padding(0, 12, 0, 0),
            Padding = Padding.Empty
        };

        AddNavigationButton(navigation, "dashboard", "▦  داشبورد");
        AddNavigationButton(navigation, "databases", "▤  دیتابیس‌ها");
        AddNavigationButton(navigation, "backups", "◷  بکاپ‌ها");
        AddNavigationButton(navigation, "storage", "▰  ذخیره‌سازی");
        AddNavigationButton(navigation, "restore", "↶  بازیابی");
        AddNavigationButton(navigation, "alerts", "●  هشدارها");
        AddNavigationButton(navigation, "reports", "▥  گزارش‌ها");
        AddNavigationButton(navigation, "settings", "⚙  تنظیمات");
        sidebar.Controls.Add(navigation, 0, 1);

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
            BackColor = Color.FromArgb(34, 44, 60)
        };
        _sidebarAgentStatus.Text = "● Agent در حال بررسی\r\nlocalhost:5188";
        _sidebarAgentStatus.Dock = DockStyle.Fill;
        _sidebarAgentStatus.Padding = new Padding(10, 8, 10, 8);
        _sidebarAgentStatus.TextAlign = ContentAlignment.MiddleRight;
        _sidebarAgentStatus.ForeColor = Color.FromArgb(190, 205, 220);
        _sidebarAgentStatus.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        footer.Controls.Add(_sidebarAgentStatus);
        sidebar.Controls.Add(footer, 0, 2);

        return sidebar;
    }

    private void AddNavigationButton(FlowLayoutPanel host, string key, string text)
    {
        var button = new Button
        {
            Text = text,
            Width = 198,
            Height = 46,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0, 0, 0, 6),
            BackColor = Color.FromArgb(27, 35, 48),
            ForeColor = Color.FromArgb(218, 225, 235),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(41, 53, 71);
        button.Click += (_, _) => ShowPage(key);
        _navigationButtons[key] = button;
        host.Controls.Add(button);
    }

    private void ShowPage(string key)
    {
        foreach (var item in _navigationButtons)
        {
            var active = string.Equals(item.Key, key, StringComparison.Ordinal);
            item.Value.BackColor = active
                ? Color.FromArgb(47, 62, 83)
                : Color.FromArgb(27, 35, 48);
            item.Value.ForeColor = active ? Color.White : Color.FromArgb(218, 225, 235);
        }

        _contentHost.SuspendLayout();
        try
        {
            _contentHost.Controls.Clear();

            switch (key)
            {
                case "databases":
                    _pageTitle.Text = "دیتابیس‌ها";
                    _databasesPage ??= BuildDatabasesPage();
                    _contentHost.Controls.Add(_databasesPage);
                    break;
                case "dashboard":
                    _pageTitle.Text = "داشبورد";
                    _dashboardPage ??= BuildDashboardPage();
                    _contentHost.Controls.Add(_dashboardPage);
                    break;
                case "backups":
                    _pageTitle.Text = "بکاپ‌ها";
                    _backupsPage ??= BuildBackupsPage();
                    _contentHost.Controls.Add(_backupsPage);
                    _ = RefreshBackupsPageAsync();
                    break;
                case "storage":
                    _pageTitle.Text = "ذخیره‌سازی";
                    _storagePage ??= BuildStoragePage();
                    _contentHost.Controls.Add(_storagePage);
                    _ = RefreshStoragePageAsync();
                    break;
                case "restore":
                    _pageTitle.Text = "بازیابی";
                    _restorePage ??= BuildRestorePage();
                    _contentHost.Controls.Add(_restorePage);
                    break;
                case "alerts":
                    _pageTitle.Text = "هشدارها";
                    _alertsPage ??= BuildAlertsPage();
                    _contentHost.Controls.Add(_alertsPage);
                    _ = RefreshAlertsPageAsync();
                    break;
                case "reports":
                    _pageTitle.Text = "گزارش‌ها";
                    _reportsPage ??= BuildReportsPage();
                    _contentHost.Controls.Add(_reportsPage);
                    _ = RefreshReportsPageAsync();
                    break;
                default:
                    _pageTitle.Text = "تنظیمات";
                    _settingsPage ??= BuildSettingsPage();
                    _contentHost.Controls.Add(_settingsPage);
                    LoadSettingsIntoControls();
                    break;
            }
        }
        finally
        {
            ApplyThemeToContent();
            _contentHost.ResumeLayout();
        }
    }

    private Control BuildDashboardPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(245, 247, 250)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 400));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var kpis = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 20),
            Padding = Padding.Empty
        };
        for (var i = 0; i < 4; i++)
            kpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        kpis.Controls.Add(BuildKpiCard("دیتابیس‌های محافظت‌شده", _protectedValue, "از کل دیتابیس‌های فعال"), 0, 0);
        kpis.Controls.Add(BuildKpiCard("خطاهای ۲۴ ساعت اخیر", _failedValue, "Job ناموفق یا متوقف‌شده"), 1, 0);
        kpis.Controls.Add(BuildKpiCard("Jobهای فعال", _activeJobsValue, "در صف یا در حال اجرا"), 2, 0);
        kpis.Controls.Add(BuildKpiCard("فضای آزاد بکاپ", _storageValue, "فضای مقصد محلی Agent"), 3, 0);
        root.Controls.Add(kpis, 0, 0);

        var chartsContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 16),
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(245, 247, 250)
        };
        chartsContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        chartsContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var chartToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(4, 2, 4, 2)
        };
        chartToolbar.Controls.Add(new Label
        {
            Text = "بازه نمودارها",
            AutoSize = true,
            Margin = new Padding(8, 8, 4, 0),
            ForeColor = Color.FromArgb(95, 105, 120)
        });

        _chartRange.DropDownStyle = ComboBoxStyle.DropDownList;
        _chartRange.Width = 110;
        _chartRange.Items.Clear();
        _chartRange.Items.AddRange(["۷ روز اخیر", "۳۰ روز اخیر"]);
        _chartRange.SelectedIndex = 1;
        _chartRange.SelectedIndexChanged += async (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed)
                await RefreshDashboardChartsAsync();
        };
        chartToolbar.Controls.Add(_chartRange);
        chartsContainer.Controls.Add(chartToolbar, 0, 0);

        ConfigureCharts();

        var charts = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        charts.Controls.Add(_backupSizeChart, 0, 0);
        charts.Controls.Add(_backupStatusChart, 1, 0);
        charts.Controls.Add(_backupDurationChart, 0, 1);
        charts.Controls.Add(_databaseSizeChart, 1, 1);
        chartsContainer.Controls.Add(charts, 0, 1);
        root.Controls.Add(chartsContainer, 0, 1);

        var lower = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));

        _attentionList.Dock = DockStyle.Fill;
        _attentionList.FlowDirection = FlowDirection.TopDown;
        _attentionList.WrapContents = false;
        _attentionList.AutoScroll = true;
        _attentionList.Padding = new Padding(10);

        _activityList.Dock = DockStyle.Fill;
        _activityList.FlowDirection = FlowDirection.TopDown;
        _activityList.WrapContents = false;
        _activityList.AutoScroll = true;
        _activityList.Padding = new Padding(10);

        lower.Controls.Add(BuildDashboardSection("نیازمند توجه", _attentionList), 0, 0);
        lower.Controls.Add(BuildDashboardSection("فعالیت‌های اخیر", _activityList), 1, 0);
        root.Controls.Add(lower, 0, 2);

        RenderEmptyList(_attentionList, "در حال دریافت وضعیت...");
        RenderEmptyList(_activityList, "در حال دریافت فعالیت‌ها...");
        return root;
    }

    private void ConfigureCharts()
    {
        _backupSizeChart.ChartTitle = "روند حجم بکاپ‌های موفق";
        _backupSizeChart.Kind = BackupChartKind.Line;
        _backupSizeChart.ValueFormatter = value => FormatBytes((long)value);
        _backupSizeChart.Margin = new Padding(6);

        _backupStatusChart.ChartTitle = "موفق / ناموفق";
        _backupStatusChart.Kind = BackupChartKind.Bar;
        _backupStatusChart.ValueFormatter = value => value.ToString("0");
        _backupStatusChart.Margin = new Padding(6);

        _backupDurationChart.ChartTitle = "میانگین مدت بکاپ";
        _backupDurationChart.Kind = BackupChartKind.Line;
        _backupDurationChart.ValueFormatter = FormatDurationAxis;
        _backupDurationChart.Margin = new Padding(6);

        _databaseSizeChart.ChartTitle = "آخرین حجم بکاپ هر دیتابیس";
        _databaseSizeChart.Kind = BackupChartKind.Bar;
        _databaseSizeChart.ValueFormatter = value => FormatBytes((long)value);
        _databaseSizeChart.Margin = new Padding(6);
    }

    private async Task RefreshDashboardChartsAsync()
    {
        try
        {
            var days = _chartRange.SelectedIndex == 0 ? 7 : 30;
            var stats = await _api.GetDashboardStatsAsync(days);
            if (stats is not null)
                RenderDashboardCharts(stats);
        }
        catch (Exception ex)
        {
            _backupSizeChart.SetData([]);
            _backupStatusChart.SetData([]);
            _backupDurationChart.SetData([]);
            _databaseSizeChart.SetData([]);
            _agentStatus.Text = $"● نمودارها بروزرسانی نشدند: {ex.Message}";
        }
    }

    private void RenderDashboardCharts(DashboardStatsResponse stats)
    {
        var dateLabels = stats.Daily
            .Select(x => FormatChartDate(x.DateUtc))
            .ToArray();

        _backupSizeChart.SetData(
            dateLabels,
            new BackupChartSeries(
                "حجم",
                stats.Daily
                    .Select(x => (double?)x.TotalSizeBytes)
                    .ToArray()));

        _backupStatusChart.SetData(
            dateLabels,
            new BackupChartSeries(
                "موفق",
                stats.Daily.Select(x => (double?)x.Succeeded).ToArray()),
            new BackupChartSeries(
                "ناموفق",
                stats.Daily.Select(x => (double?)x.Failed).ToArray()));

        _backupDurationChart.SetData(
            dateLabels,
            new BackupChartSeries(
                "مدت",
                stats.Daily.Select(x => x.AverageDurationSeconds).ToArray()));

        var databaseSizes = stats.DatabaseSizes
            .Where(x => x.SizeBytes.HasValue)
            .Take(10)
            .ToArray();

        _databaseSizeChart.SetData(
            databaseSizes.Select(x => ShortenLabel(x.DatabaseName, 15)).ToArray(),
            new BackupChartSeries(
                "حجم",
                databaseSizes.Select(x => (double?)x.SizeBytes!.Value).ToArray()));
    }

    private static string FormatChartDate(DateTime utc)
    {
        var local = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;
        var calendar = new System.Globalization.PersianCalendar();
        return $"{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00}";
    }

    private static string FormatDurationAxis(double seconds)
    {
        if (seconds < 60)
            return $"{seconds:0}s";
        if (seconds < 3600)
            return $"{seconds / 60:0.#}m";
        return $"{seconds / 3600:0.#}h";
    }

    private static string ShortenLabel(string value, int maxLength) =>
        value.Length <= maxLength
            ? value
            : value[..Math.Max(1, maxLength - 1)] + "…";


    private Control BuildKpiCard(string title, Label value, string subtitle)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(8),
            Padding = new Padding(20, 16, 20, 16),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(95, 105, 120),
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold)
        }, 0, 0);

        value.Text = "—";
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleRight;
        value.Font = new Font(Font.FontFamily, 27F, FontStyle.Bold);
        value.ForeColor = Color.FromArgb(35, 45, 60);
        card.Controls.Add(value, 0, 1);

        card.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(130, 140, 155),
            Font = new Font(Font.FontFamily, 8.5F)
        }, 0, 2);

        return card;
    }

    private Control BuildDashboardSection(string title, Control content)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(6),
            Padding = new Padding(1),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 0, 14, 0),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(45, 55, 70)
        }, 0, 0);

        card.Controls.Add(content, 0, 1);
        return card;
    }

    private void RenderDashboard(DashboardResponse dashboard)
    {
        _protectedValue.Text = $"{dashboard.ProtectedDatabases} / {dashboard.EnabledDatabases}";
        _failedValue.Text = dashboard.FailedJobsLast24Hours.ToString();
        _activeJobsValue.Text = dashboard.ActiveJobs.ToString();
        _storageValue.Text = FormatBytes(dashboard.StorageFreeBytes);

        _attentionList.SuspendLayout();
        _attentionList.Controls.Clear();
        if (dashboard.Attention.Count == 0)
        {
            RenderEmptyList(_attentionList, "مورد مهمی نیازمند توجه نیست.");
        }
        else
        {
            foreach (var item in dashboard.Attention)
                _attentionList.Controls.Add(BuildAttentionRow(item));
        }
        _attentionList.ResumeLayout();

        _activityList.SuspendLayout();
        _activityList.Controls.Clear();
        if (dashboard.RecentActivity.Count == 0)
        {
            RenderEmptyList(_activityList, "هنوز سابقه بکاپی ثبت نشده است.");
        }
        else
        {
            foreach (var item in dashboard.RecentActivity)
                _activityList.Controls.Add(BuildActivityRow(item));
        }
        _activityList.ResumeLayout();
    }

    private Control BuildAttentionRow(DashboardAttentionResponse item)
    {
        var critical = string.Equals(item.Severity, "critical", StringComparison.OrdinalIgnoreCase);
        var panel = new Panel
        {
            Width = Math.Max(320, _attentionList.ClientSize.Width - 32),
            Height = 82,
            Margin = new Padding(2, 2, 2, 8),
            Padding = new Padding(12),
            BackColor = critical
                ? Color.FromArgb(255, 244, 244)
                : Color.FromArgb(255, 249, 235),
            BorderStyle = BorderStyle.FixedSingle
        };

        panel.Controls.Add(new Label
        {
            Text = $"{(critical ? "●" : "▲")} {item.DatabaseName} — {item.Title}\r\n{item.Message}",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = critical ? Color.FromArgb(150, 45, 45) : Color.FromArgb(140, 95, 25)
        });
        return panel;
    }

    private Control BuildActivityRow(DashboardActivityResponse item)
    {
        var succeeded = item.Status == 2;
        var verifyFailed = item.VerificationStatus == 3;
        var statusText = succeeded
            ? verifyFailed ? "موفق؛ Verify ناموفق" : "موفق"
            : item.Status == 3 ? "ناموفق" : "در حال انجام";

        var panel = new Panel
        {
            Width = Math.Max(300, _activityList.ClientSize.Width - 32),
            Height = 70,
            Margin = new Padding(2, 2, 2, 8),
            Padding = new Padding(12),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        panel.Controls.Add(new Label
        {
            Text = $"{item.DatabaseName}   •   {statusText}\r\n{FormatBytes(item.SizeBytes)}   •   {FormatDashboardTime(item.CompletedAtUtc ?? item.StartedAtUtc)}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(55, 65, 80)
        });
        return panel;
    }

    private static void RenderEmptyList(Control host, string text)
    {
        host.Controls.Add(new Label
        {
            Text = text,
            Width = 420,
            Height = 56,
            Margin = new Padding(6),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(125, 135, 150)
        });
    }

    private static string FormatBytes(long? value)
    {
        if (value is null)
            return "نامشخص";

        var bytes = value.Value;
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):0.0} MB";
        return $"{bytes / (1024d * 1024 * 1024):0.00} GB";
    }

    private static string FormatDashboardTime(DateTime utc)
    {
        var local = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;
        return local.ToString("yyyy/MM/dd HH:mm");
    }

    private Control BuildBackupsPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(245, 247, 250)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(4)
        };

        var refresh = new Button
        {
            Text = "بروزرسانی",
            AutoSize = true,
            Height = 36
        };
        refresh.Click += async (_, _) => await RefreshBackupsPageAsync();

        _backupDatabaseFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _backupDatabaseFilter.Width = 190;
        _backupDatabaseFilter.SelectedIndexChanged += (_, _) => ApplyBackupsFilters();

        _backupStatusFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _backupStatusFilter.Width = 150;
        _backupStatusFilter.Items.AddRange(
        [
            "همه وضعیت‌ها",
            "در صف",
            "در حال اجرا",
            "موفق",
            "ناموفق",
            "متوقف‌شده"
        ]);
        _backupStatusFilter.SelectedIndex = 0;
        _backupStatusFilter.SelectedIndexChanged += (_, _) => ApplyBackupsFilters();

        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(_backupStatusFilter);
        toolbar.Controls.Add(new Label
        {
            Text = "وضعیت",
            AutoSize = true,
            Margin = new Padding(8, 9, 3, 0)
        });
        toolbar.Controls.Add(_backupDatabaseFilter);
        toolbar.Controls.Add(new Label
        {
            Text = "دیتابیس",
            AutoSize = true,
            Margin = new Padding(8, 9, 3, 0)
        });
        root.Controls.Add(toolbar, 0, 0);

        ConfigureJobsGrid();
        ConfigureBackupHistoryGrid();

        root.Controls.Add(BuildBackupsSection("Jobها", _jobsGrid), 0, 1);
        root.Controls.Add(BuildBackupsSection("تاریخچه بکاپ", _backupHistoryGrid), 0, 2);

        return root;
    }

    private Control BuildBackupsSection(string title, Control content)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 6, 0, 6),
            Padding = new Padding(1),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 0, 14, 0),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(45, 55, 70)
        }, 0, 0);
        card.Controls.Add(content, 0, 1);
        return card;
    }

    private void ConfigureJobsGrid()
    {
        ConfigureReadOnlyGrid(_jobsGrid);
        _jobsGrid.Columns.Clear();
        _jobsGrid.Columns.Add("Database", "دیتابیس");
        _jobsGrid.Columns.Add("Status", "وضعیت");
        _jobsGrid.Columns.Add("Stage", "مرحله");
        _jobsGrid.Columns.Add("Progress", "پیشرفت");
        _jobsGrid.Columns.Add("Started", "شروع");
        _jobsGrid.Columns.Add("Updated", "آخرین تغییر");
        _jobsGrid.Columns.Add("Error", "خطا");
    }

    private void ConfigureBackupHistoryGrid()
    {
        ConfigureReadOnlyGrid(_backupHistoryGrid);
        _backupHistoryGrid.Columns.Clear();
        _backupHistoryGrid.Columns.Add("Database", "دیتابیس");
        _backupHistoryGrid.Columns.Add("Date", "زمان");
        _backupHistoryGrid.Columns.Add("Status", "وضعیت");
        _backupHistoryGrid.Columns.Add("Verify", "Verify");
        _backupHistoryGrid.Columns.Add("Size", "حجم");
        _backupHistoryGrid.Columns.Add("Duration", "مدت");
        _backupHistoryGrid.Columns.Add("Local", "فایل Local");
        _backupHistoryGrid.Columns.Add("Error", "خطا");
    }

    private static void ConfigureReadOnlyGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 46;
        grid.RowTemplate.Height = 40;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 244, 248);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 65, 80);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 235, 246);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 40, 55);
        grid.GridColor = Color.FromArgb(228, 233, 240);
    }

    private async Task RefreshBackupsPageAsync()
    {
        try
        {
            _backupOverview = await _api.GetBackupOverviewAsync(300);
            PopulateBackupDatabaseFilter();
            ApplyBackupsFilters();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void PopulateBackupDatabaseFilter()
    {
        if (_backupOverview is null)
            return;

        var previous = _backupDatabaseFilter.SelectedItem as BackupDatabaseFilterItem;
        var previousId = previous?.Id;

        var databases = _backupOverview.Jobs
            .Select(x => new BackupDatabaseFilterItem(x.DatabaseEndpointId, x.DatabaseName))
            .Concat(_backupOverview.Backups.Select(x =>
                new BackupDatabaseFilterItem(x.DatabaseEndpointId, x.DatabaseName)))
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .OrderBy(x => x.Name)
            .ToList();

        _backupDatabaseFilter.BeginUpdate();
        _backupDatabaseFilter.Items.Clear();
        _backupDatabaseFilter.Items.Add(new BackupDatabaseFilterItem(null, "همه دیتابیس‌ها"));
        foreach (var item in databases)
            _backupDatabaseFilter.Items.Add(item);

        var selectedIndex = 0;
        if (previousId.HasValue)
        {
            for (var i = 0; i < _backupDatabaseFilter.Items.Count; i++)
            {
                if (_backupDatabaseFilter.Items[i] is BackupDatabaseFilterItem item &&
                    item.Id == previousId)
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        _backupDatabaseFilter.SelectedIndex = selectedIndex;
        _backupDatabaseFilter.EndUpdate();
    }

    private void ApplyBackupsFilters()
    {
        if (_backupOverview is null)
            return;

        var databaseId = (_backupDatabaseFilter.SelectedItem as BackupDatabaseFilterItem)?.Id;
        var statusFilter = _backupStatusFilter.SelectedIndex - 1;

        var jobs = _backupOverview.Jobs
            .Where(x => !databaseId.HasValue || x.DatabaseEndpointId == databaseId.Value)
            .Where(x => statusFilter < 0 || x.Status == statusFilter)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();

        _jobsGrid.Rows.Clear();
        foreach (var job in jobs)
        {
            var rowIndex = _jobsGrid.Rows.Add(
                job.DatabaseName,
                FormatJobStatus(job.Status),
                FormatJobStage(job.Stage),
                job.Percent.HasValue ? $"{job.Percent.Value}٪" : "—",
                FormatDashboardTime(job.StartedAtUtc ?? job.CreatedAtUtc),
                FormatDashboardTime(job.UpdatedAtUtc),
                job.ErrorMessage ?? "—");

            var row = _jobsGrid.Rows[rowIndex];
            row.Tag = job.Id;
            if (job.Status is 3 or 4)
                row.DefaultCellStyle.ForeColor = Color.FromArgb(155, 55, 55);
            else if (job.Status == 1)
                row.DefaultCellStyle.ForeColor = Color.FromArgb(45, 95, 155);
        }

        var backups = _backupOverview.Backups
            .Where(x => !databaseId.HasValue || x.DatabaseEndpointId == databaseId.Value)
            .Where(x => statusFilter < 0 ||
                        statusFilter == 2 && x.Status == 2 ||
                        statusFilter == 3 && x.Status == 3)
            .OrderByDescending(x => x.StartedAtUtc)
            .ToList();

        _backupHistoryGrid.Rows.Clear();
        foreach (var backup in backups)
        {
            var duration = backup.CompletedAtUtc.HasValue
                ? FormatDurationAxis(Math.Max(
                    0,
                    (backup.CompletedAtUtc.Value - backup.StartedAtUtc).TotalSeconds))
                : "—";

            var rowIndex = _backupHistoryGrid.Rows.Add(
                backup.DatabaseName,
                FormatDashboardTime(backup.CompletedAtUtc ?? backup.StartedAtUtc),
                FormatBackupStatus(backup.Status),
                FormatVerificationStatus(backup.VerificationStatus),
                FormatBytes(backup.SizeBytes),
                duration,
                backup.LocalFileAvailable ? "موجود" : "حذف‌شده",
                backup.Error ?? "—");

            var row = _backupHistoryGrid.Rows[rowIndex];
            row.Tag = backup.Id;
            if (backup.Status == 3 || backup.VerificationStatus == 3)
                row.DefaultCellStyle.ForeColor = Color.FromArgb(155, 55, 55);
        }
    }

    private static string FormatJobStatus(int status) => status switch
    {
        0 => "در صف",
        1 => "در حال اجرا",
        2 => "موفق",
        3 => "ناموفق",
        4 => "متوقف‌شده",
        _ => "نامشخص"
    };

    private static string FormatJobStage(string? stage) => stage?.ToLowerInvariant() switch
    {
        "queued" => "در صف",
        "backup" => "ساخت بکاپ",
        "verify" => "بررسی سلامت",
        "replicating" => "ارسال Replica",
        "retention" => "پاکسازی نگهداری",
        "complete" => "کامل",
        "failed" => "ناموفق",
        "interrupted" => "متوقف‌شده",
        _ => string.IsNullOrWhiteSpace(stage) ? "—" : stage
    };

    private sealed record BackupDatabaseFilterItem(Guid? Id, string Name)
    {
        public override string ToString() => Name;
    }

    private Control BuildStoragePage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(245, 247, 250)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var local = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 16),
            Padding = new Padding(16),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        local.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        local.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        local.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        _localStoragePath.Dock = DockStyle.Fill;
        _localStoragePath.TextAlign = ContentAlignment.MiddleRight;
        _localStoragePath.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
        _localStoragePath.RightToLeft = RightToLeft.No;

        _localStorageFree.Dock = DockStyle.Fill;
        _localStorageFree.TextAlign = ContentAlignment.MiddleRight;
        _localStorageFree.Font = new Font(Font.FontFamily, 17F, FontStyle.Bold);

        _localStorageStatus.Dock = DockStyle.Fill;
        _localStorageStatus.TextAlign = ContentAlignment.MiddleRight;

        local.Controls.Add(WrapStorageMetric("مسیر Local", _localStoragePath), 0, 0);
        local.Controls.Add(WrapStorageMetric("فضای آزاد", _localStorageFree), 1, 0);
        local.Controls.Add(WrapStorageMetric("وضعیت", _localStorageStatus), 2, 0);
        root.Controls.Add(local, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(4)
        };

        var refresh = new Button { Text = "بروزرسانی", AutoSize = true, Height = 42, Padding = new Padding(12, 3, 12, 3) };
        refresh.Click += async (_, _) => await RefreshStoragePageAsync();

        var test = new Button { Text = "تست اتصال", AutoSize = true, Height = 36 };
        test.Click += async (_, _) => await TestSelectedStorageAsync();

        var edit = new Button { Text = "ویرایش", AutoSize = true, Height = 36 };
        edit.Click += async (_, _) => await EditSelectedStorageAsync();

        var toggle = new Button { Text = "فعال / غیرفعال", AutoSize = true, Height = 36 };
        toggle.Click += async (_, _) => await ToggleSelectedStorageAsync();

        var replicaSetup = new Button { Text = "مدیریت Replica", AutoSize = true, Height = 36 };
        replicaSetup.Click += (_, _) =>
        {
            using var dialog = new ReplicaSetupForm(_api);
            dialog.ShowDialog(this);
            _ = RefreshStoragePageAsync();
        };

        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(test);
        toolbar.Controls.Add(edit);
        toolbar.Controls.Add(toggle);
        toolbar.Controls.Add(replicaSetup);
        root.Controls.Add(toolbar, 0, 1);

        ConfigureReadOnlyGrid(_storageGrid);
        _storageGrid.Columns.Clear();
        _storageGrid.Columns.Add("Name", "نام");
        _storageGrid.Columns.Add("Type", "نوع");
        _storageGrid.Columns.Add("Status", "وضعیت");
        _storageGrid.Columns.Add("Endpoint", "مسیر / حساب / مقصد");
        _storageGrid.Columns.Add("Databases", "دیتابیس‌ها");
        _storageGrid.Columns.Add("Success", "Replica موفق");
        _storageGrid.Columns.Add("Failed", "Replica ناموفق");
        _storageGrid.Columns.Add("LastSuccess", "آخرین موفقیت");
        _storageGrid.Columns.Add("LastError", "آخرین خطا");
        root.Controls.Add(BuildBackupsSection("مقصدهای ذخیره‌سازی", _storageGrid), 0, 2);

        return root;
    }

    private Control WrapStorageMetric(string title, Control value)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(110, 120, 135)
        }, 0, 0);
        panel.Controls.Add(value, 0, 1);
        return panel;
    }

    private async Task RefreshStoragePageAsync()
    {
        try
        {
            _storageOverview = await _api.GetStorageOverviewAsync();
            if (_storageOverview is null)
                return;

            _localStoragePath.Text = _storageOverview.Local.Directory;
            _localStorageFree.Text = FormatBytes(_storageOverview.Local.FreeBytes);
            _localStorageStatus.Text = _storageOverview.Local.Exists && _storageOverview.Local.Writable
                ? "● سالم و قابل نوشتن"
                : _storageOverview.Local.Exists
                    ? "▲ مسیر وجود دارد ولی قابل نوشتن نیست"
                    : "● مسیر در دسترس نیست";

            _localStorageStatus.ForeColor =
                _storageOverview.Local.Exists && _storageOverview.Local.Writable
                    ? Color.FromArgb(38, 130, 86)
                    : Color.FromArgb(160, 65, 55);

            _storageGrid.Rows.Clear();
            foreach (var target in _storageOverview.Targets)
            {
                var endpoint = target.Type switch
                {
                    2 => target.AccountEmail ?? target.FolderId ?? "Google Drive",
                    5 => target.BaseUrl ?? "OdinVault Replica",
                    _ => target.FolderId ?? "—"
                };

                var status = !target.IsEnabled
                    ? "غیرفعال"
                    : target.IsConnected
                        ? "متصل"
                        : "نیاز به اتصال";

                var rowIndex = _storageGrid.Rows.Add(
                    target.Name,
                    FormatStorageType(target.Type),
                    status,
                    endpoint,
                    target.LinkedDatabases,
                    target.SucceededReplicas,
                    target.FailedReplicas,
                    target.LastSuccessAtUtc is DateTime success
                        ? FormatDashboardTime(success)
                        : "—",
                    target.LastError ?? "—");

                var row = _storageGrid.Rows[rowIndex];
                row.Tag = target.Id;
                if (!target.IsEnabled)
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(120, 125, 135);
                else if (!target.IsConnected || !string.IsNullOrWhiteSpace(target.LastError))
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(155, 70, 55);
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private StorageTargetOverviewResponse? GetSelectedStorageTarget()
    {
        if (_storageOverview is null ||
            _storageGrid.CurrentRow?.Tag is not Guid id)
            return null;

        return _storageOverview.Targets.FirstOrDefault(x => x.Id == id);
    }

    private async Task TestSelectedStorageAsync()
    {
        var target = GetSelectedStorageTarget();
        if (target is null)
        {
            MessageBox.Show(this, "ابتدا یک مقصد را انتخاب کنید.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetBusy(true);
            var result = await _api.TestStorageTargetAsync(target.Id);
            MessageBox.Show(
                this,
                result?.Message ?? (result?.Success == true ? "اتصال موفق بود." : "تست اتصال ناموفق بود."),
                "تست اتصال",
                MessageBoxButtons.OK,
                result?.Success == true ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            await RefreshStoragePageAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task EditSelectedStorageAsync()
    {
        var target = GetSelectedStorageTarget();
        if (target is null)
        {
            MessageBox.Show(this, "ابتدا یک مقصد را انتخاب کنید.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new StorageTargetEditForm(target);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            SetBusy(true);
            await _api.UpdateStorageTargetAsync(target.Id, dialog.Request);
            await RefreshStoragePageAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ToggleSelectedStorageAsync()
    {
        var target = GetSelectedStorageTarget();
        if (target is null)
        {
            MessageBox.Show(this, "ابتدا یک مقصد را انتخاب کنید.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var request = new UpdateStorageTargetClientRequest(
            target.Name,
            target.FolderId,
            !target.IsEnabled,
            target.BaseUrl,
            null);

        try
        {
            SetBusy(true);
            await _api.UpdateStorageTargetAsync(target.Id, request);
            await RefreshStoragePageAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string FormatStorageType(int type) => type switch
    {
        1 => "Local",
        2 => "Google Drive",
        3 => "S3",
        4 => "SFTP",
        5 => "OdinVault Replica",
        _ => "نامشخص"
    };

    private Control BuildRestorePage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

        root.Controls.Add(new Label
        {
            Text = "Restore امن به دیتابیس جدید",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(42, 52, 68)
        }, 0, 0);

        root.Controls.Add(new Label
        {
            Text =
                "در این نسخه Restore فقط از بکاپ موفقی که فایل Local آن روی Agent موجود است انجام می‌شود.\r\n\r\n" +
                "• دیتابیس مقصد باید نام جدید داشته باشد.\r\n" +
                "• هیچ دیتابیس موجودی overwrite نمی‌شود.\r\n" +
                "• قبل از اجرا RESTORE VERIFYONLY و FILELISTONLY انجام می‌شود.\r\n" +
                "• مسیر MDF/LDF از مسیرهای پیش‌فرض خود SQL Server ساخته می‌شود.\r\n" +
                "• اگر Backup یا Restore دیگری روی همان endpoint در حال اجرا باشد، عملیات شروع نمی‌شود.",
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            TextAlign = ContentAlignment.TopRight,
            ForeColor = Color.FromArgb(75, 85, 100),
            Font = new Font(Font.FontFamily, 11F)
        }, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };

        var start = new Button
        {
            Text = "شروع Restore Wizard",
            AutoSize = true,
            Height = 40,
            Padding = new Padding(14, 2, 14, 2)
        };
        start.Click += (_, _) =>
        {
            using var wizard = new RestoreWizardForm(_api);
            wizard.ShowDialog(this);
        };
        actions.Controls.Add(start);
        root.Controls.Add(actions, 0, 2);

        return root;
    }

    private Control BuildAlertsPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(245, 247, 250)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(4)
        };

        var refresh = new Button
        {
            Text = "بروزرسانی",
            AutoSize = true,
            Height = 36
        };
        refresh.Click += async (_, _) => await RefreshAlertsPageAsync();

        var markSelected = new Button
        {
            Text = "خوانده شد",
            AutoSize = true,
            Height = 36
        };
        markSelected.Click += async (_, _) => await MarkSelectedAlertReadAsync();

        var markAll = new Button
        {
            Text = "خواندن همه",
            AutoSize = true,
            Height = 36
        };
        markAll.Click += async (_, _) => await MarkAllAlertsReadAsync();

        _alertReadFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _alertReadFilter.Width = 145;
        _alertReadFilter.Items.AddRange(["همه", "خوانده‌نشده", "خوانده‌شده"]);
        _alertReadFilter.SelectedIndex = 0;
        _alertReadFilter.SelectedIndexChanged += (_, _) => ApplyAlertFilters();

        _alertSeverityFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _alertSeverityFilter.Width = 140;
        _alertSeverityFilter.Items.AddRange(["همه شدت‌ها", "بحرانی", "هشدار"]);
        _alertSeverityFilter.SelectedIndex = 0;
        _alertSeverityFilter.SelectedIndexChanged += (_, _) => ApplyAlertFilters();

        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(markSelected);
        toolbar.Controls.Add(markAll);
        toolbar.Controls.Add(_alertReadFilter);
        toolbar.Controls.Add(new Label
        {
            Text = "وضعیت",
            AutoSize = true,
            Margin = new Padding(8, 9, 3, 0)
        });
        toolbar.Controls.Add(_alertSeverityFilter);
        toolbar.Controls.Add(new Label
        {
            Text = "شدت",
            AutoSize = true,
            Margin = new Padding(8, 9, 3, 0)
        });
        root.Controls.Add(toolbar, 0, 0);

        ConfigureReadOnlyGrid(_alertsGrid);
        _alertsGrid.Columns.Clear();
        _alertsGrid.Columns.Add("Read", "وضعیت");
        _alertsGrid.Columns.Add("Severity", "شدت");
        _alertsGrid.Columns.Add("Category", "نوع");
        _alertsGrid.Columns.Add("Database", "دیتابیس / منبع");
        _alertsGrid.Columns.Add("Title", "عنوان");
        _alertsGrid.Columns.Add("Message", "جزئیات");
        _alertsGrid.Columns.Add("Occurred", "زمان");
        _alertsGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
                await OpenAlertAsync(_alertsGrid.Rows[e.RowIndex]);
        };

        root.Controls.Add(BuildBackupsSection("Notification Center", _alertsGrid), 0, 1);
        return root;
    }

    private async Task RefreshAlertsPageAsync()
    {
        try
        {
            _alertsOverview = await _api.GetAlertsAsync(includeRead: true);
            UpdateAlertNavigationBadge(_alertsOverview?.UnreadCount ?? 0);
            ApplyAlertFilters();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ApplyAlertFilters()
    {
        if (_alertsOverview is null)
            return;

        IEnumerable<AlertClientResponse> alerts = _alertsOverview.Alerts;

        alerts = _alertReadFilter.SelectedIndex switch
        {
            1 => alerts.Where(x => !x.IsRead),
            2 => alerts.Where(x => x.IsRead),
            _ => alerts
        };

        alerts = _alertSeverityFilter.SelectedIndex switch
        {
            1 => alerts.Where(x => string.Equals(x.Severity, "critical", StringComparison.OrdinalIgnoreCase)),
            2 => alerts.Where(x => string.Equals(x.Severity, "warning", StringComparison.OrdinalIgnoreCase)),
            _ => alerts
        };

        _alertsGrid.Rows.Clear();
        foreach (var alert in alerts
                     .OrderBy(x => x.IsRead)
                     .ThenBy(x => x.Severity == "critical" ? 0 : 1)
                     .ThenByDescending(x => x.OccurredAtUtc))
        {
            var rowIndex = _alertsGrid.Rows.Add(
                alert.IsRead ? "خوانده‌شده" : "جدید",
                FormatAlertSeverity(alert.Severity),
                FormatAlertCategory(alert.Category),
                alert.DatabaseName,
                alert.Title,
                alert.Message,
                FormatDashboardTime(alert.OccurredAtUtc));

            var row = _alertsGrid.Rows[rowIndex];
            row.Tag = alert;

            if (!alert.IsRead)
                row.DefaultCellStyle.Font = new Font(_alertsGrid.Font, FontStyle.Bold);

            row.DefaultCellStyle.ForeColor = alert.Severity == "critical"
                ? Color.FromArgb(155, 50, 50)
                : Color.FromArgb(145, 95, 25);
        }
    }

    private async Task MarkSelectedAlertReadAsync()
    {
        if (_alertsGrid.CurrentRow?.Tag is not AlertClientResponse alert)
        {
            MessageBox.Show(
                this,
                "ابتدا یک هشدار را انتخاب کنید.",
                "OdinVault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!alert.IsRead)
            await _api.MarkAlertsReadAsync([alert.Key]);

        await RefreshAlertsPageAsync();
    }

    private async Task MarkAllAlertsReadAsync()
    {
        if (_alertsOverview is null)
            return;

        var unreadKeys = _alertsOverview.Alerts
            .Where(x => !x.IsRead)
            .Select(x => x.Key)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (unreadKeys.Length == 0)
            return;

        await _api.MarkAlertsReadAsync(unreadKeys);
        await RefreshAlertsPageAsync();
    }

    private async Task OpenAlertAsync(DataGridViewRow row)
    {
        if (row.Tag is not AlertClientResponse alert)
            return;

        if (!alert.IsRead)
        {
            try
            {
                await _api.MarkAlertsReadAsync([alert.Key]);
            }
            catch
            {
                // Navigation should still work if acknowledgement cannot be persisted.
            }
        }

        switch (alert.Category)
        {
            case "backup":
            case "verify":
            case "job":
                ShowPage("backups");
                break;
            case "replica":
            case "storage":
                ShowPage("storage");
                break;
            case "protection":
                if (alert.DatabaseId is Guid databaseId)
                    await ShowDatabaseDetailsAsync(databaseId);
                else
                    ShowPage("databases");
                break;
            default:
                ShowPage("dashboard");
                break;
        }

        _ = RefreshAllAsync();
    }

    private void UpdateAlertNavigationBadge(int unreadCount)
    {
        if (!_navigationButtons.TryGetValue("alerts", out var button))
            return;

        button.Text = unreadCount > 0
            ? $"●  هشدارها  ({unreadCount})"
            : "●  هشدارها";
    }

    private static string FormatAlertSeverity(string severity) =>
        severity.Equals("critical", StringComparison.OrdinalIgnoreCase)
            ? "بحرانی"
            : severity.Equals("warning", StringComparison.OrdinalIgnoreCase)
                ? "هشدار"
                : "اطلاع";

    private static string FormatAlertCategory(string category) => category switch
    {
        "backup" => "بکاپ",
        "verify" => "Verify",
        "replica" => "Replica",
        "job" => "Job",
        "protection" => "حفاظت",
        "storage" => "فضا",
        "health" => "Agent",
        _ => category
    };

    private Control BuildReportsPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(245, 247, 250)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 360));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(4)
        };

        var refresh = new Button { Text = "بروزرسانی", AutoSize = true, Height = 36 };
        refresh.Click += async (_, _) => await RefreshReportsPageAsync();

        var export = new Button { Text = "خروجی CSV برای Excel", AutoSize = true, Height = 42, Padding = new Padding(12, 3, 12, 3) };
        export.Click += (_, _) => ExportBackupReportCsv();

        _reportRange.DropDownStyle = ComboBoxStyle.DropDownList;
        _reportRange.Width = 130;
        _reportRange.Items.AddRange(["۷ روز", "۳۰ روز", "۹۰ روز"]);
        _reportRange.SelectedIndex = 1;
        _reportRange.SelectedIndexChanged += async (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed)
                await RefreshReportsPageAsync();
        };

        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(export);
        toolbar.Controls.Add(_reportRange);
        toolbar.Controls.Add(new Label
        {
            Text = "بازه گزارش",
            AutoSize = true,
            Margin = new Padding(8, 9, 3, 0)
        });
        root.Controls.Add(toolbar, 0, 0);

        var kpis = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 18),
            Padding = new Padding(0, 4, 0, 4)
        };
        for (var i = 0; i < 4; i++)
            kpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        kpis.Controls.Add(BuildKpiCard("نرخ موفقیت", _reportSuccessRate, "در بازه انتخاب‌شده"), 0, 0);
        kpis.Controls.Add(BuildKpiCard("بکاپ ناموفق", _reportFailureCount, "تعداد Failure"), 1, 0);
        kpis.Controls.Add(BuildKpiCard("میانگین مدت", _reportAverageDuration, "Backupهای موفق"), 2, 0);
        kpis.Controls.Add(BuildKpiCard("دیتابیس محافظت‌شده", _reportProtected, "از کل دیتابیس فعال"), 3, 0);
        root.Controls.Add(kpis, 0, 1);

        _reportStatusChart.ChartTitle = "روند موفق / ناموفق / Verify ناموفق";
        _reportStatusChart.Kind = BackupChartKind.Bar;
        _reportStatusChart.ValueFormatter = value => value.ToString("0");
        _reportStatusChart.Margin = new Padding(6);

        _reportSizeChart.ChartTitle = "حجم بکاپ‌های موفق";
        _reportSizeChart.Kind = BackupChartKind.Line;
        _reportSizeChart.ValueFormatter = value => FormatBytes((long)value);
        _reportSizeChart.Margin = new Padding(6);

        var charts = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.Controls.Add(_reportStatusChart, 0, 0);
        charts.Controls.Add(_reportSizeChart, 1, 0);
        root.Controls.Add(charts, 0, 2);

        ConfigureReadOnlyGrid(_reportDatabaseGrid);
        _reportDatabaseGrid.Columns.Clear();
        _reportDatabaseGrid.Columns.Add("Database", "دیتابیس");
        _reportDatabaseGrid.Columns.Add("State", "وضعیت");
        _reportDatabaseGrid.Columns.Add("SuccessRate", "نرخ موفقیت");
        _reportDatabaseGrid.Columns.Add("Succeeded", "موفق");
        _reportDatabaseGrid.Columns.Add("Failed", "ناموفق");
        _reportDatabaseGrid.Columns.Add("Verify", "Verify ناموفق");
        _reportDatabaseGrid.Columns.Add("Replica", "Replica ناموفق");
        _reportDatabaseGrid.Columns.Add("AverageDuration", "میانگین مدت");
        _reportDatabaseGrid.Columns.Add("LatestSize", "آخرین حجم");
        _reportDatabaseGrid.Columns.Add("Growth", "رشد حجم");
        _reportDatabaseGrid.Columns.Add("LatestBackup", "آخرین بکاپ");

        _reportDatabaseGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && _reportDatabaseGrid.Rows[e.RowIndex].Tag is Guid databaseId)
                await ShowDatabaseDetailsAsync(databaseId);
        };

        root.Controls.Add(BuildBackupsSection("گزارش دیتابیس‌ها", _reportDatabaseGrid), 0, 3);
        return root;
    }

    private async Task RefreshReportsPageAsync()
    {
        try
        {
            var days = _reportRange.SelectedIndex switch
            {
                0 => 7,
                2 => 90,
                _ => 30
            };

            _backupReport = await _api.GetBackupReportAsync(days);
            if (_backupReport is null)
                return;

            RenderBackupReport(_backupReport);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void RenderBackupReport(BackupReportResponse report)
    {
        _reportSuccessRate.Text = report.Summary.SuccessRate.HasValue
            ? $"{report.Summary.SuccessRate.Value:0.0}٪"
            : "—";
        _reportFailureCount.Text = report.Summary.Failed.ToString();
        _reportAverageDuration.Text = report.Summary.AverageDurationSeconds.HasValue
            ? FormatDurationAxis(report.Summary.AverageDurationSeconds.Value)
            : "—";
        _reportProtected.Text =
            $"{report.Summary.ProtectedDatabases} / {report.Summary.EnabledDatabases}";

        var labels = report.Daily
            .Select(x => FormatChartDate(x.DateUtc))
            .ToArray();

        _reportStatusChart.SetData(
            labels,
            new BackupChartSeries("موفق", report.Daily.Select(x => (double?)x.Succeeded).ToArray()),
            new BackupChartSeries("ناموفق", report.Daily.Select(x => (double?)x.Failed).ToArray()),
            new BackupChartSeries("Verify", report.Daily.Select(x => (double?)x.VerifyFailed).ToArray()));

        _reportSizeChart.SetData(
            labels,
            new BackupChartSeries(
                "حجم",
                report.Daily.Select(x => (double?)x.TotalSizeBytes).ToArray()));

        _reportDatabaseGrid.Rows.Clear();
        foreach (var row in report.Databases)
        {
            var rowIndex = _reportDatabaseGrid.Rows.Add(
                row.DatabaseName,
                FormatReportSeverity(row.Severity),
                row.SuccessRate.HasValue ? $"{row.SuccessRate.Value:0.0}٪" : "—",
                row.Succeeded,
                row.Failed,
                row.VerifyFailed,
                row.ReplicaFailed,
                row.AverageDurationSeconds.HasValue
                    ? FormatDurationAxis(row.AverageDurationSeconds.Value)
                    : "—",
                FormatBytes(row.LatestSizeBytes),
                row.SizeGrowthPercent.HasValue
                    ? $"{row.SizeGrowthPercent.Value:+0.0;-0.0;0.0}٪"
                    : "—",
                row.LatestBackupAtUtc.HasValue
                    ? FormatDashboardTime(row.LatestBackupAtUtc.Value)
                    : "—");

            var gridRow = _reportDatabaseGrid.Rows[rowIndex];
            gridRow.Tag = row.DatabaseId;

            gridRow.DefaultCellStyle.ForeColor = row.Severity switch
            {
                "critical" => Color.FromArgb(155, 50, 50),
                "warning" => Color.FromArgb(145, 95, 25),
                _ => Color.FromArgb(55, 65, 80)
            };
        }
    }

    private void ExportBackupReportCsv()
    {
        if (_backupReport is null)
        {
            MessageBox.Show(
                this,
                "ابتدا گزارش را دریافت کنید.",
                "OdinVault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "ذخیره گزارش OdinVault",
            Filter = "CSV (Excel)|*.csv",
            FileName = $"OdinVault-Backup-Report-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            AddExtension = true,
            DefaultExt = "csv"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var lines = new List<string>
        {
            "دیتابیس,وضعیت,کل بکاپ,موفق,ناموفق,Verify ناموفق,Replica ناموفق,نرخ موفقیت,میانگین مدت ثانیه,آخرین حجم بایت,حجم قبلی بایت,درصد رشد حجم,آخرین بکاپ"
        };

        foreach (var row in _backupReport.Databases)
        {
            lines.Add(string.Join(",",
                Csv(row.DatabaseName),
                Csv(FormatReportSeverity(row.Severity)),
                row.TotalBackups,
                row.Succeeded,
                row.Failed,
                row.VerifyFailed,
                row.ReplicaFailed,
                row.SuccessRate?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                row.AverageDurationSeconds?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                row.LatestSizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                row.PreviousSizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                row.SizeGrowthPercent?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                Csv(row.LatestBackupAtUtc.HasValue
                    ? FormatDashboardTime(row.LatestBackupAtUtc.Value)
                    : string.Empty)));
        }

        File.WriteAllText(
            dialog.FileName,
            string.Join(Environment.NewLine, lines),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        MessageBox.Show(
            this,
            "گزارش CSV ذخیره شد و با Excel قابل باز شدن است.",
            "OdinVault",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static string Csv(string value)
    {
        var normalized = value.Replace("\"", "\"\"");
        return $"\"{normalized}\"";
    }

    private static string FormatReportSeverity(string severity) => severity switch
    {
        "critical" => "بحرانی",
        "warning" => "نیاز به بررسی",
        _ => "سالم"
    };

    private Control BuildSettingsPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(4),
            BackColor = Color.FromArgb(245, 247, 250)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));

        root.Controls.Add(BuildSettingsCard(
            "رفتار برنامه",
            BuildGeneralSettingsContent()), 0, 0);

        root.Controls.Add(BuildSettingsCard(
            "ظاهر",
            BuildAppearanceSettingsContent()), 1, 0);

        root.Controls.Add(BuildSettingsCard(
            "Agent و اتصال موبایل",
            BuildAgentSettingsContent()), 0, 1);

        root.Controls.Add(BuildSettingsCard(
            "بروزرسانی و درباره",
            BuildUpdateSettingsContent()), 1, 1);

        return root;
    }

    private Control BuildSettingsCard(string title, Control content)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(8),
            Padding = new Padding(14),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            ForeColor = Color.FromArgb(45, 55, 70)
        }, 0, 0);
        card.Controls.Add(content, 0, 1);
        return card;
    }

    private Control BuildGeneralSettingsContent()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        _settingStartMinimized.Text = "اجرای Manager به‌صورت Minimized در System Tray";
        _settingStartMinimized.AutoSize = true;
        _settingStartMinimized.Margin = new Padding(8);

        _settingMinimizeToTray.Text = "Minimize و بستن پنجره به Tray منتقل شود";
        _settingMinimizeToTray.AutoSize = true;
        _settingMinimizeToTray.Margin = new Padding(8);

        _settingTrayNotifications.Text = "اعلان‌های System Tray نمایش داده شوند";
        _settingTrayNotifications.AutoSize = true;
        _settingTrayNotifications.Margin = new Padding(8);

        _settingAutoUpdate.Text = "هنگام اجرای Manager بروزرسانی بررسی شود";
        _settingAutoUpdate.AutoSize = true;
        _settingAutoUpdate.Margin = new Padding(8);

        var save = new Button
        {
            Text = "ذخیره تنظیمات",
            AutoSize = true,
            Height = 38,
            Margin = new Padding(8, 18, 8, 8)
        };
        save.Click += (_, _) => SaveManagerSettings();

        panel.Controls.AddRange(
        [
            _settingStartMinimized,
            _settingMinimizeToTray,
            _settingTrayNotifications,
            _settingAutoUpdate,
            save
        ]);

        return panel;
    }

    private Control BuildAppearanceSettingsContent()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(6)
        };

        panel.Controls.Add(new Label
        {
            Text = "تم رابط کاربری",
            AutoSize = true,
            Margin = new Padding(6)
        }, 0, 0);

        _settingTheme.DropDownStyle = ComboBoxStyle.DropDownList;
        _settingTheme.Width = 180;
        _settingTheme.Items.AddRange(["روشن", "تیره"]);
        panel.Controls.Add(_settingTheme, 0, 1);

        panel.Controls.Add(new Label
        {
            Text = "تغییر تم بلافاصله روی صفحه‌های باز اعمال می‌شود. پنجره همچنان قابل Resize و Maximize است.",
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            ForeColor = Color.FromArgb(105, 115, 130),
            Margin = new Padding(6, 14, 6, 6)
        }, 0, 2);

        var apply = new Button
        {
            Text = "اعمال ظاهر",
            AutoSize = true,
            Height = 36,
            Margin = new Padding(6, 16, 6, 6)
        };
        apply.Click += (_, _) =>
        {
            _settings = _settings with
            {
                Theme = _settingTheme.SelectedIndex == 1 ? "Dark" : "Light"
            };
            _settingsStore.Save(_settings);
            ApplyTheme();
        };
        panel.Controls.Add(apply, 0, 3);
        return panel;
    }

    private Control BuildAgentSettingsContent()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(6)
        };

        panel.Controls.Add(new Label
        {
            Text = "Agent محلی: http://127.0.0.1:5188",
            AutoSize = true,
            Margin = new Padding(6)
        }, 0, 0);

        panel.Controls.Add(new Label
        {
            Text = "آدرس اتصال موبایل",
            AutoSize = true,
            Margin = new Padding(6, 12, 6, 4)
        }, 0, 1);

        _settingMobileUrl.Dock = DockStyle.Top;
        _settingMobileUrl.RightToLeft = RightToLeft.No;
        panel.Controls.Add(_settingMobileUrl, 0, 2);

        var saveMobile = new Button
        {
            Text = "ذخیره آدرس موبایل",
            AutoSize = true,
            Height = 36,
            Margin = new Padding(6, 12, 6, 6)
        };
        saveMobile.Click += (_, _) =>
        {
            try
            {
                _api.SaveMobileBaseUrl(_settingMobileUrl.Text);
                MessageBox.Show(
                    this,
                    "آدرس اتصال موبایل ذخیره شد.",
                    "OdinVault",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        };
        panel.Controls.Add(saveMobile, 0, 3);

        var mobile = new Button
        {
            Text = "نمایش اطلاعات اتصال موبایل",
            AutoSize = true,
            Height = 36,
            Margin = new Padding(6)
        };
        mobile.Click += (_, _) => ShowMobileConnection();
        panel.Controls.Add(mobile, 0, 4);

        panel.Controls.Add(new Label
        {
            Text = "کلید Agent در UI نمایش داده نمی‌شود و از مسیر امن فعلی Manager خوانده می‌شود.",
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            ForeColor = Color.FromArgb(105, 115, 130),
            Margin = new Padding(6)
        }, 0, 5);

        return panel;
    }

    private Control BuildUpdateSettingsContent()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(6)
        };

        _settingVersion.Dock = DockStyle.Top;
        _settingVersion.TextAlign = ContentAlignment.MiddleRight;
        _settingVersion.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        panel.Controls.Add(_settingVersion, 0, 0);

        panel.Controls.Add(new Label
        {
            Text = "بروزرسانی‌ها از GitHub Release دریافت می‌شوند و SHA256 Installer قبل از اجرا بررسی می‌شود.",
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            ForeColor = Color.FromArgb(105, 115, 130),
            Margin = new Padding(6, 10, 6, 10)
        }, 0, 1);

        var check = new Button
        {
            Text = "بررسی بروزرسانی",
            AutoSize = true,
            Height = 36,
            Margin = new Padding(6)
        };
        check.Click += async (_, _) => await CheckForUpdatesAsync(silent: false);
        panel.Controls.Add(check, 0, 2);

        panel.Controls.Add(new Label
        {
            Text = "OdinVault Manager",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            Margin = new Padding(6, 18, 6, 4)
        }, 0, 3);

        panel.Controls.Add(new Label
        {
            Text = "مدیریت بکاپ SQL Server، Restore، Replica و مانیتورینگ.",
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Margin = new Padding(6)
        }, 0, 4);

        return panel;
    }

    private void LoadSettingsIntoControls()
    {
        _settingStartMinimized.Checked = _settings.StartMinimizedToTray;
        _settingMinimizeToTray.Checked = _settings.MinimizeToTray;
        _settingTrayNotifications.Checked = _settings.ShowTrayNotifications;
        _settingAutoUpdate.Checked = _settings.CheckForUpdatesOnStart;
        _settingTheme.SelectedIndex = _settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
        _settingMobileUrl.Text = _api.GetMobileBaseUrl();
        _settingVersion.Text = $"نسخه Manager: {_updates.GetCurrentVersion()}";
    }

    private void SaveManagerSettings()
    {
        _settings = new ManagerSettings(
            _settingStartMinimized.Checked,
            _settingMinimizeToTray.Checked,
            _settingTrayNotifications.Checked,
            _settingAutoUpdate.Checked,
            _settingTheme.SelectedIndex == 1 ? "Dark" : "Light");

        _settingsStore.Save(_settings);
        ApplyTheme();

        MessageBox.Show(
            this,
            "تنظیمات Manager ذخیره شد.",
            "OdinVault",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void NotifyNewAlerts(AlertsOverviewResponse? alerts)
    {
        if (alerts is null)
            return;

        var currentKeys = alerts.Alerts
            .Where(x => !x.IsRead)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (!_alertSnapshotInitialized)
        {
            _knownUnreadAlertKeys.Clear();
            _knownUnreadAlertKeys.UnionWith(currentKeys);
            _alertSnapshotInitialized = true;
            return;
        }

        var newAlerts = alerts.Alerts
            .Where(x => !x.IsRead && !_knownUnreadAlertKeys.Contains(x.Key))
            .OrderBy(x => x.Severity == "critical" ? 0 : 1)
            .ThenByDescending(x => x.OccurredAtUtc)
            .ToList();

        _knownUnreadAlertKeys.Clear();
        _knownUnreadAlertKeys.UnionWith(currentKeys);

        if (newAlerts.Count == 0 || !_settings.ShowTrayNotifications)
            return;

        var first = newAlerts[0];
        var message = newAlerts.Count == 1
            ? $"{first.DatabaseName}: {first.Title}"
            : $"{newAlerts.Count} هشدار جدید؛ {first.DatabaseName}: {first.Title}";

        ShowTrayNotification(
            "هشدار جدید OdinVault",
            message,
            first.Severity == "critical" ? ToolTipIcon.Error : ToolTipIcon.Warning);
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip();

        var open = new ToolStripMenuItem("باز کردن OdinVault");
        open.Click += (_, _) => RestoreFromTray();

        var backupAll = new ToolStripMenuItem("بکاپ همه دیتابیس‌ها الآن");
        backupAll.Click += async (_, _) => await BackupAllFromTrayAsync();

        var refresh = new ToolStripMenuItem("بروزرسانی وضعیت");
        refresh.Click += async (_, _) =>
        {
            RestoreFromTray();
            await RefreshAllAsync();
        };

        var exit = new ToolStripMenuItem("خروج از Manager");
        exit.Click += (_, _) =>
        {
            _allowExit = true;
            Close();
        };

        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(backupAll);
        menu.Items.Add(refresh);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _trayIcon.Text = "OdinVault Manager";
        _trayIcon.Icon = SystemIcons.Shield;
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray(bool showNotification)
    {
        Hide();
        ShowInTaskbar = false;

        if (showNotification && _settings.ShowTrayNotifications)
        {
            _trayIcon.BalloonTipTitle = "OdinVault";
            _trayIcon.BalloonTipText = "Manager در System Tray فعال است. Agent و زمان‌بندی‌ها مستقل به کار ادامه می‌دهند.";
            _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(3000);
        }
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Maximized;
        Activate();
        BringToFront();
    }

    private async Task BackupAllFromTrayAsync()
    {
        try
        {
            var databases = (await _api.GetDatabasesAsync())
                .Where(x => x.IsEnabled && x.Policy?.IsEnabled == true)
                .ToList();

            if (databases.Count == 0)
            {
                ShowTrayNotification("OdinVault", "هیچ دیتابیس فعالی برای بکاپ وجود ندارد.", ToolTipIcon.Info);
                return;
            }

            var succeeded = 0;
            var failed = 0;

            foreach (var database in databases)
            {
                try
                {
                    await _api.RunBackupAsync(database.Id);
                    succeeded++;
                }
                catch
                {
                    failed++;
                }
            }

            ShowTrayNotification(
                "بکاپ همه دیتابیس‌ها",
                $"{succeeded} موفق، {failed} ناموفق",
                failed == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);

            if (Visible)
                await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            ShowTrayNotification("OdinVault", ex.Message, ToolTipIcon.Error);
        }
    }

    private void ShowTrayNotification(string title, string message, ToolTipIcon icon)
    {
        if (!_settings.ShowTrayNotifications)
            return;

        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message.Length > 240 ? message[..240] : message;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(4000);
    }

    private void ApplyTheme()
    {
        var dark = _settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        BackColor = dark
            ? Color.FromArgb(24, 29, 38)
            : Color.FromArgb(245, 247, 250);

        ApplyThemeToContent();
    }

    private void ApplyThemeToContent()
    {
        var dark = _settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        var background = dark
            ? Color.FromArgb(24, 29, 38)
            : Color.FromArgb(245, 247, 250);
        var surface = dark
            ? Color.FromArgb(34, 41, 52)
            : Color.White;
        var primary = dark
            ? Color.FromArgb(226, 232, 240)
            : Color.FromArgb(45, 55, 70);
        var secondary = dark
            ? Color.FromArgb(164, 174, 188)
            : Color.FromArgb(105, 115, 130);

        _contentHost.BackColor = background;

        ApplyThemeRecursive(_contentHost, dark, background, surface, primary, secondary);
    }

    private static void ApplyThemeRecursive(
        Control control,
        bool dark,
        Color background,
        Color surface,
        Color primary,
        Color secondary)
    {
        if (control is DataGridView grid)
        {
            grid.BackgroundColor = surface;
            grid.GridColor = dark
                ? Color.FromArgb(58, 67, 80)
                : Color.FromArgb(228, 233, 240);
            grid.ColumnHeadersDefaultCellStyle.BackColor = dark
                ? Color.FromArgb(44, 52, 65)
                : Color.FromArgb(241, 244, 248);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = primary;
            grid.DefaultCellStyle.BackColor = surface;
            grid.DefaultCellStyle.ForeColor = primary;
            grid.DefaultCellStyle.SelectionBackColor = dark
                ? Color.FromArgb(59, 78, 104)
                : Color.FromArgb(226, 235, 246);
            grid.DefaultCellStyle.SelectionForeColor = primary;
        }
        else if (control is BackupChartControl chart)
        {
            chart.BackColor = surface;
            chart.ForeColor = primary;
            chart.Invalidate();
        }
        else if (control is TextBoxBase textBox)
        {
            textBox.BackColor = surface;
            textBox.ForeColor = primary;
        }
        else if (control is ComboBox combo)
        {
            combo.BackColor = surface;
            combo.ForeColor = primary;
        }
        else if (control is Label label)
        {
            if (label.ForeColor == Color.Empty ||
                label.ForeColor == SystemColors.ControlText ||
                label.ForeColor == Color.FromArgb(45, 55, 70) ||
                label.ForeColor == Color.FromArgb(55, 65, 80) ||
                label.ForeColor == Color.FromArgb(70, 80, 95) ||
                label.ForeColor == Color.FromArgb(95, 105, 120) ||
                label.ForeColor == Color.FromArgb(105, 115, 130) ||
                label.ForeColor == Color.FromArgb(125, 135, 150) ||
                label.ForeColor == Color.FromArgb(130, 140, 155))
            {
                label.ForeColor = label.Font.Bold ? primary : secondary;
            }
        }
        else if (control is Panel or TableLayoutPanel or FlowLayoutPanel)
        {
            if (control.BackColor == Color.White ||
                control.BackColor == Color.FromArgb(245, 247, 250) ||
                control.BackColor == SystemColors.Control)
            {
                control.BackColor = control.Parent == null || control.Dock == DockStyle.Fill
                    ? background
                    : surface;
            }
        }

        foreach (Control child in control.Controls)
            ApplyThemeRecursive(child, dark, background, surface, primary, secondary);
    }

    private Control BuildPlaceholderPage(string title, string description)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var text = new Label
        {
            Dock = DockStyle.Top,
            Height = 96,
            Text = $"{title}\r\n\r\n{description}",
            TextAlign = ContentAlignment.TopRight,
            ForeColor = Color.FromArgb(70, 80, 95),
            Font = new Font(Font.FontFamily, 11F)
        };
        card.Controls.Add(text);
        return card;
    }

    private Control BuildDatabasesPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(245, 247, 250)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var actionsCard = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(18, 18, 18, 14),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(2),
            BackColor = Color.White
        };

        ConfigureButton(_refreshButton, "بروزرسانی", async (_, _) => await RefreshAllAsync());
        ConfigureButton(_addButton, "افزودن دستی", async (_, _) => await AddDatabaseAsync());
        ConfigureButton(_discoverButton, "شناسایی دیتابیس‌ها", async (_, _) => await DiscoverDatabasesAsync());
        ConfigureButton(_editButton, "ویرایش", async (_, _) => await EditSelectedAsync());
        ConfigureButton(_deleteButton, "حذف", async (_, _) => await DeleteSelectedAsync());
        ConfigureButton(_testButton, "تست اتصال", async (_, _) => await TestSelectedAsync());
        ConfigureButton(_backupButton, "بکاپ انتخاب‌شده‌ها", async (_, _) => await BackupSelectedAsync());
        ConfigureButton(_detailsButton, "جزئیات", async (_, _) => await ShowSelectedDatabaseDetailsAsync());
        ConfigureButton(_mobileConnectionButton, "اتصال موبایل", (_, _) => ShowMobileConnection());
        ConfigureButton(_updateButton, "بررسی بروزرسانی", async (_, _) => await CheckForUpdatesAsync(silent: false));

        var storageButton = new Button();
        ConfigureButton(storageButton, "پشتیبان و محل دریافت", (_, _) =>
        {
            using var dialog = new ReplicaSetupForm(_api);
            dialog.ShowDialog(this);
        });

        actions.Controls.Add(storageButton);
        actions.Controls.AddRange(
        [
            _backupButton,
            _detailsButton,
            _testButton,
            _deleteButton,
            _editButton,
            _discoverButton,
            _addButton,
            _refreshButton,
            _mobileConnectionButton,
            _updateButton
        ]);
        actionsCard.Controls.Add(actions);
        root.Controls.Add(actionsCard, 0, 0);

        var gridCard = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        gridCard.Controls.Add(_grid);
        root.Controls.Add(gridCard, 0, 1);

        return root;
    }

    private void ConfigureDatabaseGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.ColumnHeadersHeight = 48;
        _grid.RowTemplate.Height = 44;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 244, 248);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 65, 80);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 235, 246);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 40, 55);
        _grid.GridColor = Color.FromArgb(228, 233, 240);

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Selected",
            HeaderText = "انتخاب",
            FillWeight = 50,
            ThreeState = false,
            ReadOnly = false
        });
        _grid.Columns.Add("Name", "نام");
        _grid.Columns.Add("Server", "سرور");
        _grid.Columns.Add("Database", "دیتابیس");
        _grid.Columns.Add("Schedule", "زمان‌بندی");
        _grid.Columns.Add("Retention", "نگهداری");
        _grid.Columns.Add("بررسی سلامت", "بررسی سلامت");
        _grid.Columns.Add("Protection", "محافظت");
        _grid.Columns.Add("LastBackup", "آخرین بکاپ");
        _grid.Columns.Add("BackupSize", "حجم");
        _grid.Columns.Add("Enabled", "فعال");

        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].Tag is Guid id)
                await ShowDatabaseDetailsAsync(id);
        };
    }

    private static void ConfigureButton(Button button, string text, EventHandler handler)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 42;
        button.MinimumSize = new Size(0, 42);
        button.Padding = new Padding(14, 4, 14, 4);
        button.FlatStyle = FlatStyle.System;
        button.Click -= handler;
        button.Click += handler;
    }

    private async Task RefreshAllAsync(bool showErrors = true)
    {
        if (_refreshAllInProgress)
            return;

        _refreshAllInProgress = true;
        SetBusy(true);
        try
        {
            var dashboardTask = _api.GetDashboardAsync();
            var databasesTask = _api.GetDatabasesAsync();
            var overviewsTask = _api.GetDatabaseOverviewsAsync();
            var alertsTask = _api.GetAlertsAsync(includeRead: false);
            var chartDays = _chartRange.SelectedIndex == 0 ? 7 : 30;
            var statsTask = _api.GetDashboardStatsAsync(chartDays);
            await Task.WhenAll(dashboardTask, databasesTask, overviewsTask, statsTask, alertsTask);

            var dashboard = await dashboardTask;
            if (dashboard is not null)
            {
                var agentHealthy = dashboard.Status.Equals("healthy", StringComparison.OrdinalIgnoreCase);
                var protectionHealthy = dashboard.ProtectionStatus.Equals("healthy", StringComparison.OrdinalIgnoreCase);

                _agentStatus.Text = agentHealthy
                    ? protectionHealthy
                        ? "● Agent فعال • حفاظت سالم"
                        : "● Agent فعال • نیاز به بررسی"
                    : "● Agent مشکل دارد";

                _agentStatus.ForeColor = !agentHealthy
                    ? Color.FromArgb(190, 65, 65)
                    : protectionHealthy
                        ? Color.FromArgb(42, 145, 96)
                        : Color.FromArgb(190, 125, 35);

                _sidebarAgentStatus.Text = agentHealthy
                    ? protectionHealthy
                        ? "● Agent Online\r\nحفاظت سالم"
                        : "● Agent Online\r\nنیاز به بررسی"
                    : "● Agent Offline\r\nlocalhost:5188";

                _sidebarAgentStatus.ForeColor = !agentHealthy
                    ? Color.FromArgb(245, 135, 135)
                    : protectionHealthy
                        ? Color.FromArgb(125, 225, 170)
                        : Color.FromArgb(245, 195, 105);

                _trayIcon.Text = agentHealthy
                    ? protectionHealthy
                        ? "OdinVault • Agent Online • Protected"
                        : "OdinVault • Agent Online • Attention"
                    : "OdinVault • Agent Offline";

                RenderDashboard(dashboard);
            }

            var stats = await statsTask;
            if (stats is not null)
                RenderDashboardCharts(stats);

            var alertSummary = await alertsTask;
            UpdateAlertNavigationBadge(alertSummary?.UnreadCount ?? 0);
            NotifyNewAlerts(alertSummary);

            var databases = await databasesTask;
            var overviews = (await overviewsTask).ToDictionary(x => x.Id);
            _grid.Rows.Clear();

            foreach (var db in databases)
            {
                overviews.TryGetValue(db.Id, out var overview);
                var rowIndex = _grid.Rows.Add(
                    false,
                    db.Name,
                    db.Port is > 0 ? $"{db.Host}:{db.Port}" : db.Host,
                    db.DatabaseName,
                    ScheduleEditor.FormatCron(db.Policy?.ScheduleCron),
                    db.Policy is null ? "-" : $"{db.Policy.MaxLocalBackups} فایل",
                    db.Policy?.VerifyAfterBackup == true ? "بله" : "خیر",
                    overview?.IsProtected == true ? "سالم" : "نیاز به بررسی",
                    overview?.LatestBackupAtUtc is DateTime lastBackup
                        ? FormatDashboardTime(lastBackup)
                        : "—",
                    FormatBytes(overview?.LatestBackupSizeBytes),
                    db.IsEnabled ? "بله" : "خیر");

                _grid.Rows[rowIndex].Tag = db.Id;
                foreach (DataGridViewCell cell in _grid.Rows[rowIndex].Cells)
                {
                    if (cell.OwningColumn.Name != "Selected")
                        cell.ReadOnly = true;
                }
            }

            _lastRefresh.Text = $"آخرین بروزرسانی: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _agentStatus.Text = "● Agent در دسترس نیست";
            _agentStatus.ForeColor = Color.FromArgb(190, 65, 65);
            _sidebarAgentStatus.Text = "● Agent Offline\r\nlocalhost:5188";
            _sidebarAgentStatus.ForeColor = Color.FromArgb(245, 135, 135);
            _trayIcon.Text = "OdinVault • Agent Offline";

            if (showErrors)
                ShowError(ex);
        }
        finally
        {
            SetBusy(false);
            _refreshAllInProgress = false;
        }
    }

    private async Task ShowSelectedDatabaseDetailsAsync()
    {
        if (!TryGetSelectedDatabaseId(out var id))
            return;

        await ShowDatabaseDetailsAsync(id);
    }

    private async Task ShowDatabaseDetailsAsync(Guid id)
    {
        SetBusy(true);
        try
        {
            var details = await _api.GetDatabaseDetailsAsync(id);
            if (details is null)
                return;

            _currentDetailsDatabaseId = id;
            _pageTitle.Text = $"جزئیات {details.Database.Name}";

            foreach (var item in _navigationButtons)
                item.Value.BackColor = Color.FromArgb(27, 35, 48);

            _contentHost.Controls.Clear();
            _contentHost.Controls.Add(BuildDatabaseDetailsPage(details));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Control BuildDatabaseDetailsPage(DatabaseDetailsResponse details)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(245, 247, 250)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10)
        };

        var back = new Button { Text = "بازگشت به دیتابیس‌ها", AutoSize = true, Height = 36 };
        back.Click += (_, _) => ShowPage("databases");
        var backupNow = new Button { Text = "بکاپ الآن", AutoSize = true, Height = 36 };
        backupNow.Click += async (_, _) =>
        {
            try
            {
                SetBusy(true);
                await _api.RunBackupAsync(details.Database.Id, progress: new Progress<string>(text => _agentStatus.Text = $"{details.Database.Name}: {text}"));
                await ShowDatabaseDetailsAsync(details.Database.Id);
                await RefreshAllAsync();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                SetBusy(false);
            }
        };

        toolbar.Controls.Add(backupNow);
        toolbar.Controls.Add(back);
        root.Controls.Add(toolbar, 0, 0);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 14)
        };
        for (var i = 0; i < 4; i++)
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        cards.Controls.Add(BuildDetailsCard(
            "وضعیت محافظت",
            details.Protection.IsProtected ? "محافظت‌شده" : "نیاز به بررسی",
            details.Database.IsEnabled ? "دیتابیس فعال" : "دیتابیس غیرفعال"), 0, 0);

        cards.Controls.Add(BuildDetailsCard(
            "آخرین بکاپ",
            details.Protection.LatestBackupAtUtc is DateTime last
                ? FormatDashboardTime(last)
                : "بدون سابقه",
            FormatBytes(details.Protection.LatestBackupSizeBytes)), 1, 0);

        cards.Controls.Add(BuildDetailsCard(
            "Verify",
            FormatVerificationStatus(details.Protection.LatestVerificationStatus),
            details.Database.Policy?.VerifyAfterBackup == true ? "بررسی پس از بکاپ فعال است" : "Verify غیرفعال است"), 2, 0);

        cards.Controls.Add(BuildDetailsCard(
            "Replica",
            $"{details.Protection.LatestReplicaSucceeded} / {details.Protection.LatestReplicaTotal}",
            "مقصد موفق / کل مقصد برای آخرین بکاپ"), 3, 0);

        root.Controls.Add(cards, 0, 1);

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        var history = BuildDatabaseHistory(details);
        main.Controls.Add(history, 0, 0);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 20));

        var sizeChart = new BackupChartControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(6),
            ChartTitle = "روند حجم بکاپ این دیتابیس",
            Kind = BackupChartKind.Line,
            ValueFormatter = value => FormatBytes((long)value)
        };

        var durationChart = new BackupChartControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(6),
            ChartTitle = "مدت زمان بکاپ",
            Kind = BackupChartKind.Line,
            ValueFormatter = FormatDurationAxis
        };

        var successful = details.Backups
            .Where(x => x.Status == 2)
            .OrderBy(x => x.StartedAtUtc)
            .TakeLast(20)
            .ToArray();
        var labels = successful.Select(x => FormatChartDate(x.StartedAtUtc)).ToArray();

        sizeChart.SetData(
            labels,
            new BackupChartSeries("حجم", successful.Select(x => x.SizeBytes.HasValue ? (double?)x.SizeBytes.Value : null).ToArray()));

        durationChart.SetData(
            labels,
            new BackupChartSeries("مدت", successful.Select(x =>
                x.CompletedAtUtc.HasValue
                    ? (double?)Math.Max(0, (x.CompletedAtUtc.Value - x.StartedAtUtc).TotalSeconds)
                    : null).ToArray()));

        right.Controls.Add(sizeChart, 0, 0);
        right.Controls.Add(durationChart, 0, 1);
        right.Controls.Add(BuildDatabaseInfoCard(details), 0, 2);
        main.Controls.Add(right, 1, 0);

        root.Controls.Add(main, 0, 2);
        return root;
    }

    private Control BuildDetailsCard(string title, string value, string subtitle)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(6),
            Padding = new Padding(14, 10, 14, 10),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(105, 115, 130)
        }, 0, 0);

        card.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 50, 65)
        }, 0, 1);

        card.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(130, 140, 155),
            Font = new Font(Font.FontFamily, 8.5F)
        }, 0, 2);

        return card;
    }

    private Control BuildDatabaseHistory(DatabaseDetailsResponse details)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(6),
            Padding = new Padding(1),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.Controls.Add(new Label
        {
            Text = "تاریخچه بکاپ",
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 0, 14, 0),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold)
        }, 0, 0);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 244, 248);
        grid.Columns.Add("Date", "زمان");
        grid.Columns.Add("Status", "وضعیت");
        grid.Columns.Add("Verify", "Verify");
        grid.Columns.Add("Size", "حجم");
        grid.Columns.Add("Duration", "مدت");
        grid.Columns.Add("Local", "Local");

        foreach (var backup in details.Backups.Take(40))
        {
            var duration = backup.CompletedAtUtc.HasValue
                ? FormatDurationAxis(Math.Max(0, (backup.CompletedAtUtc.Value - backup.StartedAtUtc).TotalSeconds))
                : "—";

            grid.Rows.Add(
                FormatDashboardTime(backup.CompletedAtUtc ?? backup.StartedAtUtc),
                FormatBackupStatus(backup.Status),
                FormatVerificationStatus(backup.VerificationStatus),
                FormatBytes(backup.SizeBytes),
                duration,
                backup.LocalFileAvailable ? "موجود" : "حذف‌شده");
        }

        card.Controls.Add(grid, 0, 1);
        return card;
    }

    private Control BuildDatabaseInfoCard(DatabaseDetailsResponse details)
    {
        var latestReplicas = details.Backups.Count == 0
            ? Array.Empty<DatabaseReplicaHistoryResponse>()
            : details.Replicas
                .Where(x => x.BackupRecordId == details.Backups[0].Id)
                .ToArray();

        var replicaText = latestReplicas.Length == 0
            ? "Replica: مقصدی برای آخرین بکاپ ثبت نشده"
            : "Replica: " + string.Join(
                " • ",
                latestReplicas.Select(x => $"{x.Name}: {FormatReplicaStatus(x.Status)}"));

        var schedule = ScheduleEditor.FormatCron(details.Database.Policy?.ScheduleCron);
        var text =
            $"SQL Server: {details.Database.Host}{(details.Database.Port is > 0 ? $":{details.Database.Port}" : string.Empty)}\r\n" +
            $"Database: {details.Database.DatabaseName}\r\n" +
            $"زمان‌بندی: {schedule}\r\n" +
            replicaText;

        return new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(6),
            Padding = new Padding(14),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Controls =
            {
                new Label
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleRight,
                    ForeColor = Color.FromArgb(70, 80, 95)
                }
            }
        };
    }

    private static string FormatBackupStatus(int status) => status switch
    {
        2 => "موفق",
        3 => "ناموفق",
        1 => "در حال اجرا",
        _ => "در انتظار"
    };

    private static string FormatVerificationStatus(int? status) => status switch
    {
        2 => "موفق",
        3 => "ناموفق",
        1 => "در حال بررسی",
        0 => "درخواست نشده",
        _ => "—"
    };

    private static string FormatReplicaStatus(int status) => status switch
    {
        2 => "موفق",
        3 => "ناموفق",
        1 => "در حال ارسال",
        _ => "در انتظار"
    };

    private async Task AddDatabaseAsync()
    {
        using var dialog = new AddDatabaseForm();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Request is null)
            return;

        SetBusy(true);
        try
        {
            var created = await _api.CreateDatabaseAsync(dialog.Request);
            if (created is not null)
            {
                try
                {
                    await _api.TestDatabaseAsync(created.Id);
                    MessageBox.Show(
                        this,
                        "دیتابیس ثبت شد و اتصال با موفقیت تست شد.",
                        "OdinVault",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        $"دیتابیس ثبت شد، اما تست اتصال ناموفق بود:{Environment.NewLine}{ex.Message}",
                        "OdinVault",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DiscoverDatabasesAsync()
    {
        using var dialog = new DiscoverDatabasesForm(_api);
        dialog.ShowDialog(this);

        if (dialog.AnyAdded)
            await RefreshAllAsync();
    }

    private async Task EditSelectedAsync()
    {
        if (!TryGetSelectedDatabaseId(out var id))
            return;

        SetBusy(true);
        DatabaseResponse? database;
        try
        {
            database = await _api.GetDatabaseAsync(id);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }
        finally
        {
            SetBusy(false);
        }

        if (database is null)
            return;

        using var dialog = new EditDatabaseForm(database);
        if (dialog.ShowDialog(this) != DialogResult.OK ||
            dialog.DatabaseRequest is null ||
            dialog.PolicyRequest is null)
            return;

        SetBusy(true);
        try
        {
            await _api.UpdateDatabaseAsync(id, dialog.DatabaseRequest);
            await _api.UpdateBackupPolicyAsync(id, dialog.PolicyRequest);
            await RefreshAllAsync();

            MessageBox.Show(
                this,
                "تنظیمات دیتابیس با موفقیت بروزرسانی شد.",
                "OdinVault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (!TryGetSelectedDatabaseId(out var id))
            return;

        var row = _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0] : _grid.CurrentRow;
        var name = row is null ? "این دیتابیس" : Convert.ToString(row.Cells["Name"].Value) ?? "این دیتابیس";

        var answer = MessageBox.Show(
            this,
            $"«{name}» از OdinVault حذف شود؟{Environment.NewLine}{Environment.NewLine}" +
            "تنظیمات و تاریخچه OdinVault حذف می‌شود، اما فایل‌های بکاپ موجود روی دیسک نگه داشته می‌شوند.",
            "حذف دیتابیس",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
            return;

        SetBusy(true);
        try
        {
            await _api.DeleteDatabaseAsync(id, deleteHistory: true, deleteFiles: false);
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TestSelectedAsync()
    {
        if (!TryGetSelectedDatabaseId(out var id))
            return;

        SetBusy(true);
        try
        {
            await _api.TestDatabaseAsync(id);
            MessageBox.Show(this, "اتصال به SQL Server موفق است.", "تست اتصال", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        _updateButton.Enabled = false;
        try
        {
            var update = await _updates.CheckAsync();

            if (!update.IsUpdateAvailable)
            {
                if (!silent)
                {
                    MessageBox.Show(
                        this,
                        $"OdinVault به‌روز است. نسخه فعلی: {update.CurrentVersion}",
                        "بروزرسانی OdinVault",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            var answer = MessageBox.Show(
                this,
                $"نسخه جدید {update.TagName} موجود است.{Environment.NewLine}" +
                $"نسخه فعلی: {update.CurrentVersion}{Environment.NewLine}{Environment.NewLine}" +
                "دانلود و نصب شود؟",
                "بروزرسانی OdinVault",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (answer != DialogResult.Yes)
                return;

            _updateButton.Text = "در حال دانلود...";
            var progress = new Progress<int>(percent =>
            {
                _updateButton.Text = $"دانلود بروزرسانی {percent}%";
            });

            var installer = await _updates.DownloadInstallerAsync(update, progress);

            MessageBox.Show(
                this,
                "دانلود کامل شد. OdinVault Manager بسته می‌شود و نصب نسخه جدید شروع خواهد شد.",
                "بروزرسانی OdinVault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            GitHubUpdateService.LaunchInstaller(installer);
            Application.Exit();
        }
        catch (Exception ex)
        {
            if (!silent)
                ShowError(ex);
        }
        finally
        {
            if (!IsDisposed)
            {
                _updateButton.Text = "بررسی بروزرسانی";
                _updateButton.Enabled = true;
            }
        }
    }

    private void ShowMobileConnection()
    {
        try
        {
            using var dialog = new MobileConnectionForm(_api);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task BackupSelectedAsync()
    {
        _grid.EndEdit();

        var selected = _grid.Rows
            .Cast<DataGridViewRow>()
            .Where(r => r.Tag is Guid)
            .Where(r => Convert.ToBoolean(r.Cells["Selected"].Value ?? false))
            .Select(r => new
            {
                Id = (Guid)r.Tag!,
                Name = Convert.ToString(r.Cells["Name"].Value) ?? "Database"
            })
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show(
                this,
                "حداقل یک دیتابیس را از ستون «انتخاب» تیک بزنید.",
                "OdinVault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"برای {selected.Count} دیتابیس انتخاب‌شده همین حالا بکاپ گرفته شود؟",
            "OdinVault",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        SetBusy(true);
        var succeeded = 0;
        var errors = new List<string>();

        try
        {
            foreach (var db in selected)
            {
                try
                {
                    _backupButton.Text = $"در حال بکاپ {succeeded + errors.Count + 1} از {selected.Count}";
                    await _api.RunBackupAsync(db.Id, progress: new Progress<string>(text => { if (!IsDisposed) _agentStatus.Text = $"{db.Name}: {text}"; }));
                    succeeded++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{db.Name}: {ex.Message}");
                }
            }

            var message = $"{succeeded} بکاپ با موفقیت انجام شد.";
            if (errors.Count > 0)
                message += Environment.NewLine + Environment.NewLine +
                           "خطاها:" + Environment.NewLine +
                           string.Join(Environment.NewLine, errors);

            MessageBox.Show(
                this,
                message,
                "OdinVault",
                MessageBoxButtons.OK,
                errors.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            await RefreshAllAsync();
        }
        finally
        {
            _backupButton.Text = "بکاپ انتخاب‌شده‌ها";
            SetBusy(false);
        }
    }

    private bool TryGetSelectedDatabaseId(out Guid id)
    {
        id = Guid.Empty;

        if (_grid.SelectedRows.Count == 1 && _grid.SelectedRows[0].Tag is Guid selected)
        {
            id = selected;
            return true;
        }

        MessageBox.Show(this, "ابتدا یک دیتابیس را انتخاب کنید.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void SetBusy(bool busy)
    {
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (!busy)
            Cursor.Current = Cursors.Default;
        _refreshButton.Enabled = !busy;
        _addButton.Enabled = !busy;
        _discoverButton.Enabled = !busy;
        _editButton.Enabled = !busy;
        _deleteButton.Enabled = !busy;
        _testButton.Enabled = !busy;
        _backupButton.Enabled = !busy;
        _detailsButton.Enabled = !busy;
        _mobileConnectionButton.Enabled = !busy;
        _updateButton.Enabled = !busy;
    }

    private void ShowError(Exception ex)
    {
        MessageBox.Show(
            this,
            ex.Message,
            "خطای OdinVault",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

internal sealed class EditDatabaseForm : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _host = new();
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535 };
    private readonly TextBox _database = new();
    private readonly TextBox _username = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _clearPassword = new() { Text = "حذف رمز ذخیره‌شده", AutoSize = true };
    private readonly TextBox _backupDirectory = new();
    private readonly NumericUpDown _maxBackups = new() { Minimum = 1, Maximum = 1000 };
    private readonly ScheduleEditor _schedule = new();
    private readonly CheckBox _trustCertificate = new() { Text = "Trust Server Certificate", AutoSize = true };
    private readonly CheckBox _verify = new() { Text = "Verify بعد از بکاپ", AutoSize = true };
    private readonly CheckBox _enabled = new() { Text = "فعال", AutoSize = true };

    public UpdateDatabaseRequest? DatabaseRequest { get; private set; }
    public UpdateBackupPolicyRequest? PolicyRequest { get; private set; }

    public EditDatabaseForm(DatabaseResponse database)
    {
        Text = $"ویرایش {database.Name}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 660);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        _name.Text = database.Name;
        _host.Text = database.Host;
        _port.Value = Math.Clamp(database.Port ?? 1433, 1, 65535);
        _database.Text = database.DatabaseName;
        _username.Text = database.Username;
        _trustCertificate.Checked = database.TrustServerCertificate;
        _enabled.Checked = database.IsEnabled;

        if (database.Policy is not null)
        {
            _backupDirectory.Text = database.Policy.BackupDirectory;
            _maxBackups.Value = Math.Clamp(database.Policy.MaxLocalBackups, 1, 1000);
            _verify.Checked = database.Policy.VerifyAfterBackup;
            _schedule.ScheduleCron = database.Policy.ScheduleCron;
        }
        else
        {
            _backupDirectory.Text = @"D:\Backups\OdinVault";
            _maxBackups.Value = 7;
            _verify.Checked = true;
        }

        BuildUi(database.HasPassword);
    }

    private void BuildUi(bool hasPassword)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 14
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField(panel, 0, "نام نمایشی", _name);
        AddField(panel, 1, "SQL Server", _host);
        AddField(panel, 2, "Port", _port);
        AddField(panel, 3, "نام دیتابیس", _database);
        AddField(panel, 4, "Username", _username);
        AddField(panel, 5, hasPassword ? "Password (خالی = بدون تغییر)" : "Password", _password);
        AddField(panel, 6, "مسیر بکاپ", _backupDirectory);
        AddField(panel, 7, "تعداد نگهداری", _maxBackups);
        AddField(panel, 8, "زمان‌بندی", _schedule);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        checks.Controls.AddRange([_enabled, _verify, _trustCertificate, _clearPassword]);
        panel.Controls.Add(checks, 0, 9);
        panel.SetColumnSpan(checks, 2);

        var note = new Label
        {
            Text = "زمان‌بندی را ساده انتخاب کنید. برای الگوهای خاص می‌توانید حالت «پیشرفته (Cron)» را انتخاب کنید.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = SystemColors.GrayText
        };
        panel.Controls.Add(note, 0, 10);
        panel.SetColumnSpan(note, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true
        };
        var cancel = new Button { Text = "انصراف", DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = "ذخیره تغییرات", AutoSize = true };
        save.Click += SaveClicked;

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        panel.Controls.Add(buttons, 0, 13);
        panel.SetColumnSpan(buttons, 2);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(panel);
    }

    private void SaveClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_name.Text) ||
            string.IsNullOrWhiteSpace(_host.Text) ||
            string.IsNullOrWhiteSpace(_database.Text) ||
            string.IsNullOrWhiteSpace(_backupDirectory.Text))
        {
            MessageBox.Show(
                this,
                "نام، سرور، نام دیتابیس و مسیر بکاپ الزامی است.",
                "OdinVault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        DatabaseRequest = new UpdateDatabaseRequest(
            _name.Text.Trim(),
            _host.Text.Trim(),
            (int)_port.Value,
            _database.Text.Trim(),
            NullIfWhiteSpace(_username.Text),
            string.IsNullOrWhiteSpace(_password.Text) ? null : _password.Text,
            _clearPassword.Checked,
            _trustCertificate.Checked,
            _enabled.Checked);

        try
        {
            PolicyRequest = new UpdateBackupPolicyRequest(
                _backupDirectory.Text.Trim(),
                (int)_maxBackups.Value,
                _verify.Checked,
                _schedule.ScheduleCron,
                _enabled.Checked);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "زمان‌بندی", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddField(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = true
        };
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(caption, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class AddDatabaseForm : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _host = new() { Text = "localhost" };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 1433 };
    private readonly TextBox _database = new();
    private readonly TextBox _username = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _backupDirectory = new() { Text = @"D:\Backups\OdinVault" };
    private readonly NumericUpDown _maxBackups = new() { Minimum = 1, Maximum = 1000, Value = 7 };
    private readonly ScheduleEditor _schedule = new();
    private readonly CheckBox _trustCertificate = new() { Text = "Trust Server Certificate", Checked = true, AutoSize = true };
    private readonly CheckBox _verify = new() { Text = "Verify بعد از بکاپ", Checked = true, AutoSize = true };
    private readonly CheckBox _enabled = new() { Text = "فعال", Checked = true, AutoSize = true };

    public CreateDatabaseRequest? Request { get; private set; }

    public AddDatabaseForm()
    {
        Text = "افزودن دیتابیس";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(570, 610);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 13
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField(panel, 0, "نام نمایشی", _name);
        AddField(panel, 1, "SQL Server", _host);
        AddField(panel, 2, "Port", _port);
        AddField(panel, 3, "نام دیتابیس", _database);
        AddField(panel, 4, "Username", _username);
        AddField(panel, 5, "Password", _password);
        AddField(panel, 6, "مسیر بکاپ روی SQL Server", _backupDirectory);
        AddField(panel, 7, "تعداد فایل محلی", _maxBackups);
        AddField(panel, 8, "زمان‌بندی", _schedule);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        checks.Controls.AddRange([_enabled, _verify, _trustCertificate]);
        panel.Controls.Add(checks, 0, 9);
        panel.SetColumnSpan(checks, 2);

        var note = new Label
        {
            Text = "مسیر بکاپ از دید SQL Server است. اگر Agent و SQL Server روی یک ماشین هستند، مسیر محلی مثل D:\\Backups\\OdinVault مناسب است.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = SystemColors.GrayText
        };
        panel.Controls.Add(note, 0, 10);
        panel.SetColumnSpan(note, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true
        };

        var cancel = new Button { Text = "انصراف", DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = "ذخیره", AutoSize = true };
        save.Click += SaveClicked;

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        panel.Controls.Add(buttons, 0, 12);
        panel.SetColumnSpan(buttons, 2);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(panel);
    }

    private static void AddField(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = true
        };

        control.Dock = DockStyle.Fill;
        panel.Controls.Add(caption, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private void SaveClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_name.Text) ||
            string.IsNullOrWhiteSpace(_host.Text) ||
            string.IsNullOrWhiteSpace(_database.Text) ||
            string.IsNullOrWhiteSpace(_backupDirectory.Text))
        {
            MessageBox.Show(this, "نام، سرور، نام دیتابیس و مسیر بکاپ الزامی است.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? scheduleCron;
        try
        {
            scheduleCron = _schedule.ScheduleCron;
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "زمان‌بندی", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Request = new CreateDatabaseRequest(
            _name.Text.Trim(),
            _host.Text.Trim(),
            (int)_port.Value,
            _database.Text.Trim(),
            NullIfWhiteSpace(_username.Text),
            NullIfWhiteSpace(_password.Text),
            _trustCertificate.Checked,
            _backupDirectory.Text.Trim(),
            (int)_maxBackups.Value,
            _verify.Checked,
            scheduleCron,
            _enabled.Checked);

        DialogResult = DialogResult.OK;
        Close();
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class MobileConnectionForm : Form
{
    private readonly AgentApiClient _api;
    private readonly TextBox _baseUrl = new();
    private readonly TextBox _apiKey = new() { ReadOnly = true, UseSystemPasswordChar = true };
    private readonly CheckBox _showKey = new() { Text = "نمایش کلید", AutoSize = true };

    public MobileConnectionForm(AgentApiClient api)
    {
        _api = api;

        Text = "اتصال موبایل";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(650, 330);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        _baseUrl.Text = _api.GetMobileBaseUrl();
        _apiKey.Text = _api.GetApiKey();

        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 6
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var title = new Label
        {
            Text = "اطلاعات کامل اتصال اپ موبایل",
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        };
        root.Controls.Add(title, 0, 0);

        var urlPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2
        };
        urlPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        urlPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        urlPanel.Controls.Add(new Label
        {
            Text = "آدرس Agent / دامنه",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        }, 0, 0);
        _baseUrl.Dock = DockStyle.Fill;
        _baseUrl.RightToLeft = RightToLeft.No;
        urlPanel.Controls.Add(_baseUrl, 1, 0);
        root.Controls.Add(urlPanel, 0, 1);

        var keyPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3
        };
        keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        keyPanel.Controls.Add(new Label
        {
            Text = "API Key",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        }, 0, 0);
        _apiKey.Dock = DockStyle.Fill;
        _apiKey.RightToLeft = RightToLeft.No;
        keyPanel.Controls.Add(_apiKey, 1, 0);
        var copyKey = new Button { Text = "کپی کلید", Dock = DockStyle.Fill };
        copyKey.Click += (_, _) =>
        {
            Clipboard.SetText(_apiKey.Text);
            MessageBox.Show(this, "کلید API کپی شد.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        keyPanel.Controls.Add(copyKey, 2, 0);
        root.Controls.Add(keyPanel, 0, 2);

        _showKey.CheckedChanged += (_, _) => _apiKey.UseSystemPasswordChar = !_showKey.Checked;
        root.Controls.Add(_showKey, 0, 3);

        var note = new Label
        {
            Text = "اگر از دامنه یا Reverse Proxy استفاده می‌کنید، آدرس HTTPS را اینجا وارد و ذخیره کنید. " +
                   "برای شبکه داخلی می‌توانید IP یا نام این سرور را همراه پورت 5188 وارد کنید.",
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            ForeColor = SystemColors.GrayText
        };
        root.Controls.Add(note, 0, 4);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        var close = new Button { Text = "بستن", AutoSize = true };
        close.Click += (_, _) => Close();

        var saveUrl = new Button { Text = "ذخیره آدرس", AutoSize = true };
        saveUrl.Click += (_, _) =>
        {
            try
            {
                _api.SaveMobileBaseUrl(_baseUrl.Text);
                MessageBox.Show(this, "آدرس اتصال موبایل ذخیره شد.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        var copyAll = new Button { Text = "کپی همه اطلاعات", AutoSize = true };
        copyAll.Click += (_, _) =>
        {
            try
            {
                _api.SaveMobileBaseUrl(_baseUrl.Text);
                var text =
                    $"OdinVault Mobile{Environment.NewLine}" +
                    $"Agent URL: {_baseUrl.Text.Trim()}{Environment.NewLine}" +
                    $"API Key: {_apiKey.Text}";
                Clipboard.SetText(text);

                MessageBox.Show(
                    this,
                    "آدرس Agent و API Key با هم کپی شدند.",
                    "OdinVault",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(close);
        buttons.Controls.Add(saveUrl);
        buttons.Controls.Add(copyAll);
        root.Controls.Add(buttons, 0, 5);

        Controls.Add(root);
    }
}
