using System.Globalization;
using System.Text;

namespace GroceryPOS.UI.Controls.Inputs.Core;

/// <summary>
/// Accent- and case-insensitive matching for search-as-you-type lists: "sua" finds "Sữa tươi", "duong" finds "Đường".
/// </summary>
public static class TextMatch
{
    /// <summary>Lower-case, without Vietnamese diacritics (đ → d), single spaces.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            builder.Append(ch switch
            {
                'đ' or 'Đ' => 'd',
                _ => char.ToLowerInvariant(ch),
            });
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>True when every word of <paramref name="query"/> occurs in <paramref name="text"/> (any order). An empty query matches everything.</summary>
    public static bool Matches(string? text, string? query)
    {
        var folded = Fold(text);
        foreach (var word in Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!folded.Contains(word, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Ranking for matches: 0 = equal, 1 = starts with the query, 2 = contains it.</summary>
    public static int Rank(string? text, string? query)
    {
        var folded = Fold(text);
        var q = Fold(query);
        return folded == q ? 0 : folded.StartsWith(q, StringComparison.Ordinal) ? 1 : 2;
    }
}
