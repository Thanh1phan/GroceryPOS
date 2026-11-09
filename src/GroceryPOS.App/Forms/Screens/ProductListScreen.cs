using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Forms.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>
/// Every product sold in the store: selling price and stock are both visible, product info is editable, but
/// stock itself always comes from the warehouse ledger (see <see cref="ProductEditForm"/>).
/// </summary>
internal sealed class ProductListScreen : BaseListView<ProductListItem>
{
    private const string Caption = "Sản phẩm";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUser _currentUser;

    public ProductListScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
        var canManage = currentUser.HasPermission(Permissions.ProductsManage);

        Grid.EmptyText = "Chưa có sản phẩm nào.";
        Grid.AddTextColumn(nameof(ProductListItem.Code), "Mã", width: 90);
        Grid.AddTextColumn(nameof(ProductListItem.Name), "Tên sản phẩm", width: 0);
        Grid.AddTextColumn(nameof(ProductListItem.CategoryName), "Danh mục", width: 140);
        Grid.AddTextColumn(nameof(ProductListItem.Unit), "ĐVT", width: 70, searchable: false);
        Grid.AddMoneyColumn(nameof(ProductListItem.CostPrice), "Giá vốn");
        Grid.AddMoneyColumn(nameof(ProductListItem.SellPrice), "Giá bán");
        Grid.AddNumberColumn(nameof(ProductListItem.StockQuantity), "Tồn kho", decimals: 3);
        Grid.AddNumberColumn(nameof(ProductListItem.MinStockLevel), "Tồn tối thiểu", decimals: 3);
        Grid.AddCheckColumn(nameof(ProductListItem.IsActive), "Đang bán");

        AddCommand(new ListCommand<ProductListItem>("Thêm sản phẩm") { Allowed = canManage }, () => EditAsync(null), ButtonVariant.Primary, Glyphs.Add);
        AddCommand(new ListCommand<ProductListItem>("Sửa") { Allowed = canManage }, item => EditAsync(item), iconGlyph: Glyphs.Edit, isDefault: true);
        AddCommand(
            new ListCommand<ProductListItem>("Ngừng bán")
            {
                Allowed = canManage,
                TextFor = p => p.IsActive ? "Ngừng bán" : "Bán lại",
            },
            ToggleActiveAsync);
        AddCommand(new ListCommand<ProductListItem>("Lịch sử giá"), ShowPriceHistory, ButtonVariant.Ghost, Glyphs.History);
        AddCommand(new ListCommand<ProductListItem>("Nhập Excel") { Allowed = canManage }, ImportAsync);
        AddCommand(new ListCommand<ProductListItem>("Xuất Excel") { Allowed = canManage }, ExportAsync);
    }

    protected override object? KeyOf(ProductListItem item) => item.Id;

    protected override async Task<IReadOnlyList<ProductListItem>?> LoadItemsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IProductService>().ListAsync(cancellationToken);
        if (result.IsFailure)
        {
            ToastError(result.Error.Message, Caption);
            return null;
        }

        return result.Value;
    }

    private async Task EditAsync(ProductListItem? item)
    {
        ProductDetail? detail = null;
        if (item is not null)
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IProductService>().GetAsync(item.Id);
            if (!IsAlive)
            {
                return;
            }

            if (result.IsFailure)
            {
                ShowWarning(result.Error.Message, Caption);
                RequestRefresh(); // the product may have been removed or changed elsewhere
                return;
            }

            detail = result.Value;
        }

        using var dialog = new ProductEditForm(_scopeFactory, _currentUser, detail);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ToastSuccess(detail is null ? "Đã thêm sản phẩm mới." : $"Đã lưu sản phẩm {detail.Code} – {detail.Name}.");
            await RefreshDataAsync();
        }
    }

    private async Task ToggleActiveAsync(ProductListItem product)
    {
        if (product.IsActive
            && !Confirm($"Ngừng bán \"{product.Name}\"?\nSản phẩm sẽ không quét/tìm được ở màn hình bán hàng; tồn kho và lịch sử được giữ nguyên.",
                Caption, "Ngừng bán", "Hủy"))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IProductService>().SetActiveAsync(product.Id, !product.IsActive);
        if (!IsAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            ShowWarning(result.Error.Message, Caption);
            return;
        }

        ToastSuccess(product.IsActive ? $"Đã ngừng bán {product.Name}." : $"Đã bán lại {product.Name}.");
        await RefreshDataAsync();
    }

    private void ShowPriceHistory(ProductListItem product)
    {
        using var dialog = new PriceHistoryForm(_scopeFactory, product.Id, product.Name);
        dialog.ShowDialog(this);
    }

    private async Task ExportAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var importExport = scope.ServiceProvider.GetRequiredService<IProductImportExportAppService>();
        var result = await RunBusyAsync(() => importExport.ExportAsync(), "Đang xuất danh sách sản phẩm…");
        if (!IsAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            ShowWarning(result.Error.Message, "Xuất Excel");
            return;
        }

        await ExcelFiles.SaveAsync(this, "Xuất Excel", "Lưu danh sách sản phẩm", $"SanPham_{DateTime.Now:yyyyMMdd}.xlsx", result.Value);
    }

    private async Task ImportAsync()
    {
        using var ofd = new OpenFileDialog { Filter = ExcelFiles.Filter, Title = "Chọn file Excel sản phẩm" };
        if (ofd.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var importExport = scope.ServiceProvider.GetRequiredService<IProductImportExportAppService>();

        Result<ImportSummary> result;
        try
        {
            result = await RunBusyAsync(async () =>
            {
                await using var stream = File.OpenRead(ofd.FileName);
                return await importExport.ImportAsync(stream);
            }, "Đang nhập sản phẩm từ Excel…");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModernDialog.Warning(this, "Không đọc được file. Nếu file đang mở trong Excel, hãy đóng file rồi thử lại.", "Nhập Excel", ex.Message);
            return;
        }

        if (!IsAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            ShowWarning(result.Error.Message, "Nhập Excel");
            return;
        }

        using var summary = new ImportSummaryForm(result.Value);
        summary.ShowDialog(this);
        await RefreshDataAsync();
    }
}
