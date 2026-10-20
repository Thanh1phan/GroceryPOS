using System.ComponentModel;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Navigation.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Navigation;

public sealed class NavItemEventArgs(NavItem item) : EventArgs
{
    public NavItem Item { get; } = item;
}

/// <summary>
/// Application sidebar: brand header with a collapse button, then sections of menu items (icon, text, shortcut hint,
/// notification badge). Items the user has no permission for are hidden (<see cref="PermissionFilter"/>).
/// Collapsed, it shows icons only with tooltips. Keyboard: ↑/↓/Home/End move, Enter/Space opens.
/// </summary>
[ToolboxItem(true)]
[DefaultEvent(nameof(ItemSelected))]
public class SidebarNav : Control, IThemeable
{
    private readonly Dictionary<string, (int Count, BadgeKind Kind)> _badges = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToolTip _toolTip = new() { InitialDelay = 300, ShowAlways = true };

    private NavMenu _menu = NavMenu.Empty;
    private NavMenu _visibleMenu = NavMenu.Empty;
    private Func<string, bool>? _permissionFilter;
    private IReadOnlyList<NavRow> _rows = Array.Empty<NavRow>();
    private SidebarLayout _layout = SidebarLayout.Arrange(Array.Empty<NavRow>(), default, false);
    private string? _selectedKey;
    private bool _collapsed;
    private int _expandedWidth = 232;
    private int _collapsedWidth = 60;
    private int _scroll;
    private int _hoverIndex = -1;
    private int _focusIndex = -1;
    private bool _hoverToggle;
    private string? _toolTipText;
    private string _brandText = "GroceryPOS";
    private string? _brandSubtitle;

