using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Shifts;

namespace GroceryPOS.Presentation.Tests;

public sealed class ShiftPresenterTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);
    private static readonly ShiftHistoryFilter Week = new(new DateTime(2026, 11, 14), new DateTime(2026, 11, 20));

    private sealed class FakeShiftView : RecordingView, IShiftView
    {
        public List<CurrentShiftState> CurrentShown { get; } = new();
        public List<IReadOnlyList<ShiftRow>> HistoryShown { get; } = new();
        public List<string?> PeriodErrors { get; } = new();
        public List<(decimal Float, string? Cashier)> OpenDialogs { get; } = new();
        public ShiftSummaryDto? OpenResult { get; set; }
        public List<(ShiftSummaryDto Shift, CashMovementType Type)> CashDialogs { get; } = new();
        public ShiftSummaryDto? CashResult { get; set; }
        public List<(ShiftSummaryDto Shift, bool ForOther)> CloseDialogs { get; } = new();
        public ShiftSummaryDto? CloseResult { get; set; }
        public List<ShiftSummaryDto> Reports { get; } = new();

        public void ShowCurrent(CurrentShiftState state) => CurrentShown.Add(state);

        public void ShowHistory(IReadOnlyList<ShiftRow> rows) => HistoryShown.Add(rows);

        public void ShowPeriodError(string? message) => PeriodErrors.Add(message);

        public ShiftSummaryDto? OpenShift(decimal suggestedFloat, string? cashierName)
        {
            OpenDialogs.Add((suggestedFloat, cashierName));
            return OpenResult;
        }

        public ShiftSummaryDto? RecordCashMovement(ShiftSummaryDto shift, CashMovementType type)
        {
            CashDialogs.Add((shift, type));
            return CashResult;
        }

        public ShiftSummaryDto? CloseShift(ShiftSummaryDto shift, bool closingForSomeoneElse)
        {
            CloseDialogs.Add((shift, closingForSomeoneElse));
            return CloseResult;
        }

        public void ShowReport(ShiftSummaryDto shift) => Reports.Add(shift);
    }

    private static readonly ShiftTotals Totals = new(
        SalesCount: 12, SalesTotal: 1_500_000, DiscountTotal: 20_000, CashSales: 1_000_000, TransferSales: 400_000, CardSales: 100_000,
        VoidCount: 1, VoidTotal: 50_000, VoidCash: 50_000, ReturnCount: 1, CashRefunds: 30_000, TransferRefunds: 0);

    private static ShiftSummaryDto Summary(
        int id,
        int userId = 1,
        ShiftStatus status = ShiftStatus.Open,
        DateTimeOffset? openedAt = null,
        decimal? variance = null,
        IReadOnlyList<CashMovementDto>? movements = null) => new(
        id, $"CA261120-{id:0000}", userId, userId == 1 ? "Thu ngân An" : "Thu ngân Bình", status,
        status == ShiftStatus.Open ? "Đang mở" : "Đã kết ca",
        openedAt ?? new DateTimeOffset(2026, 11, 20, 7, 30, 0, Vn),
        status == ShiftStatus.Open ? null : new DateTimeOffset(2026, 11, 20, 15, 0, 0, Vn),
        500_000, Totals, 100_000, 50_000, 1_470_000,
        status == ShiftStatus.Open ? null : 1_470_000 + (variance ?? 0), variance, null, null, null, null,
        movements ?? []);

    private static ShiftListItemDto Item(int id, int userId, ShiftStatus status, DateTimeOffset openedAt, decimal? variance = null) => new(
        id, $"CA{openedAt:yyMMdd}-{id:0000}", userId, userId == 1 ? "Thu ngân An" : "Thu ngân Bình", status,
        status == ShiftStatus.Open ? "Đang mở" : "Đã kết ca", openedAt,
        status == ShiftStatus.Open ? null : openedAt.AddHours(8), 1_000_000, variance);

    private static (TestHost Host, ShiftPresenter Presenter, FakeShiftView View) Create(bool supervise = false)
    {
        var host = supervise
            ? new TestHost(Permissions.ShiftsManage, Permissions.ShiftsSupervise)
            : new TestHost(Permissions.ShiftsManage);
        host.CurrentUser.Session = new UserSession(1, "an", "Thu ngân An", "Thu ngân", new HashSet<string>(), DateTimeOffset.UnixEpoch);
        host.Shifts.Shifts.AddRange(new[]
        {
            Item(1, 1, ShiftStatus.Closed, new DateTimeOffset(2026, 11, 15, 7, 0, 0, Vn), -20_000),
            Item(2, 2, ShiftStatus.Closed, new DateTimeOffset(2026, 11, 18, 7, 0, 0, Vn), 0),
            Item(3, 2, ShiftStatus.Open, new DateTimeOffset(2026, 11, 20, 6, 0, 0, Vn)),
            Item(4, 1, ShiftStatus.Closed, new DateTimeOffset(2026, 11, 10, 7, 0, 0, Vn), 5_000), // outside the week
        });

        var presenter = host.Get<ShiftPresenter>();
        var view = new FakeShiftView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Supervise_permission_drives_history_title_and_close_for_other()
    {
        var (cashierHost, cashier, _) = Create();
        var (supervisorHost, supervisor, _) = Create(supervise: true);
        using var a = cashierHost;
        using var b = supervisorHost;
        var othersOpen = ShiftRow.From(Item(3, 2, ShiftStatus.Open, DateTimeOffset.Now), Vn);
        var ownOpen = ShiftRow.From(Item(5, 1, ShiftStatus.Open, DateTimeOffset.Now), Vn);
        var othersClosed = ShiftRow.From(Item(2, 2, ShiftStatus.Closed, DateTimeOffset.Now), Vn);

        Assert.Equal("Lịch sử ca của bạn", cashier.HistoryTitle);
        Assert.Equal("Lịch sử ca (tất cả nhân viên)", supervisor.HistoryTitle);
        Assert.False(cashier.CanCloseForOther(othersOpen));
        Assert.True(supervisor.CanCloseForOther(othersOpen));
        Assert.False(supervisor.CanCloseForOther(ownOpen));
        Assert.False(supervisor.CanCloseForOther(othersClosed));
        Assert.False(supervisor.CanCloseForOther(null));
        Assert.False(supervisor.CanViewReport(null));
        Assert.True(supervisor.CanViewReport(othersClosed));
    }

    [Fact]
    public async Task Load_without_open_shift_offers_mo_ca_with_the_previous_float()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Current = new CurrentShiftDto(null, 300_000);

        await presenter.LoadAsync(Week);

        var state = view.CurrentShown.Single();
        Assert.True(state.CanOpen);
        Assert.False(state.IsOpen);
        Assert.Equal("Chưa mở ca", state.BadgeText);
        Assert.Equal(Tone.Muted, state.BadgeTone);
        Assert.Equal("Ca trước để lại két 300.000 ₫. Mở ca để bắt đầu bán hàng.", state.Subtitle);
        Assert.Equal("–", state.Revenue.Value);
    }

    [Fact]
    public async Task Load_with_open_shift_shows_kpis()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Current = new CurrentShiftDto(Summary(9), 0);

        await presenter.LoadAsync(Week);

        var state = view.CurrentShown.Single();
        Assert.True(state.IsOpen);
        Assert.False(state.CanOpen);
        Assert.Equal("Đang mở", state.BadgeText);
        Assert.Equal(Tone.Info, state.BadgeTone);
        Assert.Equal("Ca CA261120-0009", state.Title);
        Assert.Equal("Thu ngân An · mở lúc 07:30 20/11/2026 · tiền đầu ca 500.000 ₫", state.Subtitle);
        Assert.Equal("1.420.000 ₫", state.Revenue.Value); // 1.500.000 − 50.000 void − 30.000 refund
        Assert.Equal("Đã giảm giá 20.000 ₫", state.Revenue.Note);
        Assert.Equal("12", state.Invoices.Value);
        Assert.Equal("1 hủy · 1 trả hàng", state.Invoices.Note);
        Assert.Equal("1.470.000 ₫", state.Cash.Value);
        Assert.Equal("Nộp 100.000 · chi 50.000", state.Cash.Note);
        Assert.Equal("500.000 ₫", state.NonCash.Value);
        Assert.Equal("CK 400.000 · thẻ 100.000", state.NonCash.Note);
    }

    [Fact]
    public async Task Shift_open_for_more_than_twelve_hours_is_flagged()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Current = new CurrentShiftDto(Summary(9, openedAt: new DateTimeOffset(2026, 11, 19, 7, 0, 0, Vn)), 0);

        await presenter.LoadAsync(Week);

        var state = view.CurrentShown.Single();
        Assert.Equal("Mở quá lâu", state.BadgeText);
        Assert.Equal(Tone.Warning, state.BadgeTone);
        Assert.EndsWith("đã mở 27 giờ, nhớ kết ca cuối ngày", state.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_current_shift_hides_drawer_actions_but_still_loads_history()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.FailCurrentWith = Error.Forbidden();

        await presenter.LoadAsync(Week);

        var state = view.CurrentShown.Single();
        Assert.False(state.CanOpen);
        Assert.False(state.IsOpen);
        Assert.Equal("Không tải được ca làm việc", state.Title);
        Assert.Equal(Error.Forbidden().Message, state.Subtitle);
        Assert.Null(presenter.Current);
        Assert.Single(view.HistoryShown);
    }

    [Fact]
    public async Task History_uses_store_time_inclusive_to_date_and_tones()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.LoadAsync(Week);

        var search = host.Shifts.Searches.Single();
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 0, 0, 0, Vn), search.From);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 0, 0, 0, Vn), search.To);
        Assert.Null(search.Status);
        var rows = view.HistoryShown.Single();
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.ShiftId));
        Assert.Equal(new DateTime(2026, 11, 20, 6, 0, 0), rows.Single(r => r.ShiftId == 3).OpenedAt);
        Assert.Equal(Tone.Danger, rows.Single(r => r.ShiftId == 1).VarianceTone);
        Assert.Equal(Tone.Success, rows.Single(r => r.ShiftId == 2).VarianceTone);
        Assert.Equal(Tone.Muted, rows.Single(r => r.ShiftId == 3).VarianceTone);
        Assert.Equal(Tone.Info, rows.Single(r => r.ShiftId == 3).StatusTone);
        Assert.Equal(new string?[] { null }, view.PeriodErrors);
    }

    [Fact]
    public async Task Only_open_filter_reloads_history_without_the_current_shift()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(Week);
        host.Shifts.Calls.Clear();

        await presenter.FilterAsync(Week with { OnlyOpen = true });

        Assert.Equal(ShiftStatus.Open, host.Shifts.Searches[^1].Status);
        Assert.Equal(new[] { 3 }, view.HistoryShown[^1].Select(r => r.ShiftId));
        Assert.DoesNotContain("current", host.Shifts.Calls);
        Assert.True(presenter.Filter.OnlyOpen);
    }

    [Fact]
    public async Task Inverted_period_marks_the_from_date_and_does_not_search()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.FilterAsync(new ShiftHistoryFilter(new DateTime(2026, 11, 20), new DateTime(2026, 11, 14)));

        Assert.Equal(new[] { "Từ ngày phải trước đến ngày." }, view.PeriodErrors);
        Assert.Empty(host.Shifts.Searches);
        Assert.Empty(view.HistoryShown);
    }

    [Fact]
    public async Task Failed_history_shows_an_error_toast_and_keeps_the_rows()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(Week);
        host.Shifts.FailListWith = Error.Forbidden();

        await presenter.FilterAsync(Week with { OnlyOpen = true });

        Assert.Single(view.HistoryShown);
        Assert.Equal(3, presenter.History.Count);
        Assert.Equal(new[] { $"Không tải được lịch sử ca: {Error.Forbidden().Message}" }, view.Errors);
    }

    [Fact]
    public async Task Slow_older_history_load_does_not_overwrite_a_newer_one()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        var gate = new TaskCompletionSource();
        host.Shifts.BeforeListReturns = () => gate.Task;

        var older = presenter.FilterAsync(Week);
        host.Shifts.BeforeListReturns = null;
        await presenter.FilterAsync(Week with { OnlyOpen = true });
        gate.SetResult();
        await older;

        Assert.Equal(new[] { 3 }, view.HistoryShown.Single().Select(r => r.ShiftId));
    }

    [Fact]
    public async Task Open_shift_passes_the_suggested_float_toasts_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Current = new CurrentShiftDto(null, 300_000);
        view.OpenResult = Summary(9);

        await presenter.OpenShiftAsync();

        Assert.Equal((300_000m, "Thu ngân An"), view.OpenDialogs.Single());
        Assert.Equal(new[] { "Đã mở ca CA261120-0009 với tiền đầu ca 500.000 ₫." }, view.Successes);
        Assert.Single(view.HistoryShown);
    }

    [Fact]
    public async Task Open_shift_backed_out_does_nothing_more()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.OpenShiftAsync();

        Assert.Single(view.OpenDialogs);
        Assert.Empty(view.Successes);
        Assert.Empty(view.HistoryShown);
    }

    [Fact]
    public async Task Open_shift_when_one_was_opened_elsewhere_shows_it_instead()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Current = new CurrentShiftDto(Summary(9), 0);

        await presenter.OpenShiftAsync();

        Assert.Empty(view.OpenDialogs);
        Assert.Equal(new[] { "Bạn đã có ca CA261120-0009 đang mở." }, view.Infos);
        Assert.True(view.CurrentShown.Single().IsOpen);
    }

    [Fact]
    public async Task Pay_out_uses_the_fresh_shift_and_toasts_the_amount_and_drawer()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Current = new CurrentShiftDto(Summary(9), 0);
        view.CashResult = Summary(9, movements:
        [
            new CashMovementDto(1, CashMovementType.PayOut, "Chi", 20_000, "Mua đá", new DateTimeOffset(2026, 11, 20, 8, 0, 0, Vn)),
            new CashMovementDto(2, CashMovementType.PayIn, "Nộp", 100_000, "Tiền lẻ", new DateTimeOffset(2026, 11, 20, 9, 0, 0, Vn)),
            new CashMovementDto(3, CashMovementType.PayOut, "Chi", 50_000, "Trả tiền giao hàng", new DateTimeOffset(2026, 11, 20, 9, 30, 0, Vn)),
        ]);

        await presenter.MoveCashAsync(CashMovementType.PayOut);

        Assert.Equal(CashMovementType.PayOut, view.CashDialogs.Single().Type);
        Assert.Equal(9, view.CashDialogs.Single().Shift.ShiftId);
        Assert.Equal(new[] { "Đã chi 50.000 ₫ (Trả tiền giao hàng). Tiền mặt trong két: 1.470.000 ₫." }, view.Successes);
        Assert.Equal(2, host.Shifts.Calls.Count(c => c == "current")); // before the dialog and after saving
    }

    [Fact]
    public async Task Drawer_action_on_a_shift_closed_elsewhere_warns_and_refreshes()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.MoveCashAsync(CashMovementType.PayIn);
        await presenter.ShowCurrentReportAsync();
        await presenter.CloseOwnShiftAsync();

        Assert.Empty(view.CashDialogs);
        Assert.Empty(view.Reports);
        Assert.Empty(view.CloseDialogs);
        Assert.Equal(3, view.Warnings.Count);
        Assert.All(view.Warnings, w => Assert.StartsWith("Ca của bạn không còn mở", w, StringComparison.Ordinal));
        Assert.Equal(3, view.HistoryShown.Count);
    }

    [Fact]
    public async Task Mid_shift_report_shows_the_live_shift()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Current = new CurrentShiftDto(Summary(9), 0);

        await presenter.ShowCurrentReportAsync();

        Assert.Equal(9, view.Reports.Single().ShiftId);
    }

    [Fact]
    public async Task Close_own_shift_reloads_and_offers_the_report_with_the_variance()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Current = new CurrentShiftDto(Summary(9), 0);
        var closed = Summary(9, status: ShiftStatus.Closed, variance: -20_000);
        view.CloseResult = closed;

        await presenter.CloseOwnShiftAsync();

        Assert.False(view.CloseDialogs.Single().ForOther);
        Assert.Equal(new[] { "Đã kết ca CA261120-0009. Thiếu 20.000 ₫. In báo cáo kết ca?" }, view.Questions);
        Assert.Same(closed, view.Reports.Single());
        Assert.Single(view.HistoryShown);
    }

    [Fact]
    public async Task Declining_the_closing_report_prints_nothing()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Current = new CurrentShiftDto(Summary(9), 0);
        view.CloseResult = Summary(9, status: ShiftStatus.Closed, variance: 0);
        view.ConfirmAnswer = false;

        await presenter.CloseOwnShiftAsync();

        Assert.Equal(new[] { "Đã kết ca CA261120-0009. Tiền mặt khớp. In báo cáo kết ca?" }, view.Questions);
        Assert.Empty(view.Reports);
    }

    [Fact]
    public void Closed_message_mentions_extra_cash()
    {
        Assert.Equal(
            "Đã kết ca CA261120-0009. Thừa 5.000 ₫. In báo cáo kết ca?",
            ShiftPresenter.ClosedMessage(Summary(9, status: ShiftStatus.Closed, variance: 5_000)));
    }

    [Fact]
    public async Task Close_for_other_opens_the_count_sheet_for_that_shift()
    {
        var (host, presenter, view) = Create(supervise: true);
        using var _ = host;
        host.Shifts.Summaries[3] = Summary(3, userId: 2);
        await presenter.LoadAsync(Week);
        var row = presenter.History.Single(r => r.ShiftId == 3);

        await presenter.CloseForOtherAsync(row);

        var dialog = view.CloseDialogs.Single();
        Assert.True(dialog.ForOther);
        Assert.Equal(3, dialog.Shift.ShiftId);
    }

    [Fact]
    public async Task Close_for_other_when_already_closed_informs_and_refreshes()
    {
        var (host, presenter, view) = Create(supervise: true);
        using var _ = host;
        host.Shifts.Summaries[3] = Summary(3, userId: 2, status: ShiftStatus.Closed);
        await presenter.LoadAsync(Week);
        var row = presenter.History.Single(r => r.ShiftId == 3);

        await presenter.CloseForOtherAsync(row);

        Assert.Empty(view.CloseDialogs);
        Assert.Equal(new[] { "Ca CA261120-0003 của Thu ngân Bình đã được kết." }, view.Infos);
        Assert.Equal(2, view.HistoryShown.Count);
    }

    [Fact]
    public async Task Close_for_other_is_refused_without_supervise_and_ignored_for_own_shift()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Summaries[3] = Summary(3, userId: 2);

        await presenter.CloseForOtherAsync(ShiftRow.From(Item(3, 2, ShiftStatus.Open, DateTimeOffset.Now), Vn));
        await presenter.CloseForOtherAsync(ShiftRow.From(Item(5, 1, ShiftStatus.Open, DateTimeOffset.Now), Vn));

        Assert.Equal(new[] { "Bạn không có quyền kết ca của nhân viên khác." }, view.Warnings);
        Assert.Empty(view.CloseDialogs);
        Assert.DoesNotContain(host.Shifts.Calls, c => c.StartsWith("summary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Report_of_a_history_row_loads_its_summary_or_warns()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Shifts.Summaries[1] = Summary(1, status: ShiftStatus.Closed, variance: -20_000);
        await presenter.LoadAsync(Week);

        await presenter.ShowReportAsync(presenter.History.Single(r => r.ShiftId == 1));
        await presenter.ShowReportAsync(presenter.History.Single(r => r.ShiftId == 2));

        Assert.Equal(1, view.Reports.Single().ShiftId);
        Assert.Equal(new[] { "Không tìm thấy ca làm việc." }, view.Warnings);
    }

    [Fact]
    public async Task Closed_view_is_not_updated_after_awaits()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.IsAlive = false;

        await presenter.LoadAsync(Week);

        Assert.Empty(view.CurrentShown);
        Assert.Empty(view.HistoryShown);
    }
}
