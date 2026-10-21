using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Catalog;

public static class ProductErrors
{
    public static Error NotFound(int id) => Error.NotFound($"Không tìm thấy sản phẩm (mã #{id}).");

    public static readonly Error CategoryNotFound = Error.NotFound("Không tìm thấy danh mục.");

    public static Error DuplicateCode(string code) => Error.Conflict($"Mã sản phẩm \"{code}\" đã tồn tại.");

    public static Error DuplicateBarcode(string barcode) => Error.Conflict($"Mã vạch \"{barcode}\" đã được dùng cho sản phẩm khác.");

    public static readonly Error PriceChangeNotAllowed = Error.Forbidden("Bạn không có quyền thay đổi giá bán.");
}
