using GroceryPOS.UI.Controls.Grids.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Grids;

/// <summary>
/// The <see cref="DataGridView"/> inside <see cref="ModernDataGrid"/>: double-buffered, flat, read-only,
/// full-row selection, hover highlight and an empty-state message when there are no rows.
/// </summary>
internal sealed class GridTable : DataGridView
{
    private GridColors _colors = GridPalette.Resolve(ThemeManager.Current);
    private int _hoverRow = -1;

    public GridTable()
    {
        DoubleBuffered = true;
        AutoGenerateColumns = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToOrderColumns = true;
        ReadOnly = true;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        RowHeadersVisible = false;
        BorderStyle = BorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        EnableHeadersVisualStyles = false;
        StandardTab = true;
        ShowCellToolTips = true;
        DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
    }

    /// <summary>Shown in the middle of the grid when there are no rows.</summary>
    public string EmptyText { get; set; } = "Không có dữ liệu";

    public void ApplyColors(Theme theme)
    {
        _colors = GridPalette.Resolve(theme);
        BackgroundColor = _colors.Background;
        GridColor = _colors.GridLine;
        DefaultCellStyle.BackColor = _colors.Row;
        DefaultCellStyle.ForeColor = _colors.Text;
        DefaultCellStyle.SelectionBackColor = _colors.SelectionBackground;
        DefaultCellStyle.SelectionForeColor = _colors.SelectionText;
        DefaultCellStyle.Font = ThemeFonts.Get(FontRole.Body);
        AlternatingRowsDefaultCellStyle.BackColor = _colors.AlternateRow;
        ColumnHeadersDefaultCellStyle.BackColor = _colors.HeaderBackground;
        ColumnHeadersDefaultCellStyle.ForeColor = _colors.HeaderText;
        ColumnHeadersDefaultCellStyle.SelectionBackColor = _colors.HeaderBackground;
        ColumnHeadersDefaultCellStyle.SelectionForeColor = _colors.HeaderText;
        ColumnHeadersDefaultCellStyle.Font = ThemeFonts.Get(FontRole.Body, FontStyle.Bold);
        ApplyMetrics();
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyMetrics();
    }

    protected override void OnRowsAdded(DataGridViewRowsAddedEventArgs e)
    {
        base.OnRowsAdded(e);
        var height = RowHeight;
        for (var i = e.RowIndex; i < e.RowIndex + e.RowCount; i++)
        {
            Rows[i].Height = height;
        }
    }

    protected override void OnCellMouseEnter(DataGridViewCellEventArgs e)
    {
        base.OnCellMouseEnter(e);
        SetHoverRow(e.RowIndex);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHoverRow(-1);
    }

    protected override void OnCellFormatting(DataGridViewCellFormattingEventArgs e)
    {
        base.OnCellFormatting(e);
        if (e.RowIndex >= 0 && e.RowIndex == _hoverRow && e.CellStyle is not null && !Rows[e.RowIndex].Selected)
        {
            e.CellStyle.BackColor = _colors.HoverRow;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (RowCount > 0 || string.IsNullOrEmpty(EmptyText))
        {
            return;
        }

        var top = ColumnHeadersVisible ? ColumnHeadersHeight : 0;
        var bounds = new Rectangle(0, top, ClientSize.Width, Math.Max(0, ClientSize.Height - top));
        TextRenderer.DrawText(
            e.Graphics,
            EmptyText,
            ThemeFonts.Get(FontRole.Body),
            bounds,
            _colors.EmptyText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private int RowHeight => (int)Math.Round(ThemeManager.Current.Spacing.ControlHeight * DeviceDpi / 96d);

    private void ApplyMetrics()
    {
        ColumnHeadersHeight = (int)Math.Round((ThemeManager.Current.Spacing.ControlHeight + 4) * DeviceDpi / 96d);
        RowTemplate.Height = RowHeight;
        foreach (DataGridViewRow row in Rows)
        {
            row.Height = RowHeight;
        }
    }

    private void SetHoverRow(int rowIndex)
    {
        if (_hoverRow == rowIndex)
        {
            return;
        }

        var previous = _hoverRow;
        _hoverRow = rowIndex;
        if (previous >= 0 && previous < RowCount)
        {
            InvalidateRow(previous);
        }

        if (rowIndex >= 0 && rowIndex < RowCount)
        {
            InvalidateRow(rowIndex);
        }
    }
}
