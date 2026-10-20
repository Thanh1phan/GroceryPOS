using System.ComponentModel;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Display;

/// <summary>
/// Rounded surface panel with an optional header (icon, title, subtitle) and a soft shadow. Child controls are laid
/// out below the header inside <see cref="Panel.Padding"/>, so docking works as in a normal panel.
/// </summary>
[ToolboxItem(true)]
public class ModernCard : Panel, IThemeable
{
    private string? _title;
    private string? _subtitle;
    private string? _headerGlyph;
    private bool _elevated = true;
    private int _cornerRadius = -1;

    public ModernCard()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        Padding = new Padding(16);
        Size = new Size(280, 160);
        ApplyColors(ThemeManager.Current);
    }

    /// <summary>Header title ("Doanh thu 7 ngày"); no header is drawn when empty.</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? Title
    {
        get => _title;
        set => SetHeader(ref _title, value);
    }

    /// <summary>Second header line in muted text ("Cập nhật lúc 14:05").</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? Subtitle
    {
        get => _subtitle;
        set => SetHeader(ref _subtitle, value);
    }

    /// <summary>Icon from <see cref="Icons.Glyphs"/> shown before the title.</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? HeaderGlyph
    {
        get => _headerGlyph;
        set => SetHeader(ref _headerGlyph, value);
    }

    /// <summary>Draws a soft shadow under the card (it takes a few pixels at the bottom/right edge).</summary>
    [Category("Appearance")]
    [DefaultValue(true)]
    public bool Elevated
    {
        get => _elevated;
        set
        {
            if (_elevated != value)
            {
                _elevated = value;
                PerformLayout();
                Invalidate();
            }
        }
    }

    /// <summary>Corner radius in pixels at 96 DPI; -1 uses the theme's large radius.</summary>
    [Category("Appearance")]
    [DefaultValue(-1)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            if (_cornerRadius != value)
            {
                _cornerRadius = Math.Max(-1, value);
                Invalidate();
            }
        }
    }

    /// <summary>Children are placed below the header and inside the padding (and above the shadow).</summary>
    public override Rectangle DisplayRectangle
    {
        get
        {
            var shadow = ShadowSize;
            var header = HeaderHeight;
            var rect = new Rectangle(
                Padding.Left,
                Padding.Top + header,
                Width - Padding.Horizontal - shadow,
                Height - Padding.Vertical - header - shadow);
            return new Rectangle(rect.X, rect.Y, Math.Max(0, rect.Width), Math.Max(0, rect.Height));
        }
    }

    /// <summary>Height taken by the header (0 without title), including the gap to the content.</summary>
    protected int HeaderHeight
    {
        get
        {
            if (string.IsNullOrEmpty(_title))
            {
                return 0;
            }

            var height = ThemeFonts.Get(FontRole.Subtitle, FontStyle.Bold).Height;
            if (!string.IsNullOrEmpty(_subtitle))
            {
                height += ThemeFonts.Get(FontRole.Caption).Height;
            }

            return height + this.ScaleDpi(ThemeManager.Current.Spacing.Md);
        }
    }

    protected int ShadowSize => _elevated ? this.ScaleDpi(3) : 0;

    /// <summary>The card body (without the shadow strip).</summary>
    protected Rectangle CardBounds => new(0, 0, Width - ShadowSize - 1, Height - ShadowSize - 1);

    void IThemeable.ApplyTheme(Theme theme)
    {
        ApplyColors(theme);
        foreach (Control child in Controls)
        {
            ThemeManager.Apply(child);
        }
    }

    bool IThemeable.ThemesChildren => true;

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

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not null)
        {
            ThemeManager.Apply(e.Control);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Everything is painted in OnPaint to avoid flicker.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var colors = SurfacePalette.Card(theme);
        g.Clear(SurfaceColors.EffectiveBackColor(this, theme));
        g.UseHighQuality();

        var radius = this.ScaleDpi(_cornerRadius >= 0 ? _cornerRadius : theme.Spacing.RadiusLg);
        var body = CardBounds;
        if (_elevated)
        {
            var shadow = ShadowSize;
            for (var i = shadow; i >= 1; i--)
            {
                var layer = Color.FromArgb(colors.Shadow.A / shadow, colors.Shadow);
                var bounds = new RectangleF(body.X + 1, body.Y + i, body.Width + i - 1, body.Height);
                g.FillRoundedRectangle(layer, bounds, radius + i);
            }
        }

        g.FillRoundedRectangle(colors.Background, body, radius);
        g.DrawRoundedRectangle(colors.Border, new RectangleF(body.X, body.Y, body.Width + 1, body.Height + 1), radius);

        PaintHeader(g, theme, colors);
        PaintContent(g, theme, colors);
    }

    /// <summary>Hook for derived cards (KPI cards) to draw inside <see cref="DisplayRectangle"/>.</summary>
    protected virtual void PaintContent(Graphics g, Theme theme, CardColors colors)
    {
    }

    private void PaintHeader(Graphics g, Theme theme, CardColors colors)
    {
        if (string.IsNullOrEmpty(_title))
        {
            return;
        }

        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var titleFont = ThemeFonts.Get(FontRole.Subtitle, FontStyle.Bold);
        var x = Padding.Left;
        var right = CardBounds.Right - Padding.Right;

        if (_headerGlyph is not null)
        {
            var iconFont = ThemeFonts.Icon(theme.Typography.Subtitle);
            var iconSize = TextRenderer.MeasureText(g, _headerGlyph, iconFont, Size.Empty, flags);
            TextRenderer.DrawText(g, _headerGlyph, iconFont, new Rectangle(x, Padding.Top, iconSize.Width, titleFont.Height), theme.Colors.Primary, flags | TextFormatFlags.VerticalCenter);
            x += iconSize.Width + this.ScaleDpi(theme.Spacing.Sm);
        }

        TextRenderer.DrawText(g, _title, titleFont, new Rectangle(x, Padding.Top, Math.Max(0, right - x), titleFont.Height), colors.Title, flags);
        if (!string.IsNullOrEmpty(_subtitle))
        {
            var captionFont = ThemeFonts.Get(FontRole.Caption);
            TextRenderer.DrawText(g, _subtitle, captionFont, new Rectangle(x, Padding.Top + titleFont.Height, Math.Max(0, right - x), captionFont.Height), colors.Subtitle, flags);
        }
    }

    private void ApplyColors(Theme theme)
    {
        BackColor = theme.Colors.Surface;
        ForeColor = theme.Colors.TextPrimary;
        Invalidate();
    }

    private void SetHeader(ref string? field, string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value;
        if (field == value)
        {
            return;
        }

        field = value;
        PerformLayout();
        Invalidate();
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => ApplyColors(e.Current);
}
