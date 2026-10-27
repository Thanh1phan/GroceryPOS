using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Sales;

/// <summary>An invoice line as shown on the return screen.</summary>
public sealed record ReturnableLineDto(
    int SaleLineId,
    int ProductId,
    string ProductCode,
    string ProductName,
    string Unit,
    decimal SoldQuantity,
    decimal ReturnedQuantity,
    decimal RemainingQuantity,
    decimal FinalAmount,
    decimal RemainingRefund)
{
    /// <summary>Approximate refund per unit (final amount ÷ quantity), for display only.</summary>
    public decimal UnitRefund => SoldQuantity == 0 ? 0 : Money.Round(FinalAmount / SoldQuantity);
}

/// <summary>Invoice loaded on the return screen after scanning or typing its code.</summary>
public sealed record ReturnableSaleDto(
    int SaleId,
    string Code,
    DateTimeOffset SaleDate,
    int? CustomerId,
    string? CustomerName,
    decimal Total,
    decimal RefundedTotal,
    int PointsEarned,
    int PointsRedeemed,
    bool IsWithinReturnWindow,
    bool CanReturn,
    IReadOnlyList<ReturnableLineDto> Lines,
    int PointsReversed = 0,
    bool RedeemedPointsRestored = false)
{
    /// <summary>Whether every unit of the invoice has already been returned.</summary>
    public bool IsFullyReturned => Lines.All(l => l.RemainingQuantity <= 0);
}

/// <summary>One line to return: quantity, and whether the goods go back on the shelf (false = damaged, written off).</summary>
public sealed record ReturnLineInput(int SaleLineId, decimal Quantity, bool Restock = true);

public sealed record CreateReturnRequest(
    int SaleId,
    IReadOnlyList<ReturnLineInput> Lines,
    string Reason,
    PaymentMethod RefundMethod = PaymentMethod.Cash);

/// <summary>One returned product on the return slip.</summary>
public sealed record ReturnedLineDto(
    string ProductCode,
    string ProductName,
    string Unit,
    decimal Quantity,
    decimal RefundAmount,
    bool Restock);

/// <summary>Outcome of a return, for the success dialog and the return slip.</summary>
public sealed record ReturnReceipt(
    int ReturnId,
    string Code,
    string SaleCode,
    DateTimeOffset ReturnedAt,
    decimal RefundTotal,
    PaymentMethod RefundMethod,
    int PointsReversed,
    int PointsRestored,
    int? CustomerPointsBalance,
    bool SaleFullyReturned,
    decimal WrittenOffCost,
    IReadOnlyList<ReturnedLineDto> Lines,
    string Reason,
    string? CustomerName);

/// <summary>Return history entry for an invoice.</summary>
public sealed record SaleReturnSummaryDto(
    int Id,
    string Code,
    DateTimeOffset ReturnedAt,
    decimal RefundTotal,
    PaymentMethod RefundMethod,
    string Reason,
    int LineCount);
