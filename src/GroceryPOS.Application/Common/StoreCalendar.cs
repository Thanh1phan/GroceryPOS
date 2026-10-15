namespace GroceryPOS.Application.Common;

/// <summary>
/// Converts between UTC instants and the store's calendar days. Vietnam is UTC+7 all year,
/// so a store day is a fixed 24-hour window and no DST handling is needed.
/// </summary>
public static class StoreCalendar
{
    /// <summary>The store-local date of an instant.</summary>
    public static DateOnly DateOf(DateTimeOffset instant, TimeSpan storeOffset) =>
        DateOnly.FromDateTime(instant.ToOffset(storeOffset).DateTime);

    /// <summary>Midnight (store time) at the start of <paramref name="date"/>.</summary>
    public static DateTimeOffset StartOf(DateOnly date, TimeSpan storeOffset) =>
        new(date.ToDateTime(TimeOnly.MinValue), storeOffset);

    /// <summary>Store-local hour (0–23) of an instant.</summary>
    public static int HourOf(DateTimeOffset instant, TimeSpan storeOffset) => instant.ToOffset(storeOffset).Hour;
}
