using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Catalog;

public static class CategoryErrors
{
    public static readonly Error DuplicateName = Error.Conflict("Đã tồn tại danh mục với tên này.");

    public static Error NotFound(int id) => Error.NotFound($"Không tìm thấy danh mục (mã #{id}).");
}
