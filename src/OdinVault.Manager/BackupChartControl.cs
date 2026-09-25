using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace OdinVault.Manager;

internal enum BackupChartKind
{
    Line,
    Bar
}

internal sealed record BackupChartSeries(
    string Name,
    IReadOnlyList<double?> Values);

internal sealed class BackupChartControl : Control
{
    private IReadOnlyList<string> _categories = [];
    private IReadOnlyList<BackupChartSeries> _series = [];
    private readonly Color[] _palette =
    [
        Color.FromArgb(48, 116, 196),
        Color.FromArgb(214, 76, 76),
        Color.FromArgb(46, 154, 109),
        Color.FromArgb(229, 153, 53)
    ];

    public BackupChartControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(55, 65, 80);
        Padding = new Padding(12);
        Font = new Font("Segoe UI", 9F);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ChartTitle { get; set; } = string.Empty;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "داده‌ای برای نمایش وجود ندارد.";

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BackupChartKind Kind { get; set; } = BackupChartKind.Line;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<double, string>? ValueFormatter { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GridLineColor { get; set; } = Color.FromArgb(235, 239, 244);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SecondaryTextColor { get; set; } = Color.FromArgb(105, 115, 130);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.FromArgb(226, 231, 238);

    public void SetData(
        IReadOnlyList<string> categories,
        params BackupChartSeries[] series)
    {
        _categories = categories;
        _series = series;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = ClientRectangle;
        if (bounds.Width < 140 || bounds.Height < 120)
            return;

        using var borderPen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(borderPen, 0, 0, bounds.Width - 1, bounds.Height - 1);

        using var titleFont = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
        using var secondaryBrush = new SolidBrush(SecondaryTextColor);
        using var primaryBrush = new SolidBrush(ForeColor);

        e.Graphics.DrawString(
            ChartTitle,
            titleFont,
            primaryBrush,
            new RectangleF(14, 12, bounds.Width - 28, 24),
            new StringFormat { Alignment = StringAlignment.Far });

        DrawLegend(e.Graphics, bounds);

        var plot = new Rectangle(
            58,
            54,
            Math.Max(40, bounds.Width - 78),
            Math.Max(40, bounds.Height - 92));

        var values = _series
            .SelectMany(x => x.Values)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        if (_categories.Count == 0 || _series.Count == 0 || values.Count == 0)
        {
            e.Graphics.DrawString(
                EmptyText,
                Font,
                secondaryBrush,
                plot,
                new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                });
            return;
        }

        var maxValue = values.Max();
        if (maxValue <= 0)
            maxValue = 1;

        DrawAxes(e.Graphics, plot, maxValue);

        if (Kind == BackupChartKind.Bar)
            DrawBars(e.Graphics, plot, maxValue);
        else
            DrawLines(e.Graphics, plot, maxValue);

        DrawCategoryLabels(e.Graphics, plot);
    }

    private void DrawLegend(Graphics graphics, Rectangle bounds)
    {
        if (_series.Count == 0)
            return;

        var x = 14f;
        var y = 16f;
        using var brush = new SolidBrush(SecondaryTextColor);

        foreach (var item in _series.Reverse())
        {
            var index = _series.IndexOf(item);
            var color = _palette[index % _palette.Length];
            using var colorBrush = new SolidBrush(color);
            graphics.FillEllipse(colorBrush, x, y + 3, 8, 8);
            x += 12;
            var size = graphics.MeasureString(item.Name, Font);
            graphics.DrawString(item.Name, Font, brush, x, y);
            x += size.Width + 14;
        }
    }

