using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Sales;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Inventory;

/// <summary>
/// Thẻ kho of one product for a period (default: this month): opening balance, total in/out, closing balance and
/// every movement (sale, void, return, receipt, supplier return, adjustment, count) with the running balance.
/// </summary>
internal sealed class StockCardForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly int _productId;
    private readonly TimeSpan _storeOffset;
    private readonly Label _title;
    private readonly ModernDatePicker _from;
    private readonly ModernDatePicker _to;
    private readonly StatCard _opening;
    private readonly StatCard _in;
    private readonly StatCard _out;
    private readonly StatCard _closing;
    private readonly ModernDataGrid _grid;

    public StockCardForm(IServiceScopeFactory scopeFactory, int productId)
    {
        _scopeFactory = scopeFactory;
        _productId = productId;
        using (var scope = scopeFactory.CreateScope())
        {
            _storeOffset = (scope.ServiceProvider.GetService<SalesOptions>() ?? SalesOptions.Default).StoreUtcOffset;
        }

        Text = "Thẻ kho";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(1080, 680);
        Padding = new Padding(20);

        _title = new Label { Dock = DockStyle.Top, Height = 32, Font = new Font(Font.FontFamily, 13, FontStyle.Bold), Text = "Đang tải…" };

        var today = DateTime.Today;
        _from = new ModernDatePicker { Width = 150, Value = new DateTime(today.Year, today.Month, 1), ReserveMessageSpace = false };
        _to = new ModernDatePicker { Width = 150, Value = today, ReserveMessageSpace = false };
        _from.ValueChanged += async (_, _) => await ReloadAsync();
        _to.ValueChanged += async (_, _) => await ReloadAsync();
        var filter = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false };
        filter.Controls.AddRange([
            new Label { Text = "Từ ngày", AutoSize = true, Margin = new Padding(0, 12, 6, 0) }, _from,
            new Label { Text = "đến", AutoSize = true, Margin = new Padding(10, 12, 6, 0) }, _to,
        ]);

        _opening = Stat("Tồn đầu kỳ", Glyphs.History, BadgeKind.Neutral);
        _in = Stat("Nhập trong kỳ", Glyphs.Add, BadgeKind.Success);
        _out = Stat("Xuất trong kỳ", Glyphs.Cart, BadgeKind.Warning);
        _closing = Stat("Tồn cuối kỳ", Glyphs.Package, BadgeKind.Primary);
        var stats = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 120, WrapContents = false, Padding = new Padding(0, 4, 0, 8) };
        stats.Controls.AddRange([_opening, _in, _out, _closing]);

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Không có phát sinh trong khoảng thời gian này.", PageSize = 50 };
        _grid.AddDateColumn(nameof(CardRow.Timestamp), "Thời gian", width: 140, withTime: true);
        _grid.AddTextColumn(nameof(CardRow.TypeName), "Nghiệp vụ", width: 140);
        _grid.AddTextColumn(nameof(CardRow.ReferenceCode), "Chứng từ", width: 140);
        _grid.AddNumberColumn(nameof(CardRow.QuantityIn), "Nhập", width: 90, decimals: 3);
        _grid.AddNumberColumn(nameof(CardRow.QuantityOut), "Xuất", width: 90, decimals: 3);
        _grid.AddNumberColumn(nameof(CardRow.BalanceAfter), "Tồn", width: 90, decimals: 3);
        _grid.AddMoneyColumn(nameof(CardRow.UnitCost), "Đơn giá vốn", width: 120);
        _grid.AddTextColumn(nameof(CardRow.Description), "Diễn giải");

        Controls.Add(_grid);
        Controls.Add(stats);
        Controls.Add(filter);
        Controls.Add(_title);

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
        Load += async (_, _) =>
        {
            ThemeManager.Track(this);
            await ReloadAsync();
        };
    }

    private static StatCard Stat(string caption, string glyph, BadgeKind accent) => new()
    {
        Caption = caption,
        Glyph = glyph,
        Accent = accent,
        Value = "–",
        Margin = new Padding(0, 0, 16, 0),
    };

    private async Task ReloadAsync()
    {
        var fromDate = _from.Value?.Date ?? DateTime.Today;
        var toDate = (_to.Value?.Date ?? DateTime.Today).AddDays(1);
        if (fromDate >= toDate)
        {
            _to.SetError("Đến ngày phải sau từ ngày.");
            return;
        }

        _to.ClearValidation();
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IInventoryService>().GetLedgerAsync(
            _productId,
            new DateTimeOffset(fromDate, _storeOffset),
            new DateTimeOffset(toDate, _storeOffset));
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            _title.Text = result.Error.Message;
            return;
        }

        var ledger = result.Value;
        _title.Text = $"Thẻ kho: {ledger.ProductCode} – {ledger.ProductName} ({ledger.Unit})";
        _opening.Value = StockCountSheet.FormatQty(ledger.OpeningBalance);
        _in.Value = StockCountSheet.FormatQty(ledger.TotalIn);
        _out.Value = StockCountSheet.FormatQty(ledger.TotalOut);
        _closing.Value = StockCountSheet.FormatQty(ledger.ClosingBalance);
        _in.Note = $"{ledger.Entries.Count(e => e.QuantityIn > 0):#,0} phát sinh";
        _out.Note = $"{ledger.Entries.Count(e => e.QuantityOut > 0):#,0} phát sinh";
        _grid.SetItems(ledger.Entries
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Id)
            .Select(e => new CardRow(
                e.Timestamp.ToOffset(_storeOffset).DateTime,
                e.TypeName,
                e.ReferenceCode,
                e.QuantityIn,
                e.QuantityOut,
                e.BalanceAfter,
                e.UnitCost,
                string.Join(" – ", new[] { e.ReasonName, e.Note }.Where(s => !string.IsNullOrWhiteSpace(s)))))
            .ToList());
    }

    private sealed record CardRow(
        DateTime Timestamp,
        string TypeName,
        string? ReferenceCode,
        decimal QuantityIn,
        decimal QuantityOut,
        decimal BalanceAfter,
        decimal UnitCost,
        string Description);
}
