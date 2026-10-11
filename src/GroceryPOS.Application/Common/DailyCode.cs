using System.Globalization;

namespace GroceryPOS.Application.Common;

/// <summary>
/// Daily sequential document codes: <c>{prefix}{yyMMdd}-{seq:0000}</c>, e.g. <c>HD261009-0001</c>, <c>PN261011-0003</c>.
/// The date is the store's local date; uniqueness is finally enforced by a unique index on the code column.
/// </summary>
public static class DailyCode
{
    /// <summary>Prefix for all codes of one local day, e.g. "HD261009-".</summary>
    public static string PrefixFor(string documentPrefix, DateTimeOffset localNow) =>
        $"{documentPrefix.Trim().ToUpperInvariant()}{localNow.ToString("yyMMdd", CultureInfo.InvariantCulture)}-";

    /// <summary>Next code after <paramref name="lastCode"/> (the highest existing code with <paramref name="dayPrefix"/>, or null).</summary>
    public static string Next(string dayPrefix, string? lastCode)
    {
        var next = 1;
        if (lastCode is not null
            && lastCode.StartsWith(dayPrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(lastCode.AsSpan(dayPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var seq))
        {
            next = seq + 1;
        }

        return dayPrefix + next.ToString("0000", CultureInfo.InvariantCulture);
    }
}
