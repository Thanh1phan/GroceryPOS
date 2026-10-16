using System.Globalization;
using System.Text;

namespace GroceryPOS.Application.Common;

/// <summary>
/// Accent- and case-insensitive text matching for Vietnamese search boxes:
/// "sua tuoi" finds "Sữa tươi", "DUONG" finds "Đường".
/// </summary>
public static class SearchText
{
    /// <summary>Lower-cases, removes diacritics (đ → d) and collapses whitespace.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            builder.Append(c switch
            {
                'đ' or 'Đ' => 'd',
                _ => char.ToLowerInvariant(c),
            });
        }

        return builder.ToString();
    }

    /// <summary>True when every word of <paramref name="term"/> occurs in one of the <paramref name="fields"/>.</summary>
    public static bool Matches(string? term, params string?[] fields)
    {
        var words = Fold(term).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        var haystack = string.Join(' ', fields.Select(Fold));
        return words.All(w => haystack.Contains(w, StringComparison.Ordinal));
    }
}
