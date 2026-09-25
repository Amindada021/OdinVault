namespace OdinVault.Manager;

internal sealed class DiscoverDatabasesForm : Form
{
    private readonly AgentApiClient _api;
    private readonly TextBox _host = new() { Text = "localhost" };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 1433 };
    private readonly TextBox _username = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _trustCertificate = new() { Text = "Trust Server Certificate", Checked = true, AutoSize = true };
    private readonly TextBox _backupDirectory = new() { Text = @"D:\Backups\OdinVault" };
    private readonly NumericUpDown _maxBackups = new() { Minimum = 1, Maximum = 1000, Value = 7 };
    private readonly ScheduleEditor _schedule = new();
    private readonly CheckBox _verify = new() { Text = "Verify بعد از بکاپ", Checked = true, AutoSize = true };
    private readonly DataGridView _grid = new();
    private readonly Button _discoverButton = new();
    private readonly Button _addSelectedButton = new();
    private IReadOnlyList<DiscoveredDatabaseResponse> _items = [];

    public bool AnyAdded { get; private set; }

    public DiscoverDatabasesForm(AgentApiClient api)
    {
        _api = api;
        Text = "شناسایی دیتابیس‌های SQL Server";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 680);
        Size = new Size(1080, 760);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        BuildUi();
    
        UiLayout.Apply(this);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(root);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        AddField(fields, 0, 0, "SQL Server", _host);
        AddField(fields, 0, 2, "Port", _port);
        AddField(fields, 1, 0, "Username", _username);
        AddField(fields, 1, 2, "Password", _password);
        AddField(fields, 2, 0, "مسیر بکاپ", _backupDirectory);
        AddField(fields, 2, 2, "تعداد نگهداری", _maxBackups);
        AddField(fields, 3, 0, "زمان‌بندی", _schedule);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        checks.Controls.AddRange([_verify, _trustCertificate]);
        fields.Controls.Add(checks, 3, 3);

        root.Controls.Add(fields, 0, 0);

        var topActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        _discoverButton.Text = "شناسایی دیتابیس‌ها";
        _discoverButton.AutoSize = true;
        _discoverButton.Click += async (_, _) => await DiscoverAsync();
        topActions.Controls.Add(_discoverButton);
        root.Controls.Add(topActions, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _grid.Columns["Selected"].Index)
                return;

            UpdateAddButtonState();
        };

        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0)
                return;

            var row = _grid.Rows[e.RowIndex];
            if (row.Tag is not DiscoveredDatabaseResponse item ||
                item.IsSystem ||
                item.IsRegistered ||
                !item.CanBackup)
                return;

            // Clicking the checkbox itself is handled by WinForms.
            if (e.ColumnIndex == _grid.Columns["Selected"].Index)
                return;

            var cell = row.Cells["Selected"];
            cell.Value = !Convert.ToBoolean(cell.Value ?? false);
            UpdateAddButtonState();
        };

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Selected",
            HeaderText = "انتخاب",
            FillWeight = 55,
            ThreeState = false,
            ReadOnly = false
        });
        _grid.Columns.Add("Name", "دیتابیس");
        _grid.Columns.Add("State", "وضعیت");
        _grid.Columns.Add("Recovery", "Recovery");
        _grid.Columns.Add("Access", "دسترسی");
        _grid.Columns.Add("Ready", "آماده بکاپ");
        _grid.Columns.Add("Registered", "ثبت‌شده");

        root.Controls.Add(_grid, 0, 2);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };

        var close = new Button { Text = "بستن", AutoSize = true, DialogResult = DialogResult.Cancel };
        _addSelectedButton.Text = "افزودن دیتابیس‌های انتخاب‌شده";
        _addSelectedButton.AutoSize = true;
        _addSelectedButton.Enabled = false;
        _addSelectedButton.Click += async (_, _) => await AddSelectedAsync();

        bottom.Controls.Add(close);
        bottom.Controls.Add(_addSelectedButton);
        root.Controls.Add(bottom, 0, 3);

        CancelButton = close;
    }

    private static void AddField(TableLayoutPanel panel, int row, int column, string label, Control control)
    {
        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        };
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(caption, column, row);
        panel.Controls.Add(control, column + 1, row);
    }

    private async Task DiscoverAsync()
    {
        if (string.IsNullOrWhiteSpace(_host.Text))
        {
            OdinDialog.Show(this, "آدرس SQL Server را وارد کنید.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            _items = await _api.DiscoverDatabasesAsync(new DiscoverSqlServerRequest(
                _host.Text.Trim(),
                (int)_port.Value,
                NullIfWhiteSpace(_username.Text),
                NullIfWhiteSpace(_password.Text),
                _trustCertificate.Checked));

            _grid.Rows.Clear();

            foreach (var item in _items)
            {
                var readyText = item.CanBackup ? "آماده" : item.IsSystem ? "سیستمی" : "خیر";
                var index = _grid.Rows.Add(
                    item.CanBackup && !item.IsRegistered,
                    item.Name,
                    item.State,
                    item.RecoveryModel,
                    item.HasAccess ? "دارد" : "ندارد",
                    readyText,
                    item.IsRegistered ? "بله" : "خیر");

                var row = _grid.Rows[index];
                row.Tag = item;

                if (item.IsSystem || item.IsRegistered || !item.CanBackup)
                {
                    row.Cells["Selected"].ReadOnly = true;
                }
            }

            UpdateAddButtonState();

            if (_items.Count == 0)
            {
                OdinDialog.Show(this, "هیچ دیتابیسی پیدا نشد.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            OdinDialog.Show(this, ex.Message, "خطای شناسایی SQL Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task AddSelectedAsync()
    {
        _grid.EndEdit();

        var selected = _grid.Rows
            .Cast<DataGridViewRow>()
            .Where(r => r.Tag is DiscoveredDatabaseResponse)
            .Where(r => Convert.ToBoolean(r.Cells["Selected"].Value ?? false))
            .Select(r => (DiscoveredDatabaseResponse)r.Tag!)
            .Where(x => x.CanBackup && !x.IsRegistered)
            .ToList();

        if (selected.Count == 0)
        {
            OdinDialog.Show(this, "حداقل یک دیتابیس آماده بکاپ را انتخاب کنید.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_backupDirectory.Text))
        {
            OdinDialog.Show(this, "مسیر بکاپ را وارد کنید.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true);
        var succeeded = 0;
        var errors = new List<string>();

        try
        {
            foreach (var db in selected)
            {
                try
                {
                    var created = await _api.CreateDatabaseAsync(new CreateDatabaseRequest(
                        db.Name,
                        _host.Text.Trim(),
                        (int)_port.Value,
                        db.Name,
                        NullIfWhiteSpace(_username.Text),
                        NullIfWhiteSpace(_password.Text),
                        _trustCertificate.Checked,
                        _backupDirectory.Text.Trim(),
                        (int)_maxBackups.Value,
                        _verify.Checked,
                        _schedule.ScheduleCron,
                        true));

                    if (created is not null)
                    {
                        await _api.TestDatabaseAsync(created.Id);
                        succeeded++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{db.Name}: {ex.Message}");
                }
            }

            AnyAdded = succeeded > 0;

            var message = $"{succeeded} دیتابیس با موفقیت به OdinVault اضافه شد.";
            if (errors.Count > 0)
                message += Environment.NewLine + Environment.NewLine + "خطاها:" + Environment.NewLine + string.Join(Environment.NewLine, errors);

            OdinDialog.Show(
                this,
                message,
                "OdinVault",
                MessageBoxButtons.OK,
                errors.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            await DiscoverAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (!busy)
            Cursor.Current = Cursors.Default;
        _discoverButton.Enabled = !busy;
        if (busy)
            _addSelectedButton.Enabled = false;
        else
            UpdateAddButtonState();
    }

    private void UpdateAddButtonState()
    {
        _addSelectedButton.Enabled = _grid.Rows
            .Cast<DataGridViewRow>()
            .Any(r =>
                r.Tag is DiscoveredDatabaseResponse item &&
                item.CanBackup &&
                !item.IsRegistered &&
                Convert.ToBoolean(r.Cells["Selected"].Value ?? false));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
