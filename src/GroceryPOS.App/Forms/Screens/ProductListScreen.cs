using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>
/// Every product sold in the store: selling price and stock are both visible, product info is editable, but
/// stock itself always comes from the warehouse ledger (see <see cref="ProductEditForm"/>).
/// </summary>
internal sealed class ProductListScreen : UserControl
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUser _currentUser;
    private readonly bool _canManage;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _edit;
    private readonly ModernButton _toggleActive;
    private readonly ModernButton _priceHistory;

    public ProductListScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
        _canManage = currentUser.HasPermission(Permissions.ProductsManage);
        Dock = DockStyle.Fill;

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có sản phẩm nào." };
        _grid.AddTextColumn(nameof(ProductListItem.Code), "Mã", width: 90);
        _grid.AddTextColumn(nameof(ProductListItem.Name), "Tên sản phẩm", width: 0);
        _grid.AddTextColumn(nameof(ProductListItem.CategoryName), "Danh mục", width: 140);
        _grid.AddTextColumn(nameof(ProductListItem.Unit), "ĐVT", width: 70, searchable: false);
        _grid.AddMoneyColumn(nameof(ProductListItem.CostPrice), "Giá vốn");
        _grid.AddMoneyColumn(nameof(ProductListItem.SellPrice), "Giá bán");
        _grid.AddNumberColumn(nameof(ProductListItem.StockQuantity), "Tồn kho", decimals: 3);
        _grid.AddNumberColumn(nameof(ProductListItem.MinStockLevel), "Tồn tối thiểu", decimals: 3);
        _grid.AddCheckColumn(nameof(ProductListItem.IsActive), "Đang bán");
        _grid.RowActivated += (_, e) => _ = EditAsync((ProductListItem)e.Item);
        _grid.SelectionChanged += (_, _) => UpdateToolbarState();

        var add = new ModernButton { Text = "Thêm sản phẩm", Variant = ButtonVariant.Primary, Enabled = _canManage };
        add.Click += (_, _) => _ = EditAsync(null);

        _edit = new ModernButton { Text = "Sửa", Variant = ButtonVariant.Secondary, Enabled = false };
        _edit.Click += (_, _) =>
        {
            if (_grid.GetSelected<ProductListItem>() is { } product)
            {
                _ = EditAsync(product);
            }
        };

        _toggleActive = new ModernButton { Text = "Ngừng bán", Variant = ButtonVariant.Secondary, Enabled = false };
        _toggleActive.Click += async (_, _) => await ToggleActiveAsync();

        _priceHistory = new ModernButton { Text = "Lịch sử giá", Variant = ButtonVariant.Ghost, Enabled = false };
        _priceHistory.Click += (_, _) => ShowPriceHistory();

        var import = new ModernButton { Text = "Nhập Excel", Variant = ButtonVariant.Secondary, Enabled = _canManage };
        import.Click += async (_, _) => await ImportAsync();

        var export = new ModernButton { Text = "Xuất Excel", Variant = ButtonVariant.Secondary, Enabled = _canManage };
        export.Click += async (_, _) => await ExportAsync();

        _grid.ToolbarItems.Controls.AddRange([add, _edit, _toggleActive, _priceHistory, import, export]);
        Controls.Add(_grid);

        Load += async (_, _) => await ReloadAsync();
    }

    private void UpdateToolbarState()
    {
        var selected = _grid.GetSelected<ProductListItem>();
        _edit.Enabled = _canManage && selected is not null;
        _toggleActive.Enabled = _canManage && selected is not null;
        _toggleActive.Text = selected is { IsActive: true } ? "Ngừng bán" : "Bán lại";
        _priceHistory.Enabled = selected is not null;
    }

    private async Task EditAsync(ProductListItem? item)
    {
        if (item is not null && !_canManage)
        {
            return;
        }

        ProductDetail? detail = null;
        if (item is not null)
        {
            using var scope = _scopeFactory.CreateScope();
            var products = scope.ServiceProvider.GetRequiredService<IProductService>();
            var result = await products.GetAsync(item.Id);
            if (IsDisposed || !result.IsSuccess)
            {
                return;
            }

            detail = result.Value;
        }
        else if (!_canManage)
        {
            return;
        }

        using var dialog = new ProductEditForm(_scopeFactory, _currentUser, detail);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await ReloadAsync();
        }
    }

    private async Task ToggleActiveAsync()
    {
        if (_grid.GetSelected<ProductListItem>() is not { } product)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();
        await products.SetActiveAsync(product.Id, !product.IsActive);
        if (!IsDisposed)
        {
            await ReloadAsync();
        }
    }

    private void ShowPriceHistory()
    {
        if (_grid.GetSelected<ProductListItem>() is not { } product)
        {
            return;
        }

        using var dialog = new PriceHistoryForm(_scopeFactory, product.Id, product.Name);
        dialog.ShowDialog(this);
    }

    private async Task ExportAsync()
    {
        using var sfd = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "SanPham.xlsx" };
        if (sfd.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var importExport = scope.ServiceProvider.GetRequiredService<IProductImportExportAppService>();
        var result = await importExport.ExportAsync();
        if (IsDisposed)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            MessageBox.Show(this, result.Error.Message, "Xuất Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await File.WriteAllBytesAsync(sfd.FileName, result.Value);
    }

    private async Task ImportAsync()
    {
        using var ofd = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx" };
        if (ofd.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var importExport = scope.ServiceProvider.GetRequiredService<IProductImportExportAppService>();

        Result<ImportSummary> result;
        await using (var stream = File.OpenRead(ofd.FileName))
        {
            result = await importExport.ImportAsync(stream);
        }

        if (IsDisposed)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            MessageBox.Show(this, result.Error.Message, "Nhập Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var summary = new ImportSummaryForm(result.Value);
        summary.ShowDialog(this);
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();
        var result = await products.ListAsync();
        if (IsDisposed)
        {
            // The user navigated away before this load finished; touching a torn-down grid would crash.
            return;
        }

        if (result.IsSuccess)
        {
            _grid.SetItems(result.Value);
        }

        UpdateToolbarState();
    }
}
