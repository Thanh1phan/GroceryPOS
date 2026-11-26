using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Auditing;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Reports;

namespace GroceryPOS.Presentation.Tests;

public sealed class ReportsPresenterTests
{
    private static readonly ReportPeriod November = new(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20));
    private static readonly ReportPeriod October = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

    private sealed class FakeReportsView : RecordingView, IReportsView
    {
        public List<(IReadOnlyList<FilterOption<int?>> Options, int? Selected, bool Visible)> CategoryLists { get; } = new();
        public List<(string Text, Tone Tone)> Statuses { get; } = new();
        public List<bool> PeriodEnabled { get; } = new();
        public List<bool> Loading { get; } = new();
        public List<RevenueBoard> Revenues { get; } = new();
        public List<BestSellerBoard> BestSellers { get; } = new();
        public List<ProfitBoard> Profits { get; } = new();
        public List<InventoryBoard> Inventories { get; } = new();
        public List<string> BusyMessages { get; } = new();
        public List<string> SavedFiles { get; } = new();
        public List<ReportDocument> Previews { get; } = new();
        public string? SavePath { get; set; } = Path.Combine("BaoCao", "BaoCao.xlsx");

        /// <summary>What the preview "does" while open (print / save PDF through the callback).</summary>
        public Func<Func<ReportOutput, string?, Task>, Task>? WhilePreviewOpen { get; set; }

        public (string Text, Tone Tone) Status => Statuses[^1];

        public void ShowCategories(IReadOnlyList<FilterOption<int?>> options, int? selected, bool visible) => CategoryLists.Add((options, selected, visible));

        public void ShowStatus(string text, Tone tone) => Statuses.Add((text, tone));

        public void ShowPeriodEnabled(bool enabled) => PeriodEnabled.Add(enabled);

        public void ShowLoading(bool loading) => Loading.Add(loading);

        public void ShowRevenue(RevenueBoard board) => Revenues.Add(board);

        public void ShowBestSellers(BestSellerBoard board) => BestSellers.Add(board);

        public void ShowProfit(ProfitBoard board) => Profits.Add(board);

        public void ShowInventory(InventoryBoard board) => Inventories.Add(board);

        public async Task<T> RunBusyAsync<T>(Func<Task<T>> work, string message)
        {
            BusyMessages.Add(message);
            return await work();
        }

        public Task<string?> SaveExcelAsync(string suggestedFileName, byte[] content)
        {
            SavedFiles.Add(suggestedFileName);
            return Task.FromResult(SavePath);
        }

        public void ShowPrintPreview(ReportDocument document, Func<ReportOutput, string?, Task> onOutput)
        {
            Previews.Add(document);
            WhilePreviewOpen?.Invoke(onOutput).GetAwaiter().GetResult();
        }
    }

    private static RevenueRowDto Row(DateOnly? date, decimal net, int invoices = 2, decimal profit = 100_000, int returns = 0, int voided = 0, decimal refunds = 0) => new(
        date, invoices, voided, net, 0, net + refunds, returns, refunds, net, net / 11, net - net / 11, net - net / 11 - profit, profit, 0, net, 0, 0);

    private static RevenueReportDto Revenue(ReportPeriod period, decimal perDay)
    {
        var days = Enumerable.Range(0, period.Days).Select(i => Row(period.From.AddDays(i), perDay)).ToList();
        return new RevenueReportDto(period, days, Row(null, perDay * period.Days, invoices: 2 * period.Days, profit: 100_000 * period.Days));
    }

    private static ProductSalesRowDto Sale(int rank, string name, decimal revenue) =>
        new(rank, rank, $"SP{rank:000}", name, "gói", 1, "Mì gói", 10, revenue, revenue / 1.1m, revenue / 2, revenue / 1.1m - revenue / 2, 3, 0);

    private static CategoryProfitRowDto CategoryProfit(int id, string name, decimal profit) =>
        new(id, name, 3, profit * 3, profit * 2.5m, profit * 1.5m, profit, 0);

    private static InventoryValueRowDto Stock(int id, string name, decimal cost, int outOfStock = 0, int negative = 0) =>
        new(id, name, 5, cost, cost * 1.3m, cost * 1.2m, outOfStock, 1, negative);

    private static (TestHost Host, ReportsPresenter Presenter, FakeReportsView View) Create()
    {
        var host = new TestHost(Permissions.ReportsView, Permissions.ProductsView);
        host.Categories.Items.Add(new CategoryDto(2, "Nước giải khát", null, true));
        host.Categories.Items.Add(new CategoryDto(1, "Mì gói", null, true));
        host.Reports.Revenue = p => Revenue(p, p == November ? 1_100_000 : 1_000_000);
        host.Reports.BestSellers = new BestSellerReportDto(November, ProductRanking.Quantity, [Sale(1, "Mì Hảo Hảo", 300_000), Sale(2, "Coca", 100_000)], 800_000, 9);
        host.Reports.Profit = new ProfitReportDto(November, [CategoryProfit(1, "Mì gói", 50_000), CategoryProfit(2, "Nước", 90_000)], CategoryProfit(0, "Tổng", 140_000));
        host.Reports.Inventory = new InventoryValueReportDto(DateTimeOffset.UnixEpoch, [Stock(1, "Mì gói", 1_000_000), Stock(2, "Nước", 3_000_000, outOfStock: 1)], Stock(0, "Tổng", 4_000_000, outOfStock: 1));
        var presenter = host.Get<ReportsPresenter>();
        var view = new FakeReportsView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Attaching_shows_empty_tabs_and_a_loading_status()
    {
        var (host, _, view) = Create();
        using var _h = host;

        Assert.Equal(RevenueBoard.Empty, Assert.Single(view.Revenues));
        Assert.Equal(InventoryBoard.Empty, Assert.Single(view.Inventories));
        Assert.Equal(("Đang tải báo cáo…", Tone.Muted), view.Status);
        Assert.Equal(new[] { true }, view.PeriodEnabled);
    }

    [Fact]
    public async Task Load_lists_categories_and_loads_only_the_revenue_tab_with_its_trend()
    {
        var (host, presenter, view) = Create();
        using var _h = host;

        await presenter.LoadAsync(November, "Tháng này");

        var (options, selected, visible) = Assert.Single(view.CategoryLists);
        Assert.Equal(new[] { "Tất cả danh mục", "Mì gói", "Nước giải khát" }, options.Select(o => o.Name));
        Assert.Null(selected);
        Assert.True(visible);

        Assert.Equal(new[] { "revenue:1101-1120", "revenue:1012-1031" }, host.Reports.Calls);
        var board = view.Revenues[^1];
        Assert.Equal(10m, board.NetRevenue.TrendPercent);
        Assert.True(board.NetRevenue.ShowTrend);
        Assert.Equal(20, board.Rows.Count);
        Assert.Equal(new DateTime(2026, 11, 20), board.Rows[0].Date);
        Assert.Equal(("Tháng này · cập nhật lúc 10:00", Tone.Muted), view.Status);
        Assert.True(presenter.IsLoaded(ReportTab.Revenue));
        Assert.False(presenter.IsLoaded(ReportTab.BestSellers));
        Assert.Equal(new[] { true, false }, view.Loading);
    }

    [Fact]
    public async Task Without_the_previous_period_the_revenue_card_has_no_trend_but_the_sales_total()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Reports.Failures["previous"] = Error.Conflict("offline");

        await presenter.LoadAsync(November, "Tháng này");

        var card = view.Revenues[^1].NetRevenue;
        Assert.Null(card.TrendPercent);
        Assert.StartsWith("Doanh số ", card.Note);
        Assert.Equal(Tone.Muted, view.Status.Tone);
    }

    [Fact]
    public async Task A_tab_loads_once_when_shown_and_not_again_when_revisited()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");

        await presenter.SelectTabAsync(ReportTab.BestSellers);
        await presenter.SelectTabAsync(ReportTab.Revenue);
        await presenter.SelectTabAsync(ReportTab.BestSellers);

        Assert.Equal(1, host.Reports.Calls.Count(c => c == "best"));
        Assert.Equal(2, host.Reports.Calls.Count(c => c.StartsWith("revenue", StringComparison.Ordinal)));
        Assert.Equal("Mì Hảo Hảo", view.BestSellers[^1].Rows[0].ProductName);
        Assert.Equal("2/9 sản phẩm có bán · chiếm 50% doanh thu (400.000 ₫)", view.BestSellers[^1].Subtitle);
    }

    [Fact]
    public async Task The_inventory_tab_turns_the_period_off_and_keeps_its_own_status()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");

        await presenter.SelectTabAsync(ReportTab.InventoryValue);
        Assert.False(view.PeriodEnabled[^1]);
        Assert.Contains("không theo kỳ", view.Status.Text);
        Assert.Equal("4.000.000 ₫", view.Inventories[^1].CostValue.Value);
        Assert.Equal(Tone.Danger, view.Inventories[^1].StockAlerts.Accent);

        // Back to Doanh thu (already loaded): its own status comes back instead of the inventory hint.
        await presenter.SelectTabAsync(ReportTab.Revenue);
        Assert.True(view.PeriodEnabled[^1]);
        Assert.Equal("Tháng này · cập nhật lúc 10:00", view.Status.Text);
    }

    [Fact]
    public async Task Changing_the_period_reloads_the_period_tabs_but_not_inventory()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        await presenter.SelectTabAsync(ReportTab.InventoryValue);
        await presenter.SelectTabAsync(ReportTab.ProfitByCategory);
        host.Reports.Calls.Clear();

        await presenter.SetPeriodAsync(October, "Tháng trước");
        Assert.Equal(new[] { "profit:1001-1031" }, host.Reports.Calls);
        Assert.Equal("Nước", view.Profits[^1].Rows[0].CategoryName);

        await presenter.SelectTabAsync(ReportTab.InventoryValue);
        await presenter.SelectTabAsync(ReportTab.Revenue);
        Assert.DoesNotContain("inventory", host.Reports.Calls);
        Assert.Contains("revenue:1001-1031", host.Reports.Calls);
        Assert.Equal("Tháng trước · cập nhật lúc 10:00", view.Status.Text);
    }

    [Fact]
    public async Task Refresh_reloads_inventory_and_the_category_list()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        await presenter.SelectTabAsync(ReportTab.InventoryValue);

        await presenter.RefreshAsync(November, "Tháng này");

        Assert.Equal(2, host.Reports.Calls.Count(c => c == "inventory"));
        Assert.Equal(2, view.CategoryLists.Count);
    }

    [Fact]
    public async Task Best_seller_filters_are_passed_and_an_unchanged_choice_does_not_reload()
    {
        var (host, presenter, _) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        await presenter.SelectTabAsync(ReportTab.BestSellers);

        await presenter.SetBestSellerFiltersAsync(ProductRanking.Profit, 50, 1);
        await presenter.SetBestSellerFiltersAsync(ProductRanking.Profit, 50, 1);

        Assert.Equal(2, host.Reports.BestSellerQueries.Count);
        Assert.Equal(new BestSellerQuery(November, ProductRanking.Profit, 50, 1), host.Reports.BestSellerQueries[^1]);
    }

    [Fact]
    public async Task Changing_a_best_seller_filter_on_another_tab_waits_until_the_tab_is_shown()
    {
        var (host, presenter, _) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");

        await presenter.SetBestSellerFiltersAsync(ProductRanking.Revenue, 10, null);
        Assert.DoesNotContain("best", host.Reports.Calls);

        await presenter.SelectTabAsync(ReportTab.BestSellers);
        Assert.Equal(new BestSellerQuery(November, ProductRanking.Revenue, 10, null), Assert.Single(host.Reports.BestSellerQueries));
    }

    [Fact]
    public async Task Without_categories_the_filter_is_hidden_and_ranks_across_all()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Categories.FailWith = Error.Forbidden("Không có quyền");

        await presenter.LoadAsync(November, "Tháng này");
        await presenter.SelectTabAsync(ReportTab.BestSellers);
        await presenter.SetBestSellerFiltersAsync(ProductRanking.Quantity, 20, 7);

        Assert.False(view.CategoryLists[^1].Visible);
        Assert.Null(presenter.CategoryId);
        Assert.All(host.Reports.BestSellerQueries, q => Assert.Null(q.CategoryId));
        Assert.Single(host.Reports.BestSellerQueries);
    }

    [Fact]
    public async Task A_chosen_category_that_disappears_on_refresh_falls_back_to_all()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        await presenter.SelectTabAsync(ReportTab.BestSellers);
        await presenter.SetBestSellerFiltersAsync(ProductRanking.Quantity, 20, 2);

        host.Categories.Items.RemoveAll(c => c.Id == 2);
        await presenter.RefreshAsync(November, "Tháng này");

        Assert.Null(presenter.CategoryId);
        Assert.Null(view.CategoryLists[^1].Selected);
        Assert.Null(host.Reports.BestSellerQueries[^1].CategoryId);
    }

    [Fact]
    public async Task A_failed_load_for_new_inputs_clears_the_tab_instead_of_showing_old_figures()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        host.Reports.Failures["revenue"] = Error.Conflict("Mất kết nối CSDL");

        await presenter.SetPeriodAsync(October, "Tháng trước");

        Assert.Equal(RevenueBoard.Empty, view.Revenues[^1]);
        Assert.Equal(("Mất kết nối CSDL", Tone.Danger), view.Status);
        Assert.False(presenter.IsLoaded(ReportTab.Revenue));
    }

    [Fact]
    public async Task A_failed_refresh_of_the_same_inputs_keeps_the_figures_and_says_how_old_they_are()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        var shown = view.Revenues.Count;
        host.Reports.Failures["revenue"] = Error.Conflict("Mất kết nối CSDL");
        host.Clock.Now = host.Clock.Now.AddMinutes(5);

        await presenter.RefreshAsync(November, "Tháng này");

        Assert.Equal(shown, view.Revenues.Count);
        Assert.Equal(Tone.Warning, view.Status.Tone);
        Assert.Equal("Không cập nhật được (Mất kết nối CSDL) · đang hiển thị số liệu lúc 10:00", view.Status.Text);
    }

    [Fact]
    public async Task A_failed_tab_is_not_retried_in_a_loop_but_again_when_shown_again()
    {
        var (host, presenter, _) = Create();
        using var _h = host;
        host.Reports.Failures["best"] = Error.Conflict("offline");
        await presenter.LoadAsync(November, "Tháng này");

        await presenter.SelectTabAsync(ReportTab.BestSellers);
        Assert.Single(host.Reports.Calls, c => c == "best");

        host.Reports.Failures.Remove("best");
        await presenter.SelectTabAsync(ReportTab.Revenue);
        await presenter.SelectTabAsync(ReportTab.BestSellers);
        Assert.Equal(2, host.Reports.Calls.Count(c => c == "best"));
        Assert.True(presenter.IsLoaded(ReportTab.BestSellers));
    }

    [Fact]
    public async Task A_period_changed_during_a_load_wins_over_the_older_answer()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        await presenter.SelectTabAsync(ReportTab.ProfitByCategory);
        host.Reports.Calls.Clear();
        var profitsBefore = view.Profits.Count;

        Task? newer = null;
        host.Reports.BeforeReportReturns = () =>
        {
            newer = presenter.SetPeriodAsync(October, "Tháng trước"); // returns at once: a load is running
            return Task.CompletedTask;
        };

        await presenter.SetPeriodAsync(new ReportPeriod(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), "Tháng 9");
        await newer!;

        Assert.Equal(new[] { "profit:0901-0930", "profit:1001-1031" }, host.Reports.Calls);
        Assert.Equal(profitsBefore + 1, view.Profits.Count); // only the newest answer was shown
        Assert.Equal("Tháng trước · cập nhật lúc 10:00", view.Status.Text);
        Assert.Equal(new[] { true, false }, view.Loading.TakeLast(2));
    }

    [Fact]
    public async Task Export_sends_the_current_tab_with_the_filters_and_confirms_with_a_toast()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        await presenter.SelectTabAsync(ReportTab.BestSellers);
        await presenter.SetBestSellerFiltersAsync(ProductRanking.Revenue, 10, 1);

        await presenter.ExportAsync(all: false);

        var request = Assert.Single(host.ReportExports.Exports);
        Assert.Equal(new[] { ReportKind.BestSellers }, request.Reports);
        Assert.Equal((November, ProductRanking.Revenue, 10, (int?)1, "Mì gói"), (request.Period, request.RankBy, request.Top, request.CategoryId, request.CategoryName));
        Assert.Equal(new[] { "BaoCao.xlsx" }, view.SavedFiles);
        Assert.Equal(new[] { "Đã lưu BaoCao.xlsx." }, view.Successes);
        Assert.False(presenter.IsBusy);
    }

    [Fact]
    public async Task Export_all_covers_every_report_and_a_cancelled_save_shows_nothing()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        view.SavePath = null;

        await presenter.ExportAsync(all: true);

        Assert.Equal(
            new[] { ReportKind.Revenue, ReportKind.BestSellers, ReportKind.ProfitByCategory, ReportKind.InventoryValue },
            host.ReportExports.Exports[0].Reports);
        Assert.Null(host.ReportExports.Exports[0].CategoryName);
        Assert.Empty(view.Successes);
    }

    [Fact]
    public async Task A_failed_export_warns_and_nothing_is_saved()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        host.ReportExports.FailWith = Error.Forbidden("Bạn không có quyền xem báo cáo.");

        await presenter.ExportAsync(all: false);

        Assert.Equal(new[] { "Bạn không có quyền xem báo cáo." }, view.Warnings);
        Assert.Empty(view.SavedFiles);
    }

    [Fact]
    public async Task Export_before_the_first_load_does_nothing()
    {
        var (host, presenter, view) = Create();
        using var _h = host;

        await presenter.ExportAsync(all: false);
        await presenter.PrintAsync(all: false);

        Assert.Empty(host.ReportExports.Exports);
        Assert.Empty(host.ReportExports.Prepared);
        Assert.Empty(view.BusyMessages);
    }

    [Fact]
    public async Task Print_opens_the_preview_and_records_each_output()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        view.WhilePreviewOpen = async output =>
        {
            await output(ReportOutput.Printer, null);
            await output(ReportOutput.Pdf, @"C:\BaoCao\DoanhThu.pdf");
        };

        await presenter.PrintAsync(all: false);

        Assert.Equal(new[] { ReportKind.Revenue }, Assert.Single(host.ReportExports.Prepared).Reports);
        Assert.Single(view.Previews);
        Assert.Equal(new[] { (ReportOutput.Printer, (string?)null), (ReportOutput.Pdf, @"C:\BaoCao\DoanhThu.pdf") }, host.ReportExports.Printed);
        Assert.Equal(2, view.Successes.Count);
        Assert.EndsWith("DoanhThu.pdf.", view.Successes[1]);
        Assert.False(presenter.IsBusy);
    }

    [Fact]
    public async Task A_failed_print_preparation_warns_without_a_preview()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(November, "Tháng này");
        host.ReportExports.FailWith = Error.Conflict("Không đọc được dữ liệu");

        await presenter.PrintAsync(all: true);

        Assert.Empty(view.Previews);
        Assert.Equal(new[] { "Không đọc được dữ liệu" }, view.Warnings);
    }

    // ---- Boards -------------------------------------------------------------------------------------------

    [Fact]
    public void Revenue_board_switches_to_monthly_bars_for_long_periods()
    {
        var year = new ReportPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var board = RevenueBoard.From(Revenue(year, 500_000), null);

        Assert.Equal("Doanh thu thuần theo tháng", board.ChartTitle);
        Assert.Equal(6, board.Bars.Count);
        Assert.True(board.ShowBarValues);
        Assert.Equal("T1/2026", board.Bars[0].Label);

        var month = RevenueBoard.From(Revenue(November, 500_000), null);
        Assert.Equal("Doanh thu thuần theo ngày", month.ChartTitle);
        Assert.False(month.ShowBarValues); // 20 bars: values would overlap
    }

    [Fact]
    public void Revenue_board_without_sales_says_so()
    {
        var board = RevenueBoard.From(Revenue(November, 0), null);

        Assert.Equal("Không có doanh thu", board.ChartSubtitle);
        Assert.Equal("0 ₫", board.NetRevenue.Value);
    }

    [Fact]
    public void Profit_board_orders_by_profit_and_notes_categories_left_out_of_the_chart()
    {
        var rows = Enumerable.Range(1, 15).Select(i => CategoryProfit(i, $"DM{i:00}", i * 10_000)).ToList();
        var board = ProfitBoard.From(new ProfitReportDto(November, rows, CategoryProfit(0, "Tổng", 1_200_000)));

        Assert.Equal("DM15", board.Rows[0].CategoryName);
        Assert.Equal(ProfitBoard.MaxBars, board.Bars.Count);
        Assert.EndsWith("biểu đồ 12/15 danh mục", board.Subtitle);
        Assert.Equal(ProfitBoard.Empty, ProfitBoard.From(new ProfitReportDto(November, [], CategoryProfit(0, "Tổng", 0))));
    }

    [Fact]
    public void Inventory_board_flags_negative_stock_and_a_negative_potential_profit()
    {
        var totals = new InventoryValueRowDto(0, "Tổng", 10, 2_000_000, 1_500_000, 1_400_000, 0, 2, 3);
        var board = InventoryBoard.From(new InventoryValueReportDto(DateTimeOffset.UnixEpoch, [], totals));

        Assert.Equal("3 SP tồn âm – kiểm tra phiếu nhập", board.StockAlerts.Note);
        Assert.Equal(Tone.Warning, board.StockAlerts.Accent);
        Assert.Equal(Tone.Danger, board.PotentialProfit.Accent);
        Assert.Equal("0 / 2", board.StockAlerts.Value);
    }

    [Theory]
    [InlineData(950, "950")]
    [InlineData(850_000, "850 N")]
    [InlineData(1_250_000, "1,3 tr")]
    [InlineData(-2_000_000, "-2 tr")]
    [InlineData(3_400_000_000, "3,4 tỷ")]
    public void Short_money_labels(decimal amount, string expected) => Assert.Equal(expected, ReportText.ShortMoney(amount));

    [Fact]
    public void Share_handles_zero_totals() => Assert.Equal(("0%", "12,5%"), (ReportText.Share(5, 0), ReportText.Share(1, 8)));
}
