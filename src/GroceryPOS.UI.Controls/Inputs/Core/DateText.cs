using System.Globalization;

namespace GroceryPOS.UI.Controls.Inputs.Core;

/// <summary>
/// Vietnamese date entry (dd/MM/yyyy) with keyboard shortcuts for fast typing. Pure, unit-tested on Linux.
/// </summary>
public static class DateText
{
    public const string DisplayFormat = "dd/MM/yyyy";

    public static string Format(DateOnly date) => date.ToString(DisplayFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Accepts "19/10/2026", "19-10-2026", "19.10.26", "19/10" (this year), "19" (this month),
    /// "19102026", "191026", "hôm nay"/"nay", "hôm qua"/"qua", "mai", and "+3"/"-7" days from today.
    /// </summary>
    public static bool TryParse(string? text, DateOnly today, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = TextMatch.Fold(text).Trim();
        switch (s)
        {
            case "hom nay" or "nay" or "today":
                date = today;
                return true;
            case "hom qua" or "qua":
                date = today.AddDays(-1);
                return true;
            case "ngay mai" or "mai":
                date = today.AddDays(1);
                return true;
        }

        if (s.Length > 1 && s[0] is '+' or '-' && int.TryParse(s.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
        {
            if (offset > 3660)
            {
                return false;
            }

            date = today.AddDays(s[0] == '+' ? offset : -offset);
            return true;
        }

        int day, month, year;
        var parts = s.Split(['/', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && parts[0].All(char.IsAsciiDigit) && parts[0].Length is 6 or 8)
        {
            var digits = parts[0];
            day = int.Parse(digits.AsSpan(0, 2), CultureInfo.InvariantCulture);
            month = int.Parse(digits.AsSpan(2, 2), CultureInfo.InvariantCulture);
            year = ExpandYear(int.Parse(digits.AsSpan(4), CultureInfo.InvariantCulture), digits.Length - 4);
        }
        else if (parts.Length is >= 1 and <= 3 && parts.All(p => p.Length <= 4 && p.All(char.IsAsciiDigit)))
        {
            day = int.Parse(parts[0], CultureInfo.InvariantCulture);
            month = parts.Length >= 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : today.Month;
            year = parts.Length == 3 ? ExpandYear(int.Parse(parts[2], CultureInfo.InvariantCulture), parts[2].Length) : today.Year;
            if (parts[0].Length > 2 || (parts.Length >= 2 && parts[1].Length > 2) || (parts.Length == 3 && parts[2].Length is 3))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        if (year is < 1900 or > 9998 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    /// <summary>Two-digit years mean 20yy ("26" → 2026).</summary>
    private static int ExpandYear(int year, int typedDigits) => typedDigits <= 2 ? 2000 + year : year;
}
