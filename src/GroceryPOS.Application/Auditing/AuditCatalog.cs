using GroceryPOS.Domain.Auditing;

namespace GroceryPOS.Application.Auditing;

/// <summary>Groups shown in the audit log filter.</summary>
public enum AuditCategory
{
    Authentication = 0,
    UsersAndRoles = 1,
    Sales = 2,
    Catalog = 3,
    Partners = 4,
    Inventory = 5,
    Purchasing = 6,
    Shifts = 7,
    ReportsAndData = 8,
    System = 9,
    Other = 99,
}

/// <summary>How much attention an entry deserves: warnings are worth a look, critical ones are what an owner checks first.</summary>
public enum AuditSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

public sealed record AuditActionInfo(string Key, string Name, AuditCategory Category, AuditSeverity Severity);

/// <summary>
/// Vietnamese names, groups and severities of the <see cref="AuditActions"/> keys, plus display names of the audited
/// object types. Unknown keys (from a newer version or a plug-in) are shown as-is under <see cref="AuditCategory.Other"/>.
/// </summary>
public static class AuditCatalog
{
    private static readonly AuditActionInfo[] Known =
    [
        new(AuditActions.LoginSucceeded, "Đăng nhập", AuditCategory.Authentication, AuditSeverity.Info),
        new(AuditActions.LoginFailed, "Đăng nhập thất bại", AuditCategory.Authentication, AuditSeverity.Warning),
        new(AuditActions.AccountLocked, "Khóa tài khoản (sai mật khẩu)", AuditCategory.Authentication, AuditSeverity.Critical),
        new(AuditActions.Logout, "Đăng xuất", AuditCategory.Authentication, AuditSeverity.Info),
        new(AuditActions.PasswordChanged, "Đổi mật khẩu", AuditCategory.Authentication, AuditSeverity.Info),
        new(AuditActions.PasswordReset, "Đặt lại mật khẩu", AuditCategory.UsersAndRoles, AuditSeverity.Warning),
        new(AuditActions.UserCreated, "Tạo người dùng", AuditCategory.UsersAndRoles, AuditSeverity.Info),
        new(AuditActions.UserUpdated, "Sửa người dùng", AuditCategory.UsersAndRoles, AuditSeverity.Info),
        new(AuditActions.UserStatusChanged, "Khóa/mở người dùng", AuditCategory.UsersAndRoles, AuditSeverity.Warning),
        new(AuditActions.UserUnlocked, "Mở khóa tài khoản", AuditCategory.UsersAndRoles, AuditSeverity.Info),
        new(AuditActions.RoleCreated, "Tạo vai trò", AuditCategory.UsersAndRoles, AuditSeverity.Info),
        new(AuditActions.RoleUpdated, "Sửa phân quyền", AuditCategory.UsersAndRoles, AuditSeverity.Critical),
        new(AuditActions.RoleDeleted, "Xóa vai trò", AuditCategory.UsersAndRoles, AuditSeverity.Warning),
        new(AuditActions.SaleCompleted, "Bán hàng", AuditCategory.Sales, AuditSeverity.Info),
        new(AuditActions.SaleVoided, "Hủy hóa đơn", AuditCategory.Sales, AuditSeverity.Critical),
        new(AuditActions.SaleReturned, "Khách trả hàng", AuditCategory.Sales, AuditSeverity.Warning),
        new(AuditActions.ReceiptReprinted, "In lại hóa đơn", AuditCategory.Sales, AuditSeverity.Warning),
        new(AuditActions.PriceChanged, "Đổi giá bán", AuditCategory.Catalog, AuditSeverity.Warning),
        new(AuditActions.CategoryCreated, "Tạo danh mục", AuditCategory.Catalog, AuditSeverity.Info),
        new(AuditActions.CategoryUpdated, "Sửa danh mục", AuditCategory.Catalog, AuditSeverity.Info),
        new(AuditActions.CategoryStatusChanged, "Ngừng/dùng lại danh mục", AuditCategory.Catalog, AuditSeverity.Info),
        new(AuditActions.ProductCreated, "Tạo sản phẩm", AuditCategory.Catalog, AuditSeverity.Info),
        new(AuditActions.ProductUpdated, "Sửa sản phẩm", AuditCategory.Catalog, AuditSeverity.Info),
        new(AuditActions.ProductStatusChanged, "Ngừng/bán lại sản phẩm", AuditCategory.Catalog, AuditSeverity.Info),
        new(AuditActions.ProductImported, "Nhập sản phẩm từ Excel", AuditCategory.Catalog, AuditSeverity.Warning),
        new(AuditActions.ProductExported, "Xuất sản phẩm ra Excel", AuditCategory.ReportsAndData, AuditSeverity.Info),
        new(AuditActions.SupplierCreated, "Tạo nhà cung cấp", AuditCategory.Partners, AuditSeverity.Info),
        new(AuditActions.SupplierUpdated, "Sửa nhà cung cấp", AuditCategory.Partners, AuditSeverity.Info),
        new(AuditActions.SupplierStatusChanged, "Ngừng/giao dịch lại NCC", AuditCategory.Partners, AuditSeverity.Info),
        new(AuditActions.CustomerCreated, "Tạo khách hàng", AuditCategory.Partners, AuditSeverity.Info),
        new(AuditActions.CustomerUpdated, "Sửa khách hàng", AuditCategory.Partners, AuditSeverity.Info),
        new(AuditActions.CustomerStatusChanged, "Ngừng/kích hoạt khách hàng", AuditCategory.Partners, AuditSeverity.Info),
        new(AuditActions.StockAdjusted, "Điều chỉnh tồn kho", AuditCategory.Inventory, AuditSeverity.Warning),
        new(AuditActions.StockCounted, "Kiểm kê kho", AuditCategory.Inventory, AuditSeverity.Warning),
        new(AuditActions.GoodsReceived, "Nhập hàng", AuditCategory.Purchasing, AuditSeverity.Info),
        new(AuditActions.GoodsReceiptCancelled, "Hủy phiếu nhập", AuditCategory.Purchasing, AuditSeverity.Warning),
        new(AuditActions.SupplierPaid, "Trả nợ NCC", AuditCategory.Purchasing, AuditSeverity.Info),
        new(AuditActions.SupplierPaymentCancelled, "Hủy phiếu chi", AuditCategory.Purchasing, AuditSeverity.Critical),
        new(AuditActions.SupplierReturned, "Trả hàng NCC", AuditCategory.Purchasing, AuditSeverity.Warning),
        new(AuditActions.ShiftOpened, "Mở ca", AuditCategory.Shifts, AuditSeverity.Info),
        new(AuditActions.ShiftClosed, "Kết ca", AuditCategory.Shifts, AuditSeverity.Info),
        new(AuditActions.CashMoved, "Nộp/chi tiền két", AuditCategory.Shifts, AuditSeverity.Warning),
        new(AuditActions.ReportExported, "Xuất báo cáo", AuditCategory.ReportsAndData, AuditSeverity.Info),
        new(AuditActions.ReportPrinted, "In báo cáo / PDF", AuditCategory.ReportsAndData, AuditSeverity.Info),
        new(AuditActions.AuditLogExported, "Xuất nhật ký hệ thống", AuditCategory.ReportsAndData, AuditSeverity.Warning),
        new(AuditActions.SettingsChanged, "Thay đổi cấu hình", AuditCategory.System, AuditSeverity.Warning),
        new(AuditActions.BackupCreated, "Sao lưu dữ liệu", AuditCategory.System, AuditSeverity.Info),
        new(AuditActions.BackupRestored, "Phục hồi dữ liệu", AuditCategory.System, AuditSeverity.Critical),
        new(AuditActions.BackupDeleted, "Xóa bản sao lưu", AuditCategory.System, AuditSeverity.Warning),
    ];

