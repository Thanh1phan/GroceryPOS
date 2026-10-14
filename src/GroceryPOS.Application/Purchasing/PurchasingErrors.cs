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

public static class SupplierDebtErrors
{
    public static readonly Error PaymentNotFound = Error.NotFound("Không tìm thấy phiếu chi.");

    public static readonly Error ReturnNotFound = Error.NotFound("Không tìm thấy phiếu trả hàng nhà cung cấp.");

    public static readonly Error NoDebt = Error.Conflict("Nhà cung cấp không còn công nợ.");

    public static readonly Error PaymentForbidden = Error.Forbidden("Bạn không có quyền thanh toán công nợ nhà cung cấp.");

    public static readonly Error ViewForbidden = Error.Forbidden("Bạn không có quyền xem công nợ nhà cung cấp.");
}
