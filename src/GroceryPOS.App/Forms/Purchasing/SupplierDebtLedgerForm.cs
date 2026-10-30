using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Sales;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>
/// Sổ chi tiết công nợ of one supplier for a period (default: the last 3 months): opening balance, increases
/// (receipts on credit, cancelled payments), decreases (payments, returns offset against debt), closing balance
/// and every document with the running balance.
/// </summary>
internal sealed class SupplierDebtLedgerForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly int _supplierId;
    private readonly TimeSpan _storeOffset;
    private readonly Label _title;
    private readonly ModernDatePicker _from;
    private readonly ModernDatePicker _to;
    private readonly StatCard _opening;
    private readonly StatCard _increase;
    private readonly StatCard _decrease;
    private readonly StatCard _closing;
    private readonly ModernDataGrid _grid;

    public SupplierDebtLedgerForm(IServiceScopeFactory scopeFactory, int supplierId)
    {
        _scopeFactory = scopeFactory;
        _supplierId = supplierId;
        using (var scope = scopeFactory.CreateScope())
        {
            _storeOffset = (scope.ServiceProvider.GetService<SalesOptions>() ?? SalesOptions.Default).StoreUtcOffset;
        }

        Text = "Sổ chi tiết công nợ nhà cung cấp";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(1080, 680);
        Padding = new Padding(20);

        _title = new Label { Dock = DockStyle.Top, Height = 32, Font = new Font(Font.FontFamily, 13, FontStyle.Bold), Text = "Đang tải…" };

        var today = DateTime.Today;
        _from = new ModernDatePicker { Width = 150, Value = new DateTime(today.Year, today.Month, 1).AddMonths(-2), ReserveMessageSpace = false };
        _to = new ModernDatePicker { Width = 150, Value = today, ReserveMessageSpace = false };
        _from.ValueChanged += async (_, _) => await ReloadAsync();
        _to.ValueChanged += async (_, _) => await ReloadAsync();
        var filter = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false };
        filter.Controls.AddRange([
            new Label { Text = "Từ ngày", AutoSize = true, Margin = new Padding(0, 12, 6, 0) }, _from,
            new Label { Text = "đến", AutoSize = true, Margin = new Padding(10, 12, 6, 0) }, _to,
        ]);

        _opening = Stat("Nợ đầu kỳ", Glyphs.History, BadgeKind.Neutral);
        _increase = Stat("Phát sinh tăng", Glyphs.Add, BadgeKind.Warning);
        _decrease = Stat("Đã trả / cấn trừ", Glyphs.Money, BadgeKind.Success);
        _closing = Stat("Nợ cuối kỳ", Glyphs.Document, BadgeKind.Primary);
        var stats = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 120, WrapContents = false, Padding = new Padding(0, 4, 0, 8) };
        stats.Controls.AddRange([_opening, _increase, _decrease, _closing]);

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Không có phát sinh công nợ trong khoảng thời gian này.", PageSize = 50 };
        _grid.AddDateColumn(nameof(LedgerRow.Timestamp), "Thời gian", width: 140, withTime: true);
        _grid.AddTextColumn(nameof(LedgerRow.KindName), "Nghiệp vụ", width: 120);
        _grid.AddTextColumn(nameof(LedgerRow.DocumentCode), "Chứng từ", width: 140);
        _grid.AddTextColumn(nameof(LedgerRow.Description), "Diễn giải");
        _grid.AddMoneyColumn(nameof(LedgerRow.Increase), "Tăng nợ", width: 130);
        _grid.AddMoneyColumn(nameof(LedgerRow.Decrease), "Giảm nợ", width: 130);
        _grid.AddMoneyColumn(nameof(LedgerRow.Balance), "Còn nợ", width: 140);
        _grid.InnerGrid.CellFormatting += OnCellFormatting;

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
        var result = await scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>().GetLedgerAsync(
            _supplierId,
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
        _title.Text = $"Công nợ: {ledger.SupplierName}";
        _opening.Value = Vnd.Format(ledger.OpeningBalance);
        _increase.Value = Vnd.Format(ledger.TotalIncrease);
        _decrease.Value = Vnd.Format(ledger.TotalDecrease);
        _closing.Value = Vnd.Format(ledger.ClosingBalance);
        _increase.Note = $"{ledger.Entries.Count(e => e.Increase > 0):#,0} chứng từ";
        _decrease.Note = $"{ledger.Entries.Count(e => e.Decrease > 0):#,0} chứng từ";
        _closing.Note = ledger.ClosingBalance <= 0 ? "Đã thanh toán hết" : null;
        _grid.SetItems(ledger.Entries
            .Reverse()
            .Select(e => new LedgerRow(
                e.Timestamp.ToOffset(_storeOffset).DateTime,
                e.Kind,
                e.KindName,
                e.DocumentCode,
                e.Description,
                e.Increase,
                e.Decrease,
                e.Balance))
            .ToList());
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.InnerGrid.Rows[e.RowIndex].Tag is not LedgerRow row || e.CellStyle is not { } style)
        {
            return;
        }

        var colors = ThemeManager.Current.Colors;
        switch (_grid.Columns[e.ColumnIndex].DataPropertyName)
        {
            case nameof(LedgerRow.Increase) when row.Increase > 0:
                style.ForeColor = colors.Warning;
                break;
            case nameof(LedgerRow.Decrease) when row.Decrease > 0:
                style.ForeColor = colors.Success;
                break;
            case nameof(LedgerRow.KindName) when row.Kind == SupplierDebtEntryKind.PaymentCancelled:
                style.ForeColor = colors.Danger;
                break;
        }
    }

    private sealed record LedgerRow(
        DateTime Timestamp,
        SupplierDebtEntryKind Kind,
        string KindName,
        string DocumentCode,
        string Description,
        decimal Increase,
        decimal Decrease,
        decimal Balance);
}
