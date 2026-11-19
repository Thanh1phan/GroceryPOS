using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Presentation.Inventory;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Navigation;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Inventory;

/// <summary>
/// Tồn kho: KPI cards (stock value at cost, products, low/out of stock), a "Tồn kho" tab with every product and a
/// "Sắp hết hàng" tab with the low-stock alerts; from either tab: Điều chỉnh (F6), Thẻ kho (F7), Kiểm kê (F8).
/// Passive MVP view – the logic lives in <see cref="InventoryPresenter"/>.
/// </summary>
internal sealed class InventoryScreen : BaseUserControl, IInventoryView
{
    private readonly IServiceProvider _services;
    private readonly InventoryPresenter _presenter;
    private readonly bool _canAdjust;
    private readonly StatCard _value;
    private readonly StatCard _products;
    private readonly StatCard _low;
    private readonly StatCard _out;
    private readonly ModernTabControl _tabs;
    private readonly ModernTabPage _lowPage;
    private readonly ModernDataGrid _stockGrid;
    private readonly ModernDataGrid _lowGrid;
    private readonly CheckBox _showInactive;
    private readonly ModernButton _adjust;
    private readonly ModernButton _card;

    public InventoryScreen(IServiceProvider services)
    {
        _services = services;
        _presenter = services.GetRequiredService<InventoryPresenter>();
        _presenter.Attach(this);
        _canAdjust = _presenter.CanAdjust;

        // ---- KPI row --------------------------------------------------------------------------------
        _value = Stat("Giá trị tồn kho (giá vốn)", Glyphs.Money, BadgeKind.Primary);
        _products = Stat("Sản phẩm đang kinh doanh", Glyphs.Package, BadgeKind.Info);
        _low = Stat("Sắp hết hàng", Glyphs.Warning, BadgeKind.Warning);
        _out = Stat("Hết hàng", Glyphs.Error, BadgeKind.Danger);
        var stats = new StatCardRow { Padding = new Padding(24, 0, 24, 0) };
        stats.AddCards(_value, _products, _low, _out);

        _adjust = Action("Điều chỉnh (F6)", Glyphs.Edit, ButtonVariant.Secondary, () => _presenter.AdjustAsync(SelectedProductId));
        _adjust.Visible = _canAdjust;
        _card = Action("Thẻ kho (F7)", Glyphs.History, ButtonVariant.Secondary, () => { _presenter.ShowCard(SelectedProductId); return Task.CompletedTask; });
        var count = Action("Kiểm kê (F8)", Glyphs.List, ButtonVariant.Primary, _presenter.CountAsync);
        count.Visible = _canAdjust;
        var refresh = Action("Làm mới", Glyphs.Refresh, ButtonVariant.Ghost, RefreshDataAsync);
        var actions = ScreenLayout.Toolbar(_adjust, _card, count, refresh);

        // ---- Stock tab ------------------------------------------------------------------------------
        _stockGrid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có sản phẩm nào.", SearchPlaceholder = "Tìm theo mã, mã vạch, tên, nhóm…" };
        _stockGrid.AddTextColumn(nameof(StockItem.ProductCode), "Mã SP", width: 100);
        _stockGrid.AddTextColumn(nameof(StockItem.Barcode), "Mã vạch", width: 130);
        _stockGrid.AddTextColumn(nameof(StockItem.ProductName), "Tên sản phẩm");
        _stockGrid.AddTextColumn(nameof(StockItem.CategoryName), "Nhóm", width: 140);
        _stockGrid.AddTextColumn(nameof(StockItem.Unit), "ĐVT", width: 70);
        _stockGrid.AddNumberColumn(nameof(StockItem.StockQuantity), "Tồn", width: 90, decimals: 3);
        _stockGrid.AddNumberColumn(nameof(StockItem.MinStockLevel), "Định mức", width: 90, decimals: 3);
        _stockGrid.AddMoneyColumn(nameof(StockItem.CostPrice), "Giá vốn", width: 120);
        _stockGrid.AddMoneyColumn(nameof(StockItem.StockValue), "Giá trị tồn", width: 140);
        _stockGrid.AddTextColumn(nameof(StockItem.StatusName), "Trạng thái", width: 120);
        _stockGrid.RowActivated += (_, e) => _presenter.ShowCard(((StockItem)e.Item).ProductId);
        _stockGrid.SelectionChanged += (_, _) => UpdateButtons();
        _stockGrid.InnerGrid.CellFormatting += (_, e) => ToneStatusCell(_stockGrid, e);

        _showInactive = new CheckBox { Text = "Hiện SP ngừng kinh doanh", AutoSize = true, Margin = new Padding(8, 12, 8, 0) };
        _showInactive.CheckedChanged += (_, _) => _presenter.SetShowInactive(_showInactive.Checked);
        _stockGrid.ToolbarItems.Controls.Add(_showInactive);

        var stockPage = new ModernTabPage("Tồn kho") { IconGlyph = Glyphs.Package, Padding = new Padding(0, 8, 0, 0) };
        stockPage.Controls.Add(_stockGrid);

        // ---- Low-stock tab --------------------------------------------------------------------------
        _lowGrid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Không có sản phẩm nào dưới định mức tồn kho. 👍", ShowPager = false, PageSize = 500 };
        _lowGrid.AddTextColumn(nameof(LowStockRow.ProductCode), "Mã SP", width: 100);
        _lowGrid.AddTextColumn(nameof(LowStockRow.ProductName), "Tên sản phẩm");
        _lowGrid.AddTextColumn(nameof(LowStockRow.Unit), "ĐVT", width: 70);
        _lowGrid.AddNumberColumn(nameof(LowStockRow.StockQuantity), "Tồn", width: 90, decimals: 3);
        _lowGrid.AddNumberColumn(nameof(LowStockRow.MinStockLevel), "Định mức", width: 90, decimals: 3);
        _lowGrid.AddNumberColumn(nameof(LowStockRow.Shortage), "Thiếu so với định mức", width: 160, decimals: 3);
        _lowGrid.AddNumberColumn(nameof(LowStockRow.Suggested), "Đề xuất nhập", width: 120, decimals: 3);
        _lowGrid.AddMoneyColumn(nameof(LowStockRow.SuggestedCost), "Tiền nhập dự kiến", width: 150);
        _lowGrid.AddTextColumn(nameof(LowStockRow.StatusName), "Trạng thái", width: 110);
        _lowGrid.RowActivated += (_, e) => _presenter.ShowCard(((LowStockRow)e.Item).ProductId);
        _lowGrid.SelectionChanged += (_, _) => UpdateButtons();
        _lowGrid.InnerGrid.CellFormatting += (_, e) => ToneStatusCell(_lowGrid, e);

        _lowPage = new ModernTabPage("Sắp hết hàng") { IconGlyph = Glyphs.Warning, BadgeKind = BadgeKind.Warning, Padding = new Padding(0, 8, 0, 0) };
        _lowPage.Controls.Add(_lowGrid);

        _tabs = new ModernTabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(stockPage);
        _tabs.TabPages.Add(_lowPage);
        _tabs.SelectedIndexChanged += (_, _) => UpdateButtons();

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 8, 24, 24) };
        body.Controls.Add(_tabs);

        Controls.Add(body);
        Controls.Add(stats);
        Controls.Add(actions);
        UpdateButtons();
    }

    bool IView.IsAlive => IsAlive;

    public override Task RefreshDataAsync() => _presenter.LoadAsync();

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F6 when _canAdjust:
                Run(() => _presenter.AdjustAsync(SelectedProductId));
                return true;
            case Keys.F7:
                _presenter.ShowCard(SelectedProductId);
                return true;
            case Keys.F8 when _canAdjust:
                Run(_presenter.CountAsync);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Fire-and-forget from a key press; exceptions still reach the global handler.</summary>
    private static async void Run(Func<Task> work) => await work();

    private static ModernButton Action(string text, string glyph, ButtonVariant variant, Func<Task> onClick)
    {
        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = variant, Size = new Size(text.Length > 10 ? 150 : 120, 40), Margin = new Padding(8, 0, 0, 0) };
        button.Click += async (_, _) => await onClick();
        return button;
    }

    private static StatCard Stat(string caption, string glyph, BadgeKind accent) => new()
    {
        Caption = caption,
        Glyph = glyph,
        Accent = accent,
        Value = "–",
        Margin = new Padding(0, 0, 16, 0),
    };

    private static void ToneStatusCell(ModernDataGrid grid, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].DataPropertyName is not (nameof(StockItem.StatusName)) || e.Value is not string status)
        {
            return;
        }

        var palette = ThemeManager.Current.Colors;
        e.CellStyle!.ForeColor = InventoryPresenter.StatusTone(status) switch
        {
            Tone.Danger => palette.Danger,
            Tone.Warning => palette.Warning,
            Tone.Muted => palette.TextMuted,
            _ => e.CellStyle.ForeColor,
        };
    }

    private int? SelectedProductId => _tabs.SelectedIndex == 0
        ? _stockGrid.GetSelected<StockItem>()?.ProductId
        : _lowGrid.GetSelected<LowStockRow>()?.ProductId;

    private void UpdateButtons()
    {
        var selected = SelectedProductId;
        _adjust.Enabled = _presenter.CanAdjustSelection(selected);
        _card.Enabled = InventoryPresenter.CanShowCard(selected);
    }

    void IInventoryView.ShowStock(IReadOnlyList<StockItem> items)
    {
        _stockGrid.SetItems(items);
        UpdateButtons();
    }

    void IInventoryView.ShowLowStock(IReadOnlyList<LowStockRow> rows)
    {
        _lowGrid.SetItems(rows);
        _lowPage.BadgeCount = rows.Count;
        UpdateButtons();
    }

    void IInventoryView.ShowSummary(InventorySummary summary)
    {
        _value.Value = summary.StockValueText;
        _value.Note = summary.StockValueNote;
        _products.Value = summary.ProductsText;
        _products.Note = summary.ProductsNote;
        _low.Value = summary.LowStockText;
        _low.Note = summary.LowStockNote;
        _out.Value = summary.OutOfStockText;
        _out.Note = summary.OutOfStockNote;
    }

    StockAdjustmentResult? IInventoryView.OpenAdjustment(StockItem product)
    {
        using var dialog = new StockAdjustmentDialog(_services.GetRequiredService<StockAdjustmentPresenter>().For(product));
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Adjusted : null;
    }

    bool IInventoryView.OpenStockCount(IReadOnlyList<StockItem> catalog)
    {
        using var form = new StockCountForm(_services.GetRequiredService<IServiceScopeFactory>(), catalog);
        return form.ShowDialog(this) == DialogResult.OK;
    }

    void IInventoryView.OpenStockCard(int productId)
    {
        using var form = new StockCardForm(_services.GetRequiredService<IServiceScopeFactory>(), productId);
        form.ShowDialog(this);
    }

    void IInventoryView.ToastWarning(string message, string? title) => ToastWarning(message, title);

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
