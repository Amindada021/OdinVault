namespace OdinVault.Manager;

internal sealed class MainForm : Form
{
    private readonly AgentApiClient _api = new();
    private readonly GitHubUpdateService _updates = new();
    private readonly Label _agentStatus = new();
    private readonly Label _lastRefresh = new();
    private readonly DataGridView _grid = new();
    private readonly Button _refreshButton = new();
    private readonly Button _addButton = new();
    private readonly Button _discoverButton = new();
    private readonly Button _editButton = new();
    private readonly Button _deleteButton = new();
    private readonly Button _testButton = new();
    private readonly Button _backupButton = new();
    private readonly Button _mobileConnectionButton = new();
    private readonly Button _updateButton = new();
    private readonly Panel _contentHost = new();
    private readonly Label _pageTitle = new();
    private readonly Dictionary<string, Button> _navigationButtons = new(StringComparer.Ordinal);
    private Control? _databasesPage;

    public MainForm()
    {
        Text = "OdinVault Manager";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(1100, 700);
        Size = new Size(1360, 840);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        BuildUi();

        Shown += async (_, _) =>
        {
            await RefreshAllAsync();
            await CheckForUpdatesAsync(silent: true);
        };
        FormClosed += (_, _) =>
        {
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
            Padding = new Padding(24, 18, 24, 24),
            BackColor = Color.FromArgb(245, 247, 250)
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
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
            Font = new Font(Font.FontFamily, 8.5F)
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

        AddNavigationButton(navigation, "dashboard", "داشبورد");
        AddNavigationButton(navigation, "databases", "دیتابیس‌ها");
        AddNavigationButton(navigation, "backups", "بکاپ‌ها");
        AddNavigationButton(navigation, "storage", "ذخیره‌سازی");
        AddNavigationButton(navigation, "restore", "بازیابی");
        AddNavigationButton(navigation, "alerts", "هشدارها");
        AddNavigationButton(navigation, "reports", "گزارش‌ها");
        AddNavigationButton(navigation, "settings", "تنظیمات");
        sidebar.Controls.Add(navigation, 0, 1);

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
            BackColor = Color.FromArgb(34, 44, 60)
        };
        footer.Controls.Add(new Label
        {
            Text = "● Agent\r\nlocalhost:5188",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 8, 10, 8),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(190, 205, 220),
            Font = new Font(Font.FontFamily, 9F)
        });
        sidebar.Controls.Add(footer, 0, 2);

        return sidebar;
    }

    private void AddNavigationButton(FlowLayoutPanel host, string key, string text)
    {
        var button = new Button
        {
            Text = text,
            Width = 198,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(12, 0, 12, 0),
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
                    _contentHost.Controls.Add(BuildPlaceholderPage(
                        "داشبورد OdinVault",
                        "در مرحله بعد کارت‌های وضعیت، هشدارهای مهم و نمودارهای بکاپ اینجا قرار می‌گیرند."));
                    break;
                case "backups":
                    _pageTitle.Text = "بکاپ‌ها";
                    _contentHost.Controls.Add(BuildPlaceholderPage(
                        "بکاپ‌ها و Jobها",
                        "نمایش صف، بکاپ‌های در حال اجرا و تاریخچه در مرحله مربوط به این بخش اضافه می‌شود."));
                    break;
                case "storage":
                    _pageTitle.Text = "ذخیره‌سازی";
                    _contentHost.Controls.Add(BuildPlaceholderPage(
                        "ذخیره‌سازی و Replica",
                        "مدیریت Local، Google Drive و OdinVault Replica در این بخش قرار می‌گیرد."));
                    break;
                case "restore":
                    _pageTitle.Text = "بازیابی";
                    _contentHost.Controls.Add(BuildPlaceholderPage(
                        "Restore",
                        "Wizard بازیابی در مرحله Restore اضافه می‌شود."));
                    break;
                case "alerts":
                    _pageTitle.Text = "هشدارها";
                    _contentHost.Controls.Add(BuildPlaceholderPage(
                        "هشدارها",
                        "خطاهای بکاپ، فضای کم و وضعیت Replica اینجا نمایش داده می‌شوند."));
                    break;
                case "reports":
                    _pageTitle.Text = "گزارش‌ها";
                    _contentHost.Controls.Add(BuildPlaceholderPage(
                        "گزارش‌ها",
                        "تحلیل روند حجم، زمان و موفقیت بکاپ‌ها در مرحله Charts/Reports اضافه می‌شود."));
                    break;
                default:
                    _pageTitle.Text = "تنظیمات";
                    _contentHost.Controls.Add(BuildPlaceholderPage(
                        "تنظیمات",
                        "تنظیمات Agent، امنیت، اعلان‌ها و بروزرسانی در این بخش قرار می‌گیرند."));
                    break;
            }
        }
        finally
        {
            _contentHost.ResumeLayout();
        }
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var actionsCard = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(14, 14, 14, 10),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            AutoScroll = true,
            BackColor = Color.White
        };

        ConfigureButton(_refreshButton, "بروزرسانی", async (_, _) => await RefreshAllAsync());
        ConfigureButton(_addButton, "افزودن دستی", async (_, _) => await AddDatabaseAsync());
        ConfigureButton(_discoverButton, "شناسایی دیتابیس‌ها", async (_, _) => await DiscoverDatabasesAsync());
        ConfigureButton(_editButton, "ویرایش", async (_, _) => await EditSelectedAsync());
        ConfigureButton(_deleteButton, "حذف", async (_, _) => await DeleteSelectedAsync());
        ConfigureButton(_testButton, "تست اتصال", async (_, _) => await TestSelectedAsync());
        ConfigureButton(_backupButton, "بکاپ انتخاب‌شده‌ها", async (_, _) => await BackupSelectedAsync());
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
        _grid.ColumnHeadersHeight = 42;
        _grid.RowTemplate.Height = 38;
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
        _grid.Columns.Add("Enabled", "فعال");
    }

    private static void ConfigureButton(Button button, string text, EventHandler handler)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 36;
        button.Padding = new Padding(12, 2, 12, 2);
        button.FlatStyle = FlatStyle.System;
        button.Click -= handler;
        button.Click += handler;
    }

    private async Task RefreshAllAsync()
    {
        SetBusy(true);
        try
        {
            var health = await _api.GetHealthAsync();
            _agentStatus.Text = health?.Status?.Equals("healthy", StringComparison.OrdinalIgnoreCase) == true
                ? "● Agent فعال است"
                : "● Agent پاسخ می‌دهد";

            var databases = await _api.GetDatabasesAsync();
            _grid.Rows.Clear();

            foreach (var db in databases)
            {
                var rowIndex = _grid.Rows.Add(
                    false,
                    db.Name,
                    db.Port is > 0 ? $"{db.Host}:{db.Port}" : db.Host,
                    db.DatabaseName,
                    ScheduleEditor.FormatCron(db.Policy?.ScheduleCron),
                    db.Policy is null ? "-" : $"{db.Policy.MaxLocalBackups} فایل",
                    db.Policy?.VerifyAfterBackup == true ? "بله" : "خیر",
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
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

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
