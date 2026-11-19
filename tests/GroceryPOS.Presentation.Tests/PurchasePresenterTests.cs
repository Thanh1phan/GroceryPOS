using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Purchasing;

namespace GroceryPOS.Presentation.Tests;

public sealed class PurchasePresenterTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);
    private static readonly PurchaseFilter November = new(new DateTime(2026, 11, 1), new DateTime(2026, 11, 19));

    private sealed class FakePurchaseView : RecordingView, IPurchaseView
    {
        public List<IReadOnlyList<SupplierFilterOption>> SupplierFilters { get; } = new();
        public List<IReadOnlyList<PurchaseReceiptRow>> ReceiptsShown { get; } = new();
        public PurchaseSummary? SummaryShown { get; private set; }
        public List<string?> PeriodErrors { get; } = new();
        public List<GoodsReceiptDraft> EditorsOpened { get; } = new();
        public bool EditorSaves { get; set; }
        public List<GoodsReceiptDto> DetailsOpened { get; } = new();
        public string? ReasonAnswer { get; set; } = "Nhập trùng";
        public List<string> ReasonQuestions { get; } = new();

        public void ShowSupplierFilter(IReadOnlyList<SupplierFilterOption> options) => SupplierFilters.Add(options);

        public void ShowReceipts(IReadOnlyList<PurchaseReceiptRow> rows) => ReceiptsShown.Add(rows);

        public void ShowSummary(PurchaseSummary summary) => SummaryShown = summary;

        public void ShowPeriodError(string? message) => PeriodErrors.Add(message);

        public bool OpenEditor(GoodsReceiptDraft draft, IReadOnlyList<SupplierDto> suppliers)
        {
            EditorsOpened.Add(draft);
            return EditorSaves;
        }

        public void OpenDetail(GoodsReceiptDto receipt, TimeSpan storeOffset) => DetailsOpened.Add(receipt);

        public string? AskReason(string title, string message, IReadOnlyList<string> suggestions)
        {
            ReasonQuestions.Add(message);
            return ReasonAnswer;
        }
    }

    private static readonly SupplierDto Vinamilk = new(1, "NCC0001", "Vinamilk", null, null, null, null, null, 2_000_000, true);
    private static readonly SupplierDto Acecook = new(2, "NCC0002", "Acecook", null, null, null, null, null, 500_000, true);
    private static readonly SupplierDto Stopped = new(3, "NCC0003", "NCC cũ", null, null, null, null, null, 0, false);

    private static readonly StockItem Milk = new(1, "SP001", "8930001", "Sữa tươi", 1, "Sữa", "hộp", 2, 5, 7_000, 9_000, true);
    private static readonly StockItem Noodles = new(2, "SP002", null, "Mì gói", 2, "Mì", "gói", 50, 10, 3_000, 4_000, true);

    private static GoodsReceiptSummary Receipt(int id, GoodsReceiptStatus status, int supplierId, DateTimeOffset date, decimal total, decimal paid) =>
        new(id, $"PN261110-{id:0000}", date, status, status.DisplayName(), supplierId, supplierId == 1 ? "Vinamilk" : "Acecook", 2, total, paid, status == GoodsReceiptStatus.Completed ? total - paid : 0);

    private static GoodsReceiptDto Detail(GoodsReceiptSummary s) => new(
        s.Id, s.Code, s.Status, s.StatusName, s.ReceiptDate, s.SupplierId, s.SupplierName, null, null,
        s.Total, 0, s.Total, s.PaidAmount, s.DebtAmount, null, null,
        [new GoodsReceiptLineDto(1, "SP001", "Sữa tươi", "hộp", 10, 7_000, 0, 0, 70_000, 7_000, null)]);

    private static (TestHost Host, PurchasePresenter Presenter, FakePurchaseView View) Create(bool canManage = true)
    {
        var host = canManage ? new TestHost(Permissions.PurchasesManage) : new TestHost(Permissions.InventoryView);
        host.Suppliers.Items.AddRange(new[] { Vinamilk, Acecook, Stopped });
        host.Inventory.Stock.AddRange(new[] { Milk, Noodles });
        host.Receipts.Receipts.AddRange(new[]
        {
            Receipt(1, GoodsReceiptStatus.Completed, 1, new DateTimeOffset(2026, 11, 5, 9, 0, 0, Vn), 1_000_000, 600_000),
            Receipt(2, GoodsReceiptStatus.Completed, 2, new DateTimeOffset(2026, 11, 10, 14, 30, 0, Vn), 300_000, 300_000),
            Receipt(3, GoodsReceiptStatus.Cancelled, 1, new DateTimeOffset(2026, 11, 12, 8, 0, 0, Vn), 200_000, 0),
            Receipt(4, GoodsReceiptStatus.Draft, 1, new DateTimeOffset(2026, 11, 18, 10, 0, 0, Vn), 150_000, 0),
            Receipt(5, GoodsReceiptStatus.Draft, 2, new DateTimeOffset(2026, 10, 20, 10, 0, 0, Vn), 80_000, 0), // last month
            Receipt(6, GoodsReceiptStatus.Completed, 1, new DateTimeOffset(2026, 10, 31, 23, 0, 0, Vn), 999_000, 0), // outside the period
        });
        foreach (var r in host.Receipts.Receipts)
        {
            host.Receipts.Details[r.Id] = Detail(r);
        }

        var presenter = host.Get<PurchasePresenter>();
        var view = new FakePurchaseView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Permissions_drive_manage_and_price_changes()
    {
        using var manager = new TestHost(Permissions.PurchasesManage, Permissions.PricesChange);
        using var viewer = new TestHost(Permissions.InventoryView);
        var full = manager.Get<PurchasePresenter>();
        var readOnly = viewer.Get<PurchasePresenter>();
        Assert.True(full.CanManage);
        Assert.True(full.CanChangePrices);
        Assert.False(readOnly.CanManage);
        Assert.False(readOnly.CanChangePrices);
    }

    [Fact]
    public async Task Load_shows_suppliers_period_rows_in_store_time_and_the_summary()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.LoadAsync(November);

        Assert.Equal(new[] { 0, 1, 2, 3 }, view.SupplierFilters.Single().Select(o => o.Id));
        Assert.Equal("Tất cả NCC", view.SupplierFilters.Single()[0].Name);
        var rows = view.ReceiptsShown.Single();
        Assert.Equal(new[] { 1, 2, 3, 4 }, rows.Select(r => r.Id).Order());
        Assert.Equal(new DateTime(2026, 11, 10, 14, 30, 0), rows.Single(r => r.Id == 2).ReceiptDate);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, Vn), host.Receipts.Searches[0].From);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 0, 0, 0, Vn), host.Receipts.Searches[0].To); // "to" date is inclusive

        var summary = view.SummaryShown!;
        Assert.Equal(1_300_000, summary.ReceivedAmount);
        Assert.Equal(900_000, summary.PaidAmount);
        Assert.Equal(400_000, summary.NewDebt);
        Assert.Equal(2_500_000, summary.TotalSupplierDebt);
        Assert.Equal(2, summary.CompletedCount);
        Assert.Equal(2, summary.SupplierCount);
        Assert.Equal(2, summary.OpenDrafts); // drafts are counted whatever the period
        Assert.Equal("Cũ nhất: 20/10/2026", summary.DraftsNote);
        Assert.Equal("2 nhà cung cấp", summary.CountNote);
        Assert.Equal(new string?[] { null }, view.PeriodErrors);
    }

    [Fact]
    public async Task Filter_by_status_and_supplier_reloads_only_the_list()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        host.Suppliers.Calls.Clear();

        await presenter.FilterAsync(November with { Status = GoodsReceiptStatus.Completed, SupplierId = 1 });

        Assert.Equal(new[] { 1 }, view.ReceiptsShown[^1].Select(r => r.Id));
        Assert.Empty(host.Suppliers.Calls);
        Assert.Single(view.SupplierFilters);
        Assert.Equal(1, presenter.Filter.SupplierId);
    }

    [Fact]
    public async Task Inverted_period_marks_the_from_date_and_does_not_search()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.FilterAsync(new PurchaseFilter(new DateTime(2026, 11, 20), new DateTime(2026, 11, 1)));

        Assert.Equal(new[] { "Từ ngày phải trước đến ngày." }, view.PeriodErrors);
        Assert.Empty(host.Receipts.Searches);
        Assert.Empty(view.ReceiptsShown);
    }

    [Fact]
    public async Task Same_day_period_is_valid()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.FilterAsync(new PurchaseFilter(new DateTime(2026, 11, 10), new DateTime(2026, 11, 10)));

        Assert.Equal(new[] { 2 }, view.ReceiptsShown.Single().Select(r => r.Id));
    }

    [Fact]
    public async Task Failed_supplier_load_reports_it_but_still_shows_the_receipts()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Suppliers.FailWith = Error.Forbidden();

        await presenter.LoadAsync(November);

        Assert.Empty(view.SupplierFilters);
        Assert.Contains(view.Errors, e => e.StartsWith("Không tải được danh sách nhà cung cấp", StringComparison.Ordinal));
        Assert.Single(view.ReceiptsShown);
        Assert.Equal(0, view.SummaryShown!.TotalSupplierDebt);
    }

    [Fact]
    public async Task Failed_list_keeps_the_view_and_shows_an_error()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Receipts.FailSearchWith = Error.Forbidden();

        await presenter.LoadAsync(November);

        Assert.Equal(new[] { Error.Forbidden().Message }, view.Errors);
        Assert.Empty(view.ReceiptsShown);
        Assert.Null(view.SummaryShown);
    }

    [Fact]
    public async Task Failed_draft_count_shows_a_dash_instead_of_zero()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Receipts.FailDraftsWith = Error.Forbidden();

        await presenter.LoadAsync(November);

        Assert.Null(view.SummaryShown!.OpenDrafts);
        Assert.Equal("–", view.SummaryShown.DraftsText);
        Assert.Equal("Không tải được phiếu tạm", view.SummaryShown.DraftsNote);
    }

    [Fact]
    public async Task A_slow_load_overtaken_by_a_newer_filter_is_dropped()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        var gate = new TaskCompletionSource();
        host.Receipts.BeforeSearchReturns = () => gate.Task;
        var slow = presenter.FilterAsync(November);

        host.Receipts.BeforeSearchReturns = null;
        await presenter.FilterAsync(November with { SupplierId = 2 });
        gate.SetResult();
        await slow;

        Assert.Single(view.ReceiptsShown);
        Assert.Equal(new[] { 2 }, view.ReceiptsShown[0].Select(r => r.Id));
    }

    [Fact]
    public async Task Closed_view_is_not_updated()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.IsAlive = false;

        await presenter.LoadAsync(November);

        Assert.Empty(view.ReceiptsShown);
        Assert.Null(view.SummaryShown);
    }

    [Fact]
    public async Task New_receipt_uses_the_filtered_supplier_when_it_still_trades()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.FilterAsync(November with { SupplierId = 2 });

        await presenter.NewReceiptAsync(withSuggestions: false);

        var draft = view.EditorsOpened.Single();
        Assert.Equal(2, draft.SupplierId);
        Assert.Empty(draft.Lines);
        Assert.DoesNotContain("suggest", host.Receipts.Calls);
    }

    [Fact]
    public async Task New_receipt_ignores_a_stopped_filtered_supplier()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.FilterAsync(November with { SupplierId = 3 });

        await presenter.NewReceiptAsync(withSuggestions: false);

        Assert.Null(view.EditorsOpened.Single().SupplierId);
    }

    [Fact]
    public async Task New_receipt_with_suggestions_prefills_the_low_stock_lines()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Receipts.Suggestions.Add(new ReorderSuggestion(1, "SP001", "Sữa tươi", "hộp", 2, 5, 8, 7_000));

        await presenter.NewReceiptAsync(withSuggestions: true);

        var line = view.EditorsOpened.Single().Lines.Single();
        Assert.Equal(1, line.ProductId);
        Assert.Equal(8, line.Quantity);
        Assert.Empty(view.Infos);
    }

    [Fact]
    public async Task New_receipt_without_low_stock_says_so_and_opens_a_blank_receipt()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.NewReceiptAsync(withSuggestions: true);

        Assert.Equal(new[] { "Không có sản phẩm nào dưới định mức tồn kho. Mở phiếu trống." }, view.Infos);
        Assert.Empty(view.EditorsOpened.Single().Lines);
    }

    [Fact]
    public async Task Failed_suggestions_are_reported_and_a_blank_receipt_still_opens()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Receipts.FailSuggestWith = Error.Forbidden();

        await presenter.NewReceiptAsync(withSuggestions: true);

        Assert.Single(view.Errors);
        Assert.Empty(view.Infos);
        Assert.Single(view.EditorsOpened);
    }

    [Fact]
    public async Task New_receipt_needs_a_trading_supplier()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Suppliers.Items.RemoveAll(s => s.IsActive);

        await presenter.NewReceiptAsync(withSuggestions: false);

        Assert.Empty(view.EditorsOpened);
        Assert.Contains("Chưa có nhà cung cấp nào đang giao dịch", view.Infos.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_receipt_is_refused_without_permission()
    {
        var (host, presenter, view) = Create(canManage: false);
        using var _ = host;

        await presenter.NewReceiptAsync(withSuggestions: false);

        Assert.Single(view.Warnings);
        Assert.Empty(view.EditorsOpened);
    }

    [Fact]
    public async Task Saved_editor_reloads_suppliers_and_list()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        view.EditorSaves = true;

        await presenter.NewReceiptAsync(withSuggestions: false);

        Assert.Equal(2, view.SupplierFilters.Count);
        Assert.Equal(2, view.ReceiptsShown.Count);
    }

    [Fact]
    public async Task Cancelled_editor_does_not_reload()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);

        await presenter.NewReceiptAsync(withSuggestions: false);

        Assert.Single(view.ReceiptsShown);
    }

    [Fact]
    public async Task Opening_a_draft_rebuilds_it_in_the_editor()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);

        await presenter.OpenAsync(presenter.Receipts.Single(r => r.Id == 4));

        var draft = view.EditorsOpened.Single();
        Assert.Equal(4, draft.Id);
        Assert.Equal(1, draft.SupplierId);
        Assert.Single(draft.Lines);
        Assert.Empty(view.DetailsOpened);
    }

    [Fact]
    public async Task Opening_a_completed_receipt_shows_it_read_only()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);

        await presenter.OpenAsync(presenter.Receipts.Single(r => r.Id == 1));

        Assert.Equal(1, view.DetailsOpened.Single().Id);
        Assert.Empty(view.EditorsOpened);
        Assert.Single(view.ReceiptsShown); // the row was current: no reload
    }

    [Fact]
    public async Task Opening_a_draft_completed_elsewhere_shows_it_and_reloads_the_stale_list()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        var row = presenter.Receipts.Single(r => r.Id == 4);
        host.Receipts.Details[4] = host.Receipts.Details[4] with { Status = GoodsReceiptStatus.Completed, StatusName = GoodsReceiptStatus.Completed.DisplayName() };

        await presenter.OpenAsync(row);

        Assert.Single(view.DetailsOpened);
        Assert.Empty(view.EditorsOpened);
        Assert.Equal(2, view.ReceiptsShown.Count);
    }

    [Fact]
    public async Task Opening_a_vanished_receipt_warns()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        var row = presenter.Receipts[0];
        host.Receipts.Details.Remove(row.Id);

        await presenter.OpenAsync(row);

        Assert.Equal(new[] { "Không tìm thấy phiếu nhập." }, view.Warnings);
    }

    [Fact]
    public async Task Cancel_draft_asks_for_a_reason_then_confirms_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        var draft = presenter.Receipts.Single(r => r.Id == 4);

        await presenter.CancelAsync(draft);

        Assert.Contains("PN261110-0004", view.ReasonQuestions.Single(), StringComparison.Ordinal);
        Assert.Contains("cancel:4:Nhập trùng", host.Receipts.Calls);
        Assert.Equal(new[] { "Đã hủy phiếu tạm PN261110-0004." }, view.Successes);
        Assert.Equal(2, view.ReceiptsShown.Count);
    }

    [Fact]
    public async Task Cancel_with_a_blank_reason_sends_null()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        view.ReasonAnswer = "  ";

        await presenter.CancelAsync(presenter.Receipts.Single(r => r.Id == 4));

        Assert.Contains("cancel:4:", host.Receipts.Calls);
    }

    [Fact]
    public async Task Backing_out_of_the_reason_prompt_cancels_nothing()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        view.ReasonAnswer = null;

        await presenter.CancelAsync(presenter.Receipts.Single(r => r.Id == 4));

        Assert.DoesNotContain(host.Receipts.Calls, c => c.StartsWith("cancel", StringComparison.Ordinal));
        Assert.Empty(view.Successes);
    }

    [Fact]
    public async Task Failed_cancel_warns_and_does_not_reload()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        host.Receipts.FailCancelWith = new Error("receipt.status", "Phiếu nhập đã hoàn thành, không thể thay đổi.");

        await presenter.CancelAsync(presenter.Receipts.Single(r => r.Id == 4));

        Assert.Equal(new[] { "Phiếu nhập đã hoàn thành, không thể thay đổi." }, view.Warnings);
        Assert.Single(view.ReceiptsShown);
    }

    [Fact]
    public async Task Only_drafts_can_be_cancelled_and_only_with_permission()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        var completed = presenter.Receipts.Single(r => r.Id == 1);
        var draft = presenter.Receipts.Single(r => r.Id == 4);

        await presenter.CancelAsync(completed);

        Assert.Empty(view.ReasonQuestions);
        Assert.False(presenter.CanCancel(completed));
        Assert.True(presenter.CanCancel(draft));
        Assert.False(presenter.CanCancel(null));

        var (otherHost, readOnly, readOnlyView) = Create(canManage: false);
        using var __ = otherHost;
        Assert.False(readOnly.CanCancel(draft));
        await readOnly.CancelAsync(draft);
        Assert.Single(readOnlyView.Warnings);
        Assert.Empty(readOnlyView.ReasonQuestions);
    }

    [Fact]
    public void Row_tones_and_open_caption_follow_the_status()
    {
        var date = new DateTimeOffset(2026, 11, 5, 9, 0, 0, Vn);
        var completedWithDebt = PurchaseReceiptRow.From(Receipt(1, GoodsReceiptStatus.Completed, 1, date, 1_000, 400), Vn);
        var paid = PurchaseReceiptRow.From(Receipt(2, GoodsReceiptStatus.Completed, 1, date, 1_000, 1_000), Vn);
        var draft = PurchaseReceiptRow.From(Receipt(3, GoodsReceiptStatus.Draft, 1, date, 1_000, 0), Vn);
        var cancelled = PurchaseReceiptRow.From(Receipt(4, GoodsReceiptStatus.Cancelled, 1, date, 1_000, 0), Vn);

        Assert.Equal(Tone.Warning, completedWithDebt.DebtTone);
        Assert.Equal(Tone.Normal, paid.DebtTone);
        Assert.Equal(Tone.Info, draft.StatusTone);
        Assert.Equal(Tone.Muted, cancelled.RowTone);
        Assert.Equal(Tone.Muted, cancelled.StatusTone);
        Assert.Equal(Tone.Normal, completedWithDebt.RowTone);
        Assert.Equal("Sửa phiếu tạm", PurchasePresenter.OpenText(draft));
        Assert.Equal("Xem phiếu", PurchasePresenter.OpenText(paid));
        Assert.Equal("Xem phiếu", PurchasePresenter.OpenText(null));
    }

    [Fact]
    public void Default_period_is_this_month_up_to_today()
    {
        var filter = PurchaseFilter.ThisMonth(new DateTime(2026, 11, 19, 15, 0, 0));

        Assert.Equal(new DateTime(2026, 11, 1), filter.From);
        Assert.Equal(new DateTime(2026, 11, 19), filter.To);
        Assert.Null(filter.Status);
        Assert.Null(filter.SupplierId);
    }

    [Fact]
    public void Empty_summary_has_no_drafts_note_warning()
    {
        Assert.Equal("0", PurchaseSummary.Empty.DraftsText);
        Assert.Equal("Không có phiếu tồn đọng", PurchaseSummary.Empty.DraftsNote);
    }
}
