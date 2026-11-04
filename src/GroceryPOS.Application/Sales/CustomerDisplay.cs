using GroceryPOS.Application.Formatting;

namespace GroceryPOS.Application.Sales;

public enum CustomerDisplayMode
{
    /// <summary>No sale in progress: store name and a greeting.</summary>
    Welcome,

    /// <summary>Items are being scanned: last item, item count and amount to pay.</summary>
    Shopping,

    /// <summary>Right after payment: total, paid and change, until the next scan.</summary>
    ThankYou,
}

/// <summary>
/// What the customer-facing screen (màn hình khách) shows. Built from the cart on every change, so the screen
/// itself is a dumb renderer. All texts are final Vietnamese strings.
/// </summary>
public sealed record CustomerDisplayState(
    CustomerDisplayMode Mode,
    string Headline,
    string? LastItemName,
    string? LastItemDetail,
    IReadOnlyList<CustomerDisplayLine> Lines,
    string ItemsText,
    string? DiscountText,
    string? MemberText,
    string AmountCaption,
    decimal Amount,
    string? FooterText)
{
    public string AmountText => Vnd.Format(Amount);

    public static CustomerDisplayState Welcome(string storeName) => new(
        CustomerDisplayMode.Welcome,
        string.IsNullOrWhiteSpace(storeName) ? "Xin chào quý khách!" : storeName.Trim(),
        null,
        null,
        Array.Empty<CustomerDisplayLine>(),
        string.Empty,
        null,
        null,
        "Xin chào quý khách!",
        0,
        null);

    /// <summary>
    /// Shopping view of the cart. <paramref name="lastLine"/> is the line just scanned or changed (falls back to the
    /// last line); only the newest <paramref name="maxLines"/> lines are listed, newest last.
    /// </summary>
    public static CustomerDisplayState FromCart(PosCart cart, string storeName, PosCartLine? lastLine = null, int maxLines = 8)
    {
        ArgumentNullException.ThrowIfNull(cart);
        if (cart.IsEmpty)
        {
            return Welcome(storeName);
        }

        var last = lastLine is not null && cart.Lines.Contains(lastLine) ? lastLine : cart.Lines[^1];
        var lines = cart.Lines
            .Skip(Math.Max(0, cart.Lines.Count - Math.Max(1, maxLines)))
            .Select(l => new CustomerDisplayLine(l.Name, $"{FormatQuantity(l.Quantity)} × {Vnd.Format(l.UnitPrice)}", Vnd.Format(l.NetAmount)))
            .ToList();

        var discount = cart.TotalDiscount > 0 ? $"Tiết kiệm {Vnd.Format(cart.TotalDiscount)}" : null;
        string? member = null;
        if (cart.Customer is { } customer)
        {
            member = cart.PointsToEarn > 0
                ? $"{customer.Name} · +{cart.PointsToEarn:#,0} điểm"
                : customer.Name;
        }

        return new CustomerDisplayState(
            CustomerDisplayMode.Shopping,
            storeName,
            last.Name,
            $"{FormatQuantity(last.Quantity)} {last.Unit} × {Vnd.Format(last.UnitPrice)} = {Vnd.Format(last.NetAmount)}",
            lines,
            $"{cart.Lines.Count} mặt hàng · SL {FormatQuantity(cart.TotalQuantity)}",
            discount,
            member,
            "Tổng tiền thanh toán",
            cart.Total,
            null);
    }

    /// <summary>After checkout: what was paid and the change to hand back.</summary>
    public static CustomerDisplayState ThankYou(string storeName, decimal total, decimal paid, decimal change, int? pointsEarned = null)
    {
        var footer = change > 0
            ? $"Khách đưa {Vnd.Format(paid)} · Tiền thừa {Vnd.Format(change)}"
            : $"Đã thanh toán {Vnd.Format(total)}";
        if (pointsEarned is > 0)
        {
            footer += $" · +{pointsEarned:#,0} điểm";
        }

        return new CustomerDisplayState(
            CustomerDisplayMode.ThankYou,
            "Cảm ơn quý khách – hẹn gặp lại!",
            null,
            null,
            Array.Empty<CustomerDisplayLine>(),
            string.Empty,
            null,
            null,
            change > 0 ? "Tiền thừa trả khách" : "Đã thanh toán",
            change > 0 ? change : total,
            footer);
    }

    /// <summary>1 → "1", 1,5 → "1,5", 0,125 → "0,125" (vi-VN decimal comma).</summary>
    public static string FormatQuantity(decimal quantity) =>
        quantity.ToString("#,0.###", Vnd.Culture);
}

/// <summary>One row of the customer screen's item list.</summary>
public sealed record CustomerDisplayLine(string Name, string Detail, string Amount);
