namespace GroceryPOS.UI.Controls.Buttons.Core;

/// <summary>Width a button needs for its icon and text, matching how <c>ModernButton</c> paints them.</summary>
public static class ButtonMetrics
{
    /// <param name="iconWidth">Measured icon width (0 when there is no icon).</param>
    /// <param name="textWidth">Measured text width (0 when there is no text).</param>
    /// <param name="padding">Horizontal padding on each side.</param>
    /// <param name="gap">Space between icon and text (only used when both are present).</param>
    /// <param name="slack">Extra pixels against rounding differences between measuring and drawing.</param>
    public static int ContentWidth(int iconWidth, int textWidth, int padding, int gap, int slack = 2)
    {
        iconWidth = Math.Max(0, iconWidth);
        textWidth = Math.Max(0, textWidth);
        var between = iconWidth > 0 && textWidth > 0 ? Math.Max(0, gap) : 0;
        return iconWidth + between + textWidth + (2 * Math.Max(0, padding)) + Math.Max(0, slack);
    }
}
