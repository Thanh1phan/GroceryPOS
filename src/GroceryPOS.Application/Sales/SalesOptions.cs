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

    public LoyaltyPolicy Loyalty { get; init; } = LoyaltyPolicy.Default;
}
