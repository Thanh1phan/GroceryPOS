using System.Drawing;
using System.Globalization;
using System.Text;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Display.Core;

/// <summary>Initials and a stable color for user avatars (top bar, user lists).</summary>
public static class Avatar
{
    /// <summary>
    /// Avatar colors; white text reaches 4.5:1 on each of them, so the avatar reads the same in both themes.
    /// </summary>
    public static IReadOnlyList<Color> Palette { get; } = new[]
    {
        "#15803D", "#1D4ED8", "#7C3AED", "#BE185D", "#B45309", "#0F766E", "#4338CA", "#B91C1C",
    }.Select(ColorMath.FromHex).ToArray();

    public static Color Foreground => Color.White;

    /// <summary>
    /// Initials of a Vietnamese full name: family name + given name ("Nguyễn Văn An" → "NA", "Đỗ Hương" → "ĐH");
    /// a single word gives its first two letters ("admin" → "AD"); empty input gives "?".
    /// Accents on the letters are kept, the result is upper-case.
    /// </summary>
    public static string Initials(string? name)
    {
        var words = (name ?? string.Empty)
            .Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0)
            .ToArray();

        if (words.Length == 0)
        {
            return "?";
        }

        var vietnamese = CultureInfo.GetCultureInfo("vi-VN");
        if (words.Length == 1)
        {
            var elements = TextElements(words[0]).Take(2);
            return string.Concat(elements).ToUpper(vietnamese);
        }

        return (TextElements(words[0]).First() + TextElements(words[^1]).First()).ToUpper(vietnamese);
    }

    /// <summary>Stable color for a user name or username (same input → same color on every run and machine).</summary>
    public static Color ColorFor(string? key)
    {
        var text = (key ?? string.Empty).Trim().ToUpperInvariant();
        uint hash = 2166136261;
        foreach (var ch in text)
        {
            hash = (hash ^ ch) * 16777619;
        }

        return Palette[(int)(hash % (uint)Palette.Count)];
    }

    private static IEnumerable<string> TextElements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            yield return enumerator.GetTextElement();
        }
    }
}
