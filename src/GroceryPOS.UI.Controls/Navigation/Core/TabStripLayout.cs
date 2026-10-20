namespace GroceryPOS.UI.Controls.Navigation.Core;

/// <summary>Which tabs of a strip are on screen and where.</summary>
/// <param name="First">Index of the first visible tab.</param>
/// <param name="Count">Number of visible tabs.</param>
/// <param name="Overflow">True when not every tab fits, so scroll arrows are shown.</param>
/// <param name="Offsets">X offset of each visible tab, relative to the start of the strip.</param>
public sealed record TabStripArrangement(int First, int Count, bool Overflow, IReadOnlyList<int> Offsets)
{
    public static TabStripArrangement Empty { get; } = new(0, 0, false, Array.Empty<int>());

    public bool CanScrollBack => First > 0;

    public int TabCount { get; init; }

    public bool CanScrollForward => First + Count < TabCount;

    public bool IsVisible(int index) => index >= First && index < First + Count;
}

/// <summary>
/// Lays out tab headers in one row. When they do not fit, the strip scrolls tab by tab: the selected tab is always
/// fully visible and no space is wasted at the end.
/// </summary>
public static class TabStripLayout
{
    /// <param name="widths">Measured width of each tab header.</param>
    /// <param name="available">Width of the strip.</param>
    /// <param name="selected">Selected tab (-1 for none).</param>
    /// <param name="first">First visible tab requested (current scroll position).</param>
    /// <param name="arrowsWidth">Width taken by the scroll arrows when the strip overflows.</param>
    public static TabStripArrangement Arrange(IReadOnlyList<int> widths, int available, int selected, int first, int arrowsWidth)
    {
        ArgumentNullException.ThrowIfNull(widths);
        var n = widths.Count;
        if (n == 0)
        {
            return TabStripArrangement.Empty;
        }

        if (widths.Any(w => w < 0))
        {
            throw new ArgumentException("Độ rộng tab không được âm.", nameof(widths));
        }

        available = Math.Max(0, available);
        if (widths.Sum() <= available)
        {
            return Build(widths, 0, n, overflow: false);
        }

        var viewport = Math.Max(0, available - Math.Max(0, arrowsWidth));
        first = Math.Clamp(first, 0, n - 1);
        if (selected >= 0 && selected < n && selected < first)
        {
            first = selected;
        }

        var count = CountFitting(widths, first, viewport);

        // Scroll forward until the selected tab is fully on screen.
        while (selected >= first + count && selected < n && first < selected)
        {
            first++;
            count = CountFitting(widths, first, viewport);
        }

        // Pull earlier tabs in while they fit in the space left at the end.
        while (first > 0 && Used(widths, first, count) + widths[first - 1] <= viewport)
        {
            first--;
            count++;
        }

        return Build(widths, first, count, overflow: true);
    }

    private static int CountFitting(IReadOnlyList<int> widths, int first, int viewport)
    {
        var used = 0;
        var count = 0;
        for (var i = first; i < widths.Count; i++)
        {
            if (used + widths[i] > viewport && count > 0)
            {
                break;
            }

            used += widths[i];
            count++;
        }

        return count;
    }

    private static int Used(IReadOnlyList<int> widths, int first, int count)
    {
        var used = 0;
        for (var i = first; i < first + count; i++)
        {
            used += widths[i];
        }

        return used;
    }

    private static TabStripArrangement Build(IReadOnlyList<int> widths, int first, int count, bool overflow)
    {
        var offsets = new int[count];
        var x = 0;
        for (var i = 0; i < count; i++)
        {
            offsets[i] = x;
            x += widths[first + i];
        }

        return new TabStripArrangement(first, count, overflow, offsets) { TabCount = widths.Count };
    }
}
