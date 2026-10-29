using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>Read-only view of a completed or cancelled goods receipt (header, amounts and lines with landed cost).</summary>
internal sealed class GoodsReceiptDetailForm : Form
{
    public GoodsReceiptDetailForm(GoodsReceiptDto receipt, TimeSpan storeOffset)
    {
        Text = $"Phiếu nhập {receipt.Code}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(1080, 620);
        Padding = new Padding(20);

        var badge = new ModernBadge
        {
            Text = receipt.StatusName,
            Kind = receipt.Status switch
            {
                GoodsReceiptStatus.Completed => BadgeKind.Success,
                GoodsReceiptStatus.Cancelled => BadgeKind.Danger,
                _ => BadgeKind.Neutral,
            },
            Location = new Point(0, 6),
        };
        var title = new Label { AutoSize = true, Location = new Point(130, 0), Font = new Font(Font.FontFamily, 13, FontStyle.Bold), Text = $"{receipt.Code} · {receipt.SupplierName}" };
        static string Time(DateTimeOffset value, TimeSpan offset) => value.ToOffset(offset).ToString("HH:mm dd/MM/yyyy", Vnd.Culture);
        var details = new List<string> { $"Lập lúc {Time(receipt.ReceiptDate, storeOffset)}" };
        if (receipt.CompletedAt is { } done)
        {
            details.Add($"nhập kho lúc {Time(done, storeOffset)}");
        }

        if (receipt.SupplierInvoiceNo is { } invoice)
        {
            details.Add($"HĐ NCC {invoice}");
        }

        if (receipt.Status == GoodsReceiptStatus.Cancelled)
        {
            details.Add($"lý do hủy: {receipt.CancelReason ?? "(không ghi)"}");
        }

        var subtitle = new Label { AutoSize = true, Location = new Point(0, 40), Text = string.Join(" · ", details) + (receipt.Note is { } note ? $"\nGhi chú: {note}" : string.Empty) };
        var header = new Panel { Dock = DockStyle.Top, Height = 84 };
        header.Controls.AddRange([badge, title, subtitle]);

        var amounts = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            Text = $"Tiền hàng {Vnd.Format(receipt.Subtotal)} · CK phiếu {Vnd.Format(receipt.DiscountAmount)} · Tổng {Vnd.Format(receipt.Total)} · "
                + $"Đã trả {Vnd.Format(receipt.PaidAmount)} · Ghi nợ {Vnd.Format(receipt.Status == GoodsReceiptStatus.Completed ? receipt.DebtAmount : 0)}",
        };

        var grid = new ModernDataGrid { Dock = DockStyle.Fill, ShowPager = false, PageSize = 500 };
        grid.AddTextColumn(nameof(GoodsReceiptLineDto.ProductCode), "Mã SP", width: 100);
        grid.AddTextColumn(nameof(GoodsReceiptLineDto.ProductName), "Tên sản phẩm");
        grid.AddTextColumn(nameof(GoodsReceiptLineDto.Unit), "ĐVT", width: 60);
        grid.AddNumberColumn(nameof(GoodsReceiptLineDto.Quantity), "SL", width: 80, decimals: 3);
        grid.AddMoneyColumn(nameof(GoodsReceiptLineDto.UnitCost), "Giá nhập", width: 110);
        grid.AddMoneyColumn(nameof(GoodsReceiptLineDto.DiscountAmount), "CK dòng", width: 100);
        grid.AddMoneyColumn(nameof(GoodsReceiptLineDto.AllocatedDiscount), "CK phân bổ", width: 100);
        grid.AddMoneyColumn(nameof(GoodsReceiptLineDto.FinalAmount), "Thành tiền", width: 120);
        grid.AddMoneyColumn(nameof(GoodsReceiptLineDto.EffectiveUnitCost), "Giá thực", width: 110);
        grid.AddMoneyColumn(nameof(GoodsReceiptLineDto.NewSellPrice), "Giá bán mới", width: 110);
        grid.SetItems(receipt.Lines);

        var ok = new ModernButton { Text = "Đóng", Variant = ButtonVariant.Secondary, Dock = DockStyle.Right, Width = 120, DialogResult = DialogResult.OK };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(0, 12, 0, 0) };
        footer.Controls.Add(ok);
        CancelButton = ok;

        Controls.Add(grid);
        Controls.Add(amounts);
        Controls.Add(header);
        Controls.Add(footer);
        Load += (_, _) => ThemeManager.Track(this);
    }
}