    private static readonly Dictionary<string, AuditActionInfo> ByKey = Known.ToDictionary(a => a.Key, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> EntityNames = new(StringComparer.Ordinal)
    {
        ["User"] = "Người dùng",
        ["Role"] = "Vai trò",
        ["Sale"] = "Hóa đơn",
        ["SaleReturn"] = "Phiếu trả hàng",
        ["Product"] = "Sản phẩm",
        ["Category"] = "Danh mục",
        ["Supplier"] = "Nhà cung cấp",
        ["Customer"] = "Khách hàng",
        ["GoodsReceipt"] = "Phiếu nhập",
        ["SupplierPayment"] = "Phiếu chi",
        ["SupplierReturn"] = "Phiếu trả NCC",
        ["Shift"] = "Ca làm việc",
        ["Settings"] = "Cấu hình",
        ["Backup"] = "Bản sao lưu",
    };

    /// <summary>Every known action, in catalog order.</summary>
    public static IReadOnlyList<AuditActionInfo> Actions => Known;

    /// <summary>Categories in filter order (without <see cref="AuditCategory.Other"/>).</summary>
    public static IReadOnlyList<AuditCategory> Categories { get; } =
    [
        AuditCategory.Authentication, AuditCategory.UsersAndRoles, AuditCategory.Sales, AuditCategory.Catalog,
        AuditCategory.Partners, AuditCategory.Inventory, AuditCategory.Purchasing, AuditCategory.Shifts,
        AuditCategory.ReportsAndData, AuditCategory.System,
    ];

    public static bool IsKnown(string action) => ByKey.ContainsKey(action);

    public static AuditActionInfo Describe(string action) =>
        ByKey.TryGetValue(action, out var info) ? info : new AuditActionInfo(action, action, AuditCategory.Other, AuditSeverity.Info);

    public static IReadOnlyList<string> ActionsIn(AuditCategory category) =>
        Known.Where(a => a.Category == category).Select(a => a.Key).ToList();

    /// <summary>Action keys of the given severity or higher.</summary>
    public static IReadOnlyList<string> ActionsAtLeast(AuditSeverity severity) =>
        Known.Where(a => a.Severity >= severity).Select(a => a.Key).ToList();

    public static string CategoryName(AuditCategory category) => category switch
    {
        AuditCategory.Authentication => "Đăng nhập & bảo mật",
        AuditCategory.UsersAndRoles => "Người dùng & phân quyền",
        AuditCategory.Sales => "Bán hàng & trả hàng",
        AuditCategory.Catalog => "Sản phẩm & giá",
        AuditCategory.Partners => "Khách hàng & NCC",
        AuditCategory.Inventory => "Tồn kho",
        AuditCategory.Purchasing => "Nhập hàng & công nợ",
        AuditCategory.Shifts => "Ca & két tiền",
        AuditCategory.ReportsAndData => "Báo cáo & xuất dữ liệu",
        AuditCategory.System => "Cấu hình hệ thống",
        _ => "Khác",
    };

    public static string SeverityName(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Critical => "Quan trọng",
        AuditSeverity.Warning => "Cần chú ý",
        _ => "Thông tin",
    };

    /// <summary>"Hóa đơn HD261102-0001", "Sản phẩm SUA01"; null when the entry has no object.</summary>
    public static string? DescribeEntity(string? entityName, string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return string.IsNullOrWhiteSpace(entityId) ? null : entityId;
        }

        var name = EntityNames.TryGetValue(entityName, out var vietnamese) ? vietnamese : entityName;
        return string.IsNullOrWhiteSpace(entityId) ? name : $"{name} {entityId}";
    }
}
