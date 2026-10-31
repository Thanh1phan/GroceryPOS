using System.ComponentModel;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Display;

/// <summary>
/// Themed, owner-drawn column chart (doanh thu 7 ngày, doanh thu theo giờ): value axis with "nice" gridlines and
/// compact VND labels, rounded bars, one highlighted bar (today / the current hour), hover highlight with a tooltip,
/// category labels thinned out automatically when they would overlap, and an empty-state text.
/// Drop it into a <see cref="ModernCard"/> with <c>Dock = Fill</c>.
/// </summary>
[ToolboxItem(true)]
public class BarChart : Control, IThemeable
{
    private readonly ToolTip _toolTip = new() { InitialDelay = 150, ReshowDelay = 50 };
    private IReadOnlyList<ChartPoint> _points = [];
    private int _highlightIndex = -1;
    private int _hoverIndex = -1;
    private string _emptyText = "Chưa có dữ liệu";
    private Func<decimal, string> _valueFormatter = ChartScale.CompactVnd;
    private Func<decimal, string>? _tooltipFormatter;
    private bool _showValues;
    private RectangleF _plot;

    public BarChart()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Size = new Size(360, 200);
    }

    /// <summary>Bars from left to right.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<ChartPoint> Points
    {
        get => _points;
        set
        {
            _points = value ?? [];
            _hoverIndex = -1;
            _toolTip.SetToolTip(this, null);
            Invalidate();
        }
    }

    /// <summary>Bar drawn in the accent color (e.g. today); -1 = none, all bars use the soft color.</summary>
    [Category("Appearance")]
    [DefaultValue(-1)]
    public int HighlightIndex
    {
        get => _highlightIndex;
        set
        {
            _highlightIndex = value;
            Invalidate();
        }
    }

    [Category("Appearance")]
    [DefaultValue("Chưa có dữ liệu")]
    public string EmptyText
    {
        get => _emptyText;
        set
        {
            _emptyText = value ?? string.Empty;
            Invalidate();
        }
    }

    /// <summary>Draws the compact value above each bar (useful for a handful of bars).</summary>
    [Category("Appearance")]
    [DefaultValue(false)]
    public bool ShowValues
    {
        get => _showValues;
        set
        {
            _showValues = value;
            Invalidate();
        }
    }

    /// <summary>Axis and bar-top labels; defaults to <see cref="ChartScale.CompactVnd"/>.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<decimal, string> ValueFormatter
    {
        get => _valueFormatter;
        set
        {
            _valueFormatter = value ?? ChartScale.CompactVnd;
            Invalidate();
        }
    }

    /// <summary>Value in the default tooltip ("1.250.000 ₫"); defaults to <see cref="ValueFormatter"/>.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<decimal, string>? TooltipFormatter
    {
        get => _tooltipFormatter;
        set => _tooltipFormatter = value;
    }

    void IThemeable.ApplyTheme(Theme theme) => Invalidate();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = ChartScale.HitTest(_plot, _points.Count, e.X);
        if (index == _hoverIndex)
        {
            return;
        }

        _hoverIndex = index;
        _toolTip.SetToolTip(this, index < 0 ? null : TooltipOf(_points[index]));
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex >= 0)
        {
            _hoverIndex = -1;
            _toolTip.SetToolTip(this, null);
            Invalidate();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var theme = ThemeManager.Current;
        e.Graphics.Clear(BackColor.A == 255 ? BackColor : SurfaceColors.EffectiveBackColor(this, theme));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var colors = theme.Colors;
        var font = ThemeFonts.Get(FontRole.Caption);
        g.UseHighQuality();

        var axis = ChartScale.NiceAxis(_points.Count == 0 ? 0 : _points.Max(p => p.Value));
        var ticks = axis.Ticks;
        var axisWidth = ticks.Max(t => TextRenderer.MeasureText(g, _valueFormatter(t), font, Size.Empty, flags).Width);
        var gap = this.ScaleDpi(theme.Spacing.Sm);
        var top = font.Height / 2 + (_showValues ? font.Height : 0);
        var bottom = Height - font.Height - gap;
        _plot = new RectangleF(axisWidth + gap, top, Math.Max(0, Width - axisWidth - gap - 1), Math.Max(0, bottom - top));

        // Gridlines and axis labels.
        using (var grid = new Pen(ColorMath.WithAlpha(colors.Border, 160)))
        {
            foreach (var tick in ticks)
            {
                var y = _plot.Bottom - (float)(tick / axis.Max) * _plot.Height;
                g.DrawLine(grid, _plot.Left, y, _plot.Right, y);
                var label = new Rectangle(0, (int)(y - font.Height / 2f), axisWidth, font.Height);
                TextRenderer.DrawText(g, _valueFormatter(tick), font, label, colors.TextMuted, flags | TextFormatFlags.Right);
            }
        }

        if (_points.Count == 0 || _points.All(p => p.Value <= 0))
        {
            var empty = Rectangle.Round(_plot);
            TextRenderer.DrawText(g, _emptyText, ThemeFonts.Get(FontRole.Body), empty, colors.TextMuted, flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        var bars = ChartScale.Bars(_plot, _points.Select(p => p.Value).ToList(), axis.Max, 0.62f, this.ScaleDpi(48));
        var soft = ColorMath.Blend(colors.Primary, SurfaceColors.EffectiveBackColor(this, theme), 0.55);
        var radius = this.ScaleDpi(theme.Spacing.RadiusSm);
        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (i == _hoverIndex)
            {
                var slot = _plot.Width / bars.Count;
                using var hover = new SolidBrush(ColorMath.WithAlpha(colors.SurfaceAlt, 200));
                g.FillRectangle(hover, _plot.Left + slot * i, _plot.Top, slot, _plot.Height);
            }

            if (bar.Height >= 1)
            {
                var color = i == _highlightIndex || i == _hoverIndex ? colors.Primary : soft;
                var r = Math.Min(radius, Math.Min(bar.Width / 2f, bar.Height));
                g.FillRoundedRectangle(color, bar, r);
                if (bar.Height > r)
                {
                    // Square off the base so bars sit flat on the axis.
                    using var brush = new SolidBrush(color);
                    g.FillRectangle(brush, bar.X, bar.Bottom - r, bar.Width, r);
                }
            }

            if (_showValues && _points[i].Value > 0)
            {
                var text = _valueFormatter(_points[i].Value);
                var slot = _plot.Width / bars.Count;
                var rect = new Rectangle((int)(_plot.Left + slot * i), (int)(bar.Top - font.Height - 2), (int)slot, font.Height);
                TextRenderer.DrawText(g, text, font, rect, colors.TextSecondary, flags | TextFormatFlags.HorizontalCenter);
            }
        }

        // Category labels, thinned out so they never overlap (the highlighted one is always shown).
        if (_points.Count > 0)
        {
            var slot = _plot.Width / _points.Count;
            var widest = _points.Max(p => TextRenderer.MeasureText(g, p.Label, font, Size.Empty, flags).Width);
            var every = ChartScale.LabelEvery(slot, widest, gap);
            for (var i = 0; i < _points.Count; i++)
            {
                var forced = i == _highlightIndex;
                var crowdsHighlight = _highlightIndex >= 0 && !forced && Math.Abs(i - _highlightIndex) < every;
                if (!forced && (i % every != 0 || crowdsHighlight))
                {
                    continue;
                }

                var emphasized = i == _highlightIndex || i == _hoverIndex;
                var labelFont = emphasized ? ThemeFonts.Get(FontRole.Caption, FontStyle.Bold) : font;
                var center = _plot.Left + slot * i + slot / 2f;
                var labelWidth = Math.Max(slot, widest + gap);
                var rect = new Rectangle((int)(center - labelWidth / 2f), (int)_plot.Bottom + gap / 2, (int)labelWidth, font.Height);
                TextRenderer.DrawText(g, _points[i].Label, labelFont, rect, emphasized ? colors.TextPrimary : colors.TextSecondary, flags | TextFormatFlags.HorizontalCenter);
            }
        }
    }

    private string TooltipOf(ChartPoint point) =>
        point.Tooltip ?? $"{point.Label}: {(_tooltipFormatter ?? _valueFormatter)(point.Value)}";

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => Invalidate();
}
