using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>Read-only view of a posted supplier return (phiếu TN…): settlement, reason and every line with its cost.</summary>
internal sealed class SupplierReturnDetailForm : Form
{
    public SupplierReturnDetailForm(SupplierReturnDto ret, TimeSpan storeOffset)
    {
        Text = $"Phiếu trả hàng NCC {ret.Code}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(960, 600);
        Padding = new Padding(20);

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font(Font.FontFamily, 13, FontStyle.Bold),
            Text = $"{ret.Code} · {ret.SupplierName}",
        };
        var settlement = new List<string> { $"Giá trị hàng trả {Vnd.Format(ret.Total)}" };
        if (ret.RefundAmount > 0)
        {
            settlement.Add($"NCC hoàn lại {Vnd.Format(ret.RefundAmount)} ({ret.RefundMethodName})");
        }

        if (ret.DebtOffset > 0)
        {
            settlement.Add($"cấn trừ công nợ {Vnd.Format(ret.DebtOffset)}");
        }

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 76,
            Text =
                $"Ngày trả: {ret.ReturnDate.ToOffset(storeOffset):dd/MM/yyyy HH:mm}"
                + (ret.GoodsReceiptCode is { } code ? $" · theo phiếu nhập {code}" : " · không theo phiếu nhập") + "\n"
                + string.Join(" · ", settlement) + "\n"
                + $"Giá vốn hàng xuất {Vnd.Format(ret.CostValue)} · chênh lệch {Vnd.Format(ret.CostDifference)}\n"
                + $"Lý do: {ret.Reason}" + (ret.Note is { } note ? $" · Ghi chú: {note}" : string.Empty),
        };

        var grid = new ModernDataGrid { Dock = DockStyle.Fill, ShowSearch = false, ShowPager = false, PageSize = 500 };
        grid.AddTextColumn(nameof(SupplierReturnLineDto.ProductCode), "Mã SP", width: 100);
        grid.AddTextColumn(nameof(SupplierReturnLineDto.ProductName), "Tên sản phẩm");
        grid.AddTextColumn(nameof(SupplierReturnLineDto.Unit), "ĐVT", width: 60);
        grid.AddNumberColumn(nameof(SupplierReturnLineDto.Quantity), "SL trả", width: 80, decimals: 3);
        grid.AddMoneyColumn(nameof(SupplierReturnLineDto.UnitPrice), "Giá trả", width: 110);
        grid.AddMoneyColumn(nameof(SupplierReturnLineDto.Amount), "Thành tiền", width: 120);
        grid.AddMoneyColumn(nameof(SupplierReturnLineDto.CostPrice), "Giá vốn", width: 110);
        grid.AddMoneyColumn(nameof(SupplierReturnLineDto.CostValue), "Giá trị vốn", width: 120);
        grid.SetItems(ret.Lines);

        Controls.Add(grid);
        Controls.Add(info);
        Controls.Add(title);

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
        Load += (_, _) => ThemeManager.Track(this);
    }
}
