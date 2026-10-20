using System.ComponentModel;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Display;

/// <summary>
/// Small pill label for statuses ("Hết hàng", "Hoàn thành") and counts. Sizes itself to its text when
/// <see cref="Control.AutoSize"/> is on (the default).
/// </summary>
[ToolboxItem(true)]
public class ModernBadge : Control, IThemeable
{
    private BadgeKind _kind = BadgeKind.Neutral;
    private BadgeStyle _badgeStyle = BadgeStyle.Soft;

    public ModernBadge()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Color.Transparent;
        Font = ThemeFonts.Get(FontRole.Caption, FontStyle.Bold);
        AutoSize = true;
    }

    [Category("Appearance")]
    [DefaultValue(BadgeKind.Neutral)]
    public BadgeKind Kind
    {
        get => _kind;
        set
        {
            if (_kind != value)
            {
                _kind = value;
                Invalidate();
            }
        }
    }

    [Category("Appearance")]
    [DefaultValue(BadgeStyle.Soft)]
    public BadgeStyle BadgeStyle
    {
        get => _badgeStyle;
        set
        {
            if (_badgeStyle != value)
            {
                _badgeStyle = value;
                Invalidate();
            }
        }
    }

    [DefaultValue(true)]
    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public override bool AutoSize
    {
        get => base.AutoSize;
        set => base.AutoSize = value;
    }

    /// <summary>Sets text and kind together, e.g. <c>badge.Set("Sắp hết", BadgeKind.Warning)</c>.</summary>
    public void Set(string text, BadgeKind kind)
    {
        Text = text;
        Kind = kind;
    }

    public override Size GetPreferredSize(Size proposedSize) => Measure(this, Text, Font);

    /// <summary>Size of a badge with this text and font at the control's DPI.</summary>
    internal static Size Measure(Control owner, string? text, Font font)
    {
        var textSize = TextRenderer.MeasureText(text ?? string.Empty, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var height = textSize.Height + owner.ScaleDpi(6);
        var width = Math.Max(height, textSize.Width + owner.ScaleDpi(16));
        return new Size(width, height);
    }

    /// <summary>Paints a pill badge into <paramref name="bounds"/>; shared with the sidebar and tab strip.</summary>
    internal static void PaintPill(Graphics g, Rectangle bounds, string text, Font font, BadgeColors colors)
    {
        g.FillRoundedRectangle(colors.Background, bounds, bounds.Height / 2f);
        TextRenderer.DrawText(
            g,
            text,
            font,
            bounds,
            colors.Foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    void IThemeable.ApplyTheme(Theme theme) => Invalidate();

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (AutoSize)
        {
            Size = GetPreferredSize(Size.Empty);
        }

        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (AutoSize)
        {
            Size = GetPreferredSize(Size.Empty);
        }
    }

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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var surface = SurfaceColors.EffectiveBackColor(this, theme);
        g.Clear(surface);
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        g.UseHighQuality();
        var colors = BadgePalette.Resolve(theme, _kind, _badgeStyle, surface);
        var size = Measure(this, Text, Font);
        var bounds = new Rectangle(0, (Height - size.Height) / 2, Math.Min(Width, size.Width), Math.Min(Height, size.Height));
        PaintPill(g, bounds, Text, Font, colors);
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => Invalidate();
}
