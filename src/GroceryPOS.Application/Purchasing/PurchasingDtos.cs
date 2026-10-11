using GroceryPOS.Domain.Purchasing;

namespace GroceryPOS.Application.Purchasing;

/// <summary>
/// One product to receive. <see cref="UnitCost"/> is the supplier price per selling unit before discounts;
/// <see cref="NewSellPrice"/> optionally updates the shelf price when the receipt is completed (needs <c>prices.change</c>).
/// </summary>
public sealed record GoodsReceiptLineInput(
    int ProductId,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount = 0,
    decimal? NewSellPrice = null);

/// <summary>Creates (<see cref="Id"/> null) or replaces the content of a draft goods receipt.</summary>
public sealed record SaveGoodsReceiptRequest(
    int? Id,
    int SupplierId,
    IReadOnlyList<GoodsReceiptLineInput> Lines,
    decimal DiscountAmount = 0,
    decimal PaidAmount = 0,
    string? SupplierInvoiceNo = null,
    string? Note = null);

public sealed record GoodsReceiptLineDto(
    int ProductId,
    string ProductCode,
    string ProductName,
    string Unit,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    decimal AllocatedDiscount,
    decimal FinalAmount,
    decimal EffectiveUnitCost,
    decimal? NewSellPrice);

public sealed record GoodsReceiptDto(
    int Id,
    string Code,
    GoodsReceiptStatus Status,
    string StatusName,
    DateTimeOffset ReceiptDate,
    int SupplierId,
    string SupplierName,
    string? SupplierInvoiceNo,
    string? Note,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    decimal PaidAmount,
    decimal DebtAmount,
    DateTimeOffset? CompletedAt,
    string? CancelReason,
    IReadOnlyList<GoodsReceiptLineDto> Lines);

public sealed record GoodsReceiptSummary(
    int Id,
    string Code,
    DateTimeOffset ReceiptDate,
    GoodsReceiptStatus Status,
    string StatusName,
    int SupplierId,
    string SupplierName,
    int LineCount,
    decimal Total,
    decimal PaidAmount,
    decimal DebtAmount);

/// <summary>Effect of one completed line on its product.</summary>
public sealed record GoodsReceiptPostedLine(
    int ProductId,
    string ProductCode,
    string ProductName,
    decimal Quantity,
    decimal StockAfter,
    decimal OldCostPrice,
    decimal NewCostPrice,
    decimal SellPrice,
    bool SellPriceChanged)
{
    /// <summary>Selling at or below the new average cost – the UI should warn the user to review the price.</summary>
    public bool IsBelowCost => SellPrice <= NewCostPrice;
}

public sealed record GoodsReceiptCompletion(
    string Code,
    decimal Total,
    decimal PaidAmount,
    decimal DebtAmount,
    decimal SupplierDebtBalance,
    IReadOnlyList<GoodsReceiptPostedLine> Lines)
{
    public IEnumerable<GoodsReceiptPostedLine> BelowCostLines => Lines.Where(l => l.IsBelowCost);
}

/// <summary>Low-stock product with a suggested order quantity (up to twice the minimum level).</summary>
public sealed record ReorderSuggestion(
    int ProductId,
    string ProductCode,
    string ProductName,
    string Unit,
    decimal StockQuantity,
    decimal MinStockLevel,
    decimal SuggestedQuantity,
    decimal LastCostPrice)
{
    public decimal EstimatedAmount => Math.Round(SuggestedQuantity * LastCostPrice, 0, MidpointRounding.AwayFromZero);
}
