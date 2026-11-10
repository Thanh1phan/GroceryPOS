namespace GroceryPOS.UI.Controls.Inputs.Core;

/// <summary>
/// Lays out the segments of a segmented control across its width: equal widths when every label fits,
/// otherwise widths proportional to the labels. Segments always fill the whole width without gaps.
/// </summary>
public static class SegmentLayout
{
    /// <param name="textWidths">Measured width of each label.</param>
    /// <param name="width">Inner width of the control.</param>
    /// <param name="padding">Horizontal padding on each side of a label.</param>
    /// <returns>Start offset and width of each segment.</returns>
    public static IReadOnlyList<(int X, int Width)> Arrange(IReadOnlyList<int> textWidths, int width, int padding)
    {
        ArgumentNullException.ThrowIfNull(textWidths);
        var n = textWidths.Count;
        if (n == 0)
        {
            return Array.Empty<(int, int)>();
        }

        if (textWidths.Any(w => w < 0))
        {
            throw new ArgumentException("Độ rộng nhãn không được âm.", nameof(textWidths));
        }

        width = Math.Max(0, width);
        padding = Math.Max(0, padding);
        var needed = textWidths.Select(w => w + (2 * padding)).ToArray();
        var equal = width / n;
        double[] shares = needed.All(w => w <= equal) || needed.Sum() == 0
            ? Enumerable.Repeat(1.0 / n, n).ToArray()
            : needed.Select(w => (double)w / needed.Sum()).ToArray();

        var result = new (int X, int Width)[n];
        var x = 0;
        var cumulative = 0.0;
        for (var i = 0; i < n; i++)
        {
            cumulative += shares[i];
            var end = i == n - 1 ? width : (int)Math.Round(cumulative * width, MidpointRounding.AwayFromZero);
            result[i] = (x, Math.Max(0, end - x));
            x = end;
        }

        return result;
    }

    /// <summary>Width that shows every label at equal segment width.</summary>
    public static int PreferredWidth(IReadOnlyList<int> textWidths, int padding)
    {
        ArgumentNullException.ThrowIfNull(textWidths);
        return textWidths.Count == 0 ? 0 : textWidths.Count * (textWidths.Max() + (2 * Math.Max(0, padding)));
    }

    /// <summary>Index of the segment under <paramref name="x"/>, or -1 outside every segment.</summary>
    public static int HitTest(IReadOnlyList<(int X, int Width)> segments, int x)
    {
        ArgumentNullException.ThrowIfNull(segments);
        for (var i = 0; i < segments.Count; i++)
        {
            if (x >= segments[i].X && x < segments[i].X + segments[i].Width)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Keyboard step: moves by <paramref name="delta"/> and stops at the ends (no wrap-around).</summary>
    public static int Step(int selected, int delta, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        return selected < 0 ? (delta < 0 ? count - 1 : 0) : Math.Clamp(selected + delta, 0, count - 1);
    }
}
