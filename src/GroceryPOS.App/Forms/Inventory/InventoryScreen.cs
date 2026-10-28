using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Navigation;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Inventory;

/// <summary>
/// Tồn kho: KPI cards (stock value at cost, products, low/out of stock), a "Tồn kho" tab with every product and a
/// "Sắp hết hàng" tab with the low-stock alerts; from either tab: Điều chỉnh (F6), Thẻ kho (F7), Kiểm kê (F8).
/// </summary>
internal sealed class InventoryScreen : UserControl
{
    private readonly IServiceScopeFactory _scopeFactory;
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
    private IReadOnlyList<StockItem> _stock = [];

    public InventoryScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _canAdjust = currentUser.HasPermission(Permissions.InventoryAdjust);
        Dock = DockStyle.Fill;

        // ---- KPI row --------------------------------------------------------------------------------
        _value = Stat("Giá trị tồn kho (giá vốn)", Glyphs.Money, BadgeKind.Primary);
        _products = Stat("Sản phẩm đang kinh doanh", Glyphs.Package, BadgeKind.Info);
        _low = Stat("Sắp hết hàng", Glyphs.Warning, BadgeKind.Warning);
        _out = Stat("Hết hàng", Glyphs.Error, BadgeKind.Danger);
        var stats = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        stats.Controls.AddRange([_value, _products, _low, _out]);

