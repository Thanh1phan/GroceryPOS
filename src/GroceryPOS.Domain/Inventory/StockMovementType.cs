namespace GroceryPOS.Domain.Inventory;

/// <summary>Why a product's stock changed. Values are persisted; do not renumber.</summary>
public enum StockMovementType
{
    /// <summary>Opening balance when a product is first put into the system.</summary>
    Opening = 0,

    /// <summary>Goods sold at the POS (negative).</summary>
    Sale = 1,

    /// <summary>Stock returned because a sale was voided (positive).</summary>
    SaleVoid = 2,

    /// <summary>Goods returned by a customer (positive).</summary>
    CustomerReturn = 3,

    /// <summary>Goods received from a supplier (positive).</summary>
    GoodsReceipt = 4,

    /// <summary>Goods returned to a supplier (negative).</summary>
    SupplierReturn = 5,

    /// <summary>Manual adjustment with a reason (damaged, expired, lost, ...).</summary>
    Adjustment = 6,

    /// <summary>Correction from a physical stock count (counted minus recorded).</summary>
    StockCount = 7,
}

public static class StockMovementTypeExtensions
{
    /// <summary>Vietnamese label for ledgers and reports.</summary>
    public static string DisplayName(this StockMovementType type) => type switch
    {
        StockMovementType.Opening => "Tồn đầu kỳ",
        StockMovementType.Sale => "Bán hàng",
        StockMovementType.SaleVoid => "Hủy hóa đơn",
        StockMovementType.CustomerReturn => "Khách trả hàng",
        StockMovementType.GoodsReceipt => "Nhập hàng",
        StockMovementType.SupplierReturn => "Trả nhà cung cấp",
        StockMovementType.Adjustment => "Điều chỉnh",
        StockMovementType.StockCount => "Kiểm kê",
        _ => type.ToString(),
    };
}
