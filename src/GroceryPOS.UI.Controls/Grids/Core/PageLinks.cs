namespace GroceryPOS.UI.Controls.Grids.Core;

/// <summary>
/// Page numbers for the pager: always the first and last page, a window around the current one, and gaps
/// (null) where pages are skipped, e.g. 1 … 4 5 [6] 7 8 … 20.
/// </summary>
public static class PageLinks
{
    /// <param name="pageIndex">Zero-based current page.</param>
    /// <param name="pageCount">Total pages (at least 1).</param>
    /// <param name="maxSlots">Slots available including gaps; at least 5.</param>
    /// <returns>Zero-based page indexes, null for a gap ("…").</returns>
    public static IReadOnlyList<int?> Build(int pageIndex, int pageCount, int maxSlots = 7)
    {
        if (pageCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount), pageCount, "Phải có ít nhất 1 trang.");
        }

        maxSlots = Math.Max(5, maxSlots);
        pageIndex = Math.Clamp(pageIndex, 0, pageCount - 1);

        if (pageCount <= maxSlots)
        {
            return Enumerable.Range(0, pageCount).Select(i => (int?)i).ToList();
        }

        // First + last + up to two gaps leave this many slots for the window around the current page.
        var window = maxSlots - 4;
        var start = pageIndex - ((window - 1) / 2);
        var end = start + window - 1;

        // Near an edge the gap disappears, so the window can grow by that slot.
        if (start <= 2)
        {
            start = 1;
            end = maxSlots - 3;
        }
        else if (end >= pageCount - 3)
        {
            end = pageCount - 2;
            start = pageCount - maxSlots + 2;
        }

        var links = new List<int?>(maxSlots) { 0 };
        if (start > 1)
        {
            links.Add(null);
        }

        for (var i = start; i <= end; i++)
        {
            links.Add(i);
        }

        if (end < pageCount - 2)
        {
            links.Add(null);
        }

        links.Add(pageCount - 1);
        return links;
    }
}
