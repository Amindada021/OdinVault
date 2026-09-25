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
    private readonly Button _updateButton = new();

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
        ConfigureButton(_editButton, "ویرایش", async (_, _) => await EditSelectedAsync());
        ConfigureButton(_deleteButton, "حذف", async (_, _) => await DeleteSelectedAsync());
        ConfigureButton(_testButton, "تست اتصال", async (_, _) => await TestSelectedAsync());
        ConfigureButton(_backupButton, "بکاپ انتخاب‌شده‌ها", async (_, _) => await BackupSelectedAsync());
        ConfigureButton(_updateButton, "بررسی بروزرسانی", async (_, _) => await CheckForUpdatesAsync(silent: false));

        actions.Controls.AddRange([_backupButton, _testButton, _deleteButton, _editButton, _discoverButton, _addButton, _refreshButton, _updateButton]);
        root.Controls.Add(actions, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
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
                    await _api.RunBackupAsync(db.Id);
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
