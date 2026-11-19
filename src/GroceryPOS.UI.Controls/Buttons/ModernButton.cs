using System.ComponentModel;
using GroceryPOS.UI.Controls.Buttons.Core;
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
    private bool _autoFitWidth = true;
    private int _contentWidth = -1;

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
                ContentChanged();
            }
        }
    }

    /// <summary>
    /// True (default): a button with text is never narrower than its icon + text, so a fixed width chosen in code can
    /// no longer cut the caption with "…" (it grows to the right instead). Ignored for icon-only buttons, when docked
    /// Fill/Top/Bottom or with <see cref="Control.AutoSize"/>, where the layout or <see cref="GetPreferredSize"/> decides.
    /// </summary>
    [Category("Layout")]
    [DefaultValue(true)]
    public bool AutoFitWidth
    {
        get => _autoFitWidth;
        set
        {
            _autoFitWidth = value;
            ContentChanged();
        }
    }

    /// <summary>Width needed to show the icon and the whole text with the button's padding.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ContentWidth
    {
        get
        {
            if (_contentWidth < 0)
            {
                _contentWidth = MeasureContentWidth();
            }

            return _contentWidth;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public InteractionState State => InteractionStates.From(Enabled, _hovered, _pressed);

    void IThemeable.ApplyTheme(Theme theme) => Invalidate();

    /// <summary>Content width × the theme's control height; used by <see cref="Control.AutoSize"/> and layout panels.</summary>
    public override Size GetPreferredSize(Size proposedSize) =>
        new(ContentWidth, this.ScaleDpi(ThemeManager.Current.Spacing.ControlHeight));

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (FitsWidth && (specified & BoundsSpecified.Width) != 0 && width < ContentWidth)
        {
            width = ContentWidth;
        }

        base.SetBoundsCore(x, y, width, height, specified);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        ContentChanged();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ContentChanged();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ContentChanged();
    }

    protected override void OnDockChanged(EventArgs e)
    {
        base.OnDockChanged(e);
        ContentChanged();
    }

    // Icon-only buttons (pager, date arrows) are sized square on purpose and their glyph always fits centered.
    private bool FitsWidth =>
        _autoFitWidth && !AutoSize && !string.IsNullOrEmpty(Text) && Dock is not (DockStyle.Fill or DockStyle.Top or DockStyle.Bottom);

    private void ContentChanged()
    {
        _contentWidth = -1;
        if (FitsWidth && Width < ContentWidth)
        {
            Width = ContentWidth;
        }

        Invalidate();
    }

    private int MeasureContentWidth()
    {
        // Same fonts, flags and spacing as DrawContent.
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var theme = ThemeManager.Current;
        var hasText = !string.IsNullOrEmpty(Text);
        var iconWidth = _iconGlyph is null ? 0 : TextRenderer.MeasureText(_iconGlyph, ThemeFonts.Icon(Font.SizeInPoints + 1f), Size.Empty, flags).Width;
        var textWidth = hasText ? TextRenderer.MeasureText(Text, Font, Size.Empty, flags | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix)).Width : 0;
        return ButtonMetrics.ContentWidth(iconWidth, textWidth, this.ScaleDpi(theme.Spacing.Md), this.ScaleDpi(theme.Spacing.Sm), this.ScaleDpi(2));
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
        // EndEllipsis must stay out of the MeasureText flags: combined with the unconstrained Size.Empty probe,
        // GDI treats the zero-width box as the rectangle to ellipsize into and returns a near-empty size.
        const TextFormatFlags measureFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        const TextFormatFlags drawFlags = measureFlags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        var hasText = !string.IsNullOrEmpty(Text);
        var padding = this.ScaleDpi(theme.Spacing.Md);
        var gap = hasText && _iconGlyph is not null ? this.ScaleDpi(theme.Spacing.Sm) : 0;

        var iconFont = ThemeFonts.Icon(Font.SizeInPoints + 1f);
        var iconSize = _iconGlyph is null ? Size.Empty : TextRenderer.MeasureText(g, _iconGlyph, iconFont, Size.Empty, measureFlags);
        var textSize = hasText ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, measureFlags) : Size.Empty;

        var available = Math.Max(0, Width - (2 * padding) - iconSize.Width - gap);
        var textWidth = Math.Min(textSize.Width, available);
        var contentWidth = iconSize.Width + gap + textWidth;
        var x = (Width - contentWidth) / 2;

        if (_iconGlyph is not null)
        {
            TextRenderer.DrawText(g, _iconGlyph, iconFont, new Rectangle(x, 0, iconSize.Width, Height), foreground, drawFlags);
            x += iconSize.Width + gap;
        }

        if (hasText)
        {
            var textFlags = drawFlags | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix);
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
