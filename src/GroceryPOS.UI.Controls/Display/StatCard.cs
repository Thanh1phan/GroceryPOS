using System.ComponentModel;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Display;

/// <summary>
/// KPI card for the dashboard: caption ("Doanh thu hôm nay"), big value ("12.450.000 ₫"), a colored trend against
/// the previous period ("▲ 12,5%") and a note ("so với hôm qua"). Fully owner-drawn; no child controls needed.
/// </summary>
[ToolboxItem(true)]
public class StatCard : ModernCard
{
    private readonly ToolTip _toolTip = new();
    private string? _caption = "Chỉ số";
    private string? _value = "—";
    private string? _note;
    private string? _glyph;
    private BadgeKind _accent = BadgeKind.Primary;
    private decimal? _trendPercent;
    private bool _showTrend;
    private bool _higherIsBetter = true;
    private string _comparedTo = "kỳ trước";

    public StatCard()
    {
        Size = new Size(240, 128);
        Padding = new Padding(16, 14, 16, 14);
    }

    [Category("Appearance")]
    [DefaultValue("Chỉ số")]
    public string? Caption
    {
        get => _caption;
        set => Set(ref _caption, value);
    }

    /// <summary>Formatted value ("12.450.000 ₫", "128", "23,4%").</summary>
    [Category("Appearance")]
    [DefaultValue("—")]
    public string? Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    [Category("Appearance")]
    [DefaultValue(null)]
    public string? Note
    {
        get => _note;
        set => Set(ref _note, value);
    }

    /// <summary>Icon drawn in a tinted circle at the top-right corner.</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? Glyph
    {
        get => _glyph;
        set => Set(ref _glyph, value);
    }

    /// <summary>Color of the icon circle.</summary>
    [Category("Appearance")]
    [DefaultValue(BadgeKind.Primary)]
    public BadgeKind Accent
    {
        get => _accent;
        set
        {
            _accent = value;
            Invalidate();
        }
    }

    /// <summary>False for figures where growth is bad (tiền hoàn trả), so a rise is shown in red.</summary>
    [Category("Behavior")]
    [DefaultValue(true)]
    public bool HigherIsBetter
    {
        get => _higherIsBetter;
        set
        {
            _higherIsBetter = value;
            UpdateTooltip();
            Invalidate();
        }
    }

    /// <summary>
    /// Shows the change against the previous period (12.5 = +12,5%; null = nothing to compare with).
    /// </summary>
    public void SetTrend(decimal? percent, string comparedTo = "kỳ trước")
    {
        _trendPercent = percent;
        _comparedTo = string.IsNullOrWhiteSpace(comparedTo) ? "kỳ trước" : comparedTo;
        _showTrend = true;
        UpdateTooltip();
        Invalidate();
    }

    public void ClearTrend()
    {
        _showTrend = false;
        UpdateTooltip();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void PaintContent(Graphics g, Theme theme, CardColors colors)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var area = DisplayRectangle;
        var captionFont = ThemeFonts.Get(FontRole.Body);
        var valueFont = ThemeFonts.Get(FontRole.Headline, FontStyle.Bold);
        var smallFont = ThemeFonts.Get(FontRole.Caption);
        var right = area.Right;

        if (_glyph is not null)
        {
            var size = this.ScaleDpi(36);
            var circle = new Rectangle(area.Right - size, area.Top, size, size);
            var tint = BadgePalette.Resolve(theme, _accent, BadgeStyle.Soft, colors.Background);
            g.FillRoundedRectangle(tint.Background, circle, size / 2f);
            TextRenderer.DrawText(g, _glyph, ThemeFonts.Icon(theme.Typography.Subtitle), circle, tint.Foreground, flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            right = circle.Left - this.ScaleDpi(theme.Spacing.Sm);
        }

        var y = area.Top;
        TextRenderer.DrawText(g, _caption, captionFont, new Rectangle(area.Left, y, Math.Max(0, right - area.Left), captionFont.Height), colors.Subtitle, flags);
        y += captionFont.Height + this.ScaleDpi(theme.Spacing.Xs);

        TextRenderer.DrawText(g, _value, valueFont, new Rectangle(area.Left, y, Math.Max(0, right - area.Left), valueFont.Height), colors.Title, flags);
        y += valueFont.Height + this.ScaleDpi(theme.Spacing.Xs);

        var x = area.Left;
        if (_showTrend)
        {
            var trend = Trend.Of(_trendPercent, _higherIsBetter, _comparedTo);
            var badgeFont = ThemeFonts.Get(FontRole.Caption, FontStyle.Bold);
            var badgeSize = ModernBadge.Measure(this, trend.Text, badgeFont);
            var badge = new Rectangle(x, y, badgeSize.Width, badgeSize.Height);
            ModernBadge.PaintPill(g, badge, trend.Text, badgeFont, BadgePalette.Resolve(theme, trend.Kind, BadgeStyle.Soft, colors.Background));
            x = badge.Right + this.ScaleDpi(theme.Spacing.Sm);
            y += (badgeSize.Height - smallFont.Height) / 2;
        }

        var note = _note ?? (_showTrend && _trendPercent is not null ? $"so với {_comparedTo}" : null);
        if (!string.IsNullOrEmpty(note))
        {
            TextRenderer.DrawText(g, note, smallFont, new Rectangle(x, y, Math.Max(0, area.Right - x), smallFont.Height), colors.Subtitle, flags);
        }
    }

    private void UpdateTooltip() =>
        _toolTip.SetToolTip(this, _showTrend ? Trend.Of(_trendPercent, _higherIsBetter, _comparedTo).Tooltip : null);

    private void Set(ref string? field, string? value)
    {
        if (field != value)
        {
            field = value;
            Invalidate();
        }
    }
}
