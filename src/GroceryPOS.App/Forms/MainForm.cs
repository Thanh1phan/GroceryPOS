using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.App.Forms.Pos;
using GroceryPOS.App.Forms.Screens;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Navigation;
using GroceryPOS.UI.Controls.Navigation.Core;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms;

/// <summary>Application shell: sidebar + top bar around a content area that swaps per selected menu item.</summary>
public sealed class MainForm : Form
{
    private static readonly NavMenu Menu = new(new[]
    {
        new NavSection(null, new[]
        {
            new NavItem("dashboard", "Tổng quan", Glyphs.Home, new[] { Permissions.DashboardView }),
        }),
        new NavSection("Bán hàng", new[]
        {
            new NavItem("pos", "Bán hàng", Glyphs.Shop, new[] { Permissions.SalesCreate }, "F2"),
            new NavItem("returns", "Trả hàng", Glyphs.Return, new[] { Permissions.ReturnsCreate }),
            new NavItem("shifts", "Ca làm việc", Glyphs.Calculator, new[] { Permissions.ShiftsManage, Permissions.ShiftsSupervise }),
        }),
        new NavSection("Kho hàng", new[]
        {
            new NavItem("categories", "Danh mục sản phẩm", Glyphs.List, new[] { Permissions.CategoriesManage }),
            new NavItem("products", "Sản phẩm", Glyphs.Package, new[] { Permissions.ProductsView }),
            new NavItem("inventory", "Tồn kho", Glyphs.List, new[] { Permissions.InventoryView }),
            new NavItem("purchases", "Nhập hàng", Glyphs.Document, new[] { Permissions.PurchasesManage }),
        }),
        new NavSection("Đối tác", new[]
        {
            new NavItem("customers", "Khách hàng", Glyphs.People, new[] { Permissions.CustomersView }),
            new NavItem("suppliers", "Nhà cung cấp", Glyphs.People, new[] { Permissions.SuppliersManage, Permissions.PurchasesManage, Permissions.SupplierPaymentsManage }),
        }),
        new NavSection("Báo cáo & Hệ thống", new[]
        {
            new NavItem("reports", "Báo cáo", Glyphs.Chart, new[] { Permissions.ReportsView }),
            new NavItem("users", "Người dùng", Glyphs.People, new[] { Permissions.UsersView, Permissions.UsersManage }),
            new NavItem("roles", "Phân quyền", Glyphs.Admin, new[] { Permissions.RolesManage }),
            new NavItem("settings", "Cấu hình", Glyphs.Settings, new[] { Permissions.SettingsManage }),
        }),
    });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUser _currentUser;
    private readonly SidebarNav _sidebar;
    private readonly TopBar _topBar;
    private readonly Panel _content;

    public MainForm(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;

        Text = "GroceryPOS";
        ClientSize = new Size(1200, 760);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;

        _sidebar = new SidebarNav
        {
            PermissionFilter = _currentUser.HasPermission,
            Menu = Menu,
        };
        _sidebar.ItemSelected += (_, e) => ShowScreen(e.Item.Key, e.Item.Text);

        _topBar = new TopBar
        {
            ShowNotifications = true,
            UserMenu = BuildUserMenu(),
        };
        _topBar.SetUser(_currentUser.Session?.FullName, _currentUser.Session?.RoleName, _currentUser.Session?.Username);

        _content = new Panel { Dock = DockStyle.Fill };

        Controls.Add(_content);
        Controls.Add(_topBar);
        Controls.Add(_sidebar);

        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            _sidebar.SelectItem("dashboard");
            ShowScreen("dashboard", "Tổng quan");
        };
    }

    /// <summary>Raised after the user signs out, so the host can show the login screen again.</summary>
    public event EventHandler? SignedOut;

    private ContextMenuStrip BuildUserMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Đổi mật khẩu", null, (_, _) => ChangePassword());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Đăng xuất", null, async (_, _) => await SignOutAsync());
        return menu;
    }

    private void ChangePassword()
    {
        using var dialog = new ChangePasswordForm(_scopeFactory, mandatory: false);
        dialog.ShowDialog(this);
    }

    private async Task SignOutAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.LogoutAsync();
        SignedOut?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void ShowScreen(string key, string title)
    {
        _topBar.SetPage(title);
        _content.Controls.Clear();
        Control screen = key switch
        {
            "dashboard" => BuildDashboard(),
            "pos" => new PosScreen(_scopeFactory, _currentUser),
            "categories" => new CategoryListScreen(_scopeFactory, _currentUser),
            "products" => new ProductListScreen(_scopeFactory, _currentUser),
            "customers" => new CustomerListScreen(_scopeFactory, _currentUser),
            "suppliers" => new SupplierListScreen(_scopeFactory, _currentUser),
            "users" => new UserListScreen(_scopeFactory, _currentUser),
            "roles" => new RolePermissionScreen(_scopeFactory),
            _ => BuildPlaceholder(title),
        };
        screen.Dock = DockStyle.Fill;
        _content.Controls.Add(screen);
        ThemeManager.Apply(screen);
    }

    private Control BuildDashboard()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
        };

        panel.Controls.Add(MakeStat("Doanh thu hôm nay", "0 ₫", Glyphs.Money, BadgeKind.Success));
        panel.Controls.Add(MakeStat("Hóa đơn hôm nay", "0", Glyphs.Document, BadgeKind.Primary));
        panel.Controls.Add(MakeStat("Sản phẩm sắp hết hàng", "0", Glyphs.Package, BadgeKind.Warning));
        panel.Controls.Add(MakeStat("Ca đang mở", "0", Glyphs.Calculator, BadgeKind.Info));
        return panel;
    }

    private static StatCard MakeStat(string caption, string value, string glyph, BadgeKind accent) => new()
    {
        Caption = caption,
        Value = value,
        Glyph = glyph,
        Accent = accent,
        Margin = new Padding(0, 0, 16, 16),
    };

    private static Control BuildPlaceholder(string title) => new Label
    {
        Text = $"Màn hình \"{title}\" đang được xây dựng.",
        TextAlign = ContentAlignment.MiddleCenter,
        Font = ThemeFonts.Get(FontRole.Title),
    };
}
