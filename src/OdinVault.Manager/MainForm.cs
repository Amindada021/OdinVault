namespace OdinVault.Manager;

internal sealed class MainForm : Form
{
    private readonly AgentApiClient _api = new();
    private readonly Label _agentStatus = new();
    private readonly Label _lastRefresh = new();
    private readonly DataGridView _grid = new();
    private readonly Button _refreshButton = new();
    private readonly Button _addButton = new();
    private readonly Button _discoverButton = new();
    private readonly Button _testButton = new();
    private readonly Button _backupButton = new();

    public MainForm()
    {
        Text = "OdinVault Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 620);
        Size = new Size(1120, 720);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        BuildUi();

        Shown += async (_, _) => await RefreshAllAsync();
        FormClosed += (_, _) => _api.Dispose();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        var title = new Label
        {
            Text = "OdinVault",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 22F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        };

        _agentStatus.Text = "در حال بررسی Agent...";
        _agentStatus.AutoSize = true;
        _agentStatus.Dock = DockStyle.Fill;
        _agentStatus.TextAlign = ContentAlignment.MiddleRight;

        _lastRefresh.Text = "";
        _lastRefresh.AutoSize = true;
        _lastRefresh.Dock = DockStyle.Fill;
        _lastRefresh.TextAlign = ContentAlignment.MiddleLeft;

        header.Controls.Add(title, 0, 0);
        header.SetColumnSpan(title, 2);
        header.Controls.Add(_agentStatus, 0, 1);
        header.Controls.Add(_lastRefresh, 1, 1);
        root.Controls.Add(header, 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        ConfigureButton(_refreshButton, "بروزرسانی", async (_, _) => await RefreshAllAsync());
        ConfigureButton(_addButton, "افزودن دستی", async (_, _) => await AddDatabaseAsync());
        ConfigureButton(_discoverButton, "شناسایی دیتابیس‌های سرور", async (_, _) => await DiscoverDatabasesAsync());
        ConfigureButton(_testButton, "تست اتصال", async (_, _) => await TestSelectedAsync());
        ConfigureButton(_backupButton, "بکاپ الان", async (_, _) => await BackupSelectedAsync());

        actions.Controls.AddRange([_backupButton, _testButton, _discoverButton, _addButton, _refreshButton]);
        root.Controls.Add(actions, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.FixedSingle;

        _grid.Columns.Add("Name", "نام");
        _grid.Columns.Add("Server", "سرور");
        _grid.Columns.Add("Database", "دیتابیس");
        _grid.Columns.Add("Schedule", "زمان‌بندی");
        _grid.Columns.Add("Retention", "نگهداری");
        _grid.Columns.Add("Verify", "Verify");
        _grid.Columns.Add("Enabled", "فعال");

        root.Controls.Add(_grid, 0, 2);
    }

    private static void ConfigureButton(Button button, string text, EventHandler handler)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 36;
        button.Padding = new Padding(12, 2, 12, 2);
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
                    db.Name,
                    db.Port is > 0 ? $"{db.Host}:{db.Port}" : db.Host,
                    db.DatabaseName,
                    string.IsNullOrWhiteSpace(db.Policy?.ScheduleCron) ? "دستی" : db.Policy.ScheduleCron,
                    db.Policy is null ? "-" : $"{db.Policy.MaxLocalBackups} فایل",
                    db.Policy?.VerifyAfterBackup == true ? "بله" : "خیر",
                    db.IsEnabled ? "بله" : "خیر");

                _grid.Rows[rowIndex].Tag = db.Id;
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

    private async Task BackupSelectedAsync()
    {
        if (!TryGetSelectedDatabaseId(out var id))
            return;

        var answer = MessageBox.Show(
            this,
            "برای دیتابیس انتخاب‌شده همین حالا بکاپ گرفته شود؟",
            "OdinVault",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        SetBusy(true);
        try
        {
            await _api.RunBackupAsync(id);
            MessageBox.Show(
                this,
                "بکاپ با موفقیت انجام شد.",
                "OdinVault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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
        UseWaitCursor = busy;
        _refreshButton.Enabled = !busy;
        _addButton.Enabled = !busy;
        _discoverButton.Enabled = !busy;
        _testButton.Enabled = !busy;
        _backupButton.Enabled = !busy;
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
    private readonly TextBox _cron = new();
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
        AddField(panel, 8, "Cron (UTC، اختیاری)", _cron);

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
            NullIfWhiteSpace(_cron.Text),
            _enabled.Checked);

        DialogResult = DialogResult.OK;
        Close();
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
