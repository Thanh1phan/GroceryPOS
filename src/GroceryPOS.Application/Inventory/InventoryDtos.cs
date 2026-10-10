using GroceryPOS.Domain.Inventory;

namespace GroceryPOS.Application.Inventory;

/// <summary>Manual adjustment of one product. <see cref="Quantity"/> is signed (+ in, − out).</summary>
public sealed record StockAdjustmentRequest(int ProductId, decimal Quantity, StockAdjustmentReason Reason, string? Note = null);

public sealed record StockAdjustmentResult(
    int ProductId,
    string ProductCode,
    string ProductName,
    decimal Quantity,
    decimal BalanceAfter,
    decimal CostValue,
    bool IsLowStock);

public sealed record StockCountLine(int ProductId, decimal CountedQuantity);

/// <summary>Physical stock count (kiểm kê) for several products at once.</summary>
public sealed record StockCountRequest(IReadOnlyList<StockCountLine> Lines, string? Note = null);

public sealed record StockCountLineResult(
    int ProductId,
    string ProductCode,
    string ProductName,
    string Unit,
    decimal RecordedQuantity,
    decimal CountedQuantity,
    decimal UnitCost)
{
    /// <summary>Counted minus recorded: negative = shortage, positive = surplus.</summary>
    public decimal Variance => CountedQuantity - RecordedQuantity;

    public decimal VarianceValue => Math.Round(Variance * UnitCost, 0, MidpointRounding.AwayFromZero);
}

public sealed record StockCountResult(string Code, DateTimeOffset CountedAt, IReadOnlyList<StockCountLineResult> Lines)
{
    public int MismatchCount => Lines.Count(l => l.Variance != 0);

    public decimal ShortageValue => Lines.Where(l => l.Variance < 0).Sum(l => l.VarianceValue);

    public decimal SurplusValue => Lines.Where(l => l.Variance > 0).Sum(l => l.VarianceValue);

    public decimal NetVarianceValue => Lines.Sum(l => l.VarianceValue);
}

/// <summary>One row of a product's stock card (thẻ kho).</summary>
public sealed record StockLedgerEntry(
    int Id,
    DateTimeOffset Timestamp,
    StockMovementType Type,
    string TypeName,
    string? ReferenceCode,
    decimal QuantityIn,
    decimal QuantityOut,
    decimal BalanceAfter,
    decimal UnitCost,
    string? ReasonName,
    string? Note);

public sealed record StockLedger(
    int ProductId,
    string ProductCode,
    string ProductName,
    string Unit,
    decimal OpeningBalance,
    decimal TotalIn,
    decimal TotalOut,
    decimal ClosingBalance,
    IReadOnlyList<StockLedgerEntry> Entries);

public sealed record LowStockItem(
    int ProductId,
    string ProductCode,
    string ProductName,
    string Unit,
    decimal StockQuantity,
    decimal MinStockLevel,
    decimal CostPrice)
{
    public bool IsOutOfStock => StockQuantity <= 0;

    /// <summary>Quantity needed to get back to the minimum level.</summary>
    public decimal Shortage => Math.Max(0, MinStockLevel - StockQuantity);
}
