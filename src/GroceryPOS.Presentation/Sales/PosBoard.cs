using System.Globalization;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Sales;

/// <summary>
/// Everything the Bán hàng screen shows next to the cart: member card, invoice totals, tender section, the message
/// above Thanh toán and which buttons work. Computed from the <see cref="PosCart"/> so the view only copies values.
/// </summary>
public sealed record PosBoard(
    IReadOnlyList<PosCartLine> Lines,
    bool CanEditLines,
    bool CanDiscountLines,
    string CustomerInfo,
    Tone CustomerTone,
    bool HasMember,
    bool CanUseMaxPoints,
    int PointsToRedeem,
    string Gross,
    string LineDiscounts,
    string InvoiceDiscount,
    string PointsDiscount,
    string Total,
    string Earn,
    bool CanDiscountInvoice,
    PaymentMethod Method,
    decimal? CashTendered,
    string? PaymentReference,
    IReadOnlyList<decimal> QuickCash,
    string Change,
    string Blocker,
    bool CanPay)
{
    public const string ShiftMissingBlocker = "Chưa mở ca bán hàng.";

    public bool IsCash => Method == PaymentMethod.Cash;

    /// <param name="canDiscount">The cashier has <c>sales.discount</c>.</param>
    /// <param name="shiftMissing">The store requires an open shift and the cashier has none.</param>
    /// <param name="busy">A payment is being saved: Thanh toán stays off.</param>
    public static PosBoard From(PosCart cart, bool canDiscount, bool shiftMissing, bool busy)
    {
        ArgumentNullException.ThrowIfNull(cart);
        var hasLines = !cart.IsEmpty;
        var member = cart.Customer;
        return new PosBoard(
            Lines: cart.Lines,
            CanEditLines: hasLines && !busy,
            CanDiscountLines: hasLines && canDiscount && !busy,
            CustomerInfo: member is null
                ? "Khách lẻ"
                : $"{member.Name} · {member.Code}\r\nĐiểm hiện có: {Points(member.LoyaltyPoints)} (đổi tối đa {Points(cart.MaxRedeemablePoints)})",
            CustomerTone: member is null ? Tone.Muted : Tone.Normal,
            HasMember: member is not null,
            CanUseMaxPoints: member is not null && cart.MaxRedeemablePoints > 0,
            PointsToRedeem: cart.PointsToRedeem,
            Gross: Vnd.Format(cart.GrossAmount),
            LineDiscounts: PosTexts.Minus(cart.LineDiscountTotal),
            InvoiceDiscount: PosTexts.Minus(cart.InvoiceDiscountAmount)
                + (cart.InvoiceDiscount is { IsNone: false } d ? $" ({PosTexts.FormatDiscount(d)})" : string.Empty),
            PointsDiscount: cart.PointsToRedeem > 0
                ? $"{PosTexts.Minus(cart.PointsDiscountAmount)} ({Points(cart.PointsToRedeem)} điểm)"
                : PosTexts.Minus(0),
            Total: Vnd.Format(cart.Total),
            Earn: member is null ? string.Empty : $"Khách được cộng {Points(cart.PointsToEarn)} điểm",
            CanDiscountInvoice: hasLines && canDiscount && !busy,
            Method: cart.PaymentMethod,
            CashTendered: cart.CashTendered,
            PaymentReference: cart.PaymentReference,
            QuickCash: cart.PaymentMethod == PaymentMethod.Cash && hasLines ? cart.SuggestCashAmounts() : [],
            Change: Vnd.Format(cart.Change),
            Blocker: shiftMissing ? ShiftMissingBlocker : hasLines ? cart.CheckoutBlocker ?? string.Empty : string.Empty,
            CanPay: !busy && cart.CanCheckout && !shiftMissing);
    }

    private static string Points(int points) => points.ToString("#,0", Vnd.Culture);
}

/// <summary>The shift line above the scan box: the open shift, why selling is blocked, or "Chưa mở ca" with the Mở ca button.</summary>
public sealed record PosShiftBar(string Text, Tone Tone, bool ShowOpenButton)
{
    public static PosShiftBar Checking { get; } = new("Đang kiểm tra ca làm việc…", Tone.Muted, false);

