using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>
/// Shown after a goods receipt is posted: totals, paid, new debt and the supplier balance, then every product with
/// stock after, cost before → after and the shelf price. Products now sold at or below cost are highlighted and
/// listed first so the user can review their prices in Sản phẩm.
/// </summary>
internal sealed class GoodsReceiptCompletionForm : Form
{
    public GoodsReceiptCompletionForm(GoodsReceiptCompletion completion, string supplierName)
    {
        Text = $"Đã nhập kho – {completion.Code}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(1000, 600);
        Padding = new Padding(20);

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font(Font.FontFamily, 13, FontStyle.Bold),
            Text = $"✓ Đã hoàn thành phiếu nhập {completion.Code} – {supplierName}",
        };

        var stats = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 120, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        stats.Controls.AddRange([
            Stat("Tổng tiền phiếu", Vnd.Format(completion.Total), Glyphs.Document, BadgeKind.Primary, $"{completion.Lines.Count} sản phẩm"),
            Stat("Đã trả NCC", Vnd.Format(completion.PaidAmount), Glyphs.Money, BadgeKind.Success, null),
            Stat("Ghi nợ phiếu này", Vnd.Format(completion.DebtAmount), Glyphs.Warning, completion.DebtAmount > 0 ? BadgeKind.Warning : BadgeKind.Neutral, null),
            Stat("Công nợ NCC hiện tại", Vnd.Format(completion.SupplierDebtBalance), Glyphs.People, BadgeKind.Info, null),
        ]);

        var below = completion.BelowCostLines.ToList();
        var warning = new Label
        {
            Dock = DockStyle.Top,
            Height = below.Count > 0 ? 44 : 0,
            Visible = below.Count > 0,
            Text = $"⚠ {below.Count} sản phẩm có giá bán ≤ giá vốn mới (tô đỏ). Hãy xem lại giá bán trong màn hình Sản phẩm trước khi bán tiếp.",
        };

        var grid = new ModernDataGrid { Dock = DockStyle.Fill, ShowPager = false, PageSize = 500, SearchPlaceholder = "Tìm sản phẩm…" };
        grid.AddTextColumn(nameof(GoodsReceiptPostedLine.ProductCode), "Mã SP", width: 100);
        grid.AddTextColumn(nameof(GoodsReceiptPostedLine.ProductName), "Tên sản phẩm");
        grid.AddNumberColumn(nameof(GoodsReceiptPostedLine.Quantity), "SL nhập", width: 90, decimals: 3);
        grid.AddNumberColumn(nameof(GoodsReceiptPostedLine.StockAfter), "Tồn sau", width: 90, decimals: 3);
        grid.AddMoneyColumn(nameof(GoodsReceiptPostedLine.OldCostPrice), "Giá vốn cũ", width: 120);
        grid.AddMoneyColumn(nameof(GoodsReceiptPostedLine.NewCostPrice), "Giá vốn mới", width: 120);
        grid.AddMoneyColumn(nameof(GoodsReceiptPostedLine.SellPrice), "Giá bán", width: 120);
        grid.AddCheckColumn(nameof(GoodsReceiptPostedLine.SellPriceChanged), "Đổi giá", width: 70);
        grid.SetItems(completion.Lines.OrderByDescending(l => l.IsBelowCost).ToList());
        grid.InnerGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.InnerGrid.Rows[e.RowIndex].Tag is GoodsReceiptPostedLine { IsBelowCost: true } && e.CellStyle is { } style)
            {
                style.ForeColor = ThemeManager.Current.Colors.Danger;
            }
        };

        var ok = new ModernButton { Text = "Đóng (Enter)", Variant = ButtonVariant.Primary, Dock = DockStyle.Right, Width = 140, DialogResult = DialogResult.OK };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(0, 12, 0, 0) };
        footer.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = ok;

        Controls.Add(grid);
        Controls.Add(warning);
        Controls.Add(stats);
        Controls.Add(title);
        Controls.Add(footer);
        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            warning.ForeColor = ThemeManager.Current.Colors.Danger;
            title.ForeColor = ThemeManager.Current.Colors.Success;
        };
    }

    private static StatCard Stat(string caption, string value, string glyph, BadgeKind accent, string? note) => new()
    {
        Caption = caption,
        Value = value,
        Glyph = glyph,
        Accent = accent,
        Note = note,
        Margin = new Padding(0, 0, 12, 0),
    };
}
