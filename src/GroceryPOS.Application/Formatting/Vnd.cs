using System.Globalization;

namespace GroceryPOS.Application.Formatting;

/// <summary>VND formatting/parsing using vi-VN conventions ("125.000 ₫").</summary>
public static class Vnd
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Formats an amount rounded to whole đồng, e.g. 125000 → "125.000 ₫".</summary>
    public static string Format(decimal amount) =>
        Round(amount).ToString("#,0", Culture) + " ₫";

    /// <summary>Formats without the currency symbol, e.g. for grid cells: "125.000".</summary>
    public static string FormatNumber(decimal amount) => Round(amount).ToString("#,0", Culture);

    /// <summary>VND has no minor unit in practice; round half away from zero.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Parses user input such as "125.000", "125000", "125.000 ₫" or "125k". Returns false for invalid or negative values.
    /// </summary>
    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = text.Replace("₫", string.Empty).Replace("đ", string.Empty).Replace("VND", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty).Replace(" ", string.Empty).Trim();

        var multiplier = 1m;
        if (cleaned.EndsWith('k') || cleaned.EndsWith('K'))
        {
            multiplier = 1000m;
            cleaned = cleaned[..^1];
        }

        if (!decimal.TryParse(cleaned, NumberStyles.Number, Culture, out var value) || value < 0)
        {
            return false;
        }

        amount = Round(value * multiplier);
        return true;
    }
}
