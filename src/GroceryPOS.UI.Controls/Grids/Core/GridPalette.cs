using System.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Grids.Core;

public readonly record struct GridColors(
    Color Background,
    Color Row,
    Color AlternateRow,
    Color Text,
    Color HeaderBackground,
    Color HeaderText,
    Color GridLine,
    Color SelectionBackground,
    Color SelectionText,
    Color HoverRow,
    Color EmptyText);

/// <summary>Grid colors derived from the theme tokens. Selection is a tint of the brand color so text keeps its contrast.</summary>
public static class GridPalette
{
    public static GridColors Resolve(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var c = theme.Colors;
        var selection = ColorMath.Blend(c.Surface, c.Primary, theme.IsDark ? 0.30 : 0.16);
        return new GridColors(
            Background: c.Surface,
            Row: c.Surface,
            AlternateRow: ColorMath.Blend(c.Surface, c.SurfaceAlt, 0.5),
            Text: c.TextPrimary,
            HeaderBackground: c.SurfaceAlt,
            HeaderText: c.TextSecondary,
            GridLine: ColorMath.Blend(c.Surface, c.Border, 0.6),
            SelectionBackground: selection,
            SelectionText: c.TextPrimary,
            HoverRow: c.SurfaceAlt,
            EmptyText: c.TextMuted);
    }
}
