using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Formatting;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>Read-only loyalty history of one customer: KPI cards on top, purchases/returns/voids with the running balance below.</summary>
internal sealed class CustomerLoyaltyHistoryForm : Form
{
    /// <summary>Grid row: the DTO flattened with Vietnamese texts.</summary>
    private sealed record Row(
        DateTimeOffset Date,
        string KindText,
        string DocumentCode,
        decimal Amount,
        int PointsEarned,
        int PointsRedeemed,
        string Change,
        int BalanceAfter,
        string Description);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly int _customerId;
    private readonly StatCard _points;
    private readonly StatCard _spent;
    private readonly StatCard _invoices;
    private readonly ModernDataGrid _grid;

    public CustomerLoyaltyHistoryForm(IServiceScopeFactory scopeFactory, CustomerDto customer)
    {
        _scopeFactory = scopeFactory;
        _customerId = customer.Id;

        Text = $"Lịch sử tích điểm – {customer.Name} ({customer.Code})";
        ClientSize = new Size(1040, 600);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 132, Padding = new Padding(16, 16, 16, 0), WrapContents = false };
        _points = MakeCard("Điểm hiện có", Glyphs.Money, BadgeKind.Success);
        _spent = MakeCard("Tổng chi tiêu", Glyphs.Shop, BadgeKind.Primary);
        _invoices = MakeCard("Số hóa đơn", Glyphs.Document, BadgeKind.Info);
        cards.Controls.AddRange([_points, _spent, _invoices]);

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Khách hàng chưa có giao dịch nào." };
        _grid.AddDateColumn(nameof(Row.Date), "Thời gian", width: 140, withTime: true);
        _grid.AddTextColumn(nameof(Row.KindText), "Loại", width: 100);
        _grid.AddTextColumn(nameof(Row.DocumentCode), "Chứng từ", width: 130);
        _grid.AddMoneyColumn(nameof(Row.Amount), "Giá trị");
        _grid.AddNumberColumn(nameof(Row.PointsEarned), "Điểm cộng", width: 90);
        _grid.AddNumberColumn(nameof(Row.PointsRedeemed), "Điểm đổi", width: 90);
        _grid.AddTextColumn(nameof(Row.Change), "Thay đổi", width: 90, searchable: false);
        _grid.AddNumberColumn(nameof(Row.BalanceAfter), "Số dư điểm", width: 100);
        _grid.AddTextColumn(nameof(Row.Description), "Diễn giải");

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 16) };
        body.Controls.Add(_grid);
        Controls.Add(body);
        Controls.Add(cards);

        Load += async (_, _) =>
        {
            ThemeManager.Track(this);
            await ReloadAsync();
        };
    }

    private static StatCard MakeCard(string caption, string glyph, BadgeKind accent) => new()
    {
        Caption = caption,
        Glyph = glyph,
        Accent = accent,
        Value = "…",
        Margin = new Padding(0, 0, 16, 0),
    };

    private static string KindText(LoyaltyEntryKind kind) => kind switch
    {
        LoyaltyEntryKind.Purchase => "Mua hàng",
        LoyaltyEntryKind.Return => "Trả hàng",
        LoyaltyEntryKind.Void => "Hủy hóa đơn",
        _ => "Điểm đầu kỳ",
    };

    private async Task ReloadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICustomerService>().GetLoyaltyHistoryAsync(_customerId);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var history = result.Value;
        _points.Value = history.Customer.LoyaltyPoints.ToString("N0", Vnd.Culture);
        _spent.Value = Vnd.Format(history.Customer.TotalSpent);
        _invoices.Value = history.InvoiceCount.ToString("N0", Vnd.Culture);
        _grid.SetItems(history.Entries.Select(e => new Row(
            e.Date,
            KindText(e.Kind),
            e.DocumentCode,
            e.Amount,
            e.PointsEarned,
            e.PointsRedeemed,
            e.PointsChange > 0 ? "+" + e.PointsChange.ToString("N0", Vnd.Culture) : e.PointsChange.ToString("N0", Vnd.Culture),
            e.BalanceAfter,
            e.Description)).ToList());
    }
}
