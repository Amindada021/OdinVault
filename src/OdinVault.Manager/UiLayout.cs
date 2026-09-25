namespace OdinVault.Manager;

internal static class UiLayout
{
    private static string _currentLanguage = "fa";
    private static readonly Dictionary<TableLayoutPanel, TableLayoutSnapshot> TableSnapshots = new();

    public static string CurrentLanguage => _currentLanguage;

    public static void SetLanguage(string? language)
    {
        _currentLanguage = UiText.IsPersian(language) ? "fa" : "en";
    }

    public static void Apply(Control root, string? language = null, bool translateText = true)
    {
        var effectiveLanguage = language ?? _currentLanguage;
        SetLanguage(effectiveLanguage);
        ApplyRecursive(root, UiText.IsPersian(effectiveLanguage), effectiveLanguage, translateText);
    }

    private static void ApplyRecursive(
        Control control,
        bool rtl,
        string language,
        bool translateText)
    {
        if (translateText && control is not TextBoxBase and not ComboBox)
            control.Text = UiText.TranslateLiteral(control.Text, language);

        // Controls left at Inherit follow the selected language. Explicit LTR inputs
        // (URL, host, API key, paths, etc.) keep RightToLeft.No.
        if (control.RightToLeft != RightToLeft.No ||
            control is not TextBoxBase and not ComboBox)
        {
            control.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        }

        if (control is Form form)
            form.RightToLeftLayout = rtl;

        if (control is TableLayoutPanel table)
            ApplyTableDirection(table, rtl);

        if (control is FlowLayoutPanel flow)
        {
            if (flow.FlowDirection is FlowDirection.LeftToRight or FlowDirection.RightToLeft)
            {
                flow.FlowDirection = rtl
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;
            }
        }

        if (control is Label label)
            label.TextAlign = MirrorHorizontalAlignment(label.TextAlign, rtl);

        if (control is DataGridView grid)
        {
            grid.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            grid.ColumnHeadersDefaultCellStyle.Alignment = rtl
                ? DataGridViewContentAlignment.MiddleRight
                : DataGridViewContentAlignment.MiddleLeft;
        }

        foreach (Control child in control.Controls)
            ApplyRecursive(child, rtl, language, translateText);
    }

    private static void ApplyTableDirection(TableLayoutPanel table, bool rtl)
    {
        if (table.ColumnCount <= 1)
            return;

        if (!TableSnapshots.TryGetValue(table, out var snapshot))
        {
            snapshot = new TableLayoutSnapshot(
                table.ColumnCount,
                table.ColumnStyles
                    .Cast<ColumnStyle>()
                    .Select(x => new ColumnStyleSnapshot(x.SizeType, x.Width))
                    .ToArray(),
                table.Controls
                    .Cast<Control>()
                    .ToDictionary(
                        child => child,
                        child => new CellSnapshot(
                            table.GetColumn(child),
                            Math.Max(1, table.GetColumnSpan(child)))));

            TableSnapshots[table] = snapshot;
        }

        if (snapshot.ColumnCount != table.ColumnCount)
            return;

        var styleCount = Math.Min(table.ColumnStyles.Count, snapshot.ColumnStyles.Length);
        for (var target = 0; target < styleCount; target++)
        {
            var source = rtl ? styleCount - 1 - target : target;
            var original = snapshot.ColumnStyles[source];
            table.ColumnStyles[target].SizeType = original.SizeType;
            table.ColumnStyles[target].Width = original.Width;
        }

        foreach (var pair in snapshot.Cells)
        {
            var child = pair.Key;
            if (!table.Controls.Contains(child))
                continue;

            var original = pair.Value;
            var targetColumn = rtl
                ? table.ColumnCount - original.Column - original.ColumnSpan
                : original.Column;

            if (targetColumn < 0 || targetColumn >= table.ColumnCount)
                continue;

            table.SetColumn(child, targetColumn);
            table.SetColumnSpan(child, original.ColumnSpan);
        }
    }

    private static ContentAlignment MirrorHorizontalAlignment(ContentAlignment alignment, bool rtl)
    {
        if (rtl)
        {
            return alignment switch
            {
                ContentAlignment.TopLeft => ContentAlignment.TopRight,
                ContentAlignment.MiddleLeft => ContentAlignment.MiddleRight,
                ContentAlignment.BottomLeft => ContentAlignment.BottomRight,
                _ => alignment
            };
        }

        return alignment switch
        {
            ContentAlignment.TopRight => ContentAlignment.TopLeft,
            ContentAlignment.MiddleRight => ContentAlignment.MiddleLeft,
            ContentAlignment.BottomRight => ContentAlignment.BottomLeft,
            _ => alignment
        };
    }

    private sealed record TableLayoutSnapshot(
        int ColumnCount,
        ColumnStyleSnapshot[] ColumnStyles,
        Dictionary<Control, CellSnapshot> Cells);

    private sealed record ColumnStyleSnapshot(SizeType SizeType, float Width);

    private sealed record CellSnapshot(int Column, int ColumnSpan);
}
