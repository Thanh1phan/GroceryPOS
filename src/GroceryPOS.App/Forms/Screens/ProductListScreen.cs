using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Presentation.Catalog;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Forms.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>
/// Every product sold in the store – passive MVP view; the logic lives in <see cref="ProductListPresenter"/>.
/// Selling price and stock are both visible, but stock itself always comes from the warehouse ledger.
/// </summary>
internal sealed class ProductListScreen : BaseListView<ProductListItem>, IProductListView
{
    private readonly IServiceProvider _services;
    private readonly ProductListPresenter _presenter;

    public ProductListScreen(IServiceProvider services)
    {
        _services = services;
        _presenter = services.GetRequiredService<ProductListPresenter>();
        _presenter.Attach(this);
        var canManage = _presenter.CanManage;

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

        AddCommand(new ListCommand<ProductListItem>("Thêm sản phẩm") { Allowed = canManage }, _presenter.AddAsync, ButtonVariant.Primary, Glyphs.Add);
        AddCommand(new ListCommand<ProductListItem>("Sửa") { Allowed = canManage }, _presenter.EditAsync, iconGlyph: Glyphs.Edit, isDefault: true);
        AddCommand(
            new ListCommand<ProductListItem>("Ngừng bán")
            {
                Allowed = canManage,
                TextFor = ProductListPresenter.ToggleText,
            },
            _presenter.ToggleActiveAsync);
        AddCommand(new ListCommand<ProductListItem>("Lịch sử giá"), _presenter.ShowPriceHistory, ButtonVariant.Ghost, Glyphs.History);
        AddCommand(new ListCommand<ProductListItem>(ProductListPresenter.ImportTitle) { Allowed = canManage }, _presenter.ImportAsync);
        AddCommand(new ListCommand<ProductListItem>(ProductListPresenter.ExportTitle) { Allowed = canManage }, _presenter.ExportAsync);
    }

    bool IView.IsAlive => IsAlive;

    protected override object? KeyOf(ProductListItem item) => item.Id;

    protected override Task<IReadOnlyList<ProductListItem>?> LoadItemsAsync(CancellationToken cancellationToken) =>
        _presenter.LoadAsync(cancellationToken);

    bool IProductListView.OpenEditor(ProductDetail? product)
    {
        using var dialog = new ProductEditForm(_services.GetRequiredService<ProductEditPresenter>().For(product));
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    void IProductListView.OpenPriceHistory(ProductListItem product)
    {
        using var dialog = new PriceHistoryForm(_services.GetRequiredService<IServiceScopeFactory>(), product.Id, product.Name);
        dialog.ShowDialog(this);
    }

    string? IProductListView.PickImportFile()
    {
        using var dialog = new OpenFileDialog { Filter = ExcelFiles.Filter, Title = "Chọn file Excel sản phẩm" };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    Task IProductListView.SaveExcelAsync(string suggestedFileName, byte[] content) =>
        ExcelFiles.SaveAsync(this, ProductListPresenter.ExportTitle, "Lưu danh sách sản phẩm", suggestedFileName, content);

    void IProductListView.ShowImportSummary(ImportSummary summary)
    {
        using var dialog = new ImportSummaryForm(summary);
        dialog.ShowDialog(this);
    }

    void IProductListView.ShowFileProblem(string message, string title, string details) =>
        ModernDialog.Warning(this, message, title, details);

    Task<T> IProductListView.RunBusyAsync<T>(Func<Task<T>> work, string message) => RunBusyAsync(work, message);

    Task IProductListView.ReloadAsync() => RefreshDataAsync();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
