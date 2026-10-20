using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Navigation.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Navigation;

/// <summary>A page of <see cref="ModernTabControl"/>. Its <see cref="Control.Text"/> is the tab caption.</summary>
[ToolboxItem(false)]
public class ModernTabPage : Panel
{
    private string? _iconGlyph;
    private int _badgeCount;
    private BadgeKind _badgeKind = BadgeKind.Neutral;

    public ModernTabPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;
    }

    public ModernTabPage(string text)
        : this()
    {
        Text = text;
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value;
            (Parent as ModernTabControl)?.RefreshStrip();
        }
    }

    [Category("Appearance")]
    [DefaultValue(null)]
    public string? IconGlyph
    {
        get => _iconGlyph;
        set
        {
            _iconGlyph = value;
            (Parent as ModernTabControl)?.RefreshStrip();
        }
    }

    /// <summary>Count shown next to the caption ("Chờ duyệt 3"); 0 hides it.</summary>
    [Category("Appearance")]
    [DefaultValue(0)]
    public int BadgeCount
    {
        get => _badgeCount;
        set
        {
            _badgeCount = Math.Max(0, value);
            (Parent as ModernTabControl)?.RefreshStrip();
        }
    }

    [Category("Appearance")]
    [DefaultValue(BadgeKind.Neutral)]
    public BadgeKind BadgeKind
    {
        get => _badgeKind;
        set
        {
            _badgeKind = value;
            (Parent as ModernTabControl)?.RefreshStrip();
        }
    }
}

/// <summary>Cancelable tab change (e.g. ask before leaving a page with unsaved edits).</summary>
public sealed class TabSelectingEventArgs(int currentIndex, int newIndex) : CancelEventArgs
{
    public int CurrentIndex { get; } = currentIndex;

    public int NewIndex { get; } = newIndex;
}

/// <summary>
/// Flat tab control: underlined headers with optional icon and count badge, overflow arrows when the headers do
/// not fit, and a page area below. Keyboard: Ctrl+Tab / Ctrl+Shift+Tab anywhere inside, ←/→/Home/End on the strip.
/// </summary>
[ToolboxItem(true)]
[DefaultEvent(nameof(SelectedIndexChanged))]
public class ModernTabControl : Control, IThemeable
{
    private readonly TabPageCollection _pages;
    private int _selectedIndex = -1;
    private int _firstVisible;
    private int _hoverIndex = -1;
    private int _hoverArrow;
    private TabStripArrangement _arrangement = TabStripArrangement.Empty;
    private int[] _widths = Array.Empty<int>();

