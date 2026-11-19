using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Purchasing;

/// <summary>The Nhập hàng screen as seen by its presenter.</summary>
public interface IPurchaseView : IView
{
    /// <summary>Fills the supplier filter ("Tất cả NCC" first); the view keeps its selection when it is still listed.</summary>
    void ShowSupplierFilter(IReadOnlyList<SupplierFilterOption> options);

    void ShowReceipts(IReadOnlyList<PurchaseReceiptRow> rows);

    void ShowSummary(PurchaseSummary summary);

    /// <summary>Marks the "Từ" date as invalid (or clears it with null).</summary>
    void ShowPeriodError(string? message);

    /// <summary>Opens the receipt editor; true when the receipt was saved or completed (the list must reload).</summary>
    bool OpenEditor(GoodsReceiptDraft draft, IReadOnlyList<SupplierDto> suppliers);

    /// <summary>Shows a completed or cancelled receipt read-only.</summary>
    void OpenDetail(GoodsReceiptDto receipt, TimeSpan storeOffset);

    /// <summary>Asks for a reason with one-click <paramref name="suggestions"/>; null when the user backed out.</summary>
    string? AskReason(string title, string message, IReadOnlyList<string> suggestions);
}

/// <summary>
/// Nhập hàng screen logic: period/status/supplier filters with "latest wins" loading, the KPI cards, new receipt
/// (blank or pre-filled with reorder suggestions), open a draft in the editor or view a posted receipt, and
/// cancel a draft with a reason.
/// </summary>
public sealed class PurchasePresenter : Presenter<IPurchaseView>
{
    public const string Title = "Nhập hàng";

    /// <summary>One-click reasons offered when cancelling a draft.</summary>
    public static readonly IReadOnlyList<string> CancelReasons = ["Nhập trùng", "NCC không giao hàng", "Lập sai NCC"];

    private IReadOnlyList<SupplierDto> _suppliers = [];
    private PurchaseFilter _filter = PurchaseFilter.ThisMonth(DateTime.Today);
    private long _loadVersion;

