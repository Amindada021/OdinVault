using System.Text.Json;

namespace OdinVault.Manager;

internal sealed class ReplicaSetupForm : Form
{
    private readonly AgentApiClient api;
    private readonly TextBox name = new() { Width = 360, Text = "سرور پشتیبان" };
    private readonly TextBox url = new() { Width = 360, PlaceholderText = "http://188.x.x.x:5188", RightToLeft = RightToLeft.No };
    private readonly TextBox key = new() { Width = 360, UseSystemPasswordChar = true, RightToLeft = RightToLeft.No };
    private readonly CheckedListBox databases = new() { Width = 650, Height = 220, CheckOnClick = true, DisplayMember = "Name" };
    private readonly ComboBox targets = new() { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(680, 0) };
    private bool busy;

    public ReplicaSetupForm(AgentApiClient api)
    {
        this.api = api;
        Text = "مقصدهای Replica";
        Size = new Size(820, 690);
        MinimumSize = new Size(740, 620);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10);

        var form = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(22)
        };
        Controls.Add(form);

        void Label(string text) => form.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Margin = new Padding(3, 8, 3, 4)
        });

        Label("در این صفحه فقط مقصدهای ارسال بکاپِ همین سرور را تعریف می‌کنید. مسیر ذخیره هر مقصد باید روی خود سرور مقصد تنظیم شود.");
        Label("مقصدهای ثبت‌شده");
        form.Controls.Add(targets);

        Label("نام مقصد جدید");
        form.Controls.Add(name);

        Label("آدرس Agent سرور مقصد");
        form.Controls.Add(url);

        Label("کلید API سرور مقصد");
        form.Controls.Add(key);

        form.Controls.Add(new Label
        {
            Text = "بعد از ذخیره، کلید دوباره نمایش داده نمی‌شود؛ آدرس مقصد باقی می‌ماند.",
            AutoSize = true,
            ForeColor = Color.DimGray
        });

        var testConnection = new Button { Text = "تست اتصال", AutoSize = true };
        testConnection.Click += async (_, _) => await Run(async () =>
        {
            if (targets.SelectedItem is ReplicaTargetResponse selected &&
                string.IsNullOrWhiteSpace(key.Text) &&
                string.Equals(selected.BaseUrl?.TrimEnd('/'), url.Text.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                var savedResult = await api.SendAsync<StorageConnectionTestResponse>(
                    HttpMethod.Post,
                    $"api/storage-targets/{selected.Id}/connection-test");

                status.Text = savedResult.Success
                    ? $"✓ {savedResult.Message}"
                    : $"✕ {savedResult.Message}";
                return;
            }

            var result = await api.SendAsync<ReplicaConnectionTestResponse>(
                HttpMethod.Post,
                "api/storage-targets/odinvault-replica/test-connection",
                new { baseUrl = url.Text, apiKey = key.Text });

            status.Text = result.Success
                ? $"✓ {result.Message}"
                : $"✕ {result.Message}";
        });
        form.Controls.Add(testConnection);

        var add = new Button { Text = "افزودن و تست مقصد", AutoSize = true };
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

            status.Text = "مقصد تست و ثبت شد. حالا دیتابیس‌های موردنظر را به همین مقصد وصل کنید.";
        });
        form.Controls.Add(add);

        var edit = new Button { Text = "ویرایش مقصد انتخاب‌شده", AutoSize = true };
        edit.Click += async (_, _) => await Run(async () =>
        {
            if (targets.SelectedItem is not ReplicaTargetResponse target)
                throw new InvalidOperationException("ابتدا یک مقصد را از لیست انتخاب کنید.");

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

            status.Text = test.Success
                ? $"ویرایش ذخیره شد. ✓ {test.Message}"
                : $"ویرایش ذخیره شد اما تست اتصال ناموفق است: {test.Message}";

            await LoadTargets();
            targets.SelectedItem = targets.Items.Cast<ReplicaTargetResponse>()
                .FirstOrDefault(x => x.Id == target.Id);
        });
        form.Controls.Add(edit);

        var delete = new Button { Text = "حذف مقصد انتخاب‌شده", AutoSize = true };
        delete.Click += async (_, _) => await Run(async () =>
        {
            if (targets.SelectedItem is not ReplicaTargetResponse target)
                throw new InvalidOperationException("ابتدا یک مقصد را از لیست انتخاب کنید.");

            if (OdinDialog.Show(
                    this,
                    $"مقصد «{target.Name}» از لیست حذف شود؟",
                    "OdinVault",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                await api.SendAsync<JsonElement>(
                    HttpMethod.Delete,
                    $"api/storage-targets/{target.Id}");
                status.Text = "مقصد حذف شد.";
            }
            catch (Exception ex) when (ex.Message.Contains("history", StringComparison.OrdinalIgnoreCase) ||
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
                status.Text = "این مقصد تاریخچه Replica داشت؛ برای حفظ تاریخچه غیرفعال و از لیست فعال حذف شد.";
            }

            await LoadTargets();
            url.Clear();
            key.Clear();
        });
        form.Controls.Add(delete);

        Label("دیتابیس‌هایی که بکاپشان به این مقصد ارسال شود");
        form.Controls.Add(databases);

        form.Controls.Add(new Label
        {
            Text = "یک دیتابیس می‌تواند همزمان به چند مقصد Replica وصل باشد. مقصد را انتخاب کنید، تیک‌های همان مقصد را تنظیم کنید و ذخیره بزنید.",
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            ForeColor = Color.DimGray
        });

        var save = new Button { Text = "ذخیره اتصال دیتابیس‌ها به این مقصد", AutoSize = true };
        save.Click += async (_, _) => await Run(async () =>
        {
            if (targets.SelectedItem is not ReplicaTargetResponse target)
                throw new InvalidOperationException("ابتدا یک مقصد انتخاب کنید.");

            for (var i = 0; i < databases.Items.Count; i++)
            {
                var db = (DatabaseResponse)databases.Items[i];
                if (databases.GetItemChecked(i))
                {
                    await api.SendAsync<JsonElement>(
                        HttpMethod.Put,
                        $"api/databases/{db.Id}/storage-targets/{target.Id}",
                        new { isEnabled = true });
                }
                else
                {
                    await api.SendAsync<JsonElement>(
                        HttpMethod.Delete,
                        $"api/databases/{db.Id}/storage-targets/{target.Id}");
                }
            }

            status.Text = "اتصال دیتابیس‌ها به این مقصد ذخیره شد.";
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
                status.Text = "مقصد انتخاب شد؛ نام و آدرس قابل ویرایش‌اند. برای نگه‌داشتن کلید فعلی، فیلد کلید را خالی بگذارید.";
            }

            await LoadLinks();
        });

        Shown += async (_, _) => await Run(async () =>
        {
            foreach (var db in await api.GetDatabasesAsync())
                databases.Items.Add(db);

            await LoadTargets();
            await LoadLinks();
        });

        UiLayout.Apply(this);
    }

    private async Task LoadTargets()
    {
        var items = await api.SendAsync<List<ReplicaTargetResponse>>(HttpMethod.Get, "api/storage-targets");
        targets.DataSource = items.Where(x => x.Type == 5 && x.IsEnabled).ToList();
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
