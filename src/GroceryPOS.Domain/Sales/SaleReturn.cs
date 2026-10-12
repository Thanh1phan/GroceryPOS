using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Sales;

/// <summary>Quantity of one invoice line the customer brings back, and whether it can be put back on the shelf.</summary>
public sealed record ReturnItem(SaleLine Line, decimal Quantity, bool Restock = true);

/// <summary>
/// Customer return (phiếu trả hàng) against a completed invoice. Each line refunds its share of the
/// invoice line's final amount, so every discount given at the counter is honoured. Restocked lines go
/// back into stock at the cost recorded on the sale; damaged goods are refunded but not restocked.
/// <para>
/// A return is created complete: <see cref="Create"/> checks every rule, then records the returned
/// quantities on the sale. Stock and loyalty are applied by the application layer.
/// </para>
/// </summary>
public sealed class SaleReturn : AuditableEntity
{
    private readonly List<SaleReturnLine> _lines = new();

    private SaleReturn()
    {
    }

    private SaleReturn(string code, Sale sale, int userId, DateTimeOffset returnedAt, string reason, PaymentMethod refundMethod)
    {
        Code = Guard.NotEmpty(code, "return.code", "Số phiếu trả hàng không được để trống", 32).ToUpperInvariant();
        if (userId <= 0)
        {
            throw new DomainException("return.user", "Thiếu thông tin người lập phiếu.");
        }

        if (refundMethod is not (PaymentMethod.Cash or PaymentMethod.BankTransfer))
        {
            throw new DomainException("return.refund_method", "Chỉ hoàn tiền bằng tiền mặt hoặc chuyển khoản.");
        }

        SaleId = sale.Id;
        SaleCode = sale.Code;
        CustomerId = sale.CustomerId;
        UserId = userId;
        ReturnedAt = returnedAt;
        Reason = Guard.NotEmpty(reason, "return.reason", "Vui lòng nhập lý do trả hàng", 500);
        RefundMethod = refundMethod;
    }

    public string Code { get; private set; } = string.Empty;
    public int SaleId { get; private set; }
    public string SaleCode { get; private set; } = string.Empty;
    public int? CustomerId { get; private set; }
    public int UserId { get; private set; }
    public DateTimeOffset ReturnedAt { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public PaymentMethod RefundMethod { get; private set; }

    /// <summary>Loyalty points earned on the invoice that this return takes back from the customer.</summary>
    public int PointsReversed { get; private set; }

    /// <summary>Points redeemed on the invoice that are given back (only when the whole invoice is returned).</summary>
    public int PointsRestored { get; private set; }

    public IReadOnlyList<SaleReturnLine> Lines => _lines;

    /// <summary>Money handed back to the customer (VND).</summary>
    public decimal RefundTotal => _lines.Sum(l => l.RefundAmount);

    public decimal VatTotal => _lines.Sum(l => l.VatAmount);

    /// <summary>Cost value of the goods put back into stock.</summary>
    public decimal RestockedCost => _lines.Where(l => l.Restock).Sum(l => l.CostAmount);

    /// <summary>Cost value of returned goods that were written off instead of restocked.</summary>
    public decimal WrittenOffCost => _lines.Where(l => !l.Restock).Sum(l => l.CostAmount);

    public static SaleReturn Create(
        string code,
        Sale sale,
        int userId,
        DateTimeOffset returnedAt,
        string reason,
        PaymentMethod refundMethod,
        IReadOnlyList<ReturnItem> items)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(items);

        // Constructor validates the header before the sale is touched.
        var result = new SaleReturn(code, sale, userId, returnedAt, reason, refundMethod);
        var (refunds, reversed, restored) = sale.RegisterReturn(items.Select(i => (i.Line, i.Quantity)).ToList());

        for (var i = 0; i < items.Count; i++)
        {
            result._lines.Add(new SaleReturnLine(items[i].Line, items[i].Quantity, refunds[i], items[i].Restock));
        }

        result.PointsReversed = reversed;
        result.PointsRestored = restored;
        return result;
    }
}