    private void DrawAxes(Graphics graphics, Rectangle plot, double maxValue)
    {
        using var gridPen = new Pen(GridLineColor);
        using var textBrush = new SolidBrush(SecondaryTextColor);

        const int gridLines = 4;
        for (var i = 0; i <= gridLines; i++)
        {
            var ratio = i / (double)gridLines;
            var y = plot.Bottom - (float)(ratio * plot.Height);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);

            var value = maxValue * ratio;
            var label = ValueFormatter?.Invoke(value) ?? FormatNumber(value);
            var size = graphics.MeasureString(label, Font);
            graphics.DrawString(
                label,
                Font,
                textBrush,
                plot.Left - size.Width - 8,
                y - size.Height / 2);
        }
    }

    private void DrawLines(Graphics graphics, Rectangle plot, double maxValue)
    {
        if (_categories.Count == 1)
            return;

        for (var s = 0; s < _series.Count; s++)
        {
            var series = _series[s];
            var points = new List<PointF>();
            for (var i = 0; i < Math.Min(_categories.Count, series.Values.Count); i++)
            {
                var value = series.Values[i];
                if (!value.HasValue)
                    continue;

                var x = plot.Left + (float)(i * plot.Width / (double)Math.Max(1, _categories.Count - 1));
                var y = plot.Bottom - (float)(value.Value / maxValue * plot.Height);
                points.Add(new PointF(x, y));
            }

            var color = _palette[s % _palette.Length];
            using var pen = new Pen(color, 2.2F)
            {
                LineJoin = LineJoin.Round
            };
            using var pointBrush = new SolidBrush(color);

            if (points.Count > 1)
                graphics.DrawLines(pen, points.ToArray());

            foreach (var point in points)
                graphics.FillEllipse(pointBrush, point.X - 3, point.Y - 3, 6, 6);
        }
    }

    private void DrawBars(Graphics graphics, Rectangle plot, double maxValue)
    {
        var categoryCount = Math.Max(1, _categories.Count);
        var seriesCount = Math.Max(1, _series.Count);
        var slot = plot.Width / (double)categoryCount;
        var groupWidth = Math.Min(slot * 0.72, 58);
        var barWidth = Math.Max(3, groupWidth / seriesCount);

        for (var i = 0; i < categoryCount; i++)
        {
            var startX = plot.Left + i * slot + (slot - groupWidth) / 2;
            for (var s = 0; s < _series.Count; s++)
            {
                if (i >= _series[s].Values.Count || !_series[s].Values[i].HasValue)
                    continue;

                var value = Math.Max(0, _series[s].Values[i]!.Value);
                var height = value / maxValue * plot.Height;
                var rect = new RectangleF(
                    (float)(startX + s * barWidth),
                    (float)(plot.Bottom - height),
                    (float)Math.Max(2, barWidth - 2),
                    (float)Math.Max(1, height));

                using var brush = new SolidBrush(_palette[s % _palette.Length]);
                graphics.FillRectangle(brush, rect);
            }
        }
    }

    private void DrawCategoryLabels(Graphics graphics, Rectangle plot)
    {
        using var brush = new SolidBrush(SecondaryTextColor);
        var count = _categories.Count;
        if (count == 0)
            return;

        var maxLabels = Math.Max(2, Math.Min(8, plot.Width / 80));
        var step = Math.Max(1, (int)Math.Ceiling(count / (double)maxLabels));

        for (var i = 0; i < count; i += step)
        {
            var x = count == 1
                ? plot.Left + plot.Width / 2f
                : plot.Left + (float)(i * plot.Width / (double)(count - 1));

            var label = _categories[i];
            var size = graphics.MeasureString(label, Font);
            graphics.DrawString(label, Font, brush, x - size.Width / 2, plot.Bottom + 8);
        }

        if ((count - 1) % step != 0)
        {
            var label = _categories[^1];
            var size = graphics.MeasureString(label, Font);
            graphics.DrawString(label, Font, brush, plot.Right - size.Width / 2, plot.Bottom + 8);
        }
    }

    private static string FormatNumber(double value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000:0.#}B";
        if (value >= 1_000_000) return $"{value / 1_000_000:0.#}M";
        if (value >= 1_000) return $"{value / 1_000:0.#}K";
        return $"{value:0.#}";
    }
}

internal static class BackupChartSeriesExtensions
{
    public static int IndexOf(
        this IReadOnlyList<BackupChartSeries> series,
        BackupChartSeries item)
    {
        for (var i = 0; i < series.Count; i++)
        {
            if (ReferenceEquals(series[i], item) || Equals(series[i], item))
                return i;
        }

        return 0;
    }
}
