using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Drawing;

internal static class SurfaceColors
{
    /// <summary>First opaque background up the parent chain, or the theme background.</summary>
    public static Color EffectiveBackColor(Control control, Theme theme)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.BackColor.A == 255)
            {
                return parent.BackColor;
            }
        }

        return theme.Colors.Background;
    }
}
