namespace OdinVault.Manager;

internal sealed class UpdateProgressForm : Form
{
    private readonly ProgressBar _progress = new()
    {
        Dock = DockStyle.Top,
        Height = 22,
        Minimum = 0,
        Maximum = 100,
        Style = ProgressBarStyle.Continuous
    };

    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold)
    };

    private readonly Label _details = new()
    {
        Dock = DockStyle.Top,
        Height = 32,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Color.FromArgb(100, 116, 139)
    };

    public UpdateProgressForm(string version)
    {
        Text = "دانلود بروزرسانی OdinVault";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ClientSize = new Size(520, 170);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = Color.White;

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            BackColor = Color.White
        };

        var title = new Label
        {
            Text = $"نسخه {version}",
            Dock = DockStyle.Top,
            Height = 32,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59)
        };

        _status.Text = "در حال آماده‌سازی دانلود...";
        _details.Text = "0 مگابایت";

        root.Controls.Add(_details);
        root.Controls.Add(_progress);
        root.Controls.Add(_status);
        root.Controls.Add(title);
        Controls.Add(root);

        Shown += (_, _) =>
        {
            var dark = Application.OpenForms
                .OfType<MainForm>()
                .Any(form => form.IsOdinDarkTheme);

            if (dark)
            {
                BackColor = Color.FromArgb(30, 41, 59);
                root.BackColor = Color.FromArgb(30, 41, 59);
                title.ForeColor = Color.FromArgb(241, 245, 249);
                _status.ForeColor = Color.FromArgb(241, 245, 249);
                _details.ForeColor = Color.FromArgb(148, 163, 184);
            }

            Win11Backdrop.TryApply(this, transient: true, dark: dark);
        };
    
        UiLayout.Apply(this);
    }

    public void Report(UpdateDownloadProgress value)
    {
        if (IsDisposed)
            return;

        var percent = Math.Clamp(value.Percent, 0, 100);
        _progress.Value = percent;
        _status.Text = $"دانلود بروزرسانی: {percent}٪";

        var downloadedMb = value.BytesReceived / 1024d / 1024d;
        if (value.TotalBytes is > 0)
        {
            var totalMb = value.TotalBytes.Value / 1024d / 1024d;
            _details.Text = $"{downloadedMb:0.0} از {totalMb:0.0} مگابایت";
        }
        else
        {
            _details.Text = $"{downloadedMb:0.0} مگابایت دانلود شده";
        }
    }

    public void MarkCompleted()
    {
        if (IsDisposed)
            return;

        _progress.Value = 100;
        _status.Text = "دانلود کامل شد؛ در حال بررسی SHA256...";
        Refresh();
    }
}
