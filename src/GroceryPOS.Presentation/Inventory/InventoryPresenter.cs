using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Inventory;

/// <summary>The Tồn kho screen as seen by its presenter.</summary>
public interface IInventoryView : IView
{
    /// <summary>Rows of the "Tồn kho" tab (already filtered by "Hiện SP ngừng kinh doanh").</summary>
    void ShowStock(IReadOnlyList<StockItem> items);

    /// <summary>Rows of the "Sắp hết hàng" tab; the tab badge shows their count.</summary>
    void ShowLowStock(IReadOnlyList<LowStockRow> rows);

    void ShowSummary(InventorySummary summary);

    /// <summary>Opens Điều chỉnh tồn kho for <paramref name="product"/>; the saved adjustment, or null when cancelled.</summary>
    StockAdjustmentResult? OpenAdjustment(StockItem product);

    /// <summary>Opens Kiểm kê kho over <paramref name="catalog"/>; true when a count was saved.</summary>
    bool OpenStockCount(IReadOnlyList<StockItem> catalog);

    /// <summary>Opens Thẻ kho (stock ledger) of one product.</summary>
    void OpenStockCard(int productId);

    void ToastWarning(string message, string? title = null);
}

/// <summary>
/// Tồn kho screen logic: loads the stock list and low-stock alerts together ("latest wins"), computes the KPI cards,
/// filters stopped products, and runs the Điều chỉnh (F6) / Thẻ kho (F7) / Kiểm kê (F8) flows with their feedback.
/// </summary>
public sealed class InventoryPresenter : Presenter<IInventoryView>
{
    public const string Title = "Tồn kho";

    private IReadOnlyList<StockItem> _stock = [];
    private long _loadVersion;

    public InventoryPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        CanAdjust = currentUser.HasPermission(Permissions.InventoryAdjust);
    }

    /// <summary>Điều chỉnh and Kiểm kê need <c>inventory.adjust</c>; the lists and Thẻ kho are open to anyone on the screen.</summary>
    public bool CanAdjust { get; }

    /// <summary>Every product of the last successful load, stopped ones included.</summary>
    public IReadOnlyList<StockItem> Stock => _stock;

    public IReadOnlyList<LowStockRow> LowStock { get; private set; } = [];

    public InventorySummary Summary { get; private set; } = InventorySummary.Empty;

    public bool ShowInactive { get; private set; }

    /// <summary>Color of a "Trạng thái" cell (both tabs use the same status names).</summary>
    public static Tone StatusTone(string? status) => status switch
    {
        "Hết hàng" or "Tồn âm" => Tone.Danger,
        "Sắp hết" => Tone.Warning,
        "Ngừng kinh doanh" => Tone.Muted,
        _ => Tone.Normal,
    };

    public bool CanAdjustSelection(int? productId) => CanAdjust && productId is not null;

    public static bool CanShowCard(int? productId) => productId is not null;

    /// <summary>Products shown on the "Tồn kho" tab.</summary>
    public IReadOnlyList<StockItem> VisibleStock => ShowInactive ? _stock : _stock.Where(i => i.IsActive).ToList();

    public void SetShowInactive(bool showInactive)
    {
        if (ShowInactive == showInactive)
        {
            return;
        }

        ShowInactive = showInactive;
        if (ViewAlive)
        {
            View.ShowStock(VisibleStock);
        }
    }

    /// <summary>
    /// Loads stock and alerts in one scope and pushes them to the view. A failed stock list keeps what the view
    /// had; failed alerts still show the stock (with an error toast). A load overtaken by a newer one is dropped.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var version = ++_loadVersion;
        var (stock, low) = await UseAsync<IInventoryService, (Result<IReadOnlyList<StockItem>>, Result<IReadOnlyList<LowStockItem>>)>(
            async inventory => (await inventory.GetStockListAsync(cancellationToken), await inventory.GetLowStockAsync(cancellationToken)));
        if (version != _loadVersion || !ViewAlive)
        {
            return;
        }

        if (stock.IsFailure)
        {
            View.ToastError(stock.Error.Message, Title);
            return;
        }

        if (low.IsFailure)
        {
            View.ToastError($"Không tải được cảnh báo sắp hết hàng: {low.Error.Message}", Title);
        }

        _stock = stock.Value;
        LowStock = low.IsSuccess ? low.Value.Select(LowStockRow.From).ToList() : [];
        Summary = InventorySummary.From(_stock, LowStock);
        View.ShowStock(VisibleStock);
        View.ShowLowStock(LowStock);
        View.ShowSummary(Summary);
    }

    public void ShowCard(int? productId)
    {
        if (productId is { } id)
        {
            View.OpenStockCard(id);
        }
    }

    /// <summary>Adjusts the selected product, confirms with a toast (a warning when it ends under the minimum), reloads.</summary>
    public async Task AdjustAsync(int? productId)
    {
        if (!CanAdjust)
        {
            View.ShowWarning("Bạn không có quyền điều chỉnh tồn kho.", Title);
            return;
        }

        if (productId is not { } id)
        {
            return;
        }

        if (_stock.FirstOrDefault(i => i.ProductId == id) is not { } product)
        {
            View.ShowWarning("Sản phẩm không còn trong danh sách tồn kho. Hãy làm mới (F5) rồi thử lại.", Title);
            return;
        }

        if (View.OpenAdjustment(product) is not { } adjusted || !ViewAlive)
        {
            return;
        }

        var message = AdjustedMessage(adjusted, product.Unit);
        if (adjusted.IsLowStock)
        {
            View.ToastWarning($"{message} Sản phẩm đang dưới định mức tồn kho.", Title);
        }
        else
        {
            View.ToastSuccess(message, Title);
        }

        await LoadAsync();
    }

    /// <summary>Opens Kiểm kê kho over the products in business; reloads after a saved count.</summary>
    public async Task CountAsync()
    {
        if (!CanAdjust)
        {
            View.ShowWarning("Bạn không có quyền kiểm kê kho.", Title);
            return;
        }

        if (View.OpenStockCount(_stock) && ViewAlive)
        {
            await LoadAsync();
        }
    }

    /// <summary>"Đã điều chỉnh SP001 – Sữa: −2 → tồn 10 hộp." (the toast after a saved adjustment).</summary>
    public static string AdjustedMessage(StockAdjustmentResult adjusted, string unit)
    {
        ArgumentNullException.ThrowIfNull(adjusted);
        var sign = adjusted.Quantity >= 0 ? "+" : "−";
        return $"Đã điều chỉnh {adjusted.ProductCode} – {adjusted.ProductName}: {sign}{StockCountSheet.FormatQty(Math.Abs(adjusted.Quantity))} → tồn {StockCountSheet.FormatQty(adjusted.BalanceAfter)} {unit}.";
    }
}
