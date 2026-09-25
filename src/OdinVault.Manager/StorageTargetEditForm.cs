namespace OdinVault.Manager;

internal sealed class StorageTargetEditForm : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _folderId = new();
    private readonly TextBox _baseUrl = new();
    private readonly TextBox _apiKey = new();
    private readonly CheckBox _enabled = new();

    public StorageTargetEditForm(StorageTargetOverviewResponse target)
    {
        Text = "ویرایش محل ذخیره‌سازی";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, target.Type == 5 ? 360 : 300);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(18),
            AutoSize = true
        };
        Controls.Add(root);

        root.Controls.Add(LabelFor("نام مقصد"));
        _name.Text = target.Name;
        _name.Dock = DockStyle.Top;
        root.Controls.Add(_name);

        root.Controls.Add(LabelFor("Folder ID"));
        _folderId.Text = target.FolderId ?? string.Empty;
        _folderId.Dock = DockStyle.Top;
        _folderId.RightToLeft = RightToLeft.No;
        root.Controls.Add(_folderId);

        if (target.Type == 5)
        {
            root.Controls.Add(LabelFor("آدرس OdinVault Replica"));
            _baseUrl.Text = target.BaseUrl ?? string.Empty;
            _baseUrl.Dock = DockStyle.Top;
            _baseUrl.RightToLeft = RightToLeft.No;
            root.Controls.Add(_baseUrl);

            root.Controls.Add(LabelFor("API Key جدید (در صورت عدم تغییر خالی بماند)"));
            _apiKey.Dock = DockStyle.Top;
            _apiKey.RightToLeft = RightToLeft.No;
            _apiKey.UseSystemPasswordChar = true;
            root.Controls.Add(_apiKey);
        }

        _enabled.Text = "فعال";
        _enabled.Checked = target.IsEnabled;
        _enabled.AutoSize = true;
        _enabled.Margin = new Padding(3, 14, 3, 8);
        root.Controls.Add(_enabled);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 14, 0, 0)
        };

        var save = new Button
        {
            Text = "ذخیره",
            AutoSize = true,
            DialogResult = DialogResult.OK
        };
        var cancel = new Button
        {
            Text = "انصراف",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions);

        AcceptButton = save;
        CancelButton = cancel;
    
        UiLayout.Apply(this);
    }

    public UpdateStorageTargetClientRequest Request => new(
        _name.Text.Trim(),
        string.IsNullOrWhiteSpace(_folderId.Text) ? null : _folderId.Text.Trim(),
        _enabled.Checked,
        string.IsNullOrWhiteSpace(_baseUrl.Text) ? null : _baseUrl.Text.Trim(),
        string.IsNullOrWhiteSpace(_apiKey.Text) ? null : _apiKey.Text);

    private static Label LabelFor(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 10, 3, 4)
    };
}