        _adjust = Action("Điều chỉnh (F6)", Glyphs.Edit, ButtonVariant.Secondary, async () => await AdjustSelectedAsync());
        _adjust.Visible = _canAdjust;
        _card = Action("Thẻ kho (F7)", Glyphs.History, ButtonVariant.Secondary, () => { ShowCardOfSelected(); return Task.CompletedTask; });
        var count = Action("Kiểm kê (F8)", Glyphs.List, ButtonVariant.Primary, async () => await OpenCountAsync());
        count.Visible = _canAdjust;
        var refresh = Action("Làm mới", Glyphs.Refresh, ButtonVariant.Ghost, ReloadAsync);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 24, 0, 0) };
        actions.Controls.AddRange([_adjust, _card, count, refresh]);

        var header = new Panel { Dock = DockStyle.Top, Height = 140, Padding = new Padding(24, 20, 24, 0) };
        header.Controls.Add(stats);
        header.Controls.Add(actions);

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
        _stockGrid.RowActivated += (_, e) => ShowCard(((StockItem)e.Item).ProductId);
        _stockGrid.SelectionChanged += (_, _) => UpdateButtons();
        _stockGrid.InnerGrid.CellFormatting += (_, e) => ToneStatusCell(_stockGrid, e);

        _showInactive = new CheckBox { Text = "Hiện SP ngừng kinh doanh", AutoSize = true, Margin = new Padding(8, 12, 8, 0) };
        _showInactive.CheckedChanged += (_, _) => RenderStock();
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
        _lowGrid.RowActivated += (_, e) => ShowCard(((LowStockRow)e.Item).ProductId);
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
        Controls.Add(header);

        Load += async (_, _) => await ReloadAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F6 when _canAdjust:
                _ = AdjustSelectedAsync();
                return true;
            case Keys.F7:
                ShowCardOfSelected();
                return true;
            case Keys.F8 when _canAdjust:
                _ = OpenCountAsync();
                return true;
            case Keys.F5:
                _ = ReloadAsync();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

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
        e.CellStyle!.ForeColor = status switch
        {
            "Hết hàng" or "Tồn âm" => palette.Danger,
            "Sắp hết" => palette.Warning,
            "Ngừng kinh doanh" => palette.TextMuted,
            _ => e.CellStyle.ForeColor,
        };
    }

    private int? SelectedProductId => _tabs.SelectedIndex == 0
        ? _stockGrid.GetSelected<StockItem>()?.ProductId
        : _lowGrid.GetSelected<LowStockRow>()?.ProductId;

    private void UpdateButtons()
    {
        var selected = SelectedProductId is not null;
        _adjust.Enabled = _canAdjust && selected;
        _card.Enabled = selected;
    }

    private async Task ReloadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var stock = await inventory.GetStockListAsync();
        var low = await inventory.GetLowStockAsync();
        if (IsDisposed)
        {
            return;
        }

        if (stock.IsFailure)
        {
            MessageBox.Show(this, stock.Error.Message, "Tồn kho", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _stock = stock.Value;
        RenderStock();

        var lowRows = low.IsSuccess ? low.Value.Select(LowStockRow.From).ToList() : [];
        _lowGrid.SetItems(lowRows);
        _lowPage.BadgeCount = lowRows.Count;

        var active = _stock.Where(i => i.IsActive).ToList();
        _value.Value = Vnd.Format(active.Sum(i => i.StockValue));
        _value.Note = $"Giá bán: {Vnd.Format(active.Sum(i => Math.Max(0, i.StockQuantity) * i.SellPrice))}";
        _products.Value = active.Count.ToString("#,0", Vnd.Culture);
        _products.Note = $"{active.Count(i => i.StockQuantity > 0):#,0} còn hàng";
        _low.Value = lowRows.Count(r => !r.IsOutOfStock).ToString("#,0", Vnd.Culture);
        _low.Note = "Dưới hoặc bằng định mức";
        _out.Value = lowRows.Count(r => r.IsOutOfStock).ToString("#,0", Vnd.Culture);
        var negative = active.Count(i => i.StockQuantity < 0);
        _out.Note = negative > 0 ? $"{negative} sản phẩm tồn âm – cần kiểm kê" : null;
        UpdateButtons();
    }

    private void RenderStock() =>
        _stockGrid.SetItems(_showInactive.Checked ? _stock : _stock.Where(i => i.IsActive).ToList());

    private void ShowCardOfSelected()
    {
        if (SelectedProductId is { } id)
        {
            ShowCard(id);
        }
    }

    private void ShowCard(int productId)
    {
        using var form = new StockCardForm(_scopeFactory, productId);
        form.ShowDialog(this);
    }

    private async Task AdjustSelectedAsync()
    {
        if (!_canAdjust || SelectedProductId is not { } id || _stock.FirstOrDefault(i => i.ProductId == id) is not { } item)
        {
            return;
        }

        using var dialog = new StockAdjustmentDialog(_scopeFactory, item);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await ReloadAsync();
        }
    }

    private async Task OpenCountAsync()
    {
        if (!_canAdjust)
        {
            return;
        }

        using var form = new StockCountForm(_scopeFactory, _stock);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await ReloadAsync();
        }
    }

    /// <summary>Low-stock alert row with a reorder suggestion (refill to twice the minimum, as purchasing does).</summary>
    private sealed record LowStockRow(
        int ProductId,
        string ProductCode,
        string ProductName,
        string Unit,
        decimal StockQuantity,
        decimal MinStockLevel,
        decimal Shortage,
        decimal Suggested,
        decimal SuggestedCost,
        bool IsOutOfStock,
        string StatusName)
    {
        public static LowStockRow From(LowStockItem item)
        {
            // Same rule as GoodsReceiptService.SuggestedQuantity: refill to twice the minimum, whole units.
            var suggested = Math.Max(0, Math.Ceiling(Math.Max(item.MinStockLevel * 2, 0) - item.StockQuantity));
            return new LowStockRow(
                item.ProductId,
                item.ProductCode,
                item.ProductName,
                item.Unit,
                item.StockQuantity,
                item.MinStockLevel,
                item.Shortage,
                suggested,
                Math.Round(suggested * item.CostPrice, 0, MidpointRounding.AwayFromZero),
                item.IsOutOfStock,
                item.IsOutOfStock ? "Hết hàng" : "Sắp hết");
        }
    }
}
