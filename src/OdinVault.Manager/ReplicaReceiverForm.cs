namespace OdinVault.Manager;

internal sealed class ReplicaReceiverForm : Form
{
    private readonly AgentApiClient api;
    private readonly TextBox folder = new() { Width = 520, RightToLeft = RightToLeft.No };
    private readonly DataGridView received = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(680, 0) };
    private bool busy;

    public ReplicaReceiverForm(AgentApiClient api)
    {
        this.api = api;
        Text = "محل دریافت Replica روی این سرور";
        Size = new Size(860, 650);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        Controls.Add(root);

        var settings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        settings.Controls.Add(new Label
        {
            Text = "این تنظیم فقط روی سروری انجام می‌شود که قرار است بکاپ‌ها را دریافت کند. مثلاً اگر ۱۸۸ مقصد است، این صفحه را روی خود ۱۸۸ باز کنید.",
            AutoSize = true,
            MaximumSize = new Size(700, 0)
        });

        settings.Controls.Add(new Label
        {
            Text = "پوشه ذخیره بکاپ‌های دریافتی",
            AutoSize = true,
            Margin = new Padding(3, 12, 3, 4)
        });
        settings.Controls.Add(folder);

        var browse = new Button { Text = "انتخاب پوشه", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog();
            if (picker.ShowDialog(this) == DialogResult.OK)
                folder.Text = picker.SelectedPath;
        };
        settings.Controls.Add(browse);

        var save = new Button { Text = "ذخیره مسیر دریافت", AutoSize = true };
        save.Click += async (_, _) => await Run(async () =>
        {
            await api.SendAsync<ReplicaSettingsResponse>(
                HttpMethod.Put,
                "api/replica/settings",
                new { directory = folder.Text });

            await LoadReceived();
            status.Text = "مسیر دریافت روی همین سرور ذخیره شد.";
        });
        settings.Controls.Add(save);

        root.Controls.Add(settings, 0, 0);

        received.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "فایل / سرور / دیتابیس",
            DataPropertyName = "RelativePath",
            FillWeight = 65
        });
        received.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "حجم (MB)",
            DataPropertyName = "Size",
            FillWeight = 15
        });
        received.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "دریافت",
            DataPropertyName = "Date",
            FillWeight = 30
        });
        root.Controls.Add(received, 0, 1);
        root.Controls.Add(status, 0, 2);

        Shown += async (_, _) => await Run(async () =>
        {
            folder.Text = (await api.SendAsync<ReplicaSettingsResponse>(
                HttpMethod.Get,
                "api/replica/settings")).Directory;
            await LoadReceived();
        });

        UiLayout.Apply(this);
    }

    private async Task LoadReceived()
    {
        var items = await api.SendAsync<List<ReceivedBackupResponse>>(
            HttpMethod.Get,
            "api/replica/received");

        var calendar = new System.Globalization.PersianCalendar();
        received.DataSource = items.Select(x =>
        {
            var t = x.ReceivedAtUtc.AddMinutes(210);
            return new
            {
                x.RelativePath,
                Size = Math.Round(x.SizeBytes / 1048576d, 1),
                Date = $"{calendar.GetYear(t):0000}/{calendar.GetMonth(t):00}/{calendar.GetDayOfMonth(t):00} {t:HH:mm}"
            };
        }).ToList();
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UseWaitCursor = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            OdinDialog.Show(this, ex.Message, "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false;
            UseWaitCursor = false;
        }
    }
}