    public ModernTabControl()
    {
        // Created first: setting Size below already runs layout, which reads the pages.
        _pages = new TabPageCollection(this);
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.ContainerControl,
            true);
        TabStop = true;
        Size = new Size(480, 320);
        Font = ThemeFonts.Get(FontRole.Body);
    }

    public event EventHandler<TabSelectingEventArgs>? Selecting;

    public event EventHandler? SelectedIndexChanged;

    [Category("Behavior")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public Collection<ModernTabPage> TabPages => _pages;

    [Category("Behavior")]
    [DefaultValue(-1)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => TrySelect(value);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTabPage? SelectedTab
    {
        get => _selectedIndex >= 0 && _selectedIndex < _pages.Count ? _pages[_selectedIndex] : null;
        set => TrySelect(value is null ? -1 : _pages.IndexOf(value));
    }

    /// <summary>Height of the header strip.</summary>
    protected int StripHeight => this.ScaleDpi(44);

    private int ArrowWidth => this.ScaleDpi(28);

    public override Rectangle DisplayRectangle =>
        new(Padding.Left, StripHeight + Padding.Top, Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - StripHeight - Padding.Vertical));

    /// <summary>Selects a tab; returns false when it is out of range or a <see cref="Selecting"/> handler cancelled.</summary>
    public bool TrySelect(int index)
    {
        if (index < -1 || index >= _pages.Count)
        {
            return false;
        }

        if (index == _selectedIndex)
        {
            return true;
        }

        if (_selectedIndex >= 0 && index >= 0)
        {
            var args = new TabSelectingEventArgs(_selectedIndex, index);
            Selecting?.Invoke(this, args);
            if (args.Cancel)
            {
                return false;
            }
        }

        _selectedIndex = index;
        UpdatePageVisibility();
        RefreshStrip();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    void IThemeable.ApplyTheme(Theme theme)
    {
        foreach (var page in _pages)
        {
            page.BackColor = Color.Transparent;
            foreach (Control child in page.Controls)
            {
                ThemeManager.Apply(child);
            }
        }

        RefreshStrip();
    }

    bool IThemeable.ThemesChildren => true;

    internal void RefreshStrip()
    {
        MeasureTabs();
        Invalidate(new Rectangle(0, 0, Width, StripHeight));
    }

    internal void OnPageAdded(ModernTabPage page)
    {
        page.Visible = false;
        Controls.Add(page);
        ThemeManager.Apply(page);
        if (_selectedIndex < 0)
        {
            _selectedIndex = 0;
            UpdatePageVisibility();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        UpdatePageVisibility();
        RefreshStrip();
    }

    internal void OnPageRemoved(ModernTabPage page, int index)
    {
        Controls.Remove(page);
        if (_pages.Count == 0)
        {
            _selectedIndex = -1;
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (index < _selectedIndex || _selectedIndex >= _pages.Count)
        {
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (index == _selectedIndex)
        {
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        UpdatePageVisibility();
        RefreshStrip();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeManager.ThemeChanged += OnThemeChanged;
        RefreshStrip();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnHandleDestroyed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Arrange();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_pages.Count > 1 && (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab)))
        {
            var step = (keyData & Keys.Shift) == Keys.Shift ? -1 : 1;
            TrySelect(((_selectedIndex + step) % _pages.Count + _pages.Count) % _pages.Count);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var target = e.KeyCode switch
        {
            Keys.Left => _selectedIndex - 1,
            Keys.Right => _selectedIndex + 1,
            Keys.Home => 0,
            Keys.End => _pages.Count - 1,
            _ => int.MinValue,
        };

        if (target != int.MinValue && _pages.Count > 0)
        {
            TrySelect(Math.Clamp(target, 0, _pages.Count - 1));
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate(new Rectangle(0, 0, Width, StripHeight));
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate(new Rectangle(0, 0, Width, StripHeight));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (index, arrow) = HitTest(e.Location);
        if (index != _hoverIndex || arrow != _hoverArrow)
        {
            _hoverIndex = index;
            _hoverArrow = arrow;
            Cursor = index >= 0 || arrow != 0 ? Cursors.Hand : Cursors.Default;
            Invalidate(new Rectangle(0, 0, Width, StripHeight));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        _hoverArrow = 0;
        Invalidate(new Rectangle(0, 0, Width, StripHeight));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var (index, arrow) = HitTest(e.Location);
        if (arrow != 0)
        {
            Scroll(arrow);
        }
        else if (index >= 0)
        {
            Focus();
            TrySelect(index);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Y < StripHeight && _arrangement.Overflow)
        {
            Scroll(e.Delta > 0 ? -1 : 1);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var background = SurfaceColors.EffectiveBackColor(this, theme);
        if (BackColor.A == 255)
        {
            background = BackColor;
        }

        var colors = SurfacePalette.Tabs(theme, background);
        g.Clear(background);
        g.UseHighQuality();

        using (var divider = new Pen(colors.Divider))
        {
            g.DrawLine(divider, 0, StripHeight - 1, Width, StripHeight - 1);
        }

        var origin = Padding.Left;
        for (var i = 0; i < _arrangement.Count; i++)
        {
            var index = _arrangement.First + i;
            var bounds = new Rectangle(origin + _arrangement.Offsets[i], 0, _widths[index], StripHeight);
            PaintTab(g, theme, colors, _pages[index], index, bounds);
        }

        if (_arrangement.Overflow)
        {
            PaintArrows(g, theme, colors, background);
        }
    }

    private void PaintTab(Graphics g, Theme theme, TabColors colors, ModernTabPage page, int index, Rectangle bounds)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var selected = index == _selectedIndex;
        var hover = index == _hoverIndex;
        var inner = Rectangle.Inflate(bounds, -this.ScaleDpi(2), -this.ScaleDpi(6));

        if (hover && !selected && page.Enabled)
        {
            g.FillRoundedRectangle(colors.HoverBackground, inner, this.ScaleDpi(theme.Spacing.RadiusSm));
        }

        if (selected && Focused && ShowFocusCues)
        {
            g.DrawRoundedRectangle(theme.Colors.Focus, inner, this.ScaleDpi(theme.Spacing.RadiusSm), this.ScaleDpi(theme.Spacing.FocusRingWidth));
        }

        var foreground = !page.Enabled ? colors.DisabledText : selected ? colors.SelectedText : colors.Text;
        var x = bounds.X + this.ScaleDpi(14);
        if (page.IconGlyph is not null)
        {
            var iconFont = ThemeFonts.Icon(theme.Typography.Body);
            var iconSize = TextRenderer.MeasureText(g, page.IconGlyph, iconFont, Size.Empty, flags);
            TextRenderer.DrawText(g, page.IconGlyph, iconFont, new Rectangle(x, bounds.Y, iconSize.Width, bounds.Height), foreground, flags);
            x += iconSize.Width + this.ScaleDpi(6);
        }

        var font = selected ? ThemeFonts.Get(FontRole.Body, FontStyle.Bold) : Font;
        var textSize = TextRenderer.MeasureText(g, page.Text, font, Size.Empty, flags);
        TextRenderer.DrawText(g, page.Text, font, new Rectangle(x, bounds.Y, textSize.Width, bounds.Height), foreground, flags);
        x += textSize.Width;

        var count = BadgeText.Count(page.BadgeCount);
        if (count.Length > 0)
        {
            var badgeFont = ThemeFonts.Get(FontRole.Caption, FontStyle.Bold);
            var size = ModernBadge.Measure(this, count, badgeFont);
            var badge = new Rectangle(x + this.ScaleDpi(6), bounds.Y + ((bounds.Height - size.Height) / 2), size.Width, size.Height);
            ModernBadge.PaintPill(g, badge, count, badgeFont, BadgePalette.Resolve(theme, page.BadgeKind, BadgeStyle.Soft, colors.Background));
        }

        if (selected)
        {
            var thickness = this.ScaleDpi(3);
            var indicator = new Rectangle(bounds.X + this.ScaleDpi(8), bounds.Bottom - thickness, Math.Max(0, bounds.Width - this.ScaleDpi(16)), thickness);
            g.FillRoundedRectangle(colors.Indicator, indicator, thickness / 2f);
        }
    }

    private void PaintArrows(Graphics g, Theme theme, TabColors colors, Color background)
    {
        var (back, forward) = ArrowBounds();
        using (var brush = new SolidBrush(background))
        {
            g.FillRectangle(brush, Rectangle.Union(back, forward));
        }

        var iconFont = ThemeFonts.Icon(theme.Typography.Caption);
        PaintArrow(g, back, Glyphs.ChevronLeft, iconFont, _arrangement.CanScrollBack, _hoverArrow == -1, colors, theme);
        PaintArrow(g, forward, Glyphs.ChevronRight, iconFont, _arrangement.CanScrollForward, _hoverArrow == 1, colors, theme);
    }

    private void PaintArrow(Graphics g, Rectangle bounds, string glyph, Font font, bool enabled, bool hover, TabColors colors, Theme theme)
    {
        if (enabled && hover)
        {
            g.FillRoundedRectangle(colors.HoverBackground, Rectangle.Inflate(bounds, -this.ScaleDpi(2), -this.ScaleDpi(8)), this.ScaleDpi(theme.Spacing.RadiusSm));
        }

        TextRenderer.DrawText(g, glyph, font, bounds, enabled ? colors.Text : colors.DisabledText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private (Rectangle Back, Rectangle Forward) ArrowBounds()
    {
        var w = ArrowWidth;
        var right = Width - Padding.Right;
        return (new Rectangle(right - (2 * w), 0, w, StripHeight - 1), new Rectangle(right - w, 0, w, StripHeight - 1));
    }

    private (int Index, int Arrow) HitTest(Point point)
    {
        if (point.Y < 0 || point.Y >= StripHeight)
        {
            return (-1, 0);
        }

        if (_arrangement.Overflow)
        {
            var (back, forward) = ArrowBounds();
            if (back.Contains(point))
            {
                return (-1, _arrangement.CanScrollBack ? -1 : 0);
            }

            if (forward.Contains(point))
            {
                return (-1, _arrangement.CanScrollForward ? 1 : 0);
            }
        }

        for (var i = 0; i < _arrangement.Count; i++)
        {
            var index = _arrangement.First + i;
            var x = Padding.Left + _arrangement.Offsets[i];
            if (point.X >= x && point.X < x + _widths[index])
            {
                return (_pages[index].Enabled ? index : -1, 0);
            }
        }

        return (-1, 0);
    }

    private void Scroll(int direction)
    {
        var next = Math.Clamp(_firstVisible + direction, 0, Math.Max(0, _pages.Count - 1));
        if (next == _firstVisible)
        {
            return;
        }

        _firstVisible = next;

        // Scrolling by hand may hide the selected tab, so arrange without forcing it into view.
        _arrangement = TabStripLayout.Arrange(_widths, Width - Padding.Horizontal, -1, _firstVisible, 2 * ArrowWidth);
        _firstVisible = _arrangement.First;
        Invalidate(new Rectangle(0, 0, Width, StripHeight));
    }

    private void MeasureTabs()
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var theme = ThemeManager.Current;
        var bold = ThemeFonts.Get(FontRole.Body, FontStyle.Bold);
        _widths = _pages.Select(page =>
        {
            // Measured in bold so the strip does not shift when a tab becomes selected.
            var width = TextRenderer.MeasureText(page.Text, bold, Size.Empty, flags).Width + this.ScaleDpi(28);
            if (page.IconGlyph is not null)
            {
                width += TextRenderer.MeasureText(page.IconGlyph, ThemeFonts.Icon(theme.Typography.Body), Size.Empty, flags).Width + this.ScaleDpi(6);
            }

            var count = BadgeText.Count(page.BadgeCount);
            if (count.Length > 0)
            {
                width += ModernBadge.Measure(this, count, ThemeFonts.Get(FontRole.Caption, FontStyle.Bold)).Width + this.ScaleDpi(6);
            }

            return width;
        }).ToArray();
        Arrange();
    }

    private void Arrange()
    {
        if (_widths.Length != _pages.Count)
        {
            MeasureTabs();
            return;
        }

        _arrangement = TabStripLayout.Arrange(_widths, Width - Padding.Horizontal, _selectedIndex, _firstVisible, 2 * ArrowWidth);
        _firstVisible = _arrangement.First;
        Invalidate(new Rectangle(0, 0, Width, StripHeight));
    }

    private void UpdatePageVisibility()
    {
        SuspendLayout();
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == _selectedIndex;
        }

        ResumeLayout(true);
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => RefreshStrip();

    private sealed class TabPageCollection(ModernTabControl owner) : Collection<ModernTabPage>
    {
        protected override void InsertItem(int index, ModernTabPage item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (Contains(item))
            {
                throw new InvalidOperationException("Trang này đã có trong điều khiển.");
            }

            base.InsertItem(index, item);
            if (index <= owner._selectedIndex && Count > 1)
            {
                owner._selectedIndex++;
            }

            owner.OnPageAdded(item);
        }

        protected override void RemoveItem(int index)
        {
            var page = this[index];
            base.RemoveItem(index);
            owner.OnPageRemoved(page, index);
        }

        protected override void SetItem(int index, ModernTabPage item)
        {
            RemoveItem(index);
            InsertItem(index, item);
        }

        protected override void ClearItems()
        {
            var pages = this.ToList();
            base.ClearItems();
            foreach (var page in pages)
            {
                owner.Controls.Remove(page);
            }

            owner._selectedIndex = -1;
            owner._firstVisible = 0;
            owner.RefreshStrip();
            owner.SelectedIndexChanged?.Invoke(owner, EventArgs.Empty);
        }
    }
}
