using GroceryPOS.Application.Inventory;

namespace GroceryPOS.Presentation.Inventory;

/// <summary>Low-stock alert row with a reorder suggestion (refill to twice the minimum, as purchasing does).</summary>
public sealed record LowStockRow(
    int ProductId,
    string ProductCode,
    string ProductName,
    string Unit,
    decimal StockQuantity,
    decimal MinStockLevel,
    decimal Shortage,
    decimal Suggested,
    decimal SuggestedCost,
    bool IsOutOfStock,
    string StatusName)
{
    public const string OutOfStockStatus = "Hết hàng";
    public const string LowStatus = "Sắp hết";

    /// <summary>Quantity to order: refill to twice the minimum, whole units (same rule as <c>GoodsReceiptService.SuggestedQuantity</c>).</summary>
    public static decimal SuggestedQuantity(decimal stockQuantity, decimal minStockLevel) =>
        Math.Max(0, Math.Ceiling(Math.Max(minStockLevel * 2, 0) - stockQuantity));

    public static LowStockRow From(LowStockItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var suggested = SuggestedQuantity(item.StockQuantity, item.MinStockLevel);
        return new LowStockRow(
            item.ProductId,
            item.ProductCode,
            item.ProductName,
            item.Unit,
            item.StockQuantity,
            item.MinStockLevel,
            item.Shortage,
            suggested,
            Math.Round(suggested * item.CostPrice, 0, MidpointRounding.AwayFromZero),
            item.IsOutOfStock,
            item.IsOutOfStock ? OutOfStockStatus : LowStatus);
    }
}
