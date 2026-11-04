using GroceryPOS.Application.Formatting;

namespace GroceryPOS.Application.Sales;

/// <summary>One line of a parked cart, with what the cashier saw when it was parked.</summary>
public sealed record ParkedLine(
    int ProductId,
    string Code,
    string Name,
    string Unit,
    decimal UnitPrice,
    decimal Quantity,
    DiscountInput? Discount);

/// <summary>
/// A cart put on hold (lưu tạm đơn) so the counter can serve the next customer – e.g. someone who went back to
/// fetch one more item. Payment, cash tendered and redeemed points are not kept: they are entered again on resume.
/// </summary>
public sealed record ParkedOrder(
    Guid Id,
    string Label,
    DateTimeOffset ParkedAt,
    int ParkedByUserId,
    string ParkedByName,
    IReadOnlyList<ParkedLine> Lines,
    int? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    DiscountInput? InvoiceDiscount,
    string? Note,
    decimal Total)
{
    public int LineCount => Lines.Count;

    public decimal TotalQuantity => Lines.Sum(l => l.Quantity);

    /// <summary>"Sữa tươi Vinamilk 1L, Mì Hảo Hảo +3" – for the parked-orders list.</summary>
    public string Preview => ParkedOrderRules.Preview(Lines.Select(l => l.Name).ToList());
}

/// <summary>Result of putting a parked order back on the counter.</summary>
/// <param name="Order">The order that was resumed (now removed from the parked list).</param>
/// <param name="RestoredLines">Lines now in the cart.</param>
/// <param name="Notices">What changed since parking: price changes, products or member no longer available.</param>
public sealed record ParkedOrderResume(ParkedOrder Order, int RestoredLines, IReadOnlyList<string> Notices)
{
    public bool HasNotices => Notices.Count > 0;
}

/// <summary>Limits, default labels and display texts for parked orders (UI-free, tested).</summary>
public static class ParkedOrderRules
{
    /// <summary>A counter rarely has more than a couple of customers on hold; a cap keeps the list usable.</summary>
    public const int MaxParked = 20;

    public const int MaxLabelLength = 60;

    /// <summary>Parked orders older than this are flagged as forgotten in the list.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(12);

    private const string DefaultPrefix = "Đơn tạm ";

    /// <summary>
    /// Label for a new parked order: what the cashier typed, else the member's name, else the next free
    /// "Đơn tạm n" among the orders already parked.
    /// </summary>
    public static string LabelFor(string? typed, string? customerName, IEnumerable<string> existingLabels)
    {
        var text = typed?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            return text.Length > MaxLabelLength ? text[..MaxLabelLength].TrimEnd() : text;
        }

        if (!string.IsNullOrWhiteSpace(customerName))
        {
            return customerName.Trim();
        }

        var used = existingLabels
            .Where(l => l.StartsWith(DefaultPrefix, StringComparison.Ordinal))
            .Select(l => int.TryParse(l.AsSpan(DefaultPrefix.Length), out var n) ? n : 0)
            .ToHashSet();
        var next = 1;
        while (used.Contains(next))
        {
            next++;
        }

        return DefaultPrefix + next;
    }

    /// <summary>First two product names and "+n" for the rest.</summary>
    public static string Preview(IReadOnlyList<string> names, int shown = 2)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        var head = string.Join(", ", names.Take(shown));
        return names.Count > shown ? $"{head} +{names.Count - shown}" : head;
    }

    /// <summary>"vừa xong", "5 phút trước", "2 giờ trước", "hôm qua 18:20", "02/11 09:15".</summary>
    public static string Age(DateTimeOffset parkedAt, DateTimeOffset now, TimeSpan storeOffset)
    {
        var elapsed = now - parkedAt;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "vừa xong";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} phút trước";
        }

        var local = parkedAt.ToOffset(storeOffset);
        var days = Common.StoreCalendar.DateOf(now, storeOffset).DayNumber - Common.StoreCalendar.DateOf(parkedAt, storeOffset).DayNumber;
        return days switch
        {
            0 => $"{(int)elapsed.TotalHours} giờ trước",
            1 => $"hôm qua {local:HH:mm}",
            _ => $"{local:dd/MM HH:mm}",
        };
    }

    public static bool IsStale(DateTimeOffset parkedAt, DateTimeOffset now) => now - parkedAt >= StaleAfter;

    internal static string PriceChanged(ParkedLine line, decimal newPrice) =>
        $"Giá \"{line.Name}\" đã đổi: {Vnd.Format(line.UnitPrice)} → {Vnd.Format(newPrice)}.";

    internal static string ProductGone(ParkedLine line) =>
        $"\"{line.Name}\" không còn kinh doanh nên đã bị bỏ khỏi đơn.";
}
