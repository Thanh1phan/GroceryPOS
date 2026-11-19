namespace GroceryPOS.UI.Controls.Display.Core;

/// <summary>Where each card of a <c>StatCardRow</c> goes and how tall the row is.</summary>
/// <param name="Columns">Cards per row.</param>
/// <param name="Rows">Number of rows.</param>
/// <param name="Bounds">Bounds of each card, relative to the content area.</param>
/// <param name="Height">Height of the content area needed for every card.</param>
public sealed record CardGridArrangement(int Columns, int Rows, IReadOnlyList<CardBounds> Bounds, int Height)
{
    public static CardGridArrangement Empty { get; } = new(0, 0, Array.Empty<CardBounds>(), 0);
}

public readonly record struct CardBounds(int X, int Y, int Width, int Height);

/// <summary>
/// Responsive KPI-card grid: every card is shown, equally wide, filling the width. When the cards would get narrower
/// than the minimum they wrap onto more rows, balanced (5 cards → 3 + 2, not 4 + 1), and the row grows taller
/// instead of cutting cards off.
/// </summary>
public static class CardGridLayout
{
    public static CardGridArrangement Arrange(int count, int available, int minCardWidth, int cardHeight, int gap)
    {
        if (count <= 0)
        {
            return CardGridArrangement.Empty;
        }

        available = Math.Max(0, available);
        minCardWidth = Math.Max(1, minCardWidth);
        cardHeight = Math.Max(0, cardHeight);
        gap = Math.Max(0, gap);

        var fitting = Math.Clamp((available + gap) / (minCardWidth + gap), 1, count);
        var rows = (count + fitting - 1) / fitting;
        var columns = (count + rows - 1) / rows; // balance the rows
        var usable = Math.Max(0, available - (gap * (columns - 1)));
        var baseWidth = usable / columns;
        var remainder = usable - (baseWidth * columns);

        var bounds = new CardBounds[count];
        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var column = i % columns;

            // Spread the leftover pixels over the first columns so the row ends exactly at the right edge.
            var x = (column * (baseWidth + gap)) + Math.Min(column, remainder);
            var width = baseWidth + (column < remainder ? 1 : 0);
            bounds[i] = new CardBounds(x, row * (cardHeight + gap), width, cardHeight);
        }

        return new CardGridArrangement(columns, rows, bounds, (rows * cardHeight) + ((rows - 1) * gap));
    }
}

/// <summary>Picks the largest font size whose text fits (big KPI values shrink instead of being cut with "…").</summary>
public static class FitText
{
    /// <param name="candidates">Font sizes from largest to smallest.</param>
    /// <param name="measure">Width of the text at a size.</param>
    /// <param name="available">Width available.</param>
    /// <returns>The first size that fits, or the smallest one.</returns>
    public static float Choose(IReadOnlyList<float> candidates, Func<float, int> measure, int available)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(measure);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("Cần ít nhất một cỡ chữ.", nameof(candidates));
        }

        foreach (var size in candidates)
        {
            if (measure(size) <= available)
            {
                return size;
            }
        }

        return candidates[^1];
    }
}
