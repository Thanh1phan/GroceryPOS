using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Sales;

/// <summary>Discount as entered at the counter: a percentage or a VND amount.</summary>
public sealed record DiscountInput(DiscountKind Kind, decimal Value)
{
    public static DiscountInput Percent(decimal percent) => new(DiscountKind.Percent, percent);

    public static DiscountInput Amount(decimal amount) => new(DiscountKind.Amount, amount);

    public bool IsNone => Kind == DiscountKind.None || Value == 0;

    internal Discount ToDomain() => Kind switch
    {
        DiscountKind.Percent => Discount.Percent(Value),
        DiscountKind.Amount => Discount.Amount(Value),
        _ => Discount.None,
    };
}

public sealed record CheckoutLine(int ProductId, decimal Quantity, DiscountInput? Discount = null);

public sealed record PaymentInput(PaymentMethod Method, decimal Amount, string? Reference = null);

/// <summary>Everything the POS screen sends when the cashier presses "Thanh toán".</summary>
public sealed record CheckoutRequest(
    IReadOnlyList<CheckoutLine> Lines,
    IReadOnlyList<PaymentInput> Payments,
    int? CustomerId = null,
    DiscountInput? InvoiceDiscount = null,
    int PointsToRedeem = 0,
    string? Note = null)
{
    public bool HasDiscounts =>
        (InvoiceDiscount is { IsNone: false }) || Lines.Any(l => l.Discount is { IsNone: false });
}

/// <summary>Product that is at or below its minimum stock level after a sale.</summary>
public sealed record LowStockAlert(int ProductId, string ProductCode, string ProductName, decimal StockQuantity, decimal MinStockLevel);

/// <summary>Outcome of a completed checkout, used for the success toast and the receipt.</summary>
public sealed record CheckoutReceipt(
    int SaleId,
    string Code,
    DateTimeOffset CompletedAt,
    decimal Total,
    decimal AmountPaid,
    decimal Change,
    decimal TotalDiscount,
    decimal VatTotal,
    int PointsEarned,
    int PointsRedeemed,
    int? CustomerPointsBalance,
    IReadOnlyList<LowStockAlert> LowStockAlerts);

/// <summary>Compact product info for the POS quick search / barcode scan.</summary>
public sealed record PosProductDto(
    int Id,
    string Code,
    string? Barcode,
    string Name,
    string Unit,
    decimal SellPrice,
    decimal VatRate,
    decimal StockQuantity,
    bool IsLowStock);
