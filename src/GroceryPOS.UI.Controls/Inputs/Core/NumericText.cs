using System.Globalization;
using System.Text;

namespace GroceryPOS.UI.Controls.Inputs.Core;

/// <summary>
/// Formatting, parsing and live digit grouping for numeric inputs. Pure functions, unit-tested on Linux.
/// </summary>
public static class NumericText
{
    public const char GroupSeparator = '.';
    public const char DecimalSeparator = ',';

    private static readonly NumberFormatInfo Vietnamese = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NegativeSign = "-",
        NumberGroupSizes = [3],
    };

    /// <summary>Formats with vi-VN grouping, e.g. 1250000 → "1.250.000", 1.5 (3 decimals) → "1,5".</summary>
    public static string Format(decimal value, NumericFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var rounded = Round(value, format);
        var pattern = format.DecimalPlaces == 0 ? "#,0" : "#,0." + new string('#', format.DecimalPlaces);
        return rounded.ToString(pattern, Vietnamese);
    }

    /// <summary>
    /// Parses what a user typed: "1.250.000", "1250000", "1.250.000 ₫", "12,5", "25k" (when allowed).
    /// A single dot followed by other than three digits is read as a decimal point ("1.5" kg) when decimals are allowed.
    /// Does not check the range – see <see cref="Clamp"/> and <see cref="IsInRange"/>.
    /// </summary>
    public static bool TryParse(string? text, NumericFormat format, out decimal value)
    {
        ArgumentNullException.ThrowIfNull(format);
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch) && ch is not ('₫' or 'đ' or 'Đ'))
            {
                cleaned.Append(ch);
            }
        }

        var s = cleaned.ToString();
        if (s.EndsWith("VND", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^3];
        }

        var multiplier = 1m;
        if (s.Length > 0 && s[^1] is 'k' or 'K')
        {
            if (!format.AllowThousandsShortcut)
            {
                return false;
            }

            multiplier = 1000m;
            s = s[..^1];
        }

        s = NormalizeDecimalDot(s, format);
        if (s.Length == 0 || s == "-")
        {
            return false;
        }

        if (!decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, Vietnamese, out var parsed))
        {
            return false;
        }

        if (parsed < 0 && !format.AllowNegative)
        {
            return false;
        }

        try
        {
            value = Round(parsed * multiplier, format);
        }
        catch (OverflowException)
        {
            return false;
        }

        return true;
    }

    public static decimal Round(decimal value, NumericFormat format) =>
        Math.Round(value, format.DecimalPlaces, MidpointRounding.AwayFromZero);

    public static decimal Clamp(decimal value, NumericFormat format) =>
        Math.Min(format.Maximum, Math.Max(format.Minimum, Round(value, format)));

    public static bool IsInRange(decimal value, NumericFormat format) =>
        value >= format.Minimum && value <= format.Maximum;

    /// <summary>Arrow-key stepping: adds <paramref name="increment"/> × <paramref name="direction"/> and clamps. Empty starts from the minimum (or 0).</summary>
    public static decimal Step(decimal? current, decimal increment, int direction, NumericFormat format)
    {
        var start = current ?? Clamp(0, format);
        return Clamp(start + (increment * Math.Sign(direction)), format);
    }

    /// <summary>True for characters a user may type into the box (control keys are always allowed).</summary>
    public static bool IsAllowedChar(char ch, NumericFormat format) =>
        char.IsControl(ch)
        || char.IsAsciiDigit(ch)
        || ch == GroupSeparator
        || (ch == DecimalSeparator && format.DecimalPlaces > 0)
        || (ch == '-' && format.AllowNegative)
        || (ch is 'k' or 'K' && format.AllowThousandsShortcut);

    /// <summary>
    /// Re-inserts group separators while the user types and returns where the caret should go, so
    /// "1250000" becomes "1.250.000" without the caret jumping. Characters that are not part of a number are dropped;
    /// the decimal part is cut to <see cref="NumericFormat.DecimalPlaces"/>.
    /// </summary>
    public static (string Text, int Caret) Regroup(string text, int caret, NumericFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);

        // Count the meaningful characters (digits, sign, decimal comma) left of the caret.
        var meaningfulBeforeCaret = 0;
        for (var i = 0; i < caret; i++)
        {
            if (IsMeaningful(text[i], format))
            {
                meaningfulBeforeCaret++;
            }
        }

        var negative = format.AllowNegative && text.TrimStart().StartsWith('-');
        var integerDigits = new StringBuilder();
        var fractionDigits = new StringBuilder();
        var hasComma = false;
        foreach (var ch in text)
        {
            if (ch == DecimalSeparator && format.DecimalPlaces > 0 && !hasComma)
            {
                hasComma = true;
            }
            else if (char.IsAsciiDigit(ch))
            {
                (hasComma ? fractionDigits : integerDigits).Append(ch);
            }
        }

        // Leading zeros carry no meaning ("007" → "7"), but keep one before a comma ("0,5").
        var trimmedZeros = 0;
        while (integerDigits.Length > 1 && integerDigits[0] == '0')
        {
            integerDigits.Remove(0, 1);
            trimmedZeros++;
        }

        if (fractionDigits.Length > format.DecimalPlaces)
        {
            fractionDigits.Length = format.DecimalPlaces;
        }

        var result = new StringBuilder();
        if (negative)
        {
            result.Append('-');
        }

        var intPart = integerDigits.ToString();
        if (intPart.Length == 0 && hasComma)
        {
            intPart = "0";
        }

        for (var i = 0; i < intPart.Length; i++)
        {
            if (i > 0 && (intPart.Length - i) % 3 == 0)
            {
                result.Append(GroupSeparator);
            }

            result.Append(intPart[i]);
        }

        if (hasComma)
        {
            result.Append(DecimalSeparator).Append(fractionDigits);
        }

        var formatted = result.ToString();

        // Put the caret after the same number of meaningful characters (minus the zeros that were removed).
        var target = Math.Max(0, meaningfulBeforeCaret - trimmedZeros);
        var newCaret = 0;
        var seen = 0;
        while (newCaret < formatted.Length && seen < target)
        {
            if (IsMeaningful(formatted[newCaret], format))
            {
                seen++;
            }

            newCaret++;
        }

        return (formatted, Math.Min(newCaret, formatted.Length));
    }

    private static bool IsMeaningful(char ch, NumericFormat format) =>
        char.IsAsciiDigit(ch)
        || (ch == DecimalSeparator && format.DecimalPlaces > 0)
        || (ch == '-' && format.AllowNegative);

    /// <summary>"1.5" means 1,5 when decimals are allowed and the dot cannot be a thousands separator.</summary>
    private static string NormalizeDecimalDot(string s, NumericFormat format)
    {
        if (format.DecimalPlaces == 0 || s.Contains(DecimalSeparator))
        {
            return s;
        }

        var firstDot = s.IndexOf(GroupSeparator);
        if (firstDot < 0 || firstDot != s.LastIndexOf(GroupSeparator))
        {
            return s;
        }

        var digitsAfter = s.Length - firstDot - 1;
        return digitsAfter == 3 ? s : string.Concat(s.AsSpan(0, firstDot), DecimalSeparator.ToString(), s.AsSpan(firstDot + 1));
    }
}
