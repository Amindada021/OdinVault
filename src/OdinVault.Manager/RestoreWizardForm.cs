namespace OdinVault.Manager;

internal sealed class RestoreWizardForm : Form
{
    private readonly AgentApiClient api;
    private readonly ComboBox backupCombo = new();
    private readonly TextBox targetDatabase = new();
    private readonly Label selectedBackupInfo = new();
    private readonly TextBox preflightSummary = new();
    private readonly CheckBox confirm = new();
    private readonly Button preflightButton = new();
    private readonly Button restoreButton = new();
    private readonly ProgressBar progress = new();
    private readonly Label status = new();
    private IReadOnlyList<BackupHistoryOverviewResponse> candidates = [];
    private RestorePreflightClientResponse? currentPreflight;
    private bool restoreRunning;

    public RestoreWizardForm(AgentApiClient api)
    {
        this.api = api;
        Text = "بازیابی بکاپ به دیتابیس جدید";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 680);
        MinimumSize = new Size(760, 620);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Restore-to-new-DB",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold)
        }, 0, 0);

        var selectPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        selectPanel.Controls.Add(LabelFor("۱. بکاپ موفق را انتخاب کنید"));
        backupCombo.Dock = DockStyle.Top;
        backupCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        backupCombo.SelectedIndexChanged += (_, _) => UpdateSelectedBackupInfo();
        selectPanel.Controls.Add(backupCombo);
        selectedBackupInfo.Dock = DockStyle.Fill;
        selectedBackupInfo.TextAlign = ContentAlignment.MiddleRight;
        selectedBackupInfo.ForeColor = Color.FromArgb(95, 105, 120);
        selectPanel.Controls.Add(selectedBackupInfo);
        root.Controls.Add(selectPanel, 0, 1);

        var targetPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        targetPanel.Controls.Add(LabelFor("۲. نام دیتابیس مقصد جدید"), 0, 0);
        targetDatabase.Dock = DockStyle.Fill;
        targetDatabase.RightToLeft = RightToLeft.No;
        targetDatabase.TextChanged += (_, _) =>
        {
            currentPreflight = null;
            confirm.Checked = false;
            restoreButton.Enabled = false;
        };
        targetPanel.Controls.Add(targetDatabase, 0, 1);

        preflightButton.Text = "پیش‌بررسی Restore";
        preflightButton.Dock = DockStyle.Fill;
        preflightButton.Margin = new Padding(10, 22, 0, 0);
        preflightButton.Click += async (_, _) => await RunPreflightAsync();
        targetPanel.Controls.Add(preflightButton, 1, 1);
        root.Controls.Add(targetPanel, 0, 2);

        preflightSummary.Dock = DockStyle.Fill;
        preflightSummary.Multiline = true;
        preflightSummary.ReadOnly = true;
        preflightSummary.ScrollBars = ScrollBars.Vertical;
        preflightSummary.BackColor = Color.White;
        preflightSummary.Text = "پس از پیش‌بررسی، اطلاعات Backup، SQL Server و فایل‌های مقصد اینجا نمایش داده می‌شود.";
        root.Controls.Add(preflightSummary, 0, 3);

        var confirmPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        confirm.Text = "تأیید می‌کنم Restore فقط به دیتابیس جدید انجام شود و نام مقصد را بررسی کرده‌ام.";
        confirm.AutoSize = true;
        confirm.Enabled = false;
        confirm.CheckedChanged += (_, _) =>
            restoreButton.Enabled = confirm.Checked && currentPreflight is not null;
        confirmPanel.Controls.Add(confirm, 0, 0);

        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleRight;
        status.ForeColor = Color.FromArgb(95, 105, 120);
        confirmPanel.Controls.Add(status, 0, 1);
        root.Controls.Add(confirmPanel, 0, 4);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));

        progress.Dock = DockStyle.Fill;
        progress.Style = ProgressBarStyle.Marquee;
        progress.MarqueeAnimationSpeed = 0;
        actions.Controls.Add(progress, 0, 0);

        restoreButton.Text = "اجرای Restore";
        restoreButton.Enabled = false;
        restoreButton.Dock = DockStyle.Fill;
        restoreButton.Click += async (_, _) => await RunRestoreAsync();
        actions.Controls.Add(restoreButton, 1, 0);

        var close = new Button
        {
            Text = "بستن",
            Dock = DockStyle.Fill,
            DialogResult = DialogResult.Cancel
        };
        actions.Controls.Add(close, 2, 0);
        root.Controls.Add(actions, 0, 5);

        CancelButton = close;
        FormClosing += (_, e) =>
        {
            if (!restoreRunning)
                return;

            e.Cancel = true;
            MessageBox.Show(
                this,
                "Restore در حال اجراست. تا پایان عملیات این پنجره را نبندید.",
                "OdinVault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
        Shown += async (_, _) => await LoadBackupsAsync();
    }

    private async Task LoadBackupsAsync()
    {
        SetBusy(true, "در حال دریافت بکاپ‌های قابل بازیابی...");
        try
        {
            var overview = await api.GetBackupOverviewAsync(500);
            candidates = overview?.Backups
                .Where(x => x.Status == 2 && x.LocalFileAvailable)
                .OrderByDescending(x => x.StartedAtUtc)
                .ToArray()
                ?? [];

            backupCombo.Items.Clear();
            foreach (var backup in candidates)
                backupCombo.Items.Add(new RestoreBackupItem(backup));

            if (backupCombo.Items.Count > 0)
                backupCombo.SelectedIndex = 0;
            else
                selectedBackupInfo.Text = "هیچ بکاپ موفق با فایل Local موجود پیدا نشد.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    private void UpdateSelectedBackupInfo()
    {
        currentPreflight = null;
        confirm.Checked = false;
        confirm.Enabled = false;
        restoreButton.Enabled = false;

        if (backupCombo.SelectedItem is not RestoreBackupItem item)
        {
            selectedBackupInfo.Text = string.Empty;
            return;
        }

        var backup = item.Backup;
        selectedBackupInfo.Text =
            $"دیتابیس: {backup.DatabaseName}   •   حجم: {FormatBytes(backup.SizeBytes)}   •   Verify: {FormatVerificationStatus(backup.VerificationStatus)}";

        if (string.IsNullOrWhiteSpace(targetDatabase.Text))
            targetDatabase.Text = $"{backup.DatabaseName}_Restore";
    }

    private async Task RunPreflightAsync()
    {
        if (backupCombo.SelectedItem is not RestoreBackupItem item)
        {
            MessageBox.Show(this, "ابتدا بکاپ را انتخاب کنید.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var target = targetDatabase.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show(this, "نام دیتابیس مقصد را وارد کنید.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, "در حال Verify و بررسی فایل‌های Restore...");
        try
        {
            var result = await api.PreflightRestoreAsync(item.Backup.Id, target);
            if (result is null)
                throw new InvalidOperationException("Agent نتیجه پیش‌بررسی Restore را برنگرداند.");

            currentPreflight = result;
            confirm.Enabled = true;
            confirm.Checked = false;

            var files = string.Join(
                Environment.NewLine,
                result.Files.Select(x => $"• {x.LogicalName} ({x.Type})  →  {x.TargetPath}"));

            preflightSummary.Text =
                $"Source DB: {result.SourceDatabaseName}{Environment.NewLine}" +
                $"Target DB: {result.TargetDatabaseName}{Environment.NewLine}" +
                $"Backup: {result.BackupFileName}{Environment.NewLine}" +
                $"Size: {FormatBytes(result.BackupSizeBytes)}{Environment.NewLine}" +
                $"Verify: {FormatVerificationStatus(result.VerificationStatus)}{Environment.NewLine}" +
                $"SQL Server: {result.ProductVersion}{Environment.NewLine}" +
                $"Data path: {result.DataDirectory}{Environment.NewLine}" +
                $"Log path: {result.LogDirectory}{Environment.NewLine}{Environment.NewLine}" +
                $"File plan:{Environment.NewLine}{files}";

            status.Text = "پیش‌بررسی موفق بود. برای اجرای Restore تأیید نهایی را فعال کنید.";
        }
        catch (Exception ex)
        {
            currentPreflight = null;
            confirm.Enabled = false;
            confirm.Checked = false;
            restoreButton.Enabled = false;
            status.Text = "پیش‌بررسی ناموفق بود.";
            MessageBox.Show(this, ex.Message, "Restore Preflight", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false, status.Text);
        }
    }

    private async Task RunRestoreAsync()
    {
        if (currentPreflight is null ||
            backupCombo.SelectedItem is not RestoreBackupItem item ||
            !confirm.Checked)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"دیتابیس جدید «{currentPreflight.TargetDatabaseName}» از بکاپ انتخاب‌شده ساخته شود؟\n\nاین عملیات دیتابیس موجودی را overwrite نمی‌کند.",
            "تأیید Restore",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
            return;

        restoreRunning = true;
        SetBusy(true, "Restore در حال اجراست؛ این پنجره را نبندید...");
        try
        {
            var result = await api.RestoreToNewDatabaseAsync(
                item.Backup.Id,
                currentPreflight.TargetDatabaseName);

            if (result is null)
                throw new InvalidOperationException("Agent نتیجه Restore را برنگرداند.");

            progress.MarqueeAnimationSpeed = 0;
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = 100;

            status.Text =
                $"Restore موفق شد. دیتابیس {result.TargetDatabaseName} در {FormatDuration(result.DurationSeconds)} بازیابی شد.";

            MessageBox.Show(
                this,
                status.Text,
                "Restore موفق",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            confirm.Checked = false;
            confirm.Enabled = false;
            restoreButton.Enabled = false;
        }
        catch (Exception ex)
        {
            status.Text = "Restore ناموفق بود.";
            MessageBox.Show(this, ex.Message, "Restore", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            restoreRunning = false;
            if (progress.Value != 100)
            {
                progress.Style = ProgressBarStyle.Marquee;
                progress.MarqueeAnimationSpeed = 0;
            }
            UseWaitCursor = false;
            backupCombo.Enabled = true;
            targetDatabase.Enabled = true;
            preflightButton.Enabled = true;
        }
    }

    private void SetBusy(bool busy, string text)
    {
        UseWaitCursor = busy;
        backupCombo.Enabled = !busy;
        targetDatabase.Enabled = !busy;
        preflightButton.Enabled = !busy;
        restoreButton.Enabled = !busy && currentPreflight is not null && confirm.Checked;

        progress.Style = ProgressBarStyle.Marquee;
        progress.MarqueeAnimationSpeed = busy ? 30 : 0;
        if (!string.IsNullOrWhiteSpace(text))
            status.Text = text;
    }

    private static Label LabelFor(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
    };

    private static string FormatBytes(long? value)
    {
        if (value is null) return "نامشخص";
        var bytes = value.Value;
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):0.0} MB";
        return $"{bytes / (1024d * 1024 * 1024):0.00} GB";
    }

    private static string FormatVerificationStatus(int status) => status switch
    {
        2 => "موفق",
        3 => "ناموفق",
        1 => "در حال بررسی",
        _ => "درخواست نشده"
    };

    private static string FormatDuration(double seconds)
    {
        if (seconds < 60) return $"{seconds:0} ثانیه";
        if (seconds < 3600) return $"{seconds / 60:0.0} دقیقه";
        return $"{seconds / 3600:0.0} ساعت";
    }

    private sealed record RestoreBackupItem(BackupHistoryOverviewResponse Backup)
    {
        public override string ToString()
        {
            var local = Backup.StartedAtUtc.Kind == DateTimeKind.Utc
                ? Backup.StartedAtUtc.ToLocalTime()
                : Backup.StartedAtUtc;
            var calendar = new System.Globalization.PersianCalendar();
            var date =
                $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00} {local:HH:mm}";
            return $"{Backup.DatabaseName} — {date} — {FormatBytes(Backup.SizeBytes)}";
        }
    }
}