    public PurchasePresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        CanManage = currentUser.HasPermission(Permissions.PurchasesManage);
        CanChangePrices = currentUser.HasPermission(Permissions.PricesChange);
    }

    /// <summary>Creating, editing and cancelling receipts need <c>purchases.manage</c>.</summary>
    public bool CanManage { get; }

    /// <summary>The editor lets the user set new selling prices only with <c>prices.change</c>.</summary>
    public bool CanChangePrices { get; }

    public PurchaseFilter Filter => _filter;

    public IReadOnlyList<PurchaseReceiptRow> Receipts { get; private set; } = [];

    public PurchaseSummary Summary { get; private set; } = PurchaseSummary.Empty;

    /// <summary>Caption of the open button for the selected row.</summary>
    public static string OpenText(PurchaseReceiptRow? row) => row?.IsDraft == true ? "Sửa phiếu tạm" : "Xem phiếu";

    public bool CanCancel(PurchaseReceiptRow? row) => CanManage && row?.IsDraft == true;

    /// <summary>Reloads suppliers (filter + total debt) and then the list with <paramref name="filter"/>.</summary>
    public Task LoadAsync(PurchaseFilter? filter = null) => LoadCoreAsync(filter ?? _filter, withSuppliers: true);

    /// <summary>Reloads the list only (a filter changed).</summary>
    public Task FilterAsync(PurchaseFilter filter) => LoadCoreAsync(filter, withSuppliers: false);

    private async Task LoadCoreAsync(PurchaseFilter filter, bool withSuppliers)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filter = filter;
        var from = (filter.From ?? DateTime.Today.AddDays(-30)).Date;
        var to = (filter.To ?? DateTime.Today).Date.AddDays(1);
        if (from >= to)
        {
            ++_loadVersion; // an older load in flight must not overwrite the error state
            View.ShowPeriodError("Từ ngày phải trước đến ngày.");
            return;
        }

        View.ShowPeriodError(null);
        var version = ++_loadVersion;
        var loaded = await UseScopeAsync(async sp =>
        {
            var offset = (sp.GetService<SalesOptions>() ?? SalesOptions.Default).StoreUtcOffset;
            var suppliers = withSuppliers ? await sp.GetRequiredService<ISupplierService>().ListAsync() : null;
            var service = sp.GetRequiredService<IGoodsReceiptService>();
            var list = await service.SearchAsync(new GoodsReceiptFilter(
                new DateTimeOffset(from, offset), new DateTimeOffset(to, offset), filter.SupplierId, filter.Status));
            var drafts = await service.SearchAsync(new GoodsReceiptFilter(Status: GoodsReceiptStatus.Draft));
            return (offset, suppliers, list, drafts);
        });
        if (version != _loadVersion || !ViewAlive)
        {
            return;
        }

        if (loaded.suppliers is { } suppliers)
        {
            if (suppliers.IsSuccess)
            {
                _suppliers = suppliers.Value;
                View.ShowSupplierFilter(
                    [SupplierFilterOption.All, .. _suppliers.Select(s => new SupplierFilterOption(s.Id, s.Name))]);
            }
            else
            {
                View.ToastError($"Không tải được danh sách nhà cung cấp: {suppliers.Error.Message}", Title);
            }
        }

        if (loaded.list.IsFailure)
        {
            View.ToastError(loaded.list.Error.Message, Title);
            return;
        }

        Receipts = loaded.list.Value.Select(r => PurchaseReceiptRow.From(r, loaded.offset)).ToList();
        var drafts = loaded.drafts.IsSuccess ? loaded.drafts.Value.Select(r => PurchaseReceiptRow.From(r, loaded.offset)).ToList() : null;
        Summary = PurchaseSummary.From(Receipts, drafts, _suppliers);
        View.ShowReceipts(Receipts);
        View.ShowSummary(Summary);
    }

    /// <summary>
    /// Tạo phiếu (F2) or Nhập hàng sắp hết (F4): a new receipt for the supplier picked in the filter (when still
    /// trading), optionally pre-filled with the reorder suggestions.
    /// </summary>
    public async Task NewReceiptAsync(bool withSuggestions)
    {
        if (!CanManage)
        {
            View.ShowWarning("Bạn không có quyền lập phiếu nhập hàng.", Title);
            return;
        }

        var prepared = await UseScopeAsync(async sp =>
        {
            var data = await LoadEditorDataAsync(sp);
            Result<IReadOnlyList<ReorderSuggestion>>? suggestions = null;
            if (data is not null && withSuggestions)
            {
                suggestions = await sp.GetRequiredService<IGoodsReceiptService>().SuggestReorderAsync();
            }

            return (data, suggestions);
        });
        if (!ViewAlive || prepared.data is not { } data)
        {
            return;
        }

        var draft = new GoodsReceiptDraft(data.Catalog, CanChangePrices);
        if (_filter.SupplierId is { } supplierId && data.Suppliers.Any(s => s.Id == supplierId && s.IsActive))
        {
            draft.SetSupplier(supplierId);
        }

        if (prepared.suggestions is { } suggestions)
        {
            if (suggestions.IsFailure)
            {
                View.ToastError($"Không tải được gợi ý nhập hàng: {suggestions.Error.Message}", Title);
            }
            else if (draft.AddSuggestions(suggestions.Value) == 0)
            {
                View.ShowInfo("Không có sản phẩm nào dưới định mức tồn kho. Mở phiếu trống.", Title);
            }
        }

        await ShowEditorAsync(draft, data.Suppliers);
    }

    /// <summary>Enter / Mở phiếu: a draft opens in the editor, a completed or cancelled receipt read-only.</summary>
    public async Task OpenAsync(PurchaseReceiptRow? row)
    {
        if (row is null)
        {
            return;
        }

        var loaded = await UseScopeAsync(async sp =>
        {
            var offset = (sp.GetService<SalesOptions>() ?? SalesOptions.Default).StoreUtcOffset;
            var receipt = await sp.GetRequiredService<IGoodsReceiptService>().GetAsync(row.Id);
            var data = receipt.IsSuccess && receipt.Value.Status == GoodsReceiptStatus.Draft && CanManage
                ? await LoadEditorDataAsync(sp)
                : null;
            return (offset, receipt, data);
        });
        if (!ViewAlive)
        {
            return;
        }

        if (loaded.receipt.IsFailure)
        {
            View.ShowWarning(loaded.receipt.Error.Message, Title);
            return;
        }

        var receipt = loaded.receipt.Value;
        if (receipt.Status != GoodsReceiptStatus.Draft || !CanManage)
        {
            View.OpenDetail(receipt, loaded.offset);
            if (receipt.Status != row.Status)
            {
                // Someone else completed or cancelled it meanwhile: the list is stale.
                await LoadAsync();
            }

            return;
        }

        if (loaded.data is not { } data)
        {
            return;
        }

        await ShowEditorAsync(GoodsReceiptDraft.FromDto(data.Catalog, CanChangePrices, receipt), data.Suppliers);
    }

    /// <summary>Hủy phiếu tạm (Del): asks for a reason, cancels, confirms with a toast and reloads the list.</summary>
    public async Task CancelAsync(PurchaseReceiptRow? row)
    {
        if (row is not { IsDraft: true })
        {
            return;
        }

        if (!CanManage)
        {
            View.ShowWarning("Bạn không có quyền hủy phiếu nhập hàng.", Title);
            return;
        }

        var reason = View.AskReason(
            "Hủy phiếu tạm",
            $"Hủy phiếu tạm {row.Code} ({row.SupplierName}, {Vnd.Format(row.Total)})? Tồn kho và công nợ không thay đổi.",
            CancelReasons);
        if (reason is null)
        {
            return;
        }

        var result = await UseAsync<IGoodsReceiptService, Result>(s => s.CancelAsync(row.Id, FormInput.Optional(reason)));
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, Title);
            return;
        }

        View.ToastSuccess($"Đã hủy phiếu tạm {row.Code}.", Title);
        await FilterAsync(_filter);
    }

    private async Task ShowEditorAsync(GoodsReceiptDraft draft, IReadOnlyList<SupplierDto> suppliers)
    {
        if (View.OpenEditor(draft, suppliers) && ViewAlive)
        {
            await LoadAsync();
        }
    }

    /// <summary>Products and suppliers the editor needs; null (after telling the user) when they cannot be loaded.</summary>
    private async Task<(IReadOnlyList<StockItem> Catalog, IReadOnlyList<SupplierDto> Suppliers)?> LoadEditorDataAsync(IServiceProvider services)
    {
        var catalog = await services.GetRequiredService<IInventoryService>().GetStockListAsync();
        var suppliers = await services.GetRequiredService<ISupplierService>().ListAsync();
        if (!ViewAlive)
        {
            return null;
        }

        if (catalog.IsFailure || suppliers.IsFailure)
        {
            View.ShowWarning((catalog.IsFailure ? catalog.Error : suppliers.Error).Message, Title);
            return null;
        }

        if (!suppliers.Value.Any(s => s.IsActive))
        {
            View.ShowInfo("Chưa có nhà cung cấp nào đang giao dịch. Hãy thêm nhà cung cấp trước khi nhập hàng.", Title);
            return null;
        }

        return (catalog.Value, suppliers.Value);
    }
}
