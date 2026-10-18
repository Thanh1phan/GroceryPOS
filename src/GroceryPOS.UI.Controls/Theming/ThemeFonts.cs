using System.Collections.Concurrent;
using System.Drawing.Text;

namespace GroceryPOS.UI.Controls.Theming;

/// <summary>
/// Caches the fonts described by the current theme's typography tokens. Fonts live for the whole
/// application, so callers must not dispose them.
/// </summary>
public static class ThemeFonts
{
    private static readonly ConcurrentDictionary<(string Family, float Size, FontStyle Style), Font> Cache = new();
    private static readonly Lazy<HashSet<string>> InstalledFamilies = new(LoadInstalledFamilies);

    public static Font Get(FontRole role, FontStyle style = FontStyle.Regular)
    {
        var typography = ThemeManager.Current.Typography;
        return GetFont(typography.FontFamily, typography.SizeOf(role), style);
    }

    /// <summary>Icon font (Segoe Fluent Icons, falling back to Segoe MDL2 Assets) at the given size in points.</summary>
    public static Font Icon(float size)
    {
        var typography = ThemeManager.Current.Typography;
        var family = InstalledFamilies.Value.Contains(typography.IconFontFamily)
            ? typography.IconFontFamily
            : typography.IconFontFallback;
        return GetFont(family, size, FontStyle.Regular);
    }

    private static Font GetFont(string family, float size, FontStyle style) =>
        Cache.GetOrAdd((family, size, style), key => new Font(key.Family, key.Size, key.Style, GraphicsUnit.Point));

    private static HashSet<string> LoadInstalledFamilies()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
