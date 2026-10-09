using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Common;

namespace GroceryPOS.Application.Sales;

public static class SaleErrors
{
    public static readonly Error EmptyCart = new("sale.empty", "Hóa đơn chưa có sản phẩm.");

    public static readonly Error NoPayment = new("sale.unpaid", "Vui lòng nhập số tiền khách thanh toán.");

    public static readonly Error DiscountNotAllowed = Error.Forbidden("Bạn không có quyền giảm giá khi bán hàng.");

    public static readonly Error CustomerNotFound = Error.NotFound("Không tìm thấy khách hàng.");

    public static readonly Error SaleNotFound = Error.NotFound("Không tìm thấy hóa đơn.");

    public static Error ProductNotFound(int productId) => Error.NotFound($"Không tìm thấy sản phẩm (mã #{productId}).");

    public static Error ProductNotFound(string input) => Error.NotFound($"Không tìm thấy sản phẩm \"{input}\".");

    /// <summary>Turns a broken domain rule into an expected failure for the UI.</summary>
    public static Error From(DomainException exception) => new(exception.Code, exception.Message);
}
