using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Partners;

public static class PartnerErrors
{
    public static readonly Error DuplicateSupplierCode = Error.Conflict("Mã nhà cung cấp đã tồn tại.");

    public static readonly Error DuplicateCustomerPhone = Error.Conflict("Số điện thoại này đã được dùng cho khách hàng khác.");

    public static Error SupplierNotFound(int id) => Error.NotFound($"Không tìm thấy nhà cung cấp (mã #{id}).");

    public static Error CustomerNotFound(int id) => Error.NotFound($"Không tìm thấy khách hàng (mã #{id}).");

    public static Error SupplierHasDebt(decimal debt) =>
        Error.Conflict($"Nhà cung cấp còn công nợ {Money.Format(debt)}. Hãy thanh toán hết trước khi ngừng giao dịch.");
}
