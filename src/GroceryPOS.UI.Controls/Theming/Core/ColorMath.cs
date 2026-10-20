using System.Drawing;
using System.Globalization;

namespace GroceryPOS.UI.Controls.Theming;

/// <summary>
/// Small, platform-independent color helpers used by the theme (hover/pressed shades, contrast checks).
/// </summary>
public static class ColorMath
{
    /// <summary>Parses <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    public static Color FromHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        var value = hex.Trim().TrimStart('#');
        if (value.Length is not (6 or 8) ||
            !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            throw new FormatException($"'{hex}' is not a #RRGGBB or #AARRGGBB color.");
        }

        if (value.Length == 6)
        {
            argb |= 0xFF000000;
        }

        return Color.FromArgb(unchecked((int)argb));
    }

    /// <summary>Formats as <c>#RRGGBB</c> (alpha is ignored).</summary>
    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Linear blend: <paramref name="amount"/> 0 returns <paramref name="from"/>, 1 returns <paramref name="to"/>.</summary>
    public static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        static int Mix(int a, int b, double t) => (int)Math.Round(a + ((b - a) * t), MidpointRounding.AwayFromZero);
        return Color.FromArgb(
            Mix(from.A, to.A, amount),
            Mix(from.R, to.R, amount),
            Mix(from.G, to.G, amount),
            Mix(from.B, to.B, amount));
    }

    public static Color Lighten(Color color, double amount) => Blend(color, Color.FromArgb(color.A, 255, 255, 255), amount);

    public static Color Darken(Color color, double amount) => Blend(color, Color.FromArgb(color.A, 0, 0, 0), amount);

    public static Color WithAlpha(Color color, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), color);

    /// <summary>WCAG 2.x relative luminance (0 = black, 1 = white).</summary>
    public static double RelativeLuminance(Color color)
    {
        static double Channel(byte c)
        {
            var s = c / 255d;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    /// <summary>WCAG contrast ratio between two opaque colors (1 – 21). Body text needs at least 4.5.</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (light, dark) = la >= lb ? (la, lb) : (lb, la);
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>Picks whichever of the two candidates reads better on <paramref name="background"/>.</summary>
    public static Color BestContrast(Color background, Color first, Color second) =>
        ContrastRatio(background, first) >= ContrastRatio(background, second) ? first : second;

    /// <summary>
    /// Returns <paramref name="foreground"/> unchanged when it already reaches <paramref name="minimum"/> contrast on
    /// <paramref name="background"/>; otherwise moves it towards black (light backgrounds) or white (dark ones) in
    /// small steps until it does, so tinted text keeps its hue.
    /// </summary>
    public static Color EnsureContrast(Color foreground, Color background, double minimum = 4.5)
    {
        if (ContrastRatio(foreground, background) >= minimum)
        {
            return foreground;
        }

        var towardsDark = RelativeLuminance(background) >= 0.18;
        for (var step = 1; step <= 20; step++)
        {
            var amount = step * 0.05;
            var candidate = towardsDark ? Darken(foreground, amount) : Lighten(foreground, amount);
            if (ContrastRatio(candidate, background) >= minimum)
            {
                return candidate;
            }
        }

        return BestContrast(background, Color.Black, Color.White);
    }
}
