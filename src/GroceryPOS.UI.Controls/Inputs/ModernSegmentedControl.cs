using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Inputs.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Inputs;

/// <summary>Color of the selected segment of a <see cref="ModernSegmentedControl"/>.</summary>
public enum SegmentAccent
{
    Primary,
    Danger,
}

/// <summary>
/// Pill-shaped choice between a few options ("Nộp tiền vào két" | "Chi tiền từ két", "Tiền mặt" | "Chuyển khoản"),
/// replacing loose radio buttons. One tab stop; ←/→, Home/End and the first letter of an option change the
/// selection, a click selects. Its <see cref="Control.Text"/> is the selected option, so dialogs can track it.
/// </summary>
[ToolboxItem(true)]
[DefaultEvent(nameof(SelectedIndexChanged))]
public class ModernSegmentedControl : Control, IThemeable
{
    private readonly List<string> _items = new();
    private IReadOnlyList<(int X, int Width)> _segments = Array.Empty<(int, int)>();
    private int _selectedIndex = -1;
    private int _hovered = -1;
    private SegmentAccent _accent = SegmentAccent.Primary;

    public ModernSegmentedControl()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.SupportsTransparentBackColor,
            true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = ThemeFonts.Get(FontRole.Body);
        Size = new Size(240, ThemeManager.Current.Spacing.ControlHeight);
    }

    /// <summary>Raised when the user or code changes <see cref="SelectedIndex"/>.</summary>
    public event EventHandler? SelectedIndexChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> Items => _items;

    [Category("Behavior")]
    [DefaultValue(-1)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            value = _items.Count == 0 ? -1 : Math.Clamp(value, -1, _items.Count - 1);
            if (_selectedIndex == value)
            {
                return;
            }

            _selectedIndex = value;
            base.Text = SelectedItem ?? string.Empty;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Selection, value);
            OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    [Browsable(false)]
    public string? SelectedItem => _selectedIndex >= 0 ? _items[_selectedIndex] : null;

    [Category("Appearance")]
    [DefaultValue(SegmentAccent.Primary)]
    public SegmentAccent Accent
    {
        get => _accent;
        set
        {
            if (_accent != value)
            {
                _accent = value;
                Invalidate();
            }
        }
    }

    /// <summary>The selected option; read-only for callers (set <see cref="SelectedIndex"/> instead).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set { }
    }

    /// <summary>Replaces the options and selects <paramref name="selected"/> (first by default).</summary>
    public void SetItems(IEnumerable<string> items, int selected = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = -1;
        ArrangeSegments();
        SelectedIndex = selected;
        Invalidate();
    }

    public void ApplyTheme(Theme theme)
    {
        Font = ThemeFonts.Get(FontRole.Body);
        Invalidate();
    }

    protected virtual void OnSelectedIndexChanged(EventArgs e) => SelectedIndexChanged?.Invoke(this, e);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var padding = this.ScaleDpi(ThemeManager.Current.Spacing.Lg);
        return new Size(SegmentLayout.PreferredWidth(MeasureLabels(), padding) + 8, this.ScaleDpi(ThemeManager.Current.Spacing.ControlHeight));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ArrangeSegments();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ArrangeSegments();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hovered = Enabled ? SegmentLayout.HitTest(_segments, e.X - Inset) : -1;
        if (hovered != _hovered)
        {
            _hovered = hovered;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left)
        {
            return;
        }

        Focus();
        var index = SegmentLayout.HitTest(_segments, e.X - Inset);
        if (index >= 0)
        {
            SelectedIndex = index;
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Modifiers != Keys.None)
        {
            return;
        }

        var index = e.KeyCode switch
        {
            Keys.Left => SegmentLayout.Step(_selectedIndex, -1, _items.Count),
            Keys.Right => SegmentLayout.Step(_selectedIndex, 1, _items.Count),
            Keys.Home => _items.Count > 0 ? 0 : -1,
            Keys.End => _items.Count - 1,
            _ => int.MinValue,
        };
        if (index != int.MinValue)
        {
            SelectedIndex = index;
            e.Handled = true;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.Handled || char.IsControl(e.KeyChar))
        {
            return;
        }

        var start = Math.Max(0, _selectedIndex + 1);
        for (var i = 0; i < _items.Count; i++)
        {
            var index = (start + i) % _items.Count;
            if (_items[index].Length > 0 && char.ToUpperInvariant(_items[index][0]) == char.ToUpperInvariant(e.KeyChar))
            {
                SelectedIndex = index;
                e.Handled = true;
                return;
            }
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var c = theme.Colors;
        g.Clear(SurfaceColors.EffectiveBackColor(this, theme));
        g.UseHighQuality();

        var radius = this.ScaleDpi(theme.Spacing.RadiusMd);
        var track = new RectangleF(0, 0, Width - 1, Height - 1);
        g.FillRoundedRectangle(Enabled ? c.SurfaceAlt : c.DisabledBackground, track, radius);
        g.DrawRoundedRectangle(c.Border, track, radius, this.ScaleDpi(theme.Spacing.BorderWidth));

        var accent = _accent == SegmentAccent.Danger ? c.Danger : c.Primary;
        var onAccent = _accent == SegmentAccent.Danger ? c.OnDanger : c.OnPrimary;
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        for (var i = 0; i < _segments.Count && i < _items.Count; i++)
        {
            var (x, width) = _segments[i];
            var cell = new RectangleF(x + Inset, Inset, Math.Max(0, width), Math.Max(0, Height - (2 * Inset) - 1));
            var selected = i == _selectedIndex;
            Color fore;
            if (selected)
            {
                g.FillRoundedRectangle(Enabled ? accent : c.DisabledText, cell, Math.Max(2, radius - Inset));
                fore = Enabled ? onAccent : c.DisabledBackground;
            }
            else
            {
                if (i == _hovered)
                {
                    g.FillRoundedRectangle(theme.Hover(c.SurfaceAlt), cell, Math.Max(2, radius - Inset));
                }

                fore = Enabled ? c.TextPrimary : c.DisabledText;
            }

            var font = selected ? ThemeFonts.Get(FontRole.Body, FontStyle.Bold) : Font;
            TextRenderer.DrawText(g, _items[i], font, Rectangle.Round(cell), fore, flags);
        }

        if (Focused && ShowFocusCues)
        {
            g.DrawRoundedRectangle(c.Focus, track, radius, this.ScaleDpi(theme.Spacing.FocusRingWidth));
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SegmentedAccessibleObject(this);

    private int Inset => this.ScaleDpi(3);

    private int[] MeasureLabels()
    {
        var bold = ThemeFonts.Get(FontRole.Body, FontStyle.Bold);
        return _items.Select(t => TextRenderer.MeasureText(t, bold, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width).ToArray();
    }

    private void ArrangeSegments()
    {
        _segments = SegmentLayout.Arrange(MeasureLabels(), Math.Max(0, Width - (2 * Inset)), this.ScaleDpi(ThemeManager.Current.Spacing.Md));
        Invalidate();
    }

    private sealed class SegmentedAccessibleObject(ModernSegmentedControl owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.PageTabList;

        public override string? Value => owner.SelectedItem;
    }
}
