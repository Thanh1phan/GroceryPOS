using System.ComponentModel;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Navigation;

/// <summary>
/// Top bar of the app shell: page title and breadcrumb on the left; on the right a slot for screen actions
/// (<see cref="Actions"/>), a notification bell with count, a light/dark toggle and the signed-in user chip
/// (avatar initials, name, role) that opens <see cref="UserMenu"/>.
/// </summary>
[ToolboxItem(true)]
public class TopBar : Control, IThemeable
{
    private enum Part
    {
        None,
        Notifications,
        Theme,
        User,
    }

    private readonly FlowLayoutPanel _actions;
    private readonly ToolTip _toolTip = new() { InitialDelay = 300 };
    private string? _title = "Tổng quan";
    private string? _subtitle;
    private string? _userName;
    private string? _userRole;
    private string? _userKey;
    private bool _showThemeToggle = true;
    private bool _showNotifications;
    private int _notificationCount;
    private Part _hover;
    private Part _toolTipPart;

    public TopBar()
    {
        // Created first: setting Dock/Height below already runs OnLayout, which positions this panel.
        _actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = false,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        _actions.ControlAdded += (_, e) =>
        {
            ThemeManager.Apply(e.Control!);
            PerformLayout();
        };
        _actions.ControlRemoved += (_, _) => PerformLayout();
        Controls.Add(_actions);

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.ContainerControl,
            true);
        Dock = DockStyle.Top;
        Height = 64;
        Font = ThemeFonts.Get(FontRole.Body);
    }

    /// <summary>Raised after the theme toggle switched light/dark (save the choice in settings here).</summary>
    public event EventHandler? ThemeToggled;

    public event EventHandler? NotificationsClick;

    /// <summary>Raised when the user chip is clicked (before <see cref="UserMenu"/> opens).</summary>
    public event EventHandler? UserClick;

    /// <summary>Buttons of the current screen, added right-to-left next to the bell.</summary>
    [Browsable(false)]
    public FlowLayoutPanel Actions => _actions;

    /// <summary>Menu shown under the user chip (Đổi mật khẩu, Đăng xuất…).</summary>
    [Category("Behavior")]
    [DefaultValue(null)]
    public ContextMenuStrip? UserMenu { get; set; }

    [Category("Appearance")]
    [DefaultValue("Tổng quan")]
    public string? Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    /// <summary>Breadcrumb or description under the title ("Kho hàng / Kiểm kê").</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? Subtitle
    {
        get => _subtitle;
        set => Set(ref _subtitle, value);
    }

    [Category("Appearance")]
    [DefaultValue(true)]
    public bool ShowThemeToggle
    {
        get => _showThemeToggle;
        set
        {
            _showThemeToggle = value;
            PerformLayout();
            Invalidate();
        }
    }

    [Category("Appearance")]
    [DefaultValue(false)]
    public bool ShowNotifications
    {
        get => _showNotifications;
        set
        {
            _showNotifications = value;
            PerformLayout();
            Invalidate();
        }
    }

    [Category("Appearance")]
    [DefaultValue(0)]
    public int NotificationCount
    {
        get => _notificationCount;
        set
        {
            _notificationCount = Math.Max(0, value);
            Invalidate();
        }
    }

    /// <summary>Shows the signed-in user; pass nulls to hide the chip (login screen).</summary>
    /// <param name="key">Username, used for a stable avatar color.</param>
    public void SetUser(string? displayName, string? role, string? key = null)
    {
        _userName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        _userRole = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        _userKey = key ?? _userName;
        PerformLayout();
        Invalidate();
    }

    /// <summary>Sets title and breadcrumb in one call when the shell navigates.</summary>
    public void SetPage(string? title, string? subtitle = null)
    {
        _title = title;
        _subtitle = subtitle;
        Invalidate();
    }

    void IThemeable.ApplyTheme(Theme theme)
    {
        foreach (Control child in _actions.Controls)
        {
            ThemeManager.Apply(child);
        }

        Invalidate();
    }

    bool IThemeable.ThemesChildren => true;

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

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var (bell, theme, user) = Regions();
        var right = new[] { bell, theme, user }.Where(r => !r.IsEmpty).Select(r => r.Left).DefaultIfEmpty(Width - this.ScaleDpi(16)).Min();
        var height = this.ScaleDpi(ThemeManager.Current.Spacing.ControlHeight);
        var width = Math.Min(_actions.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal), Math.Max(0, right - this.ScaleDpi(240)));
        _actions.Bounds = new Rectangle(right - this.ScaleDpi(8) - width, (Height - height) / 2, Math.Max(0, width), height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var part = HitTest(e.Location);
        if (part != _hover)
        {
            _hover = part;
            Cursor = part == Part.None ? Cursors.Default : Cursors.Hand;
            UpdateToolTip();
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = Part.None;
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

        switch (HitTest(e.Location))
        {
            case Part.Theme:
                ThemeManager.ToggleMode();
                UpdateToolTip(force: true);
                ThemeToggled?.Invoke(this, EventArgs.Empty);
                break;
            case Part.Notifications:
                NotificationsClick?.Invoke(this, EventArgs.Empty);
                break;
            case Part.User:
                UserClick?.Invoke(this, EventArgs.Empty);
                var (_, _, user) = Regions();
                UserMenu?.Show(this, new Point(user.Right - UserMenu.Width, user.Bottom + this.ScaleDpi(4)));
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var colors = SurfacePalette.TopBar(theme);
        g.Clear(colors.Background);
        g.UseHighQuality();

        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var (bell, themeToggle, user) = Regions();
        var left = this.ScaleDpi(24);
        var textRight = _actions.Controls.Count > 0 ? _actions.Left : new[] { bell, themeToggle, user }.Where(r => !r.IsEmpty).Select(r => r.Left).DefaultIfEmpty(Width).Min();
        var titleFont = ThemeFonts.Get(FontRole.Title, FontStyle.Bold);
        var subtitleFont = ThemeFonts.Get(FontRole.Caption);
        var hasSubtitle = !string.IsNullOrWhiteSpace(_subtitle);
        var textHeight = titleFont.Height + (hasSubtitle ? subtitleFont.Height : 0);
        var y = (Height - textHeight) / 2;
        var width = Math.Max(0, textRight - left - this.ScaleDpi(12));
        TextRenderer.DrawText(g, _title, titleFont, new Rectangle(left, y, width, titleFont.Height), colors.Title, flags);
        if (hasSubtitle)
        {
            TextRenderer.DrawText(g, _subtitle, subtitleFont, new Rectangle(left, y + titleFont.Height, width, subtitleFont.Height), colors.Subtitle, flags);
        }

        var radius = this.ScaleDpi(theme.Spacing.RadiusMd);
        var iconFont = ThemeFonts.Icon(theme.Typography.Subtitle);
        if (!bell.IsEmpty)
        {
            PaintIconButton(g, bell, Glyphs.Bell, iconFont, _hover == Part.Notifications, colors, radius);
            var count = BadgeText.Count(_notificationCount);
            if (count.Length > 0)
            {
                var badgeFont = ThemeFonts.Get(FontRole.Caption, FontStyle.Bold);
                var size = ModernBadge.Measure(this, count, badgeFont);
                var bounds = new Rectangle(bell.X + (bell.Width / 2), bell.Y + this.ScaleDpi(2), size.Width, size.Height);
                ModernBadge.PaintPill(g, bounds, count, badgeFont, BadgePalette.Resolve(theme, BadgeKind.Danger, BadgeStyle.Solid));
            }
        }

        if (!themeToggle.IsEmpty)
        {
            PaintIconButton(g, themeToggle, theme.IsDark ? Glyphs.Sun : Glyphs.Moon, iconFont, _hover == Part.Theme, colors, radius);
        }

        if (!user.IsEmpty)
        {
            PaintUser(g, user, colors, radius);
        }

        using var border = new Pen(colors.Border);
        g.DrawLine(border, 0, Height - 1, Width, Height - 1);
    }

    private (Rectangle Bell, Rectangle Theme, Rectangle User) Regions()
    {
        var size = this.ScaleDpi(36);
        var gap = this.ScaleDpi(8);
        var right = Width - this.ScaleDpi(16);
        var top = (Height - size) / 2;

        var user = Rectangle.Empty;
        if (_userName is not null)
        {
            var width = UserChipWidth();
            user = new Rectangle(right - width, (Height - this.ScaleDpi(44)) / 2, width, this.ScaleDpi(44));
            right = user.Left - gap;
        }

        var theme = Rectangle.Empty;
        if (_showThemeToggle)
        {
            theme = new Rectangle(right - size, top, size, size);
            right = theme.Left - gap;
        }

        var bell = Rectangle.Empty;
        if (_showNotifications)
        {
            bell = new Rectangle(right - size, top, size, size);
        }

        return (bell, theme, user);
    }

    private int UserChipWidth()
    {
        var nameFont = ThemeFonts.Get(FontRole.Body, FontStyle.Bold);
        var roleFont = ThemeFonts.Get(FontRole.Caption);
        var text = Math.Max(
            TextRenderer.MeasureText(_userName ?? string.Empty, nameFont).Width,
            TextRenderer.MeasureText(_userRole ?? string.Empty, roleFont).Width);
        var chevron = UserMenu is null ? 0 : this.ScaleDpi(20);
        return Math.Min(this.ScaleDpi(260), this.ScaleDpi(8 + 32 + 10 + 12) + text + chevron);
    }

    private Part HitTest(Point point)
    {
        var (bell, theme, user) = Regions();
        return bell.Contains(point) ? Part.Notifications
            : theme.Contains(point) ? Part.Theme
            : user.Contains(point) ? Part.User
            : Part.None;
    }

    private void PaintIconButton(Graphics g, Rectangle bounds, string glyph, Font iconFont, bool hovered, TopBarColors colors, int radius)
    {
        if (hovered)
        {
            g.FillRoundedRectangle(colors.ChipHover, bounds, radius);
        }

        TextRenderer.DrawText(g, glyph, iconFont, bounds, colors.Subtitle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void PaintUser(Graphics g, Rectangle bounds, TopBarColors colors, int radius)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        if (_hover == Part.User)
        {
            g.FillRoundedRectangle(colors.ChipHover, bounds, radius);
        }

        var avatarSize = this.ScaleDpi(32);
        var avatar = new Rectangle(bounds.X + this.ScaleDpi(8), bounds.Y + ((bounds.Height - avatarSize) / 2), avatarSize, avatarSize);
        g.FillRoundedRectangle(Avatar.ColorFor(_userKey), avatar, avatarSize / 2f);
        TextRenderer.DrawText(g, Avatar.Initials(_userName), ThemeFonts.Get(FontRole.Caption, FontStyle.Bold), avatar, Avatar.Foreground, flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var x = avatar.Right + this.ScaleDpi(10);
        var right = bounds.Right - this.ScaleDpi(UserMenu is null ? 12 : 28);
        var nameFont = ThemeFonts.Get(FontRole.Body, FontStyle.Bold);
        var roleFont = ThemeFonts.Get(FontRole.Caption);
        var hasRole = _userRole is not null;
        var textHeight = nameFont.Height + (hasRole ? roleFont.Height : 0);
        var y = bounds.Y + ((bounds.Height - textHeight) / 2);
        TextRenderer.DrawText(g, _userName, nameFont, new Rectangle(x, y, Math.Max(0, right - x), nameFont.Height), colors.Title, flags);
        if (hasRole)
        {
            TextRenderer.DrawText(g, _userRole, roleFont, new Rectangle(x, y + nameFont.Height, Math.Max(0, right - x), roleFont.Height), colors.Subtitle, flags);
        }

        if (UserMenu is not null)
        {
            var chevron = new Rectangle(bounds.Right - this.ScaleDpi(24), bounds.Y, this.ScaleDpi(16), bounds.Height);
            TextRenderer.DrawText(g, Glyphs.ChevronDown, ThemeFonts.Icon(ThemeManager.Current.Typography.Caption), chevron, colors.Subtitle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void UpdateToolTip(bool force = false)
    {
        if (_hover == _toolTipPart && !force)
        {
            return;
        }

        _toolTipPart = _hover;
        _toolTip.SetToolTip(this, _hover switch
        {
            Part.Theme => ThemeManager.Current.IsDark ? "Chuyển sang giao diện sáng" : "Chuyển sang giao diện tối",
            Part.Notifications => _notificationCount > 0 ? $"{_notificationCount} thông báo" : "Thông báo",
            Part.User => "Tài khoản",
            _ => null,
        });
    }

    private void Set(ref string? field, string? value)
    {
        if (field != value)
        {
            field = value;
            Invalidate();
        }
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => Invalidate();
}
