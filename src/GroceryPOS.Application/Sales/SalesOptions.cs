using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Application.Sales;

/// <summary>Store-level sales settings (later editable from the Settings screen).</summary>
public sealed record SalesOptions
{
    public static SalesOptions Default { get; } = new();

    /// <summary>Invoice code prefix, e.g. "HD" → HD261009-0001.</summary>
    public string InvoicePrefix { get; init; } = "HD";

    /// <summary>Store's offset from UTC; Vietnam is UTC+7 all year (no DST). Used for the invoice date.</summary>
    public TimeSpan StoreUtcOffset { get; init; } = TimeSpan.FromHours(7);

    /// <summary>
    /// Allows selling more than the recorded stock (common in small shops where goods receipts are
    /// entered later). Stock then goes negative and shows up as a low-stock alert.
    /// </summary>
    public bool AllowNegativeStock { get; init; } = true;

    /// <summary>Customer return code prefix, e.g. "TH" → TH261012-0001.</summary>
    public string ReturnPrefix { get; init; } = "TH";

    /// <summary>
    /// Days after the sale (store calendar days) within which a cashier may take a return.
    /// Older invoices need the manager permission <c>sales.void</c>.
    /// </summary>
    public int ReturnWindowDays { get; init; } = 7;

    /// <summary>Shift code prefix, e.g. "CA" → CA261013-0001.</summary>
    public string ShiftPrefix { get; init; } = "CA";

    /// <summary>
    /// Absolute cash variance (VND) at which closing a shift requires a note explaining it.
    /// Any non-zero variance below this is accepted silently (small rounding with 500/1.000 notes).
    /// </summary>
    public decimal ShiftVarianceNoteThreshold { get; init; } = 5_000;

    /// <summary>
    /// Cashiers must have an open shift (ca bán hàng) to check out, so every sale lands in a cash drawer count.
    /// </summary>
    public bool RequireOpenShift { get; init; } = true;

    public LoyaltyPolicy Loyalty { get; init; } = LoyaltyPolicy.Default;
}
