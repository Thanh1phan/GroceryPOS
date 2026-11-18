using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Inventory;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Tests;

public sealed class InventoryPresenterTests
{
    private sealed class FakeInventoryView : RecordingView, IInventoryView
    {
        public List<IReadOnlyList<StockItem>> StockShown { get; } = new();
        public IReadOnlyList<LowStockRow>? LowShown { get; private set; }
        public InventorySummary? SummaryShown { get; private set; }
        public List<string> WarningToasts { get; } = new();
        public List<StockItem> AdjustedFor { get; } = new();
        public StockAdjustmentResult? AdjustmentResult { get; set; }
        public bool CountResult { get; set; }
        public int CountsOpened { get; private set; }
        public List<int> CardsOpened { get; } = new();

        public void ShowStock(IReadOnlyList<StockItem> items) => StockShown.Add(items);

        public void ShowLowStock(IReadOnlyList<LowStockRow> rows) => LowShown = rows;

        public void ShowSummary(InventorySummary summary) => SummaryShown = summary;

        public StockAdjustmentResult? OpenAdjustment(StockItem product)
        {
            AdjustedFor.Add(product);
            return AdjustmentResult;
        }

        public bool OpenStockCount(IReadOnlyList<StockItem> catalog)
        {
            CountsOpened++;
            return CountResult;
        }

        public void OpenStockCard(int productId) => CardsOpened.Add(productId);

        public void ToastWarning(string message, string? title = null) => WarningToasts.Add(message);
    }

    private static readonly StockItem Milk = new(1, "SP001", "8930001", "Sữa tươi", 1, "Sữa", "hộp", 10, 5, 7_000, 9_000, true);
    private static readonly StockItem Rice = new(2, "SP002", null, "Gạo ST25", 2, "Gạo", "kg", 3, 5, 20_000, 25_000, true);
    private static readonly StockItem Noodles = new(3, "SP003", null, "Mì gói", 3, "Mì", "gói", -2, 10, 3_000, 4_000, true);
    private static readonly StockItem Stopped = new(4, "SP004", null, "Bánh cũ", 3, "Bánh", "gói", 8, 0, 5_000, 6_000, false);

    private static (TestHost Host, InventoryPresenter Presenter, FakeInventoryView View) Create(bool canAdjust = true)
    {
        var host = canAdjust ? new TestHost(Permissions.InventoryView, Permissions.InventoryAdjust) : new TestHost(Permissions.InventoryView);
        host.Inventory.Stock.AddRange(new[] { Milk, Rice, Noodles, Stopped });
        host.Inventory.Low.AddRange(new[]
        {
            new LowStockItem(3, "SP003", "Mì gói", "gói", -2, 10, 3_000),
            new LowStockItem(2, "SP002", "Gạo ST25", "kg", 3, 5, 20_000),
        });
        var presenter = host.Get<InventoryPresenter>();
        var view = new FakeInventoryView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Can_adjust_follows_the_inventory_adjust_permission()
    {
        using var allowed = new TestHost(Permissions.InventoryAdjust);
        using var denied = new TestHost(Permissions.InventoryView);
        Assert.True(allowed.Get<InventoryPresenter>().CanAdjust);
        Assert.False(denied.Get<InventoryPresenter>().CanAdjust);
        Assert.False(denied.Get<InventoryPresenter>().CanAdjustSelection(1));
        Assert.False(allowed.Get<InventoryPresenter>().CanAdjustSelection(null));
        Assert.True(allowed.Get<InventoryPresenter>().CanAdjustSelection(1));
    }

    [Fact]
    public async Task Load_shows_active_stock_low_stock_rows_and_the_summary()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.LoadAsync();

        Assert.Equal(new[] { Milk, Rice, Noodles }, view.StockShown.Single());
        Assert.Equal(new[] { 3, 2 }, view.LowShown!.Select(r => r.ProductId));
        var summary = view.SummaryShown!;
        Assert.Equal(70_000 + 60_000, summary.StockValue); // negative stock counts as zero, stopped product excluded
        Assert.Equal(90_000 + 75_000, summary.SellValue);
        Assert.Equal(3, summary.ActiveProducts);
        Assert.Equal(2, summary.InStockProducts);
        Assert.Equal(1, summary.LowStockProducts);
        Assert.Equal(1, summary.OutOfStockProducts);
        Assert.Equal("1 sản phẩm tồn âm – cần kiểm kê", summary.OutOfStockNote);
        Assert.Equal("130.000 ₫", summary.StockValueText.Replace(' ', ' '));
        Assert.Equal(new[] { "stock", "low" }, host.Inventory.Calls);
    }

    [Fact]
    public async Task Show_inactive_re_renders_the_stock_tab_with_stopped_products()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync();

        presenter.SetShowInactive(true);
        presenter.SetShowInactive(true); // no change, no render

