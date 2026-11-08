using System.Drawing;

namespace GroceryPOS.UI.Controls.Forms.Core;

/// <summary>One field of an edit dialog: an optional caption above an input.</summary>
/// <param name="FieldHeight">Height of the input itself (including its validation line, if it reserves one).</param>
/// <param name="HasLabel">False for a field without a caption (a check box, a note).</param>
/// <param name="FullWidth">True when the field spans every column (name, address, description).</param>
/// <param name="StartsRow">True to start a new row even when the current one has room left.</param>
public readonly record struct FieldSlot(int FieldHeight, bool HasLabel = true, bool FullWidth = false, bool StartsRow = false);

/// <summary>Where a field ended up: caption bounds (empty when it has none), input bounds, row and column.</summary>
public readonly record struct FieldPlacement(Rectangle Label, Rectangle Field, int Row, int Column);

/// <summary>Spacing used by <see cref="FieldGrid"/>, in pixels.</summary>
/// <param name="ColumnGap">Space between two columns.</param>
/// <param name="LabelHeight">Height of a caption line.</param>
/// <param name="LabelGap">Space between a caption and its input.</param>
/// <param name="RowGap">Space between two rows.</param>
public readonly record struct FieldGridMetrics(int ColumnGap = 16, int LabelHeight = 20, int LabelGap = 4, int RowGap = 14)
{
    public static FieldGridMetrics Default => new(16, 20, 4, 14);
}

/// <summary>Result of <see cref="FieldGrid.Arrange"/>.</summary>
public sealed record FieldGridLayout(IReadOnlyList<FieldPlacement> Placements, int Bottom, int Rows);

/// <summary>
/// Lays out edit-dialog fields in a 1–3 column grid: fields flow left to right, a full-width field takes a row
/// of its own, and every row is as tall as its tallest caption + input so the inputs of a row line up even
/// when only some of them have a caption. Pure geometry, unit-tested; <c>BaseEditDialog</c> applies it.
/// </summary>
public static class FieldGrid
{
    public static FieldGridLayout Arrange(IReadOnlyList<FieldSlot> slots, Rectangle area, int columns, FieldGridMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (columns is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "Số cột phải từ 1 đến 3.");
        }

        var columnWidth = Math.Max(0, (area.Width - (metrics.ColumnGap * (columns - 1))) / columns);

        // First pass: assign row/column to every field.
        var cells = new (int Row, int Column, int Span)[slots.Count];
        int row = 0, column = 0;
        var rowUsed = false;
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var span = slot.FullWidth ? columns : 1;
            if (rowUsed && (slot.StartsRow || slot.FullWidth || column + span > columns))
            {
                row++;
                column = 0;
            }

            cells[i] = (row, column, span);
            column += span;
            rowUsed = true;
            if (column >= columns)
            {
                row++;
                column = 0;
                rowUsed = false;
            }
        }

        var rowCount = slots.Count == 0 ? 0 : (rowUsed ? row + 1 : row);

        // Second pass: row heights (tallest input) and whether a row has any caption.
        var rowFieldHeight = new int[rowCount];
        var rowHasLabel = new bool[rowCount];
        for (var i = 0; i < slots.Count; i++)
        {
            var r = cells[i].Row;
            rowFieldHeight[r] = Math.Max(rowFieldHeight[r], Math.Max(0, slots[i].FieldHeight));
            rowHasLabel[r] |= slots[i].HasLabel;
        }

        var rowTop = new int[rowCount];
        var y = area.Top;
        for (var r = 0; r < rowCount; r++)
        {
            rowTop[r] = y;
            var captionBlock = rowHasLabel[r] ? metrics.LabelHeight + metrics.LabelGap : 0;
            y += captionBlock + rowFieldHeight[r];
            if (r < rowCount - 1)
            {
                y += metrics.RowGap;
            }
        }

        var placements = new FieldPlacement[slots.Count];
        for (var i = 0; i < slots.Count; i++)
        {
            var (r, c, span) = cells[i];
            var x = area.Left + c * (columnWidth + metrics.ColumnGap);
            var width = span * columnWidth + (span - 1) * metrics.ColumnGap;
            if (c + span == columns)
            {
                // The last column absorbs the rounding remainder so right edges line up.
                width = area.Right - x;
            }

            var captionBlock = rowHasLabel[r] ? metrics.LabelHeight + metrics.LabelGap : 0;
            var label = slots[i].HasLabel ? new Rectangle(x, rowTop[r], width, metrics.LabelHeight) : Rectangle.Empty;
            var field = new Rectangle(x, rowTop[r] + captionBlock, width, Math.Max(0, slots[i].FieldHeight));
            placements[i] = new FieldPlacement(label, field, r, c);
        }

        return new FieldGridLayout(placements, rowCount == 0 ? area.Top : y, rowCount);
    }
}
