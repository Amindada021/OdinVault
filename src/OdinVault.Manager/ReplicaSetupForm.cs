using System.Text.Json;

namespace OdinVault.Manager;

internal sealed class ReplicaSetupForm : Form
{
    private readonly AgentApiClient api;

    private readonly TextBox name = new() { Width = 380, Text = "سرور پشتیبان" };
    private readonly TextBox url = new() { Width = 380, PlaceholderText = "http://188.x.x.x:5188", RightToLeft = RightToLeft.No };
    private readonly TextBox key = new() { Width = 380, UseSystemPasswordChar = true, RightToLeft = RightToLeft.No };
    private readonly CheckedListBox databases = new() { Width = 680, Height = 220, CheckOnClick = true, DisplayMember = "Name" };
    private readonly ComboBox targets = new() { Width = 380, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(700, 0) };

    private readonly DataGridView targetsGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };

    private bool busy;

    public ReplicaSetupForm(AgentApiClient api)
    {
        this.api = api;

        Text = "مدیریت مقصدهای Replica";
        Size = new Size(900, 760);
        MinimumSize = new Size(800, 680);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var connectionTab = new TabPage("اتصال و دیتابیس‌ها");
        var listTab = new TabPage("مقصدهای ذخیره‌شده");
        tabs.TabPages.Add(connectionTab);
        tabs.TabPages.Add(listTab);
        Controls.Add(tabs);

        BuildConnectionTab(connectionTab);
        BuildTargetsTab(listTab);

        Shown += async (_, _) => await Run(async () =>
        {
            foreach (var db in await api.GetDatabasesAsync())
                databases.Items.Add(db);

            await LoadTargets();
            await LoadLinks();
        });

        UiLayout.Apply(this);
    }

    private void BuildConnectionTab(TabPage page)
    {
        var form = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(22)
        };
        page.Controls.Add(form);

        void Label(string text) => form.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(700, 0),
            Margin = new Padding(3, 8, 3, 4)
        });

        Label("برای مقصد جدید، آدرس Agent و کلید همان سرور را وارد کن. قبل از ذخیره می‌توانی اتصال را مستقل تست کنی.");

        Label("مقصد انتخاب‌شده");
        form.Controls.Add(targets);

        Label("نام مقصد");
        form.Controls.Add(name);

        Label("آدرس Agent سرور مقصد");
        form.Controls.Add(url);

        Label("کلید API سرور مقصد");
        form.Controls.Add(key);

        form.Controls.Add(new Label
        {
            Text = "برای مقصد ذخیره‌شده، اگر نمی‌خواهی کلید عوض شود فیلد کلید را خالی بگذار.",
            AutoSize = true,
            ForeColor = Color.DimGray
        });

        var testConnection = new Button { Text = "تست اتصال", AutoSize = true };
        testConnection.Click += async (_, _) => await Run(async () =>
        {
            ReplicaConnectionTestResponse result;

            if (targets.SelectedItem is ReplicaTargetResponse selected &&
                string.IsNullOrWhiteSpace(key.Text) &&
                string.Equals(selected.BaseUrl?.TrimEnd('/'), url.Text.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                var saved = await api.SendAsync<StorageConnectionTestResponse>(
                    HttpMethod.Post,
                    $"api/storage-targets/{selected.Id}/connection-test");

                result = new ReplicaConnectionTestResponse(saved.Success, null, saved.Message);
            }
            else
            {
                result = await api.SendAsync<ReplicaConnectionTestResponse>(
                    HttpMethod.Post,
                    "api/storage-targets/odinvault-replica/test-connection",
                    new { baseUrl = url.Text, apiKey = key.Text });
            }

            status.Text = result.Success ? $"✓ {result.Message}" : $"✕ {result.Message}";

            OdinDialog.Show(
                this,
                result.Message ?? (result.Success ? "اتصال با موفقیت برقرار شد." : "اتصال برقرار نشد."),
                result.Success ? "تست اتصال موفق" : "تست اتصال ناموفق",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        });
        form.Controls.Add(testConnection);

        var add = new Button { Text = "افزودن مقصد", AutoSize = true };
        add.Click += async (_, _) => await Run(async () =>
        {
            var target = await api.SendAsync<ReplicaTargetResponse>(
                HttpMethod.Post,
                "api/storage-targets/odinvault-replica",
                new
                {
                    name = name.Text,
                    baseUrl = url.Text,
                    apiKey = key.Text,
                    isEnabled = true,
                    testConnection = true
                });

            key.Clear();
            await LoadTargets();
            targets.SelectedItem = targets.Items.Cast<ReplicaTargetResponse>()
                .FirstOrDefault(x => x.Id == target.Id);

            status.Text = "مقصد با موفقیت تست و ذخیره شد.";

            OdinDialog.Success(
                this,
                $"مقصد «{target.Name}» ثبت شد و ارتباط با Agent مقصد تأیید شد.");
        });
        form.Controls.Add(add);

        var edit = new Button { Text = "ذخیره تغییرات مقصد", AutoSize = true };
        edit.Click += async (_, _) => await Run(async () =>
        {
            if (targets.SelectedItem is not ReplicaTargetResponse target)
                throw new InvalidOperationException("ابتدا یک مقصد انتخاب کن.");

            await api.SendAsync<JsonElement>(
                HttpMethod.Put,
                $"api/storage-targets/{target.Id}",
                new
                {
                    name = name.Text,
                    folderId = (string?)null,
                    isEnabled = true,
                    baseUrl = url.Text,
                    apiKey = string.IsNullOrWhiteSpace(key.Text) ? null : key.Text
                });

            key.Clear();

            var test = await api.SendAsync<StorageConnectionTestResponse>(
                HttpMethod.Post,
                $"api/storage-targets/{target.Id}/connection-test");

            await LoadTargets();
            targets.SelectedItem = targets.Items.Cast<ReplicaTargetResponse>()
                .FirstOrDefault(x => x.Id == target.Id);

            if (test.Success)
            {
                status.Text = $"✓ {test.Message}";
                OdinDialog.Success(this, "تغییرات مقصد ذخیره شد و تست اتصال موفق بود.");
            }
            else
            {
                status.Text = $"✕ {test.Message}";
                OdinDialog.Warning(
                    this,
                    $"تغییرات ذخیره شد، اما اتصال مقصد برقرار نیست.\n\nدلیل: {test.Message}",
                    "اتصال مقصد ناموفق");
            }
        });
        form.Controls.Add(edit);

        Label("دیتابیس‌هایی که بکاپشان به این مقصد ارسال شود");
        form.Controls.Add(databases);

        form.Controls.Add(new Label
        {
            Text = "هر دیتابیس می‌تواند همزمان به چند مقصد Replica وصل باشد.",
            AutoSize = true,
            ForeColor = Color.DimGray
        });

        var save = new Button { Text = "ذخیره اتصال دیتابیس‌ها", AutoSize = true };
        save.Click += async (_, _) => await Run(async () =>
        {
            if (targets.SelectedItem is not ReplicaTargetResponse target)
                throw new InvalidOperationException("ابتدا یک مقصد انتخاب کن.");

            var linkedCount = 0;

            for (var i = 0; i < databases.Items.Count; i++)
            {
                var db = (DatabaseResponse)databases.Items[i];
                if (databases.GetItemChecked(i))
                {
                    await api.SendAsync<JsonElement>(
                        HttpMethod.Put,
                        $"api/databases/{db.Id}/storage-targets/{target.Id}",
                        new { isEnabled = true });
                    linkedCount++;
                }
                else
                {
                    await api.SendAsync<JsonElement>(
                        HttpMethod.Delete,
                        $"api/databases/{db.Id}/storage-targets/{target.Id}");
                }
            }

            status.Text = $"اتصال ذخیره شد؛ {linkedCount} دیتابیس به این مقصد متصل است.";

            OdinDialog.Success(
                this,
                $"اتصال دیتابیس‌ها به مقصد «{target.Name}» ذخیره شد.\nتعداد دیتابیس‌های متصل: {linkedCount}");
        });

        form.Controls.Add(save);
        form.Controls.Add(status);

        targets.SelectedIndexChanged += async (_, _) => await Run(async () =>
        {
            if (targets.SelectedItem is ReplicaTargetResponse selected)
            {
                name.Text = selected.Name;
                url.Text = selected.BaseUrl ?? "";
                key.Clear();
                status.Text = "مقصد انتخاب شد. کلید فعلی به‌صورت امن ذخیره شده است.";
            }

            await LoadLinks();
        });
    }

    private void BuildTargetsTab(TabPage page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        page.Controls.Add(root);

        targetsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "نام مقصد",
            DataPropertyName = "Name",
            FillWeight = 35
        });
        targetsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "آدرس",
            DataPropertyName = "BaseUrl",
            FillWeight = 65
        });
        root.Controls.Add(targetsGrid, 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(4)
        };

        var refresh = new Button { Text = "بروزرسانی لیست", AutoSize = true };
        refresh.Click += async (_, _) => await Run(LoadTargets);

        var test = new Button { Text = "تست مقصد", AutoSize = true };
        test.Click += async (_, _) => await Run(async () =>
        {
            var target = SelectedGridTarget();

            var result = await api.SendAsync<StorageConnectionTestResponse>(
                HttpMethod.Post,
                $"api/storage-targets/{target.Id}/connection-test");

            OdinDialog.Show(
                this,
                result.Message ?? (result.Success ? "اتصال برقرار است." : "اتصال برقرار نیست."),
                result.Success ? "تست اتصال موفق" : "تست اتصال ناموفق",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        });

        var edit = new Button { Text = "ویرایش", AutoSize = true };
        edit.Click += (_, _) =>
        {
            var target = SelectedGridTarget();
            var match = targets.Items.Cast<ReplicaTargetResponse>().FirstOrDefault(x => x.Id == target.Id);
            if (match is not null)
                targets.SelectedItem = match;

            name.Text = target.Name;
            url.Text = target.BaseUrl ?? "";
            key.Clear();
            ((TabControl)page.Parent!).SelectedIndex = 0;
        };

        var delete = new Button { Text = "حذف", AutoSize = true };
        delete.Click += async (_, _) => await Run(async () =>
        {
            var target = SelectedGridTarget();

            if (!OdinDialog.Confirm(this, $"مقصد «{target.Name}» حذف شود؟"))
                return;

            try
            {
                await api.SendAsync<JsonElement>(
                    HttpMethod.Delete,
                    $"api/storage-targets/{target.Id}");

                OdinDialog.Success(this, "مقصد حذف شد.");
            }
            catch (Exception ex) when (
                ex.Message.Contains("history", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("تاریخچه", StringComparison.OrdinalIgnoreCase))
            {
                await api.SendAsync<JsonElement>(
                    HttpMethod.Put,
                    $"api/storage-targets/{target.Id}",
                    new
                    {
                        name = target.Name,
                        folderId = (string?)null,
                        isEnabled = false,
                        baseUrl = target.BaseUrl,
                        apiKey = (string?)null
                    });

                OdinDialog.Show(
                    this,
                    "این مقصد دارای تاریخچه Replica است؛ برای حفظ تاریخچه غیرفعال شد و از لیست مقصدهای فعال خارج می‌شود.",
                    "مقصد غیرفعال شد",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            await LoadTargets();
        });

        actions.Controls.Add(refresh);
        actions.Controls.Add(test);
        actions.Controls.Add(edit);
        actions.Controls.Add(delete);
        root.Controls.Add(actions, 0, 1);
    }

    private ReplicaTargetResponse SelectedGridTarget()
    {
        if (targetsGrid.CurrentRow?.DataBoundItem is not ReplicaTargetResponse target)
            throw new InvalidOperationException("ابتدا یک مقصد را از لیست انتخاب کن.");

        return target;
    }

    private async Task LoadTargets()
    {
        var items = await api.SendAsync<List<ReplicaTargetResponse>>(
            HttpMethod.Get,
            "api/storage-targets");

        var active = items.Where(x => x.Type == 5 && x.IsEnabled).ToList();

        var selectedId = (targets.SelectedItem as ReplicaTargetResponse)?.Id;

        targets.DataSource = null;
        targets.DataSource = active;

        targetsGrid.DataSource = null;
        targetsGrid.DataSource = active;

        if (selectedId.HasValue)
            targets.SelectedItem = active.FirstOrDefault(x => x.Id == selectedId.Value);
        else if (active.Count > 0)
            targets.SelectedIndex = 0;
    }

    private async Task LoadLinks()
    {
        if (targets.SelectedItem is not ReplicaTargetResponse target)
            return;

        for (var i = 0; i < databases.Items.Count; i++)
        {
            var db = (DatabaseResponse)databases.Items[i];
            var links = await api.SendAsync<JsonElement>(
                HttpMethod.Get,
                $"api/databases/{db.Id}/storage-targets");

            var linked = links.EnumerateArray().Any(x =>
                x.GetProperty("id").GetGuid() == target.Id &&
                x.TryGetProperty("linkEnabled", out var linkEnabled) &&
                linkEnabled.GetBoolean());

            databases.SetItemChecked(i, linked);
        }
    }

    private async Task Run(Func<Task> action)
    {
        if (busy)
            return;

        busy = true;
        UseWaitCursor = true;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            OdinDialog.Show(
                this,
                ex.Message,
                "خطای OdinVault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false;
            UseWaitCursor = false;
        }
    }
}
