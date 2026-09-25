using System.Runtime.InteropServices;

namespace OdinVault.Manager;

internal enum OdinDialogTone
{
    Info,
    Success,
    Warning,
    Error,
    Question
}

internal sealed class OdinDialog : Form
{
    private readonly Label _title = new();
    private readonly Label _message = new();
    private readonly Panel _accent = new();
    private readonly FlowLayoutPanel _buttons = new();

    private OdinDialog(
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon)
    {
        Text = caption;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        ClientSize = new Size(560, 245);
        MinimumSize = new Size(500, 220);
        BackColor = Color.FromArgb(248, 250, 252);

        var tone = ToneFromIcon(icon);
        var palette = Palette(tone);

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = palette.Border
        };

        var surface = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(22),
            BackColor = palette.Surface
        };
        surface.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        surface.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        surface.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        surface.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        surface.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        _accent.Dock = DockStyle.Fill;
        _accent.Margin = new Padding(0, 2, 14, 2);
        _accent.BackColor = palette.Accent;
        surface.Controls.Add(_accent, 0, 0);
        surface.SetRowSpan(_accent, 3);

        _title.Text = caption;
        _title.Dock = DockStyle.Fill;
        _title.TextAlign = ContentAlignment.MiddleRight;
        _title.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        _title.ForeColor = palette.Primary;
        surface.Controls.Add(_title, 1, 0);

        _message.Text = text;
        _message.Dock = DockStyle.Fill;
        _message.TextAlign = ContentAlignment.TopRight;
        _message.Font = new Font(Font.FontFamily, 10.2F);
        _message.ForeColor = palette.Secondary;
        _message.Padding = new Padding(0, 8, 0, 8);
        surface.Controls.Add(_message, 1, 1);

        _buttons.Dock = DockStyle.Fill;
        _buttons.FlowDirection = FlowDirection.LeftToRight;
        _buttons.WrapContents = false;
        _buttons.Padding = new Padding(0, 8, 0, 0);
        _buttons.BackColor = palette.Surface;
        surface.Controls.Add(_buttons, 1, 2);

        AddButtons(buttons, palette);

        root.Controls.Add(surface);
        Controls.Add(root);

        Shown += (_, _) => Win11Backdrop.TryApply(this, transient: true, dark: palette.Dark);
    
        UiLayout.Apply(this);
    }

    public static DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon)
    {
        using var dialog = new OdinDialog(text, caption, buttons, icon);
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    public static DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
    {
        using var dialog = new OdinDialog(text, caption, buttons, icon);
        dialog.ApplyDefaultButton(defaultButton);
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    public static DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption)
        => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Success(IWin32Window? owner, string text, string caption = "OdinVault")
        => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Error(IWin32Window? owner, string text, string caption = "خطای OdinVault")
        => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Warning(IWin32Window? owner, string text, string caption = "OdinVault")
        => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static bool Confirm(IWin32Window? owner, string text, string caption = "OdinVault")
        => Show(owner, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void ApplyDefaultButton(MessageBoxDefaultButton defaultButton)
    {
        var buttons = _buttons.Controls.OfType<Button>().ToArray();
        if (buttons.Length == 0)
            return;

        var index = defaultButton switch
        {
            MessageBoxDefaultButton.Button2 => Math.Min(1, buttons.Length - 1),
            MessageBoxDefaultButton.Button3 => Math.Min(2, buttons.Length - 1),
            _ => 0
        };

        AcceptButton = buttons[index];
        buttons[index].Select();
    }

    private void AddButtons(MessageBoxButtons buttons, DialogPalette palette)
    {
        foreach (var spec in ButtonSpecs(buttons))
        {
            var button = new Button
            {
                Text = spec.Text,
                DialogResult = spec.Result,
                AutoSize = true,
                Height = 38,
                MinimumSize = new Size(92, 38),
                Padding = new Padding(14, 2, 14, 2),
                Margin = new Padding(6, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold),
                BackColor = spec.Primary ? palette.Accent : palette.ButtonSurface,
                ForeColor = spec.Primary ? Color.White : palette.Primary
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = spec.Primary ? palette.Accent : palette.Border;
            button.FlatAppearance.MouseOverBackColor = spec.Primary
                ? palette.AccentHover
                : palette.ButtonHover;
            _buttons.Controls.Add(button);

            if (spec.Result is DialogResult.OK or DialogResult.Yes)
                AcceptButton = button;
            if (spec.Result is DialogResult.Cancel or DialogResult.No)
                CancelButton = button;
        }
    }

    private static IEnumerable<(string Text, DialogResult Result, bool Primary)> ButtonSpecs(MessageBoxButtons buttons)
    {
        return buttons switch
        {
            MessageBoxButtons.YesNo =>
            [
                ("خیر", DialogResult.No, false),
                ("بله", DialogResult.Yes, true)
            ],
            MessageBoxButtons.YesNoCancel =>
            [
                ("انصراف", DialogResult.Cancel, false),
                ("خیر", DialogResult.No, false),
                ("بله", DialogResult.Yes, true)
            ],
            MessageBoxButtons.OKCancel =>
            [
                ("انصراف", DialogResult.Cancel, false),
                ("تأیید", DialogResult.OK, true)
            ],
            MessageBoxButtons.RetryCancel =>
            [
                ("انصراف", DialogResult.Cancel, false),
                ("تلاش مجدد", DialogResult.Retry, true)
            ],
            MessageBoxButtons.AbortRetryIgnore =>
            [
                ("نادیده گرفتن", DialogResult.Ignore, false),
                ("تلاش مجدد", DialogResult.Retry, true),
                ("توقف", DialogResult.Abort, false)
            ],
            _ =>
            [
                ("بستن", DialogResult.OK, true)
            ]
        };
    }

    private static OdinDialogTone ToneFromIcon(MessageBoxIcon icon) => icon switch
    {
        MessageBoxIcon.Error => OdinDialogTone.Error,
        MessageBoxIcon.Warning => OdinDialogTone.Warning,
        MessageBoxIcon.Question => OdinDialogTone.Question,
        _ => OdinDialogTone.Info
    };

    private static DialogPalette Palette(OdinDialogTone tone)
    {
        var dark = Application.OpenForms
            .OfType<MainForm>()
            .Any(form => form.IsOdinDarkTheme);

        if (dark)
        {
            var accent = tone switch
            {
                OdinDialogTone.Error => Color.FromArgb(248, 113, 113),
                OdinDialogTone.Warning => Color.FromArgb(251, 191, 36),
                OdinDialogTone.Success => Color.FromArgb(74, 222, 128),
                _ => Color.FromArgb(96, 165, 250)
            };
            return new DialogPalette(
                true,
                Color.FromArgb(30, 41, 59),
                Color.FromArgb(241, 245, 249),
                Color.FromArgb(203, 213, 225),
                Color.FromArgb(71, 85, 105),
                accent,
                Color.FromArgb(51, 65, 85),
                Color.FromArgb(71, 85, 105),
                ControlPaint.Dark(accent));
        }

        var lightAccent = tone switch
        {
            OdinDialogTone.Error => Color.FromArgb(220, 38, 38),
            OdinDialogTone.Warning => Color.FromArgb(217, 119, 6),
            OdinDialogTone.Success => Color.FromArgb(22, 163, 74),
            _ => Color.FromArgb(37, 99, 235)
        };

        return new DialogPalette(
            false,
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(15, 23, 42),
            Color.FromArgb(71, 85, 105),
            Color.FromArgb(226, 232, 240),
            lightAccent,
            Color.FromArgb(248, 250, 252),
            Color.FromArgb(241, 245, 249),
            ControlPaint.Dark(lightAccent));
    }

    private sealed record DialogPalette(
        bool Dark,
        Color Surface,
        Color Primary,
        Color Secondary,
        Color Border,
        Color Accent,
        Color ButtonSurface,
        Color ButtonHover,
        Color AccentHover);
}

internal static class Win11Backdrop
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;

    private const int DwmwcpRound = 2;
    private const int DwmSystemBackdropMainWindow = 2;
    private const int DwmSystemBackdropTransientWindow = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    public static void TryApply(Form form, bool transient, bool dark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        try
        {
            var corner = DwmwcpRound;
            DwmSetWindowAttribute(
                form.Handle,
                DwmwaWindowCornerPreference,
                ref corner,
                sizeof(int));

            var darkValue = dark ? 1 : 0;
            DwmSetWindowAttribute(
                form.Handle,
                DwmwaUseImmersiveDarkMode,
                ref darkValue,
                sizeof(int));

            var backdrop = transient
                ? DwmSystemBackdropTransientWindow
                : DwmSystemBackdropMainWindow;
            DwmSetWindowAttribute(
                form.Handle,
                DwmwaSystemBackdropType,
                ref backdrop,
                sizeof(int));
        }
        catch
        {
            // Windows Server/RDP can disable backdrop composition. The custom surface remains usable.
        }
    }
}
