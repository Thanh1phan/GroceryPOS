namespace GroceryPOS.Domain.Catalog;

/// <summary>
/// Barcode helpers. Retail barcodes (EAN-8, EAN-13, UPC-A) carry a check digit that is validated;
/// other numeric/alphanumeric codes are accepted as internal store codes.
/// </summary>
public static class Barcode
{
    public const int MaxLength = 32;

    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>True if the code has the shape of an EAN-8/UPC-A/EAN-13 (8, 12 or 13 digits).</summary>
    public static bool LooksLikeGtin(string code) =>
        code.Length is 8 or 12 or 13 && code.All(char.IsAsciiDigit);

    /// <summary>Validates the GTIN check digit (mod-10, weights 3/1 from the right).</summary>
    public static bool HasValidCheckDigit(string code)
    {
        if (!LooksLikeGtin(code))
        {
            return false;
        }

        var sum = 0;
        var weight = 3;
        for (var i = code.Length - 2; i >= 0; i--)
        {
            sum += (code[i] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }

        var check = (10 - sum % 10) % 10;
        return check == code[^1] - '0';
    }

    /// <summary>Acceptable barcode: GTIN-shaped codes must have a valid check digit; others just need sane characters.</summary>
    public static bool IsValid(string code)
    {
        if (code.Length is 0 or > MaxLength)
        {
            return false;
        }

        if (LooksLikeGtin(code))
        {
            return HasValidCheckDigit(code);
        }

        return code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.');
    }
}