    /// <param name="problem">Why the shift could not be read (e.g. no shift permission); null when it was read.</param>
    public static PosShiftBar From(ShiftSummaryDto? shift, string? problem, SalesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (shift is not null)
        {
            var openedAt = shift.OpenedAt.ToOffset(options.StoreUtcOffset).ToString("HH:mm dd/MM", CultureInfo.InvariantCulture);
            return new($"Ca {shift.Code} · mở lúc {openedAt} · tiền đầu ca {Vnd.Format(shift.OpeningFloat)}", Tone.Muted, false);
        }

        if (problem is not null)
        {
            return options.RequireOpenShift
                ? new($"Không thể bán hàng: {problem}", Tone.Danger, false)
                : new(problem, Tone.Muted, false);
        }

        return options.RequireOpenShift
            ? new("Chưa mở ca – cần mở ca trước khi thanh toán.", Tone.Warning, true)
            : new("Chưa mở ca.", Tone.Muted, true);
    }
}

/// <summary>Status-line and confirmation texts of the Bán hàng screen.</summary>
public static class PosTexts
{
    public const string Ready = "Sẵn sàng bán hàng.";

    public const string NoDiscountPermission = "Bạn không có quyền giảm giá khi bán.";

    public static string Minus(decimal amount) => amount == 0 ? Vnd.Format(0) : "−" + Vnd.Format(amount);

    public static string FormatDiscount(DiscountInput discount)
    {
        ArgumentNullException.ThrowIfNull(discount);
        return discount.Kind == DiscountKind.Percent
            ? $"{discount.Value.ToString("0.##", Vnd.Culture)}%"
            : Vnd.Format(discount.Value);
    }

    public static string Quantity(decimal quantity) => quantity.ToString("0.###", Vnd.Culture);

    /// <summary>"Đã thêm … × 2 – 30.000 ₫", flagged when the cart now asks for more than is in stock.</summary>
    public static (string Text, Tone Tone) Added(PosCartLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = $"Đã thêm {line.Name} × {Quantity(line.Quantity)} – {Vnd.Format(line.NetAmount)}";
        return line.ExceedsStock
            ? ($"{text} (vượt tồn kho: còn {Quantity(line.Product.StockQuantity)})", Tone.Warning)
            : (text, Tone.Success);
    }

    /// <summary>Success line after a payment: code, total, change, points earned and items running low.</summary>
    public static string Paid(CheckoutReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var text = $"✔ {receipt.Code}: {Vnd.Format(receipt.Total)}";
        if (receipt.Change > 0)
        {
            text += $" · Tiền thừa {Vnd.Format(receipt.Change)}";
        }

        if (receipt.PointsEarned > 0)
        {
            text += $" · +{receipt.PointsEarned.ToString("#,0", Vnd.Culture)} điểm";
        }

        if (receipt.LowStockAlerts.Count > 0)
        {
            text += $" · Sắp hết: {string.Join(", ", receipt.LowStockAlerts.Take(3).Select(a => a.ProductName))}";
        }

        return text;
    }

    /// <summary>Caption of the Đơn chờ button: "Đơn chờ (F11) · 2" when orders are waiting.</summary>
    public static string ParkedCaption(int count) => count > 0 ? $"Đơn chờ (F11) · {count}" : "Đơn chờ (F11)";

    /// <summary>The question before leaving the screen with an unpaid cart.</summary>
    public static string LeaveQuestion(PosCart cart)
    {
        ArgumentNullException.ThrowIfNull(cart);
        return $"Đơn hàng đang bán ({cart.Lines.Count} sản phẩm, {Vnd.Format(cart.Total)}) chưa thanh toán. " +
            "Rời màn hình Bán hàng sẽ bỏ đơn này. (Chọn \"Tiếp tục bán\" rồi nhấn F10 nếu muốn lưu tạm đơn.)";
    }
}