        Assert.Equal(2, view.StockShown.Count);
        Assert.Contains(Stopped, view.StockShown[1]);
        Assert.Equal(4, presenter.Stock.Count);
    }

    [Fact]
    public async Task Failed_stock_load_shows_an_error_and_keeps_the_view()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Inventory.FailStockWith = Error.Forbidden();

        await presenter.LoadAsync();

        Assert.Equal(new[] { Error.Forbidden().Message }, view.Errors);
        Assert.Empty(view.StockShown);
        Assert.Null(view.SummaryShown);
    }

    [Fact]
    public async Task Failed_low_stock_load_still_shows_the_stock_and_reports_the_alerts_error()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Inventory.FailLowWith = Error.Forbidden();

        await presenter.LoadAsync();

        Assert.Single(view.StockShown);
        Assert.Empty(view.LowShown!);
        Assert.Single(view.Errors);
        Assert.StartsWith("Không tải được cảnh báo sắp hết hàng", view.Errors[0]);
    }

    [Fact]
    public async Task An_older_load_finishing_last_does_not_overwrite_a_newer_one()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        var gate = new TaskCompletionSource();
        host.Inventory.BeforeStockReturns = () => gate.Task;
        var older = presenter.LoadAsync();

        host.Inventory.BeforeStockReturns = null;
        host.Inventory.Stock.RemoveAll(s => s.ProductId == Rice.ProductId);
        await presenter.LoadAsync();
        gate.SetResult();
        await older;

        Assert.Single(view.StockShown);
        Assert.DoesNotContain(Rice, view.StockShown[0]);
    }

    [Fact]
    public async Task Load_after_the_view_closed_does_not_touch_it()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.IsAlive = false;

        await presenter.LoadAsync();

        Assert.Empty(view.StockShown);
        Assert.Empty(view.Errors);
    }

    [Fact]
    public async Task Adjust_without_permission_warns_and_opens_nothing()
    {
        var (host, presenter, view) = Create(canAdjust: false);
        using var _ = host;
        await presenter.LoadAsync();

        await presenter.AdjustAsync(Milk.ProductId);
        await presenter.CountAsync();

        Assert.Empty(view.AdjustedFor);
        Assert.Equal(0, view.CountsOpened);
        Assert.Equal(2, view.Warnings.Count);
    }

    [Fact]
    public async Task Saved_adjustment_toasts_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync();
        view.AdjustmentResult = new StockAdjustmentResult(1, "SP001", "Sữa tươi", -2, 8, -14_000, false);

        await presenter.AdjustAsync(Milk.ProductId);

        Assert.Equal(new[] { Milk }, view.AdjustedFor);
        Assert.Equal(new[] { "Đã điều chỉnh SP001 – Sữa tươi: −2 → tồn 8 hộp." }, view.Successes);
        Assert.Equal(2, view.StockShown.Count);
    }

    [Fact]
    public async Task Adjustment_ending_under_the_minimum_toasts_a_warning()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync();
        view.AdjustmentResult = new StockAdjustmentResult(1, "SP001", "Sữa tươi", -6.5m, 3.5m, -45_500, true);

        await presenter.AdjustAsync(Milk.ProductId);

        Assert.Empty(view.Successes);
        Assert.Equal(new[] { "Đã điều chỉnh SP001 – Sữa tươi: −6,5 → tồn 3,5 hộp. Sản phẩm đang dưới định mức tồn kho." }, view.WarningToasts);
    }

    [Fact]
    public async Task Cancelled_adjustment_does_not_reload()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync();

        await presenter.AdjustAsync(Milk.ProductId);
        await presenter.AdjustAsync(null);

        Assert.Single(view.AdjustedFor);
        Assert.Single(view.StockShown);
        Assert.Empty(view.Successes);
    }

    [Fact]
    public async Task Adjusting_a_product_no_longer_listed_asks_to_refresh()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync();

        await presenter.AdjustAsync(999);

        Assert.Empty(view.AdjustedFor);
        Assert.Contains("F5", Assert.Single(view.Warnings));
    }

    [Fact]
    public async Task Saved_count_reloads_and_cancelled_count_does_not()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync();

        await presenter.CountAsync();
        Assert.Single(view.StockShown);

        view.CountResult = true;
        await presenter.CountAsync();
        Assert.Equal(2, view.StockShown.Count);
        Assert.Equal(2, view.CountsOpened);
    }

    [Fact]
    public void Stock_card_opens_only_for_a_selection()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        presenter.ShowCard(null);
        presenter.ShowCard(2);

        Assert.Equal(new[] { 2 }, view.CardsOpened);
        Assert.False(InventoryPresenter.CanShowCard(null));
    }

    [Theory]
    [InlineData("Hết hàng", Tone.Danger)]
    [InlineData("Tồn âm", Tone.Danger)]
    [InlineData("Sắp hết", Tone.Warning)]
    [InlineData("Ngừng kinh doanh", Tone.Muted)]
    [InlineData("Còn hàng", Tone.Normal)]
    [InlineData(null, Tone.Normal)]
    public void Status_tone_colors_the_status_cells(string? status, Tone expected) =>
        Assert.Equal(expected, InventoryPresenter.StatusTone(status));

    [Theory]
    [InlineData(-2, 10, 22)]
    [InlineData(3, 5, 7)]
    [InlineData(2.5, 2, 2)]
    [InlineData(4, 2, 0)]
    [InlineData(0, 0, 0)]
    public void Low_stock_suggestion_refills_to_twice_the_minimum_in_whole_units(decimal stock, decimal min, decimal expected) =>
        Assert.Equal(expected, LowStockRow.SuggestedQuantity(stock, min));

    [Fact]
    public void Low_stock_row_carries_the_suggested_cost_and_status()
    {
        var row = LowStockRow.From(new LowStockItem(2, "SP002", "Gạo", "kg", 3, 5, 20_000));

        Assert.Equal(7, row.Suggested);
        Assert.Equal(140_000, row.SuggestedCost);
        Assert.Equal(2, row.Shortage);
        Assert.Equal(LowStockRow.LowStatus, row.StatusName);
        Assert.Equal(LowStockRow.OutOfStockStatus, LowStockRow.From(new LowStockItem(3, "x", "y", "z", 0, 1, 1)).StatusName);
    }
}
