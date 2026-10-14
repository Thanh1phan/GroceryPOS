using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Purchasing;

/// <summary>Pay part or all of a supplier's debt (phiếu chi).</summary>
public sealed record SupplierPaymentRequest(
    int SupplierId,
    decimal Amount,
    PaymentMethod Method = PaymentMethod.Cash,
    string? Reference = null,
    string? Note = null);

public sealed record SupplierPaymentDto(
    int Id,
    string Code,
    int SupplierId,
    string SupplierName,
    decimal Amount,
    PaymentMethod Method,
    string MethodName,
    DateTimeOffset PaidAt,
    string? Reference,
    string? Note,
    decimal DebtBefore,
    decimal DebtAfter,
    bool IsCancelled,
    DateTimeOffset? CancelledAt,
    string? CancelReason);

/// <summary>A supplier the store still owes money to (danh sách công nợ phải trả).</summary>
public sealed record SupplierPayableDto(int SupplierId, string Code, string Name, string? Phone, decimal DebtBalance);

/// <summary>What changed a supplier's debt. Values are not persisted.</summary>
public enum SupplierDebtEntryKind
{
    GoodsReceipt = 1,
    Payment = 2,
    PaymentCancelled = 3,
    SupplierReturn = 4,
}

public static class SupplierDebtLabels
{
    public static string Of(SupplierDebtEntryKind kind) => kind switch
    {
        SupplierDebtEntryKind.GoodsReceipt => "Nhập hàng",
        SupplierDebtEntryKind.Payment => "Thanh toán",
        SupplierDebtEntryKind.PaymentCancelled => "Hủy phiếu chi",
        SupplierDebtEntryKind.SupplierReturn => "Trả hàng",
        _ => kind.ToString(),
    };
}

/// <summary>One line of the supplier debt ledger (sổ chi tiết công nợ).</summary>
public sealed record SupplierDebtEntry(
    DateTimeOffset Timestamp,
    SupplierDebtEntryKind Kind,
    string KindName,
    string DocumentCode,
    string Description,
    decimal Increase,
    decimal Decrease,
    decimal Balance);

/// <summary>Supplier debt ledger for [From, To): opening balance, entries with running balance, closing balance.</summary>
public sealed record SupplierDebtLedgerDto(
    int SupplierId,
    string SupplierName,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal OpeningBalance,
    decimal TotalIncrease,
    decimal TotalDecrease,
    decimal ClosingBalance,
    IReadOnlyList<SupplierDebtEntry> Entries);

/// <summary>A goods receipt line as shown on the supplier return screen.</summary>
public sealed record ReturnableReceiptLineDto(
    int ProductId,
    string ProductCode,
    string ProductName,
    string Unit,
    decimal ReceivedQuantity,
    decimal ReturnedQuantity,
    decimal StockQuantity,
    decimal LandedUnitCost)
{
    public decimal RemainingQuantity => Math.Max(0, ReceivedQuantity - ReturnedQuantity);

    /// <summary>What can be returned now: limited by the receipt and by what is still in stock.</summary>
    public decimal MaxReturnable => Math.Max(0, Math.Min(RemainingQuantity, StockQuantity));
}

/// <summary>Completed goods receipt loaded on the supplier return screen.</summary>
public sealed record ReturnableReceiptDto(
    int ReceiptId,
    string Code,
    DateTimeOffset ReceiptDate,
    int SupplierId,
    string SupplierName,
    decimal SupplierDebtBalance,
    IReadOnlyList<ReturnableReceiptLineDto> Lines)
{
    public bool CanReturn => Lines.Any(l => l.MaxReturnable > 0);
}

/// <summary>One product to return; <see cref="UnitPrice"/> null = landed cost of the receipt line (or current cost).</summary>
public sealed record SupplierReturnLineInput(int ProductId, decimal Quantity, decimal? UnitPrice = null);

public sealed record CreateSupplierReturnRequest(
    int SupplierId,
    IReadOnlyList<SupplierReturnLineInput> Lines,
    string Reason,
    int? GoodsReceiptId = null,
    decimal RefundAmount = 0,
    PaymentMethod? RefundMethod = null,
    string? Note = null);

public sealed record SupplierReturnLineDto(
    int ProductId,
    string ProductCode,
    string ProductName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    decimal CostPrice,
    decimal CostValue);

public sealed record SupplierReturnDto(
    int Id,
    string Code,
    DateTimeOffset ReturnDate,
    int SupplierId,
    string SupplierName,
    string? GoodsReceiptCode,
    string Reason,
    string? Note,
    decimal Total,
    decimal RefundAmount,
    string? RefundMethodName,
    decimal DebtOffset,
    decimal CostValue,
    decimal CostDifference,
    IReadOnlyList<SupplierReturnLineDto> Lines);

/// <summary>Result of creating a supplier return: the slip, the supplier's new balance and stock after the return.</summary>
public sealed record SupplierReturnReceipt(
    SupplierReturnDto Return,
    decimal SupplierDebtBalance,
    IReadOnlyDictionary<int, decimal> StockAfter);

public sealed record SupplierReturnSummary(
    int Id,
    string Code,
    DateTimeOffset ReturnDate,
    int SupplierId,
    string SupplierName,
    string? GoodsReceiptCode,
    int LineCount,
    decimal Total,
    decimal RefundAmount,
    decimal DebtOffset);
