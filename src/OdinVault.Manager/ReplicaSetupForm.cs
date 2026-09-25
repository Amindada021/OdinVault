using System.Text.Json;

namespace OdinVault.Manager;

internal sealed class ReplicaSetupForm : Form
{
    private readonly AgentApiClient api;
    private readonly TextBox folder = new() { Width = 480, RightToLeft = RightToLeft.No };
    private readonly TextBox name = new() { Width = 330, Text = "سرور پشتیبان" };
    private readonly TextBox url = new() { Width = 330, PlaceholderText = "http://188.x.x.x:5188", RightToLeft = RightToLeft.No };
    private readonly TextBox key = new() { Width = 330, UseSystemPasswordChar = true, RightToLeft = RightToLeft.No };
    private readonly CheckedListBox databases = new() { Width = 620, Height = 180, CheckOnClick = true, DisplayMember = "Name" };
    private readonly ComboBox targets = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
    private readonly DataGridView received = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(640, 0) };
    private readonly Label receiveStatus = new() { AutoSize = true, MaximumSize = new Size(640, 0) };
    private bool busy;

    public ReplicaSetupForm(AgentApiClient api)
    {
        this.api = api;
        Text = "پشتیبان و محل دریافت بکاپ";
        Size = new Size(800, 730);
        MinimumSize = new Size(720, 650);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        Controls.Add(tabs);
        var send = new TabPage("ارسال به سرور پشتیبان");
        var receive = new TabPage("این سرور محل دریافت است");
        tabs.TabPages.AddRange([send, receive]);
        var form = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(18) };
        send.Controls.Add(form);
        void Label(string text) => form.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(3, 8, 3, 4) });
        Label("روی سرور اصلی: مقصد را انتخاب یا اضافه کن، دیتابیس‌ها را تیک بزن و اتصال را ذخیره کن.");
        Label("مقصدهای ثبت‌شده"); form.Controls.Add(targets);
        Label("نام مقصد جدید"); form.Controls.Add(name);
        Label("آدرس سرور پشتیبان"); form.Controls.Add(url);
        Label("کلید API سرور پشتیبان (از بخش اتصال موبایل همان سرور)"); form.Controls.Add(key);
        var add = new Button { Text = "افزودن مقصد", AutoSize = true };
        add.Click += async (_, _) => await Run(async () =>
        {
            var target = await api.SendAsync<ReplicaTargetResponse>(HttpMethod.Post, "api/storage-targets/odinvault-replica", new { name = name.Text, baseUrl = url.Text, apiKey = key.Text, isEnabled = true, testConnection = false });
            key.Clear();
            await LoadTargets();
            targets.SelectedItem = targets.Items.Cast<ReplicaTargetResponse>().FirstOrDefault(x => x.Id == target.Id);
            status.Text = "مقصد ثبت شد. دیتابیس‌های مورد نظر را انتخاب و اتصال را ذخیره کن.";
        });
        form.Controls.Add(add);
        Label("دیتابیس‌هایی که بکاپشان به این مقصد ارسال شود"); form.Controls.Add(databases);
        var save = new Button { Text = "ذخیره اتصال دیتابیس‌ها", AutoSize = true };
        save.Click += async (_, _) => await Run(async () =>
        {
            if (targets.SelectedItem is not ReplicaTargetResponse target) throw new InvalidOperationException("ابتدا یک مقصد انتخاب کن.");
            for (var i = 0; i < databases.Items.Count; i++)
            {
                var db = (DatabaseResponse)databases.Items[i];
                if (databases.GetItemChecked(i))
                    await api.SendAsync<JsonElement>(HttpMethod.Put, $"api/databases/{db.Id}/storage-targets/{target.Id}", new { isEnabled = true });
                else
                    await api.SendAsync<JsonElement>(HttpMethod.Delete, $"api/databases/{db.Id}/storage-targets/{target.Id}");
            }
            status.Text = "ذخیره شد. بکاپ‌های بعدی این دیتابیس‌ها خودکار ارسال می‌شوند.";
        });
        form.Controls.Add(save); form.Controls.Add(status);
        targets.SelectedIndexChanged += async (_, _) => await Run(LoadLinks);

        var receiveRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(18) };
        receiveRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        receiveRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        receiveRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        receive.Controls.Add(receiveRoot);
        var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        settings.Controls.Add(new Label { Text = "روی سرور پشتیبان: فقط پوشه دریافت را انتخاب کن؛ ثبت دیتابیس لازم نیست.\nبکاپ‌ها خودکار به تفکیک نام سرور اصلی و دیتابیس پوشه‌بندی می‌شوند.", AutoSize = true });
        settings.Controls.Add(folder);
        var browse = new Button { Text = "انتخاب پوشه دریافت", AutoSize = true };
        browse.Click += (_, _) => { using var picker = new FolderBrowserDialog(); if (picker.ShowDialog(this) == DialogResult.OK) folder.Text = picker.SelectedPath; };
        settings.Controls.Add(browse);
        var saveFolder = new Button { Text = "ذخیره مسیر و بروزرسانی فایل‌ها", AutoSize = true };
        saveFolder.Click += async (_, _) => await Run(async () =>
        {
            await api.SendAsync<ReplicaSettingsResponse>(HttpMethod.Put, "api/replica/settings", new { directory = folder.Text });
            await LoadReceived();
            receiveStatus.Text = "مسیر دریافت ذخیره شد؛ نیازی به ری‌استارت سرویس نیست.";
        });
        settings.Controls.Add(saveFolder);
        receiveRoot.Controls.Add(settings, 0, 0);
        received.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "فایل / سرور / دیتابیس", DataPropertyName = "RelativePath", FillWeight = 65 });
        received.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "حجم (MB)", DataPropertyName = "Size", FillWeight = 15 });
        received.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "دریافت (تهران)", DataPropertyName = "Date", FillWeight = 30 });
        receiveRoot.Controls.Add(received, 0, 1);
        receiveRoot.Controls.Add(receiveStatus, 0, 2);
        Shown += async (_, _) => await Run(async () =>
        {
            foreach (var db in await api.GetDatabasesAsync()) databases.Items.Add(db);
            folder.Text = (await api.SendAsync<ReplicaSettingsResponse>(HttpMethod.Get, "api/replica/settings")).Directory;
            await LoadTargets();
            await LoadLinks();
            await LoadReceived();
        });
    }

    private async Task LoadTargets()
    {
        var items = await api.SendAsync<List<ReplicaTargetResponse>>(HttpMethod.Get, "api/storage-targets");
        targets.DataSource = items.Where(x => x.Type == 5 && x.IsEnabled).ToList();
    }
    private async Task LoadLinks()
    {
        if (targets.SelectedItem is not ReplicaTargetResponse target) return;
        for (var i = 0; i < databases.Items.Count; i++)
        {
            var db = (DatabaseResponse)databases.Items[i];
            var links = await api.SendAsync<JsonElement>(HttpMethod.Get, $"api/databases/{db.Id}/storage-targets");
            var linked = links.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == target.Id && x.GetProperty("isEnabled").GetBoolean());
            databases.SetItemChecked(i, linked);
        }
    }
    private async Task LoadReceived()
    {
        var items = await api.SendAsync<List<ReceivedBackupResponse>>(HttpMethod.Get, "api/replica/received");
        var calendar = new System.Globalization.PersianCalendar();
        received.DataSource = items.Select(x =>
        {
            var t = x.ReceivedAtUtc.AddMinutes(210);
            return new { x.RelativePath, Size = Math.Round(x.SizeBytes / 1048576d, 1), Date = $"{calendar.GetYear(t):0000}/{calendar.GetMonth(t):00}/{calendar.GetDayOfMonth(t):00} {t:HH:mm}" };
        }).ToList();
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UseWaitCursor = true;
        try { await action(); }
        catch (Exception ex) { OdinDialog.Show(this, ex.Message, "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { busy = false; UseWaitCursor = false; }
    }
}
