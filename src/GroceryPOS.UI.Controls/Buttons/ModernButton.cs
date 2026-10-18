using System.ComponentModel;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Buttons;

/// <summary>
/// Rounded, owner-drawn button with Primary / Secondary / Danger / Ghost variants and an optional icon glyph.
/// It is still a real <see cref="Button"/>, so <c>AcceptButton</c>, <c>DialogResult</c>, mnemonics and
/// accessibility keep working.
/// </summary>
[ToolboxItem(true)]
[DefaultEvent(nameof(Click))]
public class ModernButton : Button, IThemeable
{
    private ButtonVariant _variant = ButtonVariant.Primary;
    private int _cornerRadius = -1;
    private string? _iconGlyph;
    private bool _hovered;
    private bool _pressed;

    public ModernButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        Size = new Size(120, ThemeManager.Current.Spacing.ControlHeight);
        Font = ThemeFonts.Get(FontRole.Body, FontStyle.Bold);
    }

    [Category("Appearance")]
    [Description("Kiểu nút: Primary (chính), Secondary (phụ), Danger (nguy hiểm), Ghost (chỉ chữ).")]
    [DefaultValue(ButtonVariant.Primary)]
    public ButtonVariant Variant
    {
        get => _variant;
        set
        {
            if (_variant != value)
            {
                _variant = value;
                Invalidate();
            }
        }
    }

    /// <summary>Corner radius in pixels at 96 DPI; -1 uses the theme's medium radius.</summary>
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

    /// <summary>Icon from <see cref="Icons.Glyphs"/> drawn before the text (or alone when there is no text).</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? IconGlyph
    {
        get => _iconGlyph;
        set
        {
            if (_iconGlyph != value)
            {
                _iconGlyph = string.IsNullOrEmpty(value) ? null : value;
                Invalidate();
            }
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public InteractionState State => InteractionStates.From(Enabled, _hovered, _pressed);

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

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        var theme = ThemeManager.Current;
        g.Clear(EffectiveParentBackColor(theme));
        g.UseHighQuality();

        var colors = ButtonPalette.Resolve(theme, _variant, State);
        var radius = this.ScaleDpi(_cornerRadius >= 0 ? _cornerRadius : theme.Spacing.RadiusMd);
        var bounds = new RectangleF(0, 0, Width, Height);

        g.FillRoundedRectangle(colors.Background, bounds, radius);
        g.DrawRoundedRectangle(colors.Border, bounds, radius, this.ScaleDpi(theme.Spacing.BorderWidth));

        if (Focused && ShowFocusCues)
        {
            var ring = this.ScaleDpi(theme.Spacing.FocusRingWidth);
            g.DrawRoundedRectangle(theme.Colors.Focus, bounds, radius, ring);
        }

        DrawContent(g, theme, colors.Foreground);
    }

    private void DrawContent(Graphics g, Theme theme, Color foreground)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        var hasText = !string.IsNullOrEmpty(Text);
        var padding = this.ScaleDpi(theme.Spacing.Md);
        var gap = hasText && _iconGlyph is not null ? this.ScaleDpi(theme.Spacing.Sm) : 0;

        var iconFont = ThemeFonts.Icon(Font.SizeInPoints + 1f);
        var iconSize = _iconGlyph is null ? Size.Empty : TextRenderer.MeasureText(g, _iconGlyph, iconFont, Size.Empty, flags);
        var textSize = hasText ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags) : Size.Empty;

        var available = Math.Max(0, Width - (2 * padding) - iconSize.Width - gap);
        var textWidth = Math.Min(textSize.Width, available);
        var contentWidth = iconSize.Width + gap + textWidth;
        var x = (Width - contentWidth) / 2;

        if (_iconGlyph is not null)
        {
            TextRenderer.DrawText(g, _iconGlyph, iconFont, new Rectangle(x, 0, iconSize.Width, Height), foreground, flags);
            x += iconSize.Width + gap;
        }

        if (hasText)
        {
            var textFlags = flags | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textWidth, Height), foreground, textFlags);
        }
    }

    private Color EffectiveParentBackColor(Theme theme)
    {
        for (var parent = Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.BackColor.A == 255)
            {
                return parent.BackColor;
            }
        }

        return theme.Colors.Background;
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => Invalidate();
}
