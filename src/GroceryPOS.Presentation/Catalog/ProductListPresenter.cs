using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Catalog;

/// <summary>The Sản phẩm screen as seen by its presenter.</summary>
public interface IProductListView : IView
{
    /// <summary>Opens the add (<paramref name="product"/> = null) or edit dialog; true when it was saved.</summary>
    bool OpenEditor(ProductDetail? product);

    /// <summary>Shows the (read-only) price history of <paramref name="product"/>.</summary>
    void OpenPriceHistory(ProductListItem product);

    /// <summary>Asks for the Excel file to import; null when cancelled.</summary>
    string? PickImportFile();

    /// <summary>Asks where to save <paramref name="content"/> and writes it (the view reports locked files itself).</summary>
    Task SaveExcelAsync(string suggestedFileName, byte[] content);

    /// <summary>Shows the per-row result of an import.</summary>
    void ShowImportSummary(ImportSummary summary);

    /// <summary>A file problem with technical details (e.g. the file is open in Excel).</summary>
    void ShowFileProblem(string message, string title, string details);

    /// <summary>Runs <paramref name="work"/> behind the loading overlay.</summary>
    Task<T> RunBusyAsync<T>(Func<Task<T>> work, string message);

    /// <summary>Reloads the list (keeps the selection by key).</summary>
    Task ReloadAsync();
}

/// <summary>
/// Sản phẩm screen logic: add/edit (always from fresh details), stop/resume selling, price history and Excel
/// import/export. Stock itself is never edited here – it comes from the warehouse ledger.
/// </summary>
public sealed class ProductListPresenter : Presenter<IProductListView>
{
    public const string Title = "Sản phẩm";
    public const string ImportTitle = "Nhập Excel";
    public const string ExportTitle = "Xuất Excel";
    public const string NoPermission = "Bạn không có quyền quản lý sản phẩm.";
    public const string UnreadableFile = "Không đọc được file. Nếu file đang mở trong Excel, hãy đóng file rồi thử lại.";

    public ProductListPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        CanManage = currentUser.HasPermission(Permissions.ProductsManage);
    }

    /// <summary>Add, edit, stop/resume and Excel import/export need <c>products.manage</c>; price history is open to all.</summary>
    public bool CanManage { get; }

    /// <summary>Caption of the stop/resume button for the selected row.</summary>
    public static string ToggleText(ProductListItem? product) => product is { IsActive: false } ? "Bán lại" : "Ngừng bán";

    public static string StopQuestion(ProductListItem product) =>
        $"Ngừng bán \"{product.Name}\"?\nSản phẩm sẽ không quét/tìm được ở màn hình bán hàng; tồn kho và lịch sử được giữ nguyên.";

    /// <summary>Suggested export file name, e.g. <c>SanPham_20261114.xlsx</c>.</summary>
    public static string ExportFileName(DateTime localNow) => $"SanPham_{localNow:yyyyMMdd}.xlsx";

    /// <summary>All products, or null after showing the error (the view keeps what it had).</summary>
    public async Task<IReadOnlyList<ProductListItem>?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await UseAsync<IProductService, Result<IReadOnlyList<ProductListItem>>>(
            products => products.ListAsync(cancellationToken));
        if (result.IsSuccess)
        {
            return result.Value;
        }

        if (ViewAlive)
        {
            View.ToastError(result.Error.Message, Title);
        }

        return null;
    }

    public Task AddAsync() => OpenEditorAsync(null);

    /// <summary>Re-reads the product first so the dialog never edits stale prices or stock.</summary>
    public async Task EditAsync(ProductListItem product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        var detail = await UseAsync<IProductService, Result<ProductDetail>>(products => products.GetAsync(product.Id));
        if (!ViewAlive)
        {
            return;
        }

        if (detail.IsFailure)
        {
            View.ShowWarning(detail.Error.Message, Title);
            await View.ReloadAsync(); // the product may have been removed or changed elsewhere
            return;
        }

        await OpenEditorAsync(detail.Value);
    }

    public void ShowPriceHistory(ProductListItem product)
    {
        ArgumentNullException.ThrowIfNull(product);
        View.OpenPriceHistory(product);
    }

    /// <summary>Stops selling an active product (after confirmation) or resumes an inactive one, then reloads.</summary>
    public async Task ToggleActiveAsync(ProductListItem product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        if (product.IsActive && !View.Confirm(StopQuestion(product), Title, "Ngừng bán"))
        {
            return;
        }

        var result = await UseAsync<IProductService, Result>(products => products.SetActiveAsync(product.Id, !product.IsActive));
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, Title);
            return;
        }

        View.ToastSuccess(product.IsActive ? $"Đã ngừng bán {product.Name}." : $"Đã bán lại {product.Name}.");
        await View.ReloadAsync();
    }

    public async Task ExportAsync()
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, ExportTitle);
            return;
        }

        var result = await View.RunBusyAsync(
            () => UseAsync<IProductImportExportAppService, Result<byte[]>>(io => io.ExportAsync()),
            "Đang xuất danh sách sản phẩm…");
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, ExportTitle);
            return;
        }

        await View.SaveExcelAsync(ExportFileName(DateTime.Now), result.Value);
    }

    /// <summary>Imports the picked workbook, shows the per-row summary and reloads (even when some rows failed).</summary>
    public async Task ImportAsync()
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, ImportTitle);
            return;
        }

        if (View.PickImportFile() is not { } path)
        {
            return;
        }

        Result<ImportSummary> result;
        try
        {
            result = await View.RunBusyAsync(
                () => UseAsync<IProductImportExportAppService, Result<ImportSummary>>(async io =>
                {
                    await using var stream = File.OpenRead(path);
                    return await io.ImportAsync(stream);
                }),
                "Đang nhập sản phẩm từ Excel…");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (ViewAlive)
            {
                View.ShowFileProblem(UnreadableFile, ImportTitle, ex.Message);
            }

            return;
        }

        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, ImportTitle);
            return;
        }

        View.ShowImportSummary(result.Value);
        await View.ReloadAsync();
    }

    private async Task OpenEditorAsync(ProductDetail? product)
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        if (!View.OpenEditor(product) || !ViewAlive)
        {
            return;
        }

        View.ToastSuccess(product is null ? "Đã thêm sản phẩm mới." : $"Đã lưu sản phẩm {product.Code} – {product.Name}.");
        await View.ReloadAsync();
    }
}
