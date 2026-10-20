using GroceryPOS.UI.Controls.Navigation.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Tests;

public class NavigationCoreTests
{
    private static readonly NavSection Overview = new(null, new[] { new NavItem("dashboard", "Tổng quan", Permissions: new[] { "dashboard.view" }) });

    private static readonly NavSection Sales = new("Bán hàng", new[]
    {
        new NavItem("pos", "Bán hàng", Permissions: new[] { "sales.create" }, Shortcut: "F2"),
        new NavItem("returns", "Trả hàng", Permissions: new[] { "returns.create" }),
        new NavItem("shifts", "Ca làm việc", Permissions: new[] { "shifts.manage", "shifts.supervise" }),
    });

    private static readonly NavSection Admin = new("Quản trị", new[]
    {
        new NavItem("users", "Người dùng", Permissions: new[] { "users.manage" }),
        new NavItem("roles", "Vai trò", Permissions: new[] { "roles.manage" }),
    });

    private static readonly NavSection Account = new("Tài khoản", new[] { new NavItem("password", "Đổi mật khẩu") });

    private static NavMenu Menu() => new(new[] { Overview, Sales, Admin, Account });

    private static Func<string, bool> Grants(params string[] permissions) => p => permissions.Contains(p);

    [Fact]
    public void Filter_keeps_items_with_any_required_permission_and_drops_empty_sections()
    {
        var cashier = Menu().Filter(Grants("sales.create", "shifts.manage"));

        Assert.Equal(new[] { "pos", "shifts", "password" }, cashier.Items.Select(i => i.Key));
        Assert.Equal(new[] { "Bán hàng", "Tài khoản" }, cashier.Sections.Select(s => s.Title));
    }

    [Fact]
    public void Items_without_permissions_are_visible_to_everyone()
    {
        var nobody = Menu().Filter(_ => false);

        Assert.Equal("password", Assert.Single(nobody.Items).Key);
    }

    [Fact]
    public void Duplicate_or_blank_keys_are_rejected()
    {
        var duplicate = new NavSection("X", new[] { new NavItem("pos", "A"), new NavItem("POS", "B") });
        Assert.Throws<ArgumentException>(() => new NavMenu(new[] { duplicate }));
        Assert.Throws<ArgumentException>(() => new NavMenu(new[] { new NavSection("X", new[] { new NavItem(" ", "A") }) }));
    }

    [Fact]
    public void Find_is_case_insensitive()
    {
        Assert.Equal("Trả hàng", Menu().Find("RETURNS")?.Text);
        Assert.Null(Menu().Find("missing"));
        Assert.Null(Menu().Find(null));
    }

    [Fact]
    public void Flatten_puts_headers_before_titled_sections_only()
    {
        var rows = Menu().Flatten();

        Assert.Equal(NavRowKind.Item, rows[0].Kind); // untitled overview section has no header
        Assert.Equal(NavRowKind.Header, rows[1].Kind);
        Assert.Equal("Bán hàng", rows[1].Section.Title);
        Assert.Equal(7, rows.Count(r => r.IsItem));
        Assert.Equal(10, rows.Count);
        Assert.DoesNotContain(Menu().Flatten(includeHeaders: false), r => r.Kind == NavRowKind.Header);
    }

    [Fact]
    public void Cursor_skips_headers_and_wraps()
    {
        var rows = Menu().Flatten();
        var first = NavCursor.First(rows);
        var last = NavCursor.Last(rows);

        Assert.Equal(0, first);
        Assert.Equal(rows.Count - 1, last);
        Assert.Equal(2, NavCursor.Move(rows, 0, +1)); // jumps over the "Bán hàng" header
        Assert.Equal(0, NavCursor.Move(rows, 2, -1));
        Assert.Equal(0, NavCursor.Move(rows, last, +1));
        Assert.Equal(last, NavCursor.Move(rows, 0, -1));
        Assert.Equal(NavCursor.IndexOf(rows, "users"), NavCursor.Move(rows, NavCursor.IndexOf(rows, "shifts"), +1));
    }

    [Fact]
    public void Cursor_on_empty_menu_returns_minus_one()
    {
        Assert.Equal(-1, NavCursor.First(Array.Empty<NavRow>()));
        Assert.Equal(-1, NavCursor.IndexOf(Menu().Flatten(), "nope"));
    }

    private static readonly SidebarMetrics Metrics = new(ItemHeight: 40, HeaderHeight: 28, SectionGap: 8, CollapsedSeparator: 9);

    [Fact]
    public void Expanded_layout_stacks_headers_with_section_gaps()
    {
        var rows = Menu().Flatten();
        var layout = SidebarLayout.Arrange(rows, Metrics, collapsed: false);

        Assert.Equal(new NavRowBounds(0, 0, 40), layout.Rows[0]);
        Assert.Equal(new NavRowBounds(1, 48, 28), layout.Rows[1]); // gap + header
        Assert.Equal(76, layout.Rows[2].Top);
        Assert.Equal((7 * 40) + (3 * 28) + (3 * 8), layout.ContentHeight);
    }

