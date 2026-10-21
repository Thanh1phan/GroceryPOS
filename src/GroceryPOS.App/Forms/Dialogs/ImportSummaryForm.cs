using GroceryPOS.Application.Catalog;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>Results of a product import: counts plus every row's outcome, so a failure is never silently dropped.</summary>
internal sealed class ImportSummaryForm : Form
{
    private sealed record RowView(int RowNumber, string Code, string Outcome, string? Message);

    public ImportSummaryForm(ImportSummary summary)
    {
        Text = "Kết quả nhập Excel";
        ClientSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;

        var header = new Label
        {
            Text = $"Đã tạo: {summary.Created}    Đã cập nhật: {summary.Updated}    Lỗi: {summary.Failed}",
            AutoSize = true,
            Location = new Point(16, 16),
            Font = ThemeFonts.Get(FontRole.Title, FontStyle.Bold),
        };

        var grid = new ModernDataGrid
        {
            Location = new Point(16, 56),
            Size = new Size(608, 320),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            EmptyText = "Không có dòng dữ liệu nào.",
        };
        grid.AddNumberColumn(nameof(RowView.RowNumber), "Dòng", width: 70);
        grid.AddTextColumn(nameof(RowView.Code), "Mã sản phẩm", width: 120, searchable: false);
        grid.AddTextColumn(nameof(RowView.Outcome), "Kết quả", width: 110, searchable: false);
        grid.AddTextColumn(nameof(RowView.Message), "Chi tiết", width: 0);
        grid.SetItems(summary.Rows.Select(r => new RowView(r.RowNumber, r.Code, DisplayName(r.Outcome), r.Message)));

        Controls.AddRange([header, grid]);

        Load += (_, _) => ThemeManager.Track(this);
    }

    private static string DisplayName(ImportRowOutcome outcome) => outcome switch
    {
        ImportRowOutcome.Created => "Đã tạo",
        ImportRowOutcome.Updated => "Đã cập nhật",
        ImportRowOutcome.Failed => "Lỗi",
        _ => outcome.ToString(),
    };
}
