using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Sales;

/// <summary>One invoice line on the return screen with what the cashier picked to take back.</summary>
public sealed class ReturnSheetLine
{
    internal ReturnSheetLine(ReturnableLineDto line)
    {
        Line = line;
    }

    public ReturnableLineDto Line { get; }

    public int SaleLineId => Line.SaleLineId;

    /// <summary>Quantity to return (0 = not returned).</summary>
    public decimal Quantity { get; internal set; }

    /// <summary>True = goods go back on the shelf; false = damaged/expired, written off.</summary>
    public bool Restock { get; internal set; } = true;

    public bool IsSelected => Quantity > 0;

    /// <summary>What happens to the goods, as shown in the grid.</summary>
    public string HandlingText => !IsSelected ? string.Empty : Restock ? "Nhập lại kho" : "Hủy hàng (hỏng)";

    /// <summary>Nothing left to return on this line (returned earlier).</summary>
    public bool IsExhausted => Line.RemainingQuantity <= 0;

    /// <summary>
    /// Refund for <see cref="Quantity"/>, computed exactly as <c>SaleLine.RefundFor</c>: the share of the line's final
    /// amount, and returning the last remaining units refunds whatever is left to the đồng.
    /// </summary>
    public decimal Refund
    {
        get
        {
            if (Quantity <= 0 || Line.SoldQuantity <= 0)
            {
                return 0;
            }

            if (Quantity >= Line.RemainingQuantity)
            {
                return Line.RemainingRefund;
            }

            return Math.Min(Line.RemainingRefund, Money.Round(Line.FinalAmount * Quantity / Line.SoldQuantity));
        }
    }
}

/// <summary>
/// UI-free state of the customer return screen (phiếu trả hàng): quantities picked per invoice line, restock or
/// write-off per line, reason, refund method, the refund and loyalty preview and the blocker that keeps "Hoàn tiền"
/// disabled. Mirrors the domain rules of <c>SaleReturn.Create</c> so the cashier sees the exact refund before
/// submitting; <see cref="BuildRequest"/> produces the <see cref="CreateReturnRequest"/>.
/// </summary>
public sealed class ReturnSheet
{
    /// <summary>One-click reasons offered next to the reason box.</summary>
    public static readonly IReadOnlyList<string> QuickReasons =
    [
        "Hàng lỗi / hư hỏng",
        "Hàng hết hạn sử dụng",
        "Khách đổi ý",
        "Bán nhầm hàng",
        "Tính tiền sai",
    ];

    private readonly List<ReturnSheetLine> _lines;

    public ReturnSheet(ReturnableSaleDto sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        Sale = sale;
        _lines = sale.Lines.Select(l => new ReturnSheetLine(l)).ToList();
    }

    /// <summary>Raised whenever a quantity, restock flag, reason or refund method changes.</summary>
    public event EventHandler? Changed;

    public ReturnableSaleDto Sale { get; }

    public IReadOnlyList<ReturnSheetLine> Lines => _lines;

    public string? Reason { get; private set; }

    public PaymentMethod RefundMethod { get; private set; } = PaymentMethod.Cash;

    /// <summary>Whether anything on the invoice may still be returned by this user.</summary>
    public bool CanReturnAnything => Sale.CanReturn && !Sale.IsFullyReturned;

    public IEnumerable<ReturnSheetLine> SelectedLines => _lines.Where(l => l.IsSelected);

    public int SelectedLineCount => _lines.Count(l => l.IsSelected);

    public decimal SelectedQuantity => _lines.Sum(l => l.Quantity);

    /// <summary>Money handed back to the customer for the current selection.</summary>
    public decimal RefundTotal => _lines.Sum(l => l.Refund);

    /// <summary>Refund of lines whose goods are written off (not restocked).</summary>
    public decimal WrittenOffRefund => _lines.Where(l => l.IsSelected && !l.Restock).Sum(l => l.Refund);

    /// <summary>Whether this return takes back everything still left on the invoice.</summary>
    public bool ReturnsEverythingLeft =>
        _lines.Any(l => l.IsSelected) && _lines.All(l => l.Quantity >= l.Line.RemainingQuantity);

    /// <summary>Loyalty points this return will take back from the member (same rule as <c>Sale.RegisterReturn</c>).</summary>
    public int PointsToReverse
    {
        get
        {
            if (Sale.CustomerId is null || SelectedLineCount == 0)
            {
                return 0;
            }

            var refunded = Sale.RefundedTotal + RefundTotal;
            var target = ReturnsEverythingLeft || Sale.Total == 0
                ? Sale.PointsEarned
                : (int)Math.Floor(Sale.PointsEarned * refunded / Sale.Total);
            return Math.Max(0, target - Sale.PointsReversed);
        }
    }