    [Fact]
    public void Collapsed_layout_uses_separators_instead_of_headers()
    {
        var rows = Menu().Flatten(includeHeaders: false);
        var layout = SidebarLayout.Arrange(rows, Metrics, collapsed: true);

        Assert.Equal(49, layout.Rows[1].Top); // 40 + separator
        Assert.Equal(89, layout.Rows[2].Top); // same section, no separator
        Assert.Equal((7 * 40) + (3 * 9), layout.ContentHeight);
    }

    [Fact]
    public void Hit_test_and_scroll_into_view()
    {
        var rows = Menu().Flatten();
        var layout = SidebarLayout.Arrange(rows, Metrics, collapsed: false);

        Assert.Equal(0, layout.HitTest(10));
        Assert.Equal(-1, layout.HitTest(44)); // in the section gap
        Assert.Equal(1, layout.HitTest(50));
        Assert.Equal(-1, layout.HitTest(5000));

        var last = rows.Count - 1;
        var viewport = 200;
        var scroll = layout.ScrollIntoView(last, 0, viewport);
        Assert.Equal(layout.ContentHeight - viewport, scroll);
        Assert.Equal(0, layout.ScrollIntoView(0, scroll, viewport));
        Assert.Equal(0, layout.ScrollIntoView(0, 40, 2000)); // content fits → clamped to 0
        Assert.Equal(0, SidebarLayout.MaxScroll(100, 300));
    }

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void Sidebar_colors_are_readable(ThemeMode mode)
    {
        var colors = SidebarPalette.Resolve(Themes.For(mode));

        Assert.True(ColorMath.ContrastRatio(colors.ItemText, colors.Background) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(colors.ItemText, colors.HoverBackground) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(colors.SelectedText, colors.SelectedBackground) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(colors.HeaderText, colors.Background) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(colors.BrandText, colors.Background) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(colors.ItemIcon, colors.HoverBackground) >= 3.0);
        Assert.True(ColorMath.ContrastRatio(colors.Indicator, colors.SelectedBackground) >= 3.0);
        Assert.NotEqual(colors.Background, colors.SelectedBackground);
    }

    [Fact]
    public void Tabs_that_fit_are_laid_out_without_scrolling()
    {
        var result = TabStripLayout.Arrange(new[] { 80, 100, 60 }, 300, selected: 2, first: 1, arrowsWidth: 56);

        Assert.False(result.Overflow);
        Assert.Equal(0, result.First);
        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { 0, 80, 180 }, result.Offsets);
        Assert.False(result.CanScrollBack);
        Assert.False(result.CanScrollForward);
    }

    [Fact]
    public void Overflowing_strip_scrolls_to_show_the_selected_tab()
    {
        var widths = new[] { 100, 100, 100, 100, 100, 100 };
        var result = TabStripLayout.Arrange(widths, 356, selected: 5, first: 0, arrowsWidth: 56);

        Assert.True(result.Overflow);
        Assert.Equal(3, result.First);
        Assert.Equal(3, result.Count);
        Assert.True(result.IsVisible(5));
        Assert.True(result.CanScrollBack);
        Assert.False(result.CanScrollForward);
    }

    [Fact]
    public void Overflowing_strip_keeps_scroll_position_when_selection_is_visible()
    {
        var widths = new[] { 100, 100, 100, 100, 100, 100 };
        var result = TabStripLayout.Arrange(widths, 356, selected: 2, first: 1, arrowsWidth: 56);

        Assert.Equal(1, result.First);
        Assert.Equal(3, result.Count);
        Assert.True(result.CanScrollForward);
    }

    [Fact]
    public void Overflowing_strip_scrolls_back_to_a_selected_tab_before_the_window()
    {
        var widths = new[] { 100, 100, 100, 100, 100, 100 };
        var result = TabStripLayout.Arrange(widths, 356, selected: 0, first: 4, arrowsWidth: 56);

        Assert.Equal(0, result.First);
        Assert.True(result.IsVisible(0));
    }

    [Fact]
    public void Overflowing_strip_fills_space_left_at_the_end()
    {
        var widths = new[] { 100, 100, 100, 100, 100, 100 };
        var result = TabStripLayout.Arrange(widths, 356, selected: -1, first: 5, arrowsWidth: 56);

        Assert.Equal(3, result.First);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void A_tab_wider_than_the_strip_is_still_shown()
    {
        var result = TabStripLayout.Arrange(new[] { 500, 80 }, 300, selected: 0, first: 0, arrowsWidth: 56);

        Assert.Equal(0, result.First);
        Assert.Equal(1, result.Count);
        Assert.True(result.CanScrollForward);
    }

    [Fact]
    public void Empty_or_invalid_tab_lists()
    {
        Assert.Same(TabStripArrangement.Empty, TabStripLayout.Arrange(Array.Empty<int>(), 300, -1, 0, 56));
        Assert.Throws<ArgumentException>(() => TabStripLayout.Arrange(new[] { -1 }, 300, 0, 0, 56));
    }
}
