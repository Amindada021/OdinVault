namespace OdinVault.Manager;

internal sealed class MobileConnectionPage : UserControl
{
    private readonly AgentApiClient _api;
    private readonly Action _themeRefresh;

    private readonly ComboBox _scheme = new();
    private readonly TextBox _host = new();
    private readonly TextBox _port = new();
    private readonly TextBox _apiKey = new() { ReadOnly = true, UseSystemPasswordChar = true };
    private readonly CheckBox _showKey = new() { Text = "نمایش کلید", AutoSize = true };
    private readonly Label _preview = new();

    public MobileConnectionPage(AgentApiClient api, Action themeRefresh)
    {
        _api = api;
        _themeRefresh = themeRefresh;

        Dock = DockStyle.Fill;
        RightToLeft = RightToLeft.Yes;
        BackColor = Color.FromArgb(246, 248, 252);

        _scheme.Items.AddRange(["http", "https"]);
        LoadCurrentEndpoint();
        _apiKey.Text = _api.GetApiKey();

        BuildUi();
        UpdatePreview();
    
        UiLayout.Apply(this);
    }

    public void Reload()
    {
        LoadCurrentEndpoint();
        _apiKey.Text = _api.GetApiKey();
        UpdatePreview();
        _themeRefresh();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(4),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(246, 248, 252)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        root.Controls.Add(BuildEndpointCard(), 0, 0);
        root.Controls.Add(BuildSecurityCard(), 1, 0);
        root.Controls.Add(BuildTestCard(), 0, 1);
        root.Controls.Add(BuildFutureCard(), 1, 1);

        Controls.Add(root);
    }

    private Control BuildEndpointCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(20),
            BackColor = Color.White
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        content.Controls.Add(Title("آدرس اتصال موبایل"), 0, 0);
        content.Controls.Add(Description("IP یا دامنه‌ای که اپ موبایل برای دسترسی به OdinVault Agent استفاده می‌کند."), 0, 1);

