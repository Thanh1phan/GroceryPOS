using GroceryPOS.Application.Auditing;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Auditing;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Tests;

public sealed class AuditLogPresenterTests
{
    private static readonly ReportPeriod Week = new(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 20));
    private static readonly ReportPeriod Today = ReportPeriod.SingleDay(new DateOnly(2026, 11, 20));

    private sealed class FakeAuditLogView : RecordingView, IAuditLogView
    {
        public List<IReadOnlyList<string>> UserLists { get; } = new();
        public List<AuditLogFilters> FilterStates { get; } = new();
        public List<AuditLogCards> CardStates { get; } = new();
        public List<(string Text, Tone Tone)> Statuses { get; } = new();
        public List<AuditLogDetail> Details { get; } = new();
        public List<bool> Reloads { get; } = new();
        public List<string> Searches { get; } = new();
        public List<string> BusyMessages { get; } = new();
        public List<string> WarningToasts { get; } = new();
        public List<string> Saved { get; } = new();
        public int ClearedSearches { get; private set; }
        public string? SavePath { get; set; } = "/home/chu/NhatKy.xlsx";
        public AuditGridRequest CurrentGrid { get; set; } = new(null, false, 0, AuditLogQuery.DefaultPageSize);

        /// <summary>When set, RunBusyAsync waits for it before running the work.</summary>
        public Task? BusyGate { get; set; }

        public AuditLogCards Cards => CardStates[^1];
        public AuditLogDetail Detail => Details[^1];
        public (string Text, Tone Tone) Status => Statuses[^1];

        public void ShowUsers(IReadOnlyList<string> usernames) => UserLists.Add(usernames);

        public void ShowFilters(AuditLogFilters filters) => FilterStates.Add(filters);

        public void ShowCards(AuditLogCards cards) => CardStates.Add(cards);

        public void ShowStatus(string text, Tone tone) => Statuses.Add((text, tone));

        public void ShowDetail(AuditLogDetail detail) => Details.Add(detail);

        public void ReloadEntries(bool firstPage) => Reloads.Add(firstPage);

        public void SetSearch(string text)
        {
            Searches.Add(text);
            CurrentGrid = CurrentGrid with { Search = text, PageIndex = 0 };
        }

        public void ClearSearch()
        {
            ClearedSearches++;
            CurrentGrid = CurrentGrid with { Search = null, PageIndex = 0 };
        }

        public async Task<T> RunBusyAsync<T>(Func<Task<T>> work, string message)
        {
            BusyMessages.Add(message);
            if (BusyGate is { } gate)
            {
                await gate;
            }

            return await work();
        }

        public Task<string?> SaveExcelAsync(string suggestedFileName, byte[] content)
        {
            Saved.Add(suggestedFileName);
            return Task.FromResult(SavePath);
        }

        public void ToastWarning(string message, string? title = null) => WarningToasts.Add(message);
    }

    private static (TestHost Host, AuditLogPresenter Presenter, FakeAuditLogView View) Create(bool canView = true)
    {
        var host = canView ? new TestHost(Permissions.AuditLogView) : new TestHost();
        var presenter = host.Get<AuditLogPresenter>();
        var view = new FakeAuditLogView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    private static AuditGridRequest Grid(int page = 0, string? search = null) => new(search, false, page, AuditLogQuery.DefaultPageSize);

    [Fact]
    public async Task Without_permission_nothing_is_loaded_and_export_is_refused()
    {
        var (host, presenter, view) = Create(canView: false);
        using var _h = host;

        await presenter.LoadAsync(Week, "7 ngày qua");
        var page = await presenter.LoadPageAsync(Grid());
        await presenter.ExportAsync();

        Assert.Equal((AuditLogErrors.Denied.Message, Tone.Danger), view.Status);
        Assert.Null(page);
        Assert.Empty(view.Reloads);
        Assert.Empty(host.AuditLogs.Searches);
        Assert.Empty(host.AuditLogs.Summaries);
        Assert.Empty(host.AuditLogs.Exports);
        Assert.Equal(new[] { AuditLogErrors.Denied.Message }, view.Warnings);
    }

    [Fact]
    public async Task Load_fills_users_cards_and_starts_the_grid()
    {
        var (host, presenter, view) = Create();
        using var _h = host;

        Assert.Equal(AuditLogCards.Empty, view.Cards);
        Assert.Equal(AuditLogDetail.None, view.Detail);

        await presenter.LoadAsync(Week, "7 ngày qua");

        Assert.Equal(new[] { "admin", "quanly", "thungan1" }, view.UserLists.Single());
        Assert.Equal(new[] { false }, view.Reloads);
        Assert.Equal(new[] { Week }, host.AuditLogs.Summaries);
        var cards = view.Cards;
        Assert.Equal("12", cards.Total.Value);
        Assert.Equal("2 quan trọng · 5 cần chú ý", cards.Total.Note);
        Assert.Equal("3", cards.Security.Value);
        Assert.Equal(Tone.Warning, cards.Security.Accent);
        Assert.Equal("Không có tài khoản bị khóa", cards.Security.Note);
        Assert.Equal("Hủy 1 · trả 2 · in lại 0", cards.SalesExceptions.Note);
        Assert.Equal("3", cards.StockAndPrice.Value);
    }

    [Fact]
    public void Locked_accounts_turn_the_security_card_red_and_counts_use_vietnamese_grouping()
    {
        var cards = AuditLogCards.From(new AuditLogSummaryDto(Week, 12_345, 0, 0, 4, 2, 0, 0, 0, 0, 0));

        Assert.Equal("12.345", cards.Total.Value);
        Assert.Equal("6", cards.Security.Value);
        Assert.Equal(Tone.Danger, cards.Security.Accent);
        Assert.Equal("2 lần khóa tài khoản", cards.Security.Note);
    }

    [Fact]
    public async Task A_page_is_loaded_with_the_filters_and_the_status_names_count_and_filters()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.AuditLogs.Entries.Add(FakeAuditLogService.Entry(1, AuditActions.SaleVoided));
        await presenter.LoadAsync(Week, "7 ngày qua");

        presenter.SetCategory(AuditCategory.Sales);
        presenter.SetUser("quanly");
        var page = await presenter.LoadPageAsync(Grid(page: 2, search: "  HD01 "));

        Assert.NotNull(page);
        var query = host.AuditLogs.Searches[^1];
        Assert.Equal(Week, query.Period);
        Assert.Equal(AuditCategory.Sales, query.Category);
        Assert.Equal("quanly", query.Username);
        Assert.Equal("HD01", query.Search);
        Assert.Equal(2, query.PageIndex);
        Assert.Null(query.Actions);
        Assert.Equal(new[] { false, true, true }, view.Reloads);
        Assert.StartsWith("7 ngày qua · 1 sự kiện khớp bộ lọc · cập nhật lúc ", view.Status.Text);
        Assert.Contains("Bộ lọc: Nhóm: Bán hàng & trả hàng; Người dùng: quanly; Tìm: \"HD01\"", view.Status.Text);
        Assert.Equal(Tone.Muted, view.Status.Tone);
    }

    [Fact]
    public async Task A_failed_page_reports_the_error_and_returns_null()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        host.AuditLogs.SearchError = Error.Validation("Khoảng thời gian quá dài.");

        var page = await presenter.LoadPageAsync(Grid());

        Assert.Null(page);
        Assert.Equal(("Khoảng thời gian quá dài.", Tone.Danger), view.Status);
    }

    [Fact]
    public async Task The_stock_and_price_card_filters_exactly_what_it_counts()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        presenter.SetSeverity(AuditSeverity.Critical);

        presenter.ApplyQuickFilter(AuditQuickFilter.StockAndPrice);
        await presenter.LoadPageAsync(Grid());

        var query = host.AuditLogs.Searches[^1];
        Assert.Null(query.MinSeverity); // previously this card only set "from warning" and listed every warning
        Assert.Null(query.Category);
        Assert.Equal(new[] { AuditActions.PriceChanged, AuditActions.StockAdjusted, AuditActions.StockCounted }, query.Actions);
        Assert.Equal(new AuditLogFilters(null, null, null, AuditQuickFilter.StockAndPrice), view.FilterStates[^1]);
        Assert.Contains("Hành động: Đổi giá bán, Điều chỉnh tồn kho, Kiểm kê kho", view.Status.Text);
    }

    [Fact]
    public async Task Choosing_a_group_ends_the_card_shortcut_and_the_total_card_clears_it()
    {
        var (host, presenter, _) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");

        presenter.ApplyQuickFilter(AuditQuickFilter.Security);
        Assert.Equal(new[] { AuditActions.LoginFailed, AuditActions.AccountLocked }, presenter.BuildQuery(Grid()).Actions);

        presenter.SetCategory(AuditCategory.Inventory);
        Assert.Equal(AuditQuickFilter.None, presenter.Filters.QuickFilter);
        Assert.Null(presenter.BuildQuery(Grid()).Actions);

        presenter.ApplyQuickFilter(AuditQuickFilter.SalesExceptions);
        presenter.ApplyQuickFilter(AuditQuickFilter.None);
        Assert.Equal(AuditLogFilters.None, presenter.Filters);
    }

    [Fact]
    public async Task Setting_the_same_filter_again_does_not_reload()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");

        presenter.SetUser("quanly");
        presenter.SetUser("quanly");
        presenter.SetUser("  ");
        presenter.SetUser(null);

        Assert.Equal(new[] { false, true, true }, view.Reloads);
    }

    [Fact]
    public async Task Clear_filters_resets_everything_and_reloads_once()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        presenter.SetSeverity(AuditSeverity.Warning);
        view.CurrentGrid = Grid(search: "SUA01");
        var reloadsBefore = view.Reloads.Count;

        presenter.ClearFilters();

        Assert.Equal(AuditLogFilters.None, presenter.Filters);
        Assert.Equal(AuditLogFilters.None, view.FilterStates[^1]);
        Assert.Equal(1, view.ClearedSearches);
        Assert.Equal(reloadsBefore, view.Reloads.Count); // clearing the search box already reloads

        presenter.ClearFilters();
        Assert.Equal(1, view.ClearedSearches);
        Assert.Equal(reloadsBefore + 1, view.Reloads.Count);
    }

    [Fact]
    public async Task Changing_the_period_reloads_page_one_and_the_cards_but_not_for_the_same_period()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");

        await presenter.SetPeriodAsync(Today, "Hôm nay");
        await presenter.SetPeriodAsync(Today, "Hôm nay");

        Assert.Equal(new[] { false, true }, view.Reloads);
        Assert.Equal(new[] { Week, Today }, host.AuditLogs.Summaries);
        Assert.Equal(Today, presenter.BuildQuery(Grid()).Period);
    }

    [Fact]
    public async Task A_slow_summary_of_an_older_period_cannot_overwrite_the_newer_cards()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        host.AuditLogs.Summary = p => new AuditLogSummaryDto(p, p == Today ? 1 : 99, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var slow = new TaskCompletionSource();
        var fast = new TaskCompletionSource();
        host.AuditLogs.SummaryGates.Enqueue(slow);
        host.AuditLogs.SummaryGates.Enqueue(fast);

        var first = presenter.SetPeriodAsync(new ReportPeriod(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), "Tháng 10");
        var second = presenter.SetPeriodAsync(Today, "Hôm nay");
        fast.SetResult();
        await second;
        slow.SetResult();
        await first;

        Assert.Equal("1", view.Cards.Total.Value);
    }

    [Fact]
    public async Task A_failed_summary_blanks_the_cards_and_reports_the_error()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.AuditLogs.SummaryError = Error.Validation("Khoảng thời gian không hợp lệ.");

        await presenter.LoadAsync(Week, "7 ngày qua");

        Assert.Equal(AuditLogCards.Empty, view.Cards);
        Assert.Contains(view.Statuses, s => s == ("Khoảng thời gian không hợp lệ.", Tone.Danger));
    }

    [Fact]
    public async Task A_failed_user_list_is_reported_and_the_screen_still_loads()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.AuditLogs.UsersError = Error.Conflict("Mất kết nối CSDL.");

        await presenter.LoadAsync(Week, "7 ngày qua");

        Assert.Equal(new[] { "Mất kết nối CSDL." }, view.Errors);
        Assert.Empty(view.UserLists);
        Assert.Equal(new[] { false }, view.Reloads);
    }

    [Fact]
    public async Task A_user_filter_that_disappeared_on_reload_falls_back_to_everyone()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        presenter.SetUser("thungan1");
        host.AuditLogs.Usernames.Remove("thungan1");

        await presenter.LoadAsync(Week, "7 ngày qua");

        Assert.Null(presenter.Filters.Username);
        Assert.Null(view.FilterStates[^1].Username);
    }

    [Fact]
    public async Task Selecting_an_entry_shows_its_detail_and_the_shortcuts_it_allows()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");

        presenter.Select(FakeAuditLogService.Entry(4, AuditActions.SaleVoided, severity: AuditSeverity.Critical, entityId: "HD261120-0001", details: "Lý do: khách đổi ý\r\nTiền: 87.000 ₫"));

        var detail = view.Detail;
        Assert.Equal("Tên " + AuditActions.SaleVoided, detail.Title);
        Assert.Equal(Tone.Danger, detail.TitleTone);
        Assert.Equal("20/11/2026 09:30:15", detail.Time);
        Assert.Equal("quanly (#7)", detail.User);
        Assert.Equal("Hóa đơn HD261120-0001", detail.Entity);
        Assert.Equal(new[] { "Lý do: khách đổi ý", "Tiền: 87.000 ₫" }, detail.Lines);
        Assert.True(detail.CanFilterUser);
        Assert.True(detail.CanFilterEntity);

        presenter.Select(FakeAuditLogService.Entry(5, AuditActions.LoginFailed, username: "(không rõ)"));
        Assert.False(view.Detail.CanFilterUser); // not in the user filter – the button used to silently show everyone
        Assert.False(view.Detail.CanFilterEntity);
        Assert.Equal(new[] { "(không có nội dung)" }, view.Detail.Lines);

        presenter.Select(null);
        Assert.Equal(AuditLogDetail.None, view.Detail);
    }

    [Fact]
    public async Task Same_user_and_same_object_shortcuts_filter_from_the_selected_entry()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        presenter.Select(FakeAuditLogService.Entry(4, AuditActions.SaleVoided, username: "thungan1", entityId: "HD261120-0001"));

        presenter.FilterBySelectedUser();
        presenter.FilterBySelectedEntity();

        Assert.Equal("thungan1", presenter.Filters.Username);
        Assert.Equal("thungan1", view.FilterStates[^1].Username);
        Assert.Equal(new[] { "HD261120-0001" }, view.Searches);

        presenter.Select(FakeAuditLogService.Entry(5, AuditActions.LoginFailed, username: "(không rõ)"));
        var reloads = view.Reloads.Count;
        presenter.FilterBySelectedUser();
        presenter.FilterBySelectedEntity();
        Assert.Equal("thungan1", presenter.Filters.Username);
        Assert.Equal(reloads, view.Reloads.Count);
        Assert.Single(view.Searches);
    }

    [Fact]
    public async Task Export_saves_the_filtered_log_then_reloads_list_and_cards()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        presenter.ApplyQuickFilter(AuditQuickFilter.SalesExceptions);
        view.CurrentGrid = Grid(page: 3, search: "HD");

        await presenter.ExportAsync();

        var query = host.AuditLogs.Exports.Single();
        Assert.Equal("HD", query.Search);
        Assert.Equal(new[] { AuditActions.SaleVoided, AuditActions.SaleReturned, AuditActions.ReceiptReprinted }, query.Actions);
        Assert.Equal(new[] { "Đang xuất nhật ký…" }, view.BusyMessages);
        Assert.Equal(new[] { "NhatKy_20261114-20261120.xlsx" }, view.Saved);
        Assert.Equal(new[] { "Đã lưu NhatKy.xlsx." }, view.Successes);
        Assert.False(view.Reloads[^1]); // stays on the page
        Assert.Equal(2, host.AuditLogs.Summaries.Count);
        Assert.False(presenter.IsExporting);
    }

    [Fact]
    public async Task Cancelled_save_still_reloads_because_the_export_was_audited()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        view.SavePath = null;
        var reloads = view.Reloads.Count;

        await presenter.ExportAsync();

        Assert.Empty(view.Successes);
        Assert.Equal(reloads + 1, view.Reloads.Count);
    }

    [Fact]
    public async Task A_refused_export_shows_the_reason_and_does_not_reload()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        host.AuditLogs.ExportError = AuditLogErrors.NothingToExport;
        var reloads = view.Reloads.Count;

        await presenter.ExportAsync();

        Assert.Equal(new[] { AuditLogErrors.NothingToExport.Message }, view.Warnings);
        Assert.Empty(view.Saved);
        Assert.Equal(reloads, view.Reloads.Count);
    }

    [Fact]
    public async Task Only_one_export_runs_at_a_time()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        var gate = new TaskCompletionSource();
        view.BusyGate = gate.Task;

        var first = presenter.ExportAsync();
        Assert.True(presenter.IsExporting);
        await presenter.ExportAsync(); // Ctrl+E pressed again while the first one runs
        gate.SetResult();
        await first;

        Assert.Single(host.AuditLogs.Exports);
        Assert.Single(view.BusyMessages);
    }

    [Fact]
    public async Task Nothing_reaches_a_closed_view()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync(Week, "7 ngày qua");
        var cards = view.CardStates.Count;
        var statuses = view.Statuses.Count;
        view.IsAlive = false;

        await presenter.SetPeriodAsync(Today, "Hôm nay");
        var page = await presenter.LoadPageAsync(Grid());

        Assert.Null(page);
        Assert.Equal(cards, view.CardStates.Count);
        Assert.Equal(statuses, view.Statuses.Count);
    }

    [Fact]
    public void Filter_options_start_with_the_all_entry()
    {
        Assert.Equal("Tất cả nhóm", AuditLogPresenter.CategoryOptions[0].Name);
        Assert.Null(AuditLogPresenter.CategoryOptions[0].Value);
        Assert.Equal(AuditCatalog.Categories.Count + 1, AuditLogPresenter.CategoryOptions.Count);
        Assert.Equal(new AuditSeverity?[] { null, AuditSeverity.Warning, AuditSeverity.Critical }, AuditLogPresenter.SeverityOptions.Select(o => o.Value));
    }
}
