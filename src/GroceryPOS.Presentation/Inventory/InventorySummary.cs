using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;

namespace GroceryPOS.Presentation.Inventory;

/// <summary>
/// The four KPI cards of the Tồn kho screen. Only products in business count; negative stock counts as zero
/// in the values (as <see cref="StockItem.StockValue"/> does).
/// </summary>
public sealed record InventorySummary(
    decimal StockValue,
    decimal SellValue,
    int ActiveProducts,
    int InStockProducts,
    int LowStockProducts,
    int OutOfStockProducts,
    int NegativeStockProducts)
{
    public static readonly InventorySummary Empty = new(0, 0, 0, 0, 0, 0, 0);

    public string StockValueText => Vnd.Format(StockValue);

    public string StockValueNote => $"Giá bán: {Vnd.Format(SellValue)}";

    public string ProductsText => Count(ActiveProducts);

    public string ProductsNote => $"{Count(InStockProducts)} còn hàng";

    public string LowStockText => Count(LowStockProducts);

    public string LowStockNote => "Dưới hoặc bằng định mức";

    public string OutOfStockText => Count(OutOfStockProducts);

    /// <summary>Calls for a stock count when some products went negative; null otherwise.</summary>
    public string? OutOfStockNote => NegativeStockProducts > 0 ? $"{Count(NegativeStockProducts)} sản phẩm tồn âm – cần kiểm kê" : null;

    public static InventorySummary From(IEnumerable<StockItem> stock, IEnumerable<LowStockRow> lowStock)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(lowStock);
        var active = stock.Where(i => i.IsActive).ToList();
        var low = lowStock.ToList();
        return new InventorySummary(
            active.Sum(i => i.StockValue),
            active.Sum(i => Math.Max(0, i.StockQuantity) * i.SellPrice),
            active.Count,
            active.Count(i => i.StockQuantity > 0),
            low.Count(r => !r.IsOutOfStock),
            low.Count(r => r.IsOutOfStock),
            active.Count(i => i.StockQuantity < 0));
    }

    private static string Count(int value) => value.ToString("#,0", Vnd.Culture);
}