        var endpoint = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 2,
            Margin = new Padding(0, 8, 0, 0)
        };
        endpoint.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        endpoint.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        endpoint.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        endpoint.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        endpoint.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        endpoint.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        endpoint.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        endpoint.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        endpoint.Controls.Add(Caption("Protocol"), 0, 0);
        endpoint.Controls.Add(Caption("Base / Host"), 2, 0);
        endpoint.Controls.Add(Caption("Port"), 4, 0);

        _scheme.Dock = DockStyle.Fill;
        _scheme.DropDownStyle = ComboBoxStyle.DropDownList;
        _scheme.RightToLeft = RightToLeft.No;
        _scheme.SelectedIndexChanged += (_, _) => UpdatePreview();
        endpoint.Controls.Add(_scheme, 1, 0);

        _host.Dock = DockStyle.Fill;
        _host.RightToLeft = RightToLeft.No;
        _host.PlaceholderText = "188.213.65.140 یا vault.example.com";
        _host.TextChanged += (_, _) => UpdatePreview();
        endpoint.Controls.Add(_host, 3, 0);

        _port.Dock = DockStyle.Fill;
        _port.RightToLeft = RightToLeft.No;
        _port.PlaceholderText = "5188";
        _port.TextChanged += (_, _) => UpdatePreview();
        endpoint.Controls.Add(_port, 5, 0);

        var note = Description("Port اختیاری است؛ برای HTTPS استاندارد می‌توانید آن را خالی بگذارید.");
        endpoint.Controls.Add(note, 0, 1);
        endpoint.SetColumnSpan(note, 6);
        content.Controls.Add(endpoint, 0, 2);

        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0, 8, 0, 0)
        };
        previewPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        previewPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewPanel.Controls.Add(Caption("URL نهایی"), 0, 0);
        _preview.Dock = DockStyle.Fill;
        _preview.TextAlign = ContentAlignment.MiddleLeft;
        _preview.RightToLeft = RightToLeft.No;
        _preview.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
        _preview.ForeColor = Color.FromArgb(37, 99, 235);
        previewPanel.Controls.Add(_preview, 1, 0);
        content.Controls.Add(previewPanel, 0, 3);

        content.Controls.Add(Description(
            "برای اتصال از اینترنت، Port باید در Firewall/NAT قابل دسترس باشد. برای دامنه می‌توانید SSL و Reverse Proxy استفاده کنید."), 0, 4);

        var save = CreateButton("ذخیره آدرس اتصال", primary: true);
        save.Click += (_, _) => SaveEndpoint(showMessage: true);
        content.Controls.Add(save, 0, 5);

        return Card(content);
    }

    private Control BuildSecurityCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(20),
            BackColor = Color.White
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        content.Controls.Add(Title("امنیت و API Key"), 0, 0);
        content.Controls.Add(Description("اپ موبایل برای درخواست‌های Agent از API Key استفاده می‌کند."), 0, 1);

        _apiKey.Dock = DockStyle.Fill;
        _apiKey.RightToLeft = RightToLeft.No;
        content.Controls.Add(_apiKey, 0, 2);

        _showKey.CheckedChanged += (_, _) => _apiKey.UseSystemPasswordChar = !_showKey.Checked;
        content.Controls.Add(_showKey, 0, 3);

        content.Controls.Add(Description(
            "کلید را فقط روی دستگاه‌های مورد اعتماد وارد کنید. در نسخه‌های بعدی این بخش محل Pairing، QR و مدیریت دستگاه‌ها خواهد بود."), 0, 4);

        var copy = CreateButton("کپی API Key");
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(_apiKey.Text);
            OdinDialog.Show(this, "کلید API کپی شد.", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        content.Controls.Add(copy, 0, 5);

        return Card(content);
    }

    private Control BuildTestCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20),
            BackColor = Color.White
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        content.Controls.Add(Title("بررسی اتصال"), 0, 0);
        content.Controls.Add(Description("Port و خود Agent را جداگانه تست کنید. نتیجه هر تست در Modal نمایش داده می‌شود."), 0, 1);
        content.Controls.Add(Description(
            "• بررسی Port: اتصال TCP به Host/Port\r\n" +
            "• تست Agent: بررسی /api/health به همراه API Key"), 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var testAgent = CreateButton("تست اتصال Agent", primary: true);
        testAgent.Click += async (_, _) => await TestAgentAsync(testAgent);
        var testPort = CreateButton("بررسی باز بودن Port");
        testPort.Click += async (_, _) => await TestPortAsync(testPort);
        actions.Controls.Add(testAgent);
        actions.Controls.Add(testPort);
        content.Controls.Add(actions, 0, 3);

        return Card(content);
    }

    private Control BuildFutureCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20),
            BackColor = Color.White
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        content.Controls.Add(Title("Mobile Hub"), 0, 0);
        content.Controls.Add(Description(
            "این بخش برای توسعه آینده آماده است:\r\n\r\n" +
            "• QR Pairing\r\n" +
            "• دستگاه‌های متصل\r\n" +
            "• لغو دسترسی یک دستگاه\r\n" +
            "• Domain / SSL status\r\n" +
            "• آخرین اتصال موبایل"), 0, 1);

        content.Controls.Add(new Label
        {
            Text = "آماده برای توسعه نسخه‌های بعدی",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(37, 99, 235),
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold)
        }, 0, 2);

        return Card(content);
    }

    private async Task TestPortAsync(Button button)
    {
        var original = button.Text;
        try
        {
            var host = NormalizeHost();
            var port = ResolvePort();
            button.Enabled = false;
            button.Text = "در حال بررسی...";

            var result = await _api.TestPortAsync(host, port);
            OdinDialog.Show(
                this,
                result.IsOpen
                    ? $"اتصال TCP با موفقیت برقرار شد.{Environment.NewLine}{Environment.NewLine}" +
                      $"Host: {host}{Environment.NewLine}" +
                      $"Port: {port}{Environment.NewLine}" +
                      $"Latency: {(result.LatencyMilliseconds.HasValue ? $"{result.LatencyMilliseconds.Value:0} ms" : "نامشخص")}"
                    : $"اتصال به Port برقرار نشد.{Environment.NewLine}{Environment.NewLine}" +
                      $"Host: {host}{Environment.NewLine}" +
                      $"Port: {port}{Environment.NewLine}" +
                      $"جزئیات: {result.Error}",
                "بررسی Port",
                MessageBoxButtons.OK,
                result.IsOpen ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            OdinDialog.Show(this, ex.Message, "بررسی Port", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            button.Text = original;
            button.Enabled = true;
        }
    }

    private async Task TestAgentAsync(Button button)
    {
        var original = button.Text;
        try
        {
            var url = BuildUrl();
            button.Enabled = false;
            button.Text = "در حال تست...";

            var result = await _api.TestAgentEndpointAsync(url);
            OdinDialog.Show(
                this,
                result.Success
                    ? $"Agent با موفقیت پاسخ داد.{Environment.NewLine}{Environment.NewLine}URL: {url}{Environment.NewLine}Status: {result.Message}"
                    : $"اتصال Agent ناموفق بود.{Environment.NewLine}{Environment.NewLine}URL: {url}{Environment.NewLine}جزئیات: {result.Message}",
                "تست اتصال Agent",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            OdinDialog.Show(this, ex.Message, "تست اتصال Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            button.Text = original;
            button.Enabled = true;
        }
    }

    private void SaveEndpoint(bool showMessage)
    {
        try
        {
            var url = BuildUrl();
            _api.SaveMobileBaseUrl(url);
            if (showMessage)
                OdinDialog.Show(this, $"آدرس اتصال موبایل ذخیره شد:{Environment.NewLine}{url}", "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            OdinDialog.Show(this, ex.Message, "OdinVault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void LoadCurrentEndpoint()
    {
        var current = _api.GetMobileBaseUrl();
        if (!Uri.TryCreate(current, UriKind.Absolute, out var uri))
        {
            _scheme.SelectedIndex = 0;
            _host.Text = Environment.MachineName;
            _port.Text = "5188";
            return;
        }

        _scheme.SelectedItem = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        _host.Text = uri.Host;
        var explicitPort = !uri.IsDefaultPort || current.Contains($":{uri.Port}", StringComparison.OrdinalIgnoreCase);
        _port.Text = explicitPort ? uri.Port.ToString() : string.Empty;
    }

    private void UpdatePreview()
    {
        try
        {
            _preview.Text = BuildUrl();
            _preview.ForeColor = Color.FromArgb(37, 99, 235);
        }
        catch
        {
            _preview.Text = "آدرس ناقص یا نامعتبر";
            _preview.ForeColor = Color.FromArgb(190, 65, 65);
        }
    }

    private string BuildUrl()
    {
        var host = NormalizeHost();
        var scheme = Convert.ToString(_scheme.SelectedItem)?.Trim().ToLowerInvariant();
        if (scheme is not ("http" or "https"))
            throw new InvalidOperationException("Protocol باید HTTP یا HTTPS باشد.");

        int? port = null;
        var portText = _port.Text.Trim();
        if (!string.IsNullOrWhiteSpace(portText))
        {
            if (!int.TryParse(portText, out var parsed) || parsed is < 1 or > 65535)
                throw new InvalidOperationException("Port باید بین 1 تا 65535 باشد.");
            port = parsed;
        }

        var builder = new UriBuilder(scheme, host)
        {
            Port = port ?? -1,
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private string NormalizeHost()
    {
        var host = _host.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Base / Host الزامی است.");

        if (host.Contains("://", StringComparison.Ordinal) ||
            host.Contains('/') || host.Contains('?') || host.Contains('#') || host.Contains('@'))
            throw new InvalidOperationException("در Base / Host فقط IP یا نام دامنه را وارد کنید.");

        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new InvalidOperationException("Base / Host معتبر نیست.");

        return host;
    }

    private int ResolvePort()
    {
        var text = _port.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Equals(Convert.ToString(_scheme.SelectedItem), "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;

        if (!int.TryParse(text, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("Port باید بین 1 تا 65535 باشد.");

        return port;
    }

    private static Control Card(Control content)
    {
        var card = new ReaLTaiizor.Controls.Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8),
            Padding = new Padding(1),
            BackColor = Color.White,
            EdgeColor = Color.FromArgb(226, 232, 240)
        };
        card.Controls.Add(content);
        return card;
    }

    private static Label Title(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", 12F, FontStyle.Bold),
        ForeColor = Color.FromArgb(30, 41, 59)
    };

    private static Label Description(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.TopRight,
        ForeColor = Color.FromArgb(100, 116, 139),
        AutoEllipsis = true
    };

    private static Label Caption(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight
    };

    private static Button CreateButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 40,
            MinimumSize = new Size(110, 40),
            Padding = new Padding(14, 3, 14, 3),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(51, 65, 85),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(37, 99, 235) : Color.FromArgb(203, 213, 225);
        return button;
    }
}