    public SidebarNav()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        TabStop = true;
        Dock = DockStyle.Left;
        Width = _expandedWidth;
        Font = ThemeFonts.Get(FontRole.Body);
        AccessibleRole = AccessibleRole.MenuBar;
        AccessibleName = "Menu chính";
    }

    /// <summary>Raised when the user opens an item (click, Enter) or <see cref="SelectItem(string, bool)"/> asks for it.</summary>
    public event EventHandler<NavItemEventArgs>? ItemSelected;

    public event EventHandler? CollapsedChanged;

    /// <summary>Full menu; what is shown depends on <see cref="PermissionFilter"/>.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public NavMenu Menu
    {
        get => _menu;
        set
        {
            _menu = value ?? NavMenu.Empty;
            Rebuild();
        }
    }

    /// <summary>
    /// Permission check of the signed-in user (e.g. <c>currentUser.HasPermission</c>); null shows everything.
    /// Set it again after sign-in or a role change.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, bool>? PermissionFilter
    {
        get => _permissionFilter;
        set
        {
            _permissionFilter = value;
            Rebuild();
        }
    }

    /// <summary>The menu the user actually sees.</summary>
    [Browsable(false)]
    public NavMenu VisibleMenu => _visibleMenu;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedKey
    {
        get => _selectedKey;
        set => SelectItem(value, raiseEvent: false);
    }

    [Browsable(false)]
    public NavItem? SelectedItem => _visibleMenu.Find(_selectedKey);

    [Category("Appearance")]
    [DefaultValue(false)]
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value)
            {
                return;
            }

            _collapsed = value;
            _scroll = 0;
            Width = this.ScaleDpi(_collapsed ? _collapsedWidth : _expandedWidth);
            Rebuild();
            CollapsedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Category("Layout")]
    [DefaultValue(232)]
    public int ExpandedWidth
    {
        get => _expandedWidth;
        set
        {
            _expandedWidth = Math.Max(120, value);
            if (!_collapsed)
            {
                Width = this.ScaleDpi(_expandedWidth);
            }
        }
    }

    [Category("Layout")]
    [DefaultValue(60)]
    public int CollapsedWidth
    {
        get => _collapsedWidth;
        set
        {
            _collapsedWidth = Math.Max(40, value);
            if (_collapsed)
            {
                Width = this.ScaleDpi(_collapsedWidth);
            }
        }
    }

    [Category("Appearance")]
    [DefaultValue("GroceryPOS")]
    public string BrandText
    {
        get => _brandText;
        set
        {
            _brandText = value ?? string.Empty;
            Invalidate();
        }
    }

    /// <summary>Second line under the brand, usually the store name.</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? BrandSubtitle
    {
        get => _brandSubtitle;
        set
        {
            _brandSubtitle = value;
            Invalidate();
        }
    }

    /// <summary>Shows a count on an item (e.g. low-stock products); 0 removes it.</summary>
    public void SetBadge(string key, int count, BadgeKind kind = BadgeKind.Danger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (count <= 0)
        {
            _badges.Remove(key);
        }
        else
        {
            _badges[key] = (count, kind);
        }

        Invalidate();
    }

    public int GetBadge(string key) => _badges.TryGetValue(key, out var badge) ? badge.Count : 0;

    /// <summary>
    /// Selects an item by key. Returns false when the key is not in the visible menu (unknown or not permitted);
    /// a null key clears the selection.
    /// </summary>
    public bool SelectItem(string? key, bool raiseEvent = true)
    {
        if (key is null)
        {
            _selectedKey = null;
            Invalidate();
            return true;
        }

        var item = _visibleMenu.Find(key);
        if (item is null)
        {
            return false;
        }

        _selectedKey = item.Key;
        _focusIndex = NavCursor.IndexOf(_rows, item.Key);
        EnsureVisible(_focusIndex);
        Invalidate();
        if (raiseEvent)
        {
            ItemSelected?.Invoke(this, new NavItemEventArgs(item));
        }

        return true;
    }

    void IThemeable.ApplyTheme(Theme theme) => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
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

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Width = this.ScaleDpi(_collapsed ? _collapsedWidth : _expandedWidth);
        Rebuild();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _scroll = Math.Clamp(_scroll, 0, SidebarLayout.MaxScroll(_layout.ContentHeight, ListViewport.Height));
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down:
                MoveFocus(NavCursor.Move(_rows, _focusIndex, +1));
                break;
            case Keys.Up:
                MoveFocus(NavCursor.Move(_rows, _focusIndex, -1));
                break;
            case Keys.Home:
                MoveFocus(NavCursor.First(_rows));
                break;
            case Keys.End:
                MoveFocus(NavCursor.Last(_rows));
                break;
            case Keys.Enter:
            case Keys.Space:
                if (_focusIndex >= 0 && _rows[_focusIndex].Item is { } item)
                {
                    SelectItem(item.Key);
                }

                break;
            default:
                base.OnKeyDown(e);
                return;
        }

        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_focusIndex < 0)
        {
            _focusIndex = _selectedKey is null ? NavCursor.First(_rows) : NavCursor.IndexOf(_rows, _selectedKey);
        }

        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var toggle = ToggleBounds.Contains(e.Location);
        var index = HitTest(e.Location);
        if (index != _hoverIndex || toggle != _hoverToggle)
        {
            _hoverIndex = index;
            _hoverToggle = toggle;
            Cursor = toggle || (index >= 0 && _rows[index].IsItem) ? Cursors.Hand : Cursors.Default;
            UpdateToolTip();
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        _hoverToggle = false;
        UpdateToolTip();
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (ToggleBounds.Contains(e.Location))
        {
            Collapsed = !Collapsed;
            return;
        }

        var index = HitTest(e.Location);
        if (index >= 0 && _rows[index].Item is { } item)
        {
            _focusIndex = index;
            SelectItem(item.Key);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var max = SidebarLayout.MaxScroll(_layout.ContentHeight, ListViewport.Height);
        var next = Math.Clamp(_scroll - (e.Delta / 120 * Metrics.ItemHeight), 0, max);
        if (next != _scroll)
        {
            _scroll = next;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var colors = SidebarPalette.Resolve(theme);
        g.Clear(colors.Background);
        g.UseHighQuality();

        PaintBrand(g, theme, colors);

        var viewport = ListViewport;
        var state = g.Save();
        g.SetClip(viewport);
        foreach (var bounds in _layout.Rows)
        {
            var top = viewport.Top + bounds.Top - _scroll;
            if (top + bounds.Height < viewport.Top || top > viewport.Bottom)
            {
                continue;
            }

            var row = _rows[bounds.Index];
            var rect = new Rectangle(0, top, Width - 1, bounds.Height);
            if (row.Kind == NavRowKind.Header)
            {
                PaintHeader(g, rect, row.Section.Title ?? string.Empty, colors);
            }
            else
            {
                PaintItem(g, theme, rect, bounds.Index, row.Item!, colors);
            }
        }

        if (_collapsed)
        {
            PaintCollapsedSeparators(g, viewport, colors);
        }

        g.Restore(state);

        // Separator line towards the content.
        using var border = new Pen(colors.Border);
        g.DrawLine(border, Width - 1, 0, Width - 1, Height);
    }

    private SidebarMetrics Metrics => new(
        ItemHeight: this.ScaleDpi(40),
        HeaderHeight: this.ScaleDpi(28),
        SectionGap: this.ScaleDpi(8),
        CollapsedSeparator: this.ScaleDpi(9));

    private int BrandHeight => this.ScaleDpi(60);

    private Rectangle ListViewport => new(0, BrandHeight, Width, Math.Max(0, Height - BrandHeight - this.ScaleDpi(8)));

    private Rectangle ToggleBounds
    {
        get
        {
            var size = this.ScaleDpi(36);
            var x = _collapsed ? (Width - size) / 2 : this.ScaleDpi(12);
            return new Rectangle(x, (BrandHeight - size) / 2, size, size);
        }
    }

    private void Rebuild()
    {
        _visibleMenu = _permissionFilter is null ? _menu : _menu.Filter(_permissionFilter);
        _rows = _visibleMenu.Flatten(includeHeaders: !_collapsed);
        _layout = SidebarLayout.Arrange(_rows, Metrics, _collapsed);
        if (_selectedKey is not null && _visibleMenu.Find(_selectedKey) is null)
        {
            _selectedKey = null;
        }

        _focusIndex = _selectedKey is null ? -1 : NavCursor.IndexOf(_rows, _selectedKey);
        _hoverIndex = -1;
        _scroll = Math.Clamp(_scroll, 0, SidebarLayout.MaxScroll(_layout.ContentHeight, ListViewport.Height));
        EnsureVisible(_focusIndex);
        Invalidate();
    }

    private void MoveFocus(int index)
    {
        if (index < 0)
        {
            return;
        }

        _focusIndex = index;
        EnsureVisible(index);
        Invalidate();
    }

    private void EnsureVisible(int index)
    {
        // Before the first layout the viewport has no height; scrolling then would hide the top rows.
        if (index >= 0 && ListViewport.Height > 0)
        {
            _scroll = _layout.ScrollIntoView(index, _scroll, ListViewport.Height);
        }
    }

    private int HitTest(Point point)
    {
        var viewport = ListViewport;
        return viewport.Contains(point) ? _layout.HitTest(point.Y - viewport.Top + _scroll) : -1;
    }

    private void UpdateToolTip()
    {
        string? text = null;
        if (_hoverToggle)
        {
            text = _collapsed ? "Mở rộng menu" : "Thu gọn menu";
        }
        else if (_collapsed && _hoverIndex >= 0 && _rows[_hoverIndex].Item is { } item)
        {
            text = item.Shortcut is null ? item.Text : $"{item.Text} ({item.Shortcut})";
        }

        if (text != _toolTipText)
        {
            _toolTipText = text;
            _toolTip.SetToolTip(this, text);
        }
    }

    private void PaintBrand(Graphics g, Theme theme, SidebarColors colors)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var toggle = ToggleBounds;
        if (_hoverToggle)
        {
            g.FillRoundedRectangle(colors.HoverBackground, toggle, this.ScaleDpi(theme.Spacing.RadiusMd));
        }

        TextRenderer.DrawText(g, Glyphs.Menu, ThemeFonts.Icon(theme.Typography.Subtitle), toggle, colors.ItemText, flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (_collapsed)
        {
            return;
        }

        var x = toggle.Right + this.ScaleDpi(theme.Spacing.Sm);
        var width = Math.Max(0, Width - x - this.ScaleDpi(theme.Spacing.Md));
        var brandFont = ThemeFonts.Get(FontRole.Title, FontStyle.Bold);
        var subtitleFont = ThemeFonts.Get(FontRole.Caption);
        var hasSubtitle = !string.IsNullOrWhiteSpace(_brandSubtitle);
        var textHeight = brandFont.Height + (hasSubtitle ? subtitleFont.Height : 0);
        var y = (BrandHeight - textHeight) / 2;
        TextRenderer.DrawText(g, _brandText, brandFont, new Rectangle(x, y, width, brandFont.Height), colors.BrandText, flags);
        if (hasSubtitle)
        {
            TextRenderer.DrawText(g, _brandSubtitle, subtitleFont, new Rectangle(x, y + brandFont.Height, width, subtitleFont.Height), colors.HeaderText, flags);
        }
    }

    private void PaintHeader(Graphics g, Rectangle rect, string title, SidebarColors colors)
    {
        var font = ThemeFonts.Get(FontRole.Caption, FontStyle.Bold);
        var inset = this.ScaleDpi(20);
        var bounds = new Rectangle(rect.X + inset, rect.Y, Math.Max(0, rect.Width - (2 * inset)), rect.Height);
        TextRenderer.DrawText(
            g,
            title.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("vi-VN")),
            font,
            bounds,
            colors.HeaderText,
            TextFormatFlags.SingleLine | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void PaintItem(Graphics g, Theme theme, Rectangle rect, int index, NavItem item, SidebarColors colors)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var margin = this.ScaleDpi(8);
        var gap = this.ScaleDpi(2);
        var pill = new Rectangle(rect.X + margin, rect.Y + gap, Math.Max(0, rect.Width - (2 * margin)), rect.Height - (2 * gap));
        var radius = this.ScaleDpi(theme.Spacing.RadiusMd);
        var selected = string.Equals(item.Key, _selectedKey, StringComparison.OrdinalIgnoreCase);

        if (selected)
        {
            g.FillRoundedRectangle(colors.SelectedBackground, pill, radius);
            var bar = new Rectangle(pill.X, pill.Y + this.ScaleDpi(8), this.ScaleDpi(3), Math.Max(0, pill.Height - this.ScaleDpi(16)));
            g.FillRoundedRectangle(colors.Indicator, bar, bar.Width / 2f);
        }
        else if (index == _hoverIndex)
        {
            g.FillRoundedRectangle(colors.HoverBackground, pill, radius);
        }

        if (index == _focusIndex && Focused && ShowFocusCues)
        {
            g.DrawRoundedRectangle(theme.Colors.Focus, pill, radius, this.ScaleDpi(theme.Spacing.FocusRingWidth));
        }

        var text = selected ? colors.SelectedText : colors.ItemText;
        var icon = selected ? colors.SelectedText : colors.ItemIcon;
        var iconFont = ThemeFonts.Icon(theme.Typography.Subtitle);
        var iconWidth = this.ScaleDpi(36);
        var iconRect = _collapsed
            ? pill
            : new Rectangle(pill.X + this.ScaleDpi(6), pill.Y, iconWidth, pill.Height);
        if (item.Glyph is not null)
        {
            TextRenderer.DrawText(g, item.Glyph, iconFont, iconRect, icon, flags | TextFormatFlags.HorizontalCenter);
        }

        _badges.TryGetValue(item.Key, out var badge);
        var badgeText = BadgeText.Count(badge.Count);
        var badgeFont = ThemeFonts.Get(FontRole.Caption, FontStyle.Bold);
        var badgeColors = BadgePalette.Resolve(theme, badge.Kind, BadgeStyle.Solid);

        if (_collapsed)
        {
            if (badgeText.Length > 0)
            {
                var size = ModernBadge.Measure(this, badgeText, badgeFont);
                var center = iconRect.X + (iconRect.Width / 2);
                var bounds = new Rectangle(center + this.ScaleDpi(2), pill.Y + this.ScaleDpi(1), size.Width, size.Height);
                ModernBadge.PaintPill(g, bounds, badgeText, badgeFont, badgeColors);
            }

            return;
        }

        var right = pill.Right - this.ScaleDpi(10);
        if (badgeText.Length > 0)
        {
            var size = ModernBadge.Measure(this, badgeText, badgeFont);
            var bounds = new Rectangle(right - size.Width, pill.Y + ((pill.Height - size.Height) / 2), size.Width, size.Height);
            ModernBadge.PaintPill(g, bounds, badgeText, badgeFont, badgeColors);
            right = bounds.Left - this.ScaleDpi(6);
        }
        else if (item.Shortcut is not null)
        {
            var font = ThemeFonts.Get(FontRole.Caption);
            var size = TextRenderer.MeasureText(g, item.Shortcut, font, Size.Empty, flags);
            TextRenderer.DrawText(g, item.Shortcut, font, new Rectangle(right - size.Width, pill.Y, size.Width, pill.Height), colors.ShortcutText, flags);
            right -= size.Width + this.ScaleDpi(6);
        }

        var textX = iconRect.Right + this.ScaleDpi(4);
        var textFont = selected ? ThemeFonts.Get(FontRole.Body, FontStyle.Bold) : Font;
        TextRenderer.DrawText(g, item.Text, textFont, new Rectangle(textX, pill.Y, Math.Max(0, right - textX), pill.Height), text, flags);
    }

    private void PaintCollapsedSeparators(Graphics g, Rectangle viewport, SidebarColors colors)
    {
        using var pen = new Pen(colors.Separator);
        var inset = this.ScaleDpi(14);
        var separator = Metrics.CollapsedSeparator;
        for (var i = 1; i < _rows.Count; i++)
        {
            if (!ReferenceEquals(_rows[i].Section, _rows[i - 1].Section))
            {
                var y = viewport.Top + _layout.Rows[i].Top - _scroll - (separator / 2) - 1;
                g.DrawLine(pen, inset, y, Width - inset, y);
            }
        }
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => Invalidate();
}
