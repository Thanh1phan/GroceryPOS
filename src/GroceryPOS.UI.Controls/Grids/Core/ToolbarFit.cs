namespace GroceryPOS.UI.Controls.Grids.Core;

/// <summary>
/// Height of the grid toolbar: one row (<c>minimum</c>) unless the buttons wrapped onto more rows because the grid is
/// narrower than the buttons – then tall enough for the lowest button, so no button is cut off or hidden.
/// </summary>
public static class ToolbarFit
{
    /// <param name="minimum">Height of a single-row toolbar (search box / one row of buttons).</param>
    /// <param name="itemBottoms">Bottom edge of each visible button including its bottom margin, relative to the button area.</param>
    /// <param name="verticalPadding">Toolbar padding above and below the button area.</param>
    public static int Height(int minimum, IEnumerable<int> itemBottoms, int verticalPadding)
    {
        ArgumentNullException.ThrowIfNull(itemBottoms);
        var lowest = 0;
        foreach (var bottom in itemBottoms)
        {
            lowest = Math.Max(lowest, bottom);
        }

        return lowest == 0 ? Math.Max(0, minimum) : Math.Max(minimum, lowest + Math.Max(0, verticalPadding));
    }
}
