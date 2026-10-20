using System.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Display.Core;

public readonly record struct CardColors(Color Background, Color Border, Color Title, Color Subtitle, Color Shadow);

public readonly record struct TopBarColors(Color Background, Color Border, Color Title, Color Subtitle, Color ChipHover);

public readonly record struct TabColors(Color Background, Color Text, Color SelectedText, Color HoverBackground, Color Indicator, Color Divider, Color DisabledText);

/// <summary>Colors of cards, the top bar and tab strips. Pure, so contrast is unit-tested.</summary>
public static class SurfacePalette
{
    public static CardColors Card(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var c = theme.Colors;
        var shadow = theme.IsDark ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(22, 15, 23, 42);
        return new CardColors(c.Surface, ColorMath.Blend(c.Surface, c.Border, theme.IsDark ? 0.8 : 0.7), c.TextPrimary, c.TextSecondary, shadow);
    }

    public static TopBarColors TopBar(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var c = theme.Colors;
        return new TopBarColors(c.Surface, ColorMath.Blend(c.Surface, c.Border, 0.7), c.TextPrimary, c.TextSecondary, c.SurfaceAlt);
    }

    /// <param name="background">Color behind the strip (the tab control's parent surface).</param>
    public static TabColors Tabs(Theme theme, Color background)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var c = theme.Colors;
        var hover = ColorMath.Blend(background, c.SurfaceAlt, theme.IsDark ? 0.9 : 1.0);
        if (hover.ToArgb() == background.ToArgb())
        {
            // On a SurfaceAlt page the hover would be invisible; step it a little further.
            hover = theme.Hover(background);
        }

        return new TabColors(
            background,
            ColorMath.EnsureContrast(c.TextSecondary, hover),
            ColorMath.EnsureContrast(c.Primary, background),
            hover,
            c.Primary,
            ColorMath.Blend(background, c.Border, 0.7),
            c.DisabledText);
    }
}
