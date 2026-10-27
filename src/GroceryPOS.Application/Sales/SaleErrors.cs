using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Common;

namespace GroceryPOS.Application.Sales;

public static class SaleErrors
{
    public static readonly Error EmptyCart = new("sale.empty", "Hóa đơn chưa có sản phẩm.");

    public static readonly Error NoPayment = new("sale.unpaid", "Vui lòng nhập số tiền khách thanh toán.");

    public static readonly Error DiscountNotAllowed = Error.Forbidden("Bạn không có quyền giảm giá khi bán hàng.");

    public static readonly Error CustomerNotFound = Error.NotFound("Không tìm thấy khách hàng.");

    public static readonly Error NoOpenShift = Error.Conflict("Bạn chưa mở ca bán hàng. Vui lòng mở ca trước khi thanh toán.");

    public static readonly Error SaleNotFound = Error.NotFound("Không tìm thấy hóa đơn.");

    public static Error ProductNotFound(int productId) => Error.NotFound($"Không tìm thấy sản phẩm (mã #{productId}).");

    public static Error ProductNotFound(string input) => Error.NotFound($"Không tìm thấy sản phẩm \"{input}\".");

    /// <summary>Turns a broken domain rule into an expected failure for the UI.</summary>
    public static Error From(DomainException exception) => new(exception.Code, exception.Message);
}

public static class ReturnErrors
{
    public static readonly Error SaleVoided = Error.Conflict("Hóa đơn đã bị hủy, không thể trả hàng.");

    public static readonly Error SaleNotCompleted = Error.Conflict("Hóa đơn chưa hoàn tất.");

    public static readonly Error AlreadyFullyReturned = Error.Conflict("Hóa đơn đã được trả hết hàng.");

    public static readonly Error EmptyReturn = new("return.empty", "Vui lòng chọn sản phẩm cần trả.");

    public static readonly Error NoOpenShiftForCashRefund =
        Error.Conflict("Bạn chưa mở ca nên không thể hoàn tiền mặt từ két. Vui lòng mở ca hoặc hoàn bằng chuyển khoản.");

    public static readonly Error ReturnNotFound = Error.NotFound("Không tìm thấy phiếu trả hàng.");

    public static Error WindowExpired(int days) =>
        Error.Forbidden($"Hóa đơn đã quá {days} ngày đổi trả. Vui lòng nhờ quản lý thực hiện.");

    public static Error LineNotFound(int saleLineId) => Error.NotFound($"Dòng hàng #{saleLineId} không thuộc hóa đơn này.");
}
