using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Purchasing;

public static class PurchasingErrors
{
    public static readonly Error EmptyReceipt = new("receipt.empty", "Phiếu nhập chưa có sản phẩm.");

    public static readonly Error ReceiptNotFound = Error.NotFound("Không tìm thấy phiếu nhập.");

    public static readonly Error SupplierNotFound = Error.NotFound("Không tìm thấy nhà cung cấp.");

    public static readonly Error PriceChangeNotAllowed = Error.Forbidden("Bạn không có quyền thay đổi giá bán.");

    public static readonly Error InvalidPeriod = Error.Validation("Khoảng thời gian không hợp lệ (từ ngày phải trước đến ngày).");

    public static Error SupplierInactive(string name) => Error.Validation($"Nhà cung cấp \"{name}\" đã ngừng giao dịch.");

    public static Error ProductNotFound(int productId) => Error.NotFound($"Không tìm thấy sản phẩm (mã #{productId}).");

    public static readonly Error DiscountTooLarge = new("receipt.discount", "Chiết khấu phiếu nhập vượt quá tổng tiền hàng.");

    public static readonly Error OverPaid = new("receipt.paid", "Số tiền thanh toán vượt quá tổng tiền phiếu nhập.");
}
