namespace GroceryPOS.Domain.Identity;

/// <summary>
/// Catalog of permission keys. Keys are stable strings persisted in the database;
/// <see cref="DisplayNames"/> holds the Vietnamese labels shown in the role editor.
/// </summary>
public static class Permissions
{
    public const string DashboardView = "dashboard.view";

    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    public const string CategoriesManage = "categories.manage";
    public const string ProductsView = "products.view";
    public const string ProductsManage = "products.manage";
    public const string PricesChange = "prices.change";
    public const string SuppliersManage = "suppliers.manage";
    public const string CustomersView = "customers.view";
    public const string CustomersManage = "customers.manage";

    public const string SalesCreate = "sales.create";
    public const string SalesDiscount = "sales.discount";
    public const string SalesVoid = "sales.void";
    public const string ReturnsCreate = "returns.create";
    public const string ShiftsManage = "shifts.manage";

    public const string PurchasesManage = "purchases.manage";
    public const string InventoryView = "inventory.view";
    public const string InventoryAdjust = "inventory.adjust";

    public const string ReportsView = "reports.view";
    public const string AuditLogView = "auditlog.view";
    public const string SettingsManage = "settings.manage";

    public static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>
    {
        [DashboardView] = "Xem tổng quan",
        [UsersView] = "Xem người dùng",
        [UsersManage] = "Quản lý người dùng",
        [RolesManage] = "Quản lý vai trò & phân quyền",
        [CategoriesManage] = "Quản lý danh mục",
        [ProductsView] = "Xem sản phẩm",
        [ProductsManage] = "Quản lý sản phẩm",
        [PricesChange] = "Thay đổi giá bán",
        [SuppliersManage] = "Quản lý nhà cung cấp",
        [CustomersView] = "Xem khách hàng",
        [CustomersManage] = "Quản lý khách hàng",
        [SalesCreate] = "Bán hàng",
        [SalesDiscount] = "Giảm giá khi bán",
        [SalesVoid] = "Hủy hóa đơn",
        [ReturnsCreate] = "Trả hàng",
        [ShiftsManage] = "Mở/đóng ca, két tiền",
        [PurchasesManage] = "Nhập hàng",
        [InventoryView] = "Xem tồn kho",
        [InventoryAdjust] = "Điều chỉnh tồn kho",
        [ReportsView] = "Xem báo cáo",
        [AuditLogView] = "Xem nhật ký hệ thống",
        [SettingsManage] = "Cấu hình hệ thống",
    };

    public static IReadOnlyCollection<string> All => (IReadOnlyCollection<string>)DisplayNames.Keys;

    public static bool IsKnown(string permission) => DisplayNames.ContainsKey(permission);
}