    /// <summary>Points redeemed on the invoice that are given back – only when the whole invoice comes back.</summary>
    public int PointsToRestore =>
        Sale.CustomerId is not null && ReturnsEverythingLeft && !Sale.RedeemedPointsRestored ? Sale.PointsRedeemed : 0;

    /// <summary>Why the return cannot be made yet, or null when it can.</summary>
    public string? Blocker
    {
        get
        {
            if (Sale.IsFullyReturned)
            {
                return "Hóa đơn đã được trả hết hàng.";
            }

            if (!Sale.CanReturn)
            {
                return "Hóa đơn đã quá hạn đổi trả. Vui lòng nhờ quản lý thực hiện.";
            }

            if (SelectedLineCount == 0)
            {
                return "Chọn sản phẩm và số lượng khách trả.";
            }

            if (Reason is null)
            {
                return "Vui lòng nhập lý do trả hàng.";
            }

            return null;
        }
    }

    public bool CanSubmit => Blocker is null;

    public ReturnSheetLine LineOf(int saleLineId) =>
        _lines.FirstOrDefault(l => l.SaleLineId == saleLineId)
        ?? throw new ArgumentOutOfRangeException(nameof(saleLineId), saleLineId, "Dòng hàng không thuộc hóa đơn này.");

    /// <summary>Sets the quantity to return, rounded like sale quantities and clamped to 0…remaining.</summary>
    public void SetQuantity(int saleLineId, decimal quantity)
    {
        var line = LineOf(saleLineId);
        var rounded = Math.Round(quantity, SaleLine.QuantityDecimals, MidpointRounding.AwayFromZero);
        line.Quantity = Math.Clamp(rounded, 0, Math.Max(0, line.Line.RemainingQuantity));
        OnChanged();
    }

    /// <summary>+1 / −1 for the stepper buttons (fractions are kept, e.g. 1,5 kg − 1 = 0,5 kg).</summary>
    public void Step(int saleLineId, int delta) => SetQuantity(saleLineId, LineOf(saleLineId).Quantity + delta);

    /// <summary>Takes back everything left on the line, or nothing when it is already fully picked.</summary>
    public void ToggleLine(int saleLineId)
    {
        var line = LineOf(saleLineId);
        SetQuantity(saleLineId, line.Quantity >= line.Line.RemainingQuantity ? 0 : line.Line.RemainingQuantity);
    }

    public void SetRestock(int saleLineId, bool restock)
    {
        LineOf(saleLineId).Restock = restock;
        OnChanged();
    }

    /// <summary>Picks every remaining unit of every line (trả cả hóa đơn).</summary>
    public void ReturnAll()
    {
        foreach (var line in _lines)
        {
            line.Quantity = Math.Max(0, line.Line.RemainingQuantity);
        }

        OnChanged();
    }

    /// <summary>Clears every picked quantity (keeps reason and refund method).</summary>
    public void Clear()
    {
        foreach (var line in _lines)
        {
            line.Quantity = 0;
        }

        OnChanged();
    }

    public void SetReason(string? reason)
    {
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        OnChanged();
    }

    /// <summary>Refunds are paid in cash from the drawer or by bank transfer.</summary>
    public void SetRefundMethod(PaymentMethod method)
    {
        if (method is not (PaymentMethod.Cash or PaymentMethod.BankTransfer))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "Chỉ hoàn tiền bằng tiền mặt hoặc chuyển khoản.");
        }

        RefundMethod = method;
        OnChanged();
    }

    /// <summary>Short summary for the confirmation prompt, e.g. "Hoàn 35.000 ₫ tiền mặt cho 1 sản phẩm".</summary>
    public string Summary =>
        $"Hoàn {Vnd.Format(RefundTotal)} {(RefundMethod == PaymentMethod.Cash ? "tiền mặt" : "chuyển khoản")} cho {SelectedLineCount} sản phẩm";

    /// <summary>The request for <see cref="IReturnService.CreateAsync"/>, or the blocker as a validation error.</summary>
    public Result<CreateReturnRequest> BuildRequest()
    {
        if (Blocker is { } blocker)
        {
            return Error.Validation(blocker);
        }

        return new CreateReturnRequest(
            Sale.SaleId,
            SelectedLines.Select(l => new ReturnLineInput(l.SaleLineId, l.Quantity, l.Restock)).ToList(),
            Reason!,
            RefundMethod);
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
