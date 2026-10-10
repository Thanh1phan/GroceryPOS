using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Inventory;

public static class InventoryErrors
{
    public static readonly Error EmptyCount = Error.Validation("Phiếu kiểm kê chưa có sản phẩm.");

    public static readonly Error InvalidPeriod = Error.Validation("Khoảng thời gian không hợp lệ (từ ngày phải trước đến ngày).");

    public static Error ProductNotFound(int productId) => Error.NotFound($"Không tìm thấy sản phẩm (mã #{productId}).");

    public static Error DuplicateCountLine(string productName) =>
        Error.Validation($"Sản phẩm \"{productName}\" xuất hiện nhiều lần trong phiếu kiểm kê.");

    public static Error NegativeCount(string productName) =>
        Error.Validation($"Số lượng kiểm kê của \"{productName}\" không được âm.");
}
