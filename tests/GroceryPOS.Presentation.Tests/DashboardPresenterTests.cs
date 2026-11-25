using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.Presentation.Dashboard;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Tests;

public sealed class DashboardPresenterTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);
    private static readonly DateOnly Today = new(2026, 11, 20);

    private sealed class FakeDashboardView : RecordingView, IDashboardView
    {
        public bool CanNavigate { get; set; } = true;
        public List<(IReadOnlySet<DashboardShortcut> Shortcuts, bool ShiftCard)> Layouts { get; } = new();
        public List<DashboardHeader> Headers { get; } = new();
        public List<DashboardBoard> Boards { get; } = new();
        public List<DashboardCard> ShiftCards { get; } = new();
        public List<DashboardShortcut> Navigations { get; } = new();

        public DashboardHeader Header => Headers[^1];
        public DashboardCard ShiftCard => ShiftCards[^1];

        public void ShowLayout(IReadOnlySet<DashboardShortcut> shortcuts, bool showShiftCard) => Layouts.Add((shortcuts, showShiftCard));

        public void ShowHeader(DashboardHeader header) => Headers.Add(header);

        public void ShowBoard(DashboardBoard board)
        {
            Boards.Add(board);
            Headers.Add(board.Header);
        }

        public void ShowShiftCard(DashboardCard card) => ShiftCards.Add(card);

        public void Navigate(DashboardShortcut shortcut) => Navigations.Add(shortcut);
    }

    private static DashboardDto Data(
        decimal revenue = 2_500_000,
        int invoices = 10,
        decimal? profit = 600_000,
        decimal refunds = 0,
        decimal? change = 25,
        int outOfStock = 1,
        int low = 1,
        IReadOnlyList<LowStockAlert>? lowItems = null,
        IReadOnlyList<DashboardTopProductDto>? top = null,
        IReadOnlyList<HourlyPointDto>? hours = null) => new(
        Today,
        revenue,
        invoices,
        invoices == 0 ? 0 : revenue / invoices,
        refunds,
        profit,
        2_000_000,
        change,
        Enumerable.Range(0, 7).Select(i => new DailyPointDto(Today.AddDays(i - 6), i == 3 ? 3_000_000 : 1_000_000, 5)).ToList(),
        hours ?? [new HourlyPointDto(8, 400_000, 3), new HourlyPointDto(9, 900_000, 4)],
        top ?? [new DashboardTopProductDto(1, "SP001", "Mì Hảo Hảo", "gói", 24, 120_000), new DashboardTopProductDto(2, "SP002", "Gạo ST25", "kg", 2.5m, 75_000)],
        outOfStock,
        low,
        lowItems ?? [new LowStockAlert(3, "SP003", "Nước mắm", 0, 5), new LowStockAlert(4, "SP004", "Đường", 2, 10)]);

    private static ShiftSummaryDto Shift(DateTimeOffset openedAt) => new(
        7, "CA261120-0007", 1, "Thu ngân An", ShiftStatus.Open, "Đang mở", openedAt, null, 500_000,
        new ShiftTotals(3, 300_000, 0, 300_000, 0, 0, 0, 0, 0, 0, 0, 0),
        0, 0, 800_000, null, null, null, null, null, null, []);

    private static (TestHost Host, DashboardPresenter Presenter, FakeDashboardView View) Create(
        bool canNavigate = true,
        params string[] permissions)
    {
        var host = new TestHost(permissions.Length == 0
            ? [Permissions.DashboardView, Permissions.SalesCreate, Permissions.InventoryView, Permissions.ShiftsManage, Permissions.ReportsView]
            : permissions);
        host.CurrentUser.Session = new UserSession(1, "an", "Thu ngân An", "Thu ngân", new HashSet<string>(), DateTimeOffset.UnixEpoch);
        host.Reports.Dashboard = Data();
        var presenter = host.Get<DashboardPresenter>();
        var view = new FakeDashboardView { CanNavigate = canNavigate };
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Attaching_shows_the_permitted_shortcuts_and_a_loading_header()
    {
        var (host, _, view) = Create();
        using var _h = host;

        var (shortcuts, shiftCard) = Assert.Single(view.Layouts);
        Assert.Equal(new[] { DashboardShortcut.Pos, DashboardShortcut.Inventory }, shortcuts.OrderBy(s => s));
        Assert.True(shiftCard);
        Assert.Equal(DashboardHeader.Loading, view.Header);
        Assert.Equal(DashboardShiftCard.Loading, view.ShiftCard);
    }

    [Fact]
    public async Task Without_shift_permission_the_shift_card_is_hidden_and_not_loaded()
    {
        var (host, presenter, view) = Create(true, Permissions.DashboardView);
        using var _h = host;

        await presenter.LoadAsync();

        Assert.False(view.Layouts[0].ShiftCard);
        Assert.Empty(view.Layouts[0].Shortcuts);
        Assert.Empty(view.ShiftCards);
        Assert.DoesNotContain("current", host.Shifts.Calls);
        Assert.Single(view.Boards);
    }

    [Fact]
    public void Without_navigation_no_shortcut_is_shown_or_opened()
    {
        var (host, presenter, view) = Create(canNavigate: false);
        using var _h = host;

        Assert.Empty(view.Layouts[0].Shortcuts);
        Assert.False(presenter.Open(DashboardShortcut.Pos));
        Assert.Empty(view.Navigations);
    }

    [Fact]
    public void Open_navigates_only_with_permission()
    {
        var (host, presenter, view) = Create();
        using var _h = host;

        Assert.True(presenter.Open(DashboardShortcut.Inventory));
        Assert.False(presenter.Open(DashboardShortcut.Purchases));
        Assert.Equal(new[] { DashboardShortcut.Inventory }, view.Navigations);
    }

    [Fact]
    public async Task Load_shows_the_kpis_with_greeting_and_trend()
    {
        var (host, presenter, view) = Create();
        using var _h = host;

        await presenter.LoadAsync();

        var board = Assert.Single(view.Boards);
        var now = host.Clock.GetLocalNow().DateTime;
        Assert.Equal($"{DashboardFigures.Greeting(now.Hour)}, Thu ngân An!", board.Header.Greeting);
        Assert.Equal($"Thứ sáu, 20/11/2026 · cập nhật lúc {now:HH:mm}", board.Header.Subtitle);
        Assert.Equal(Tone.Muted, board.Header.SubtitleTone);
        Assert.Equal("2.500.000 ₫", board.Revenue.Value);
        Assert.True(board.Revenue.ShowTrend);
        Assert.Equal(25, board.Revenue.TrendPercent);
        Assert.Null(board.Revenue.Note);
        Assert.Equal("10", board.Invoices.Value);
        Assert.Equal("TB 250.000 ₫ / hóa đơn", board.Invoices.Note);
        Assert.Equal(DashboardProfitKind.Profit, board.ProfitKind);
        Assert.Equal("600.000 ₫", board.Profit.Value);
        Assert.Equal(Tone.Info, board.Profit.Accent);
        Assert.Same(board, presenter.Board);
    }

    [Fact]
    public async Task Cashiers_see_refunds_instead_of_profit()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Reports.Dashboard = Data(profit: null, refunds: 45_000);

        await presenter.LoadAsync();

        var board = view.Boards[^1];
        Assert.Equal(DashboardProfitKind.Refunds, board.ProfitKind);
        Assert.Equal(DashboardBoard.RefundCaption, board.Profit.Caption);
        Assert.Equal("45.000 ₫", board.Profit.Value);
        Assert.Equal(Tone.Warning, board.Profit.Accent);
    }

    [Fact]
    public async Task A_loss_is_shown_in_red_and_no_trend_shows_yesterday()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Reports.Dashboard = Data(profit: -10_000, change: null, invoices: 0, revenue: 0);

        await presenter.LoadAsync();

        var board = view.Boards[^1];
        Assert.Equal(Tone.Danger, board.Profit.Accent);
        Assert.Equal("Hôm qua: 2.000.000 ₫", board.Revenue.Note);
        Assert.Equal("Chưa có hóa đơn", board.Invoices.Note);
    }

    [Fact]
    public async Task No_open_shift_asks_to_open_one()
    {
        var (host, presenter, view) = Create();
        using var _h = host;

        await presenter.LoadAsync();

        Assert.Equal("Chưa mở ca", view.ShiftCard.Value);
        Assert.Equal(Tone.Warning, view.ShiftCard.Accent);
    }

    [Fact]
    public async Task Open_shift_shows_the_cash_in_the_drawer_in_store_time()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Shifts.Current = new CurrentShiftDto(Shift(new DateTimeOffset(2026, 11, 20, 7, 30, 0, Vn)), 0);

        await presenter.LoadAsync();

        var card = view.ShiftCard;
        Assert.Equal("Ca CA261120-0007", card.Caption);
        Assert.Equal("800.000 ₫", card.Value);
        Assert.Equal("Tiền mặt trong két · mở lúc 07:30", card.Note);
        Assert.Equal(Tone.Success, card.Accent);
    }

    [Fact]
    public async Task A_shift_open_for_12_hours_or_more_is_flagged()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Shifts.Current = new CurrentShiftDto(Shift(new DateTimeOffset(2026, 11, 19, 20, 0, 0, Vn)), 0);

        await presenter.LoadAsync();

        Assert.Equal(Tone.Warning, view.ShiftCard.Accent);
        Assert.Equal("Đã mở 14 giờ (từ 20:00 19/11) · nhớ kết ca", view.ShiftCard.Note);
    }

    [Fact]
    public async Task A_failed_shift_load_does_not_hide_the_figures()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Shifts.FailCurrentWith = new Error("db", "Mất kết nối");

        await presenter.LoadAsync();

        Assert.Single(view.Boards);
        Assert.Equal(DashboardShiftCard.Failed, view.ShiftCard);
        Assert.Empty(view.Errors);
    }

    [Fact]
    public async Task First_load_failure_shows_the_error_and_toasts_on_F5_only()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Reports.FailWith = new Error("db", "Không kết nối được cơ sở dữ liệu.");

        await presenter.LoadAsync(automatic: true);
        Assert.Equal("Không kết nối được cơ sở dữ liệu.", view.Header.Subtitle);
        Assert.Equal(Tone.Danger, view.Header.SubtitleTone);
        Assert.StartsWith("Chào", view.Header.Greeting);
        Assert.Empty(view.Errors);

        await presenter.LoadAsync();
        Assert.Equal(new[] { "Không kết nối được cơ sở dữ liệu." }, view.Errors);
        Assert.Empty(view.Boards);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_last_figures_and_says_how_old_they_are()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync();
        var loadedAt = host.Clock.GetLocalNow().DateTime;

        host.Clock.Now = host.Clock.Now.AddMinutes(3);
        host.Reports.FailWith = new Error("db", "Mất kết nối");
        await presenter.LoadAsync(automatic: true);

        var now = host.Clock.GetLocalNow().DateTime;
        Assert.Single(view.Boards);
        Assert.Equal(Tone.Warning, view.Header.SubtitleTone);
        Assert.Equal($"Không cập nhật được lúc {now:HH:mm} · đang hiển thị số liệu lúc {loadedAt:HH:mm}", view.Header.Subtitle);
        Assert.Empty(view.Errors);
        Assert.NotNull(presenter.Board);

        await presenter.LoadAsync();
        Assert.Equal(new[] { "Mất kết nối" }, view.Errors);

        host.Reports.FailWith = null;
        await presenter.LoadAsync(automatic: true);
        Assert.Equal(2, view.Boards.Count);
        Assert.Equal(Tone.Muted, view.Header.SubtitleTone);
    }

    [Fact]
    public async Task The_timer_skips_its_tick_while_a_load_is_running()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var gate = new TaskCompletionSource();
        host.Reports.BeforeDashboardReturns = () => gate.Task;

        var first = presenter.LoadAsync();
        Assert.True(presenter.IsLoading);
        await presenter.LoadAsync(automatic: true);
        Assert.Equal(1, host.Reports.DashboardCalls);

        gate.SetResult();
        await first;
        Assert.False(presenter.IsLoading);
        Assert.Single(view.Boards);
    }

    [Fact]
    public async Task A_slow_load_cannot_overwrite_a_newer_one()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var gate = new TaskCompletionSource();
        host.Reports.Dashboard = Data(revenue: 1_000_000);
        host.Reports.BeforeDashboardReturns = () => gate.Task;

        var slow = presenter.LoadAsync();
        host.Reports.Dashboard = Data(revenue: 3_000_000);
        await presenter.LoadAsync();
        gate.SetResult();
        await slow;

        var board = Assert.Single(view.Boards);
        Assert.Equal("3.000.000 ₫", board.Revenue.Value);
    }

    [Fact]
    public async Task Nothing_is_shown_after_the_view_closed()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.IsAlive = false;

        await presenter.LoadAsync();

        Assert.Empty(view.Boards);
        Assert.Single(view.ShiftCards); // only the loading placeholder from Attach
    }

    // ---- DashboardBoard ---------------------------------------------------------------------------------

    [Fact]
    public void Board_lists_and_charts()
    {
        var now = new DateTime(2026, 11, 20, 9, 15, 0);
        var board = DashboardBoard.From(Data(outOfStock: 3, low: 4), "  Chị Hoa ", now);

        Assert.Equal("Chào buổi sáng, Chị Hoa!", board.Header.Greeting);
        Assert.Equal(new[] { "24 gói", "2,5 kg" }, board.TopProducts.Select(r => r.QuantityText));
        Assert.Equal("Top 2 theo doanh thu", board.TopSubtitle);
        Assert.Equal("3 hết hàng · 4 sắp hết (hiện 2)", board.LowSubtitle);
        Assert.Equal(new[] { Tone.Danger, Tone.Warning }, board.LowStock.Select(r => r.StockTone));

        Assert.Equal(7, board.Week.Bars.Count);
        Assert.Equal(6, board.Week.HighlightIndex);
        Assert.Equal("Hôm nay", board.Week.Bars[6].Label);
        Assert.Contains("cao nhất T3 17/11", board.Week.Subtitle);

        // 6h–22h window, current hour 9 highlighted, peak 9h.
        Assert.Equal("6h", board.Hours.Bars[0].Label);
        Assert.Equal(17, board.Hours.Bars.Count);
        Assert.Equal(3, board.Hours.HighlightIndex);
        Assert.Equal("Đông khách nhất 9h–10h · 900.000 ₫ (4 HĐ)", board.Hours.Subtitle);
    }

    [Fact]
    public void Board_for_an_empty_day()
    {
        var board = DashboardBoard.From(
            Data(revenue: 0, invoices: 0, outOfStock: 0, low: 0, lowItems: [], top: [], hours: []),
            null,
            new DateTime(2026, 11, 20, 20, 0, 0));

        Assert.Equal("Chào buổi tối", board.Header.Greeting);
        Assert.Null(board.TopSubtitle);
        Assert.Equal("Tồn kho ổn định", board.LowSubtitle);
        Assert.Equal("Chưa có hóa đơn hôm nay", board.Hours.Subtitle);
        Assert.Equal(14, board.Hours.HighlightIndex);
    }
}
