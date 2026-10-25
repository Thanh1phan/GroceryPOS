using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Sales.Receipts;

/// <summary>One item on the receipt. Amounts are VND, VAT-inclusive.</summary>
public sealed record ReceiptItem(
    string Name,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal GrossAmount,
    string? DiscountLabel,
    decimal DiscountAmount,
    decimal Amount);

public sealed record ReceiptPayment(string Label, decimal Amount, string? Reference);

/// <summary>Member block: name, masked phone and the points movement of this invoice.</summary>
public sealed record ReceiptMember(string Name, string? MaskedPhone, int PointsEarned, int PointsRedeemed, int? PointsBalance);

/// <summary>Everything printed on a sales receipt (hóa đơn bán lẻ), snapshotted from the sale.</summary>
public sealed record ReceiptData(
    StoreProfile Store,
    string Code,
    DateTimeOffset IssuedAt,
    string CashierName,
    SaleStatus Status,
    IReadOnlyList<ReceiptItem> Items,
    decimal GrossAmount,
    decimal LineDiscountTotal,
    string? InvoiceDiscountLabel,
    decimal InvoiceDiscountAmount,
    decimal PointsDiscountAmount,
    decimal Total,
    IReadOnlyList<ReceiptPayment> Payments,
    decimal AmountPaid,
    decimal Change,
    IReadOnlyList<VatSummary> VatBreakdown,
    ReceiptMember? Member,
    string? Note,
    bool IsReprint,
    DateTimeOffset? PrintedAt = null)
{
    public decimal TotalQuantity => Items.Sum(i => i.Quantity);

    public decimal TotalDiscount => GrossAmount - Total;
}
