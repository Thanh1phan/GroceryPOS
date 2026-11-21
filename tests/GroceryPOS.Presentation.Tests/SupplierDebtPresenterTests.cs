using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Presentation.Purchasing;

namespace GroceryPOS.Presentation.Tests;

public sealed class SupplierDebtPresenterTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);
    private static readonly DocumentPeriod November = new(new DateTime(2026, 11, 1), new DateTime(2026, 11, 21));

    private sealed class FakeSupplierDebtView : RecordingView, ISupplierDebtView
    {
        public List<IReadOnlyList<SupplierPayableDto>> PayablesShown { get; } = new();
        public List<IReadOnlyList<SupplierPaymentRow>> PaymentsShown { get; } = new();
        public List<IReadOnlyList<SupplierReturnRow>> ReturnsShown { get; } = new();
        public SupplierDebtSummary? SummaryShown { get; private set; }
        public List<string?> PeriodErrors { get; } = new();
        public List<SupplierPayableDto> PaymentDialogs { get; } = new();
        public SupplierPaymentDto? PaymentResult { get; set; }
        public List<int> LedgersOpened { get; } = new();
        public List<int?> ReturnEditors { get; } = new();
        public SupplierReturnReceipt? ReturnResult { get; set; }
        public List<SupplierReturnDto> ReturnDetails { get; } = new();
        public string? ReasonAnswer { get; set; } = "Lập nhầm phiếu";
        public List<string> ReasonQuestions { get; } = new();
        public int ReturnsTabSelected { get; private set; }

        public void ShowPayables(IReadOnlyList<SupplierPayableDto> payables) => PayablesShown.Add(payables);

        public void ShowPayments(IReadOnlyList<SupplierPaymentRow> rows) => PaymentsShown.Add(rows);

        public void ShowReturns(IReadOnlyList<SupplierReturnRow> rows) => ReturnsShown.Add(rows);

        public void ShowSummary(SupplierDebtSummary summary) => SummaryShown = summary;

        public void ShowPeriodError(string? message) => PeriodErrors.Add(message);

        public SupplierPaymentDto? OpenPayment(SupplierPayableDto payable)
        {
            PaymentDialogs.Add(payable);
            return PaymentResult;
        }

        public void OpenLedger(int supplierId) => LedgersOpened.Add(supplierId);

        public SupplierReturnReceipt? OpenReturnEditor(IReadOnlyList<StockItem> catalog, IReadOnlyList<SupplierDto> suppliers, int? supplierId)
        {
            ReturnEditors.Add(supplierId);
            return ReturnResult;
        }

        public void OpenReturnDetail(SupplierReturnDto supplierReturn, TimeSpan storeOffset) => ReturnDetails.Add(supplierReturn);

        public string? AskReason(string title, string message, IReadOnlyList<string> suggestions)
        {
            ReasonQuestions.Add(message);
            return ReasonAnswer;
        }

        public void SelectReturnsTab() => ReturnsTabSelected++;
    }

    private static readonly SupplierDto Vinamilk = new(1, "NCC0001", "Vinamilk", null, null, null, null, null, 2_000_000, true);
    private static readonly SupplierDto Acecook = new(2, "NCC0002", "Acecook", null, null, null, null, null, 500_000, true);
    private static readonly SupplierDto Stopped = new(3, "NCC0003", "NCC cũ", null, null, null, null, null, 0, false);
    private static readonly StockItem Milk = new(1, "SP001", "8930001", "Sữa tươi", 1, "Sữa", "hộp", 2, 5, 7_000, 9_000, true);

    private static SupplierPaymentDto Payment(int id, int supplierId, DateTimeOffset paidAt, decimal amount, bool cancelled = false, string? reason = null) =>
        new(id, $"PC261110-{id:0000}", supplierId, supplierId == 1 ? "Vinamilk" : "Acecook", amount, PaymentMethod.Cash, "Tiền mặt",
            paidAt, null, null, amount + 100_000, 100_000, cancelled, cancelled ? paidAt.AddHours(1) : null, reason);

    private static SupplierReturnSummary Return(int id, int supplierId, DateTimeOffset date, decimal total, decimal refund, decimal offset) =>
        new(id, $"TN261110-{id:0000}", date, supplierId, supplierId == 1 ? "Vinamilk" : "Acecook", id == 1 ? "PN261105-0001" : null, 2, total, refund, offset);

    private static (TestHost Host, SupplierDebtPresenter Presenter, FakeSupplierDebtView View) Create(params string[] permissions)
    {
        var host = new TestHost(permissions.Length == 0 ? [Permissions.SupplierPaymentsManage, Permissions.PurchasesManage] : permissions);
        host.Suppliers.Items.AddRange(new[] { Vinamilk, Acecook, Stopped });
        host.Inventory.Stock.Add(Milk);
        host.SupplierPayments.Payables.AddRange(new[]
        {
            new SupplierPayableDto(2, "NCC0002", "Acecook", "0901234567", 500_000),
            new SupplierPayableDto(1, "NCC0001", "Vinamilk", null, 2_000_000),
        });
        host.SupplierPayments.Payments.AddRange(new[]
        {
            Payment(1, 1, new DateTimeOffset(2026, 11, 5, 9, 0, 0, Vn), 1_000_000),
            Payment(2, 2, new DateTimeOffset(2026, 11, 10, 14, 30, 0, Vn), 300_000, cancelled: true, reason: "Sai số tiền"),
            Payment(3, 2, new DateTimeOffset(2026, 11, 21, 23, 59, 0, Vn), 200_000),
            Payment(4, 1, new DateTimeOffset(2026, 10, 31, 23, 0, 0, Vn), 999_000), // outside the period
        });
        host.SupplierReturns.Returns.AddRange(new[]
        {
            Return(1, 1, new DateTimeOffset(2026, 11, 8, 10, 0, 0, Vn), 140_000, 0, 140_000),
            Return(2, 2, new DateTimeOffset(2026, 11, 12, 16, 0, 0, Vn), 60_000, 60_000, 0),
        });
        host.SupplierReturns.Details[1] = new SupplierReturnDto(
            1, "TN261110-0001", new DateTimeOffset(2026, 11, 8, 10, 0, 0, Vn), 1, "Vinamilk", "PN261105-0001", "Hàng lỗi", null,
            140_000, 0, null, 140_000, 140_000, 0, []);

        var presenter = host.Get<SupplierDebtPresenter>();
        var view = new FakeSupplierDebtView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    private static SupplierReturnReceipt Posted(decimal total, decimal refund, decimal offset, decimal debtAfter) => new(
        new SupplierReturnDto(9, "TN261121-0001", DateTimeOffset.Now, 1, "Vinamilk", null, "Hết hạn", null, total, refund,
            refund > 0 ? "Tiền mặt" : null, offset, total, 0, []),
        debtAfter,
        new Dictionary<int, decimal>());

    [Fact]
    public void Permissions_drive_paying_and_returns()
    {
        using var payer = new TestHost(Permissions.SupplierPaymentsManage);
        using var buyer = new TestHost(Permissions.PurchasesManage);
        Assert.True(payer.Get<SupplierDebtPresenter>().CanPay);
        Assert.False(payer.Get<SupplierDebtPresenter>().CanReturn);
        Assert.False(buyer.Get<SupplierDebtPresenter>().CanPay);
        Assert.True(buyer.Get<SupplierDebtPresenter>().CanReturn);
    }

    [Fact]
    public async Task Load_shows_payables_documents_in_store_time_and_the_summary()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.LoadAsync(November);

        Assert.Equal(2, view.PayablesShown.Single().Count);
        var payments = view.PaymentsShown.Single();
        Assert.Equal(new[] { 1, 2, 3 }, payments.Select(p => p.Id));
        Assert.Equal(new DateTime(2026, 11, 10, 14, 30, 0), payments[1].PaidAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, Vn), host.SupplierPayments.Searches[0].From);
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 0, 0, 0, Vn), host.SupplierPayments.Searches[0].To); // "to" date is inclusive
        Assert.Equal(2, view.ReturnsShown.Single().Count);
        Assert.Equal("–", view.ReturnsShown[0].Single(r => r.Id == 2).GoodsReceiptCode);

        var summary = view.SummaryShown!;
        Assert.Equal(2_500_000, summary.TotalDebt);
        Assert.Equal("2 nhà cung cấp", summary.TotalNote);
        Assert.Equal(1_200_000, summary.PaidAmount); // the cancelled payment does not count
        Assert.Equal("2 phiếu chi · 1 phiếu đã hủy", summary.PaidNote);
        Assert.Equal(200_000, summary.ReturnedAmount);
        Assert.Equal($"2 phiếu · cấn trừ nợ {GroceryPOS.Application.Formatting.Vnd.Format(140_000)}", summary.ReturnedNote);
        Assert.Equal("Vinamilk", summary.LargestNote); // largest debt even though the list was not sorted
        Assert.Equal(new string?[] { null }, view.PeriodErrors);
        Assert.Empty(view.Errors);
    }

    [Fact]
    public void Payment_rows_describe_their_status()
    {
        var at = new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero);
        var paid = SupplierPaymentRow.From(Payment(1, 1, at, 50_000), Vn);
        var cancelled = SupplierPaymentRow.From(Payment(2, 1, at, 50_000, cancelled: true, reason: "Sai số tiền"), Vn);
        var cancelledNoReason = SupplierPaymentRow.From(Payment(3, 1, at, 50_000, cancelled: true), Vn);

        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), paid.PaidAt);
        Assert.Equal("Đã chi", paid.StatusName);
        Assert.Equal("Đã hủy: Sai số tiền", cancelled.StatusName);
        Assert.Equal("Đã hủy", cancelledNoReason.StatusName);
        Assert.Equal(Mvp.Tone.Danger, cancelled.StatusTone);
        Assert.Equal(Mvp.Tone.Muted, cancelled.RowTone);
        Assert.Equal(Mvp.Tone.Normal, paid.RowTone);
    }

    [Fact]
    public async Task Without_purchases_permission_returns_are_neither_loaded_nor_counted()
    {
        var (host, presenter, view) = Create(Permissions.SupplierPaymentsManage);
        using var _ = host;

        await presenter.LoadAsync(November);

        Assert.Empty(host.SupplierReturns.Searches);
        Assert.Empty(view.ReturnsShown);
        Assert.Equal("–", view.SummaryShown!.ReturnedText);
        Assert.Equal("Cần quyền nhập hàng", view.SummaryShown.ReturnedNote);
    }

    [Fact]
    public async Task Changing_the_period_reloads_documents_only()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        host.SupplierPayments.Calls.Clear();

        await presenter.FilterAsync(new DocumentPeriod(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)));

        Assert.DoesNotContain("payables", host.SupplierPayments.Calls);
        Assert.Single(view.PayablesShown);
        Assert.Equal(new[] { 4 }, view.PaymentsShown[^1].Select(p => p.Id));
        Assert.Equal(2_500_000, view.SummaryShown!.TotalDebt); // debt is "as of now", not per period
    }

    [Fact]
    public async Task Inverted_period_marks_the_from_date_and_does_not_search()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.FilterAsync(new DocumentPeriod(new DateTime(2026, 11, 20), new DateTime(2026, 11, 1)));

        Assert.Equal(new[] { "Từ ngày phải trước đến ngày." }, view.PeriodErrors);
        Assert.Empty(host.SupplierPayments.Searches);
        Assert.Empty(view.PaymentsShown);
    }

    [Fact]
    public async Task Inverted_period_on_a_full_reload_still_refreshes_the_payables()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.LoadAsync(new DocumentPeriod(new DateTime(2026, 11, 20), new DateTime(2026, 11, 1)));

        Assert.Single(view.PayablesShown);
        Assert.Empty(host.SupplierPayments.Searches);
        Assert.Equal("Từ ngày phải trước đến ngày.", view.PeriodErrors.Single());
    }

    [Fact]
    public async Task Failed_payables_are_reported_and_the_documents_still_load()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.SupplierPayments.FailPayablesWith = Error.Forbidden();

        await presenter.LoadAsync(November);

        Assert.Empty(view.PayablesShown);
        Assert.Contains(view.Errors, e => e.StartsWith("Không tải được công nợ phải trả", StringComparison.Ordinal));
        Assert.Single(view.PaymentsShown);
        Assert.Equal("–", view.SummaryShown!.TotalText);
        Assert.Equal("Không tải được công nợ", view.SummaryShown.TotalNote);
    }

    [Fact]
    public async Task Failed_returns_are_reported_instead_of_showing_a_blank_card()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.SupplierReturns.FailSearchWith = Error.Forbidden();

        await presenter.LoadAsync(November);

        Assert.Contains(view.Errors, e => e.StartsWith("Không tải được phiếu trả hàng NCC", StringComparison.Ordinal));
        Assert.Equal("Không tải được phiếu trả", view.SummaryShown!.ReturnedNote);
        Assert.Single(view.PaymentsShown);
    }

    [Fact]
    public async Task Failed_payments_are_reported()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.SupplierPayments.FailSearchWith = Error.Forbidden();

        await presenter.LoadAsync(November);

        Assert.Contains(view.Errors, e => e.StartsWith("Không tải được phiếu chi", StringComparison.Ordinal));
        Assert.Equal("–", view.SummaryShown!.PaidText);
    }

    [Fact]
    public async Task A_slow_load_overtaken_by_a_newer_period_is_dropped()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        var gate = new TaskCompletionSource();
        host.SupplierPayments.BeforeSearchReturns = () => gate.Task;
        var slow = presenter.FilterAsync(November);

        host.SupplierPayments.BeforeSearchReturns = null;
        await presenter.FilterAsync(new DocumentPeriod(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)));
        gate.SetResult();
        await slow;

        Assert.Single(view.PaymentsShown);
        Assert.Equal(new[] { 4 }, view.PaymentsShown[0].Select(p => p.Id));
    }

    [Fact]
    public async Task Closed_view_is_not_updated()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.IsAlive = false;

        await presenter.LoadAsync(November);

        Assert.Empty(view.PayablesShown);
        Assert.Null(view.SummaryShown);
    }

    [Fact]
    public async Task Pay_is_offered_only_for_suppliers_still_owed()
    {
        var (host, presenter, _) = Create();
        using var __ = host;
        await presenter.LoadAsync(November);

        Assert.True(presenter.CanPayFor(1));
        Assert.False(presenter.CanPayFor(3));
        Assert.False(presenter.CanPayFor(null));
    }

    [Fact]
    public async Task Pay_opens_the_dialog_with_the_fresh_balance_then_confirms_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        host.SupplierPayments.Payables[1] = host.SupplierPayments.Payables[1] with { DebtBalance = 2_300_000 }; // a new receipt meanwhile
        view.PaymentResult = Payment(10, 1, DateTimeOffset.Now, 300_000) with { DebtAfter = 2_000_000 };

        await presenter.PayAsync(1);

        Assert.Equal(2_300_000, view.PaymentDialogs.Single().DebtBalance);
        var toast = Assert.Single(view.Successes);
        Assert.StartsWith("Phiếu chi PC261110-0010", toast, StringComparison.Ordinal);
        Assert.Contains("Còn nợ Vinamilk", toast, StringComparison.Ordinal);
        Assert.Equal(2, view.PayablesShown.Count);
    }

    [Fact]
    public async Task Pay_for_a_supplier_settled_elsewhere_explains_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        host.SupplierPayments.Payables.RemoveAll(p => p.SupplierId == 1);

        await presenter.PayAsync(1);

        Assert.Empty(view.PaymentDialogs);
        Assert.StartsWith("Vinamilk không còn công nợ", view.Infos.Single(), StringComparison.Ordinal);
        Assert.Equal(2, view.PayablesShown.Count);
        Assert.False(presenter.CanPayFor(1));
    }

    [Fact]
    public async Task Cancelled_payment_dialog_reloads_only_when_the_balance_was_stale()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);

        await presenter.PayAsync(2);
        Assert.Single(view.PayablesShown);

        host.SupplierPayments.Payables[0] = host.SupplierPayments.Payables[0] with { DebtBalance = 700_000 };
        await presenter.PayAsync(2);
        Assert.Equal(2, view.PayablesShown.Count);
        Assert.Empty(view.Successes);
    }

    [Fact]
    public async Task Pay_without_permission_is_refused()
    {
        var (host, presenter, view) = Create(Permissions.PurchasesManage);
        using var _ = host;

        await presenter.PayAsync(1);

        Assert.Single(view.Warnings);
        Assert.Empty(view.PaymentDialogs);
        Assert.DoesNotContain("payables", host.SupplierPayments.Calls);
    }

    [Fact]
    public void Ledger_opens_for_a_selected_supplier_only()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        presenter.ShowLedger(null);
        presenter.ShowLedger(2);

        Assert.Equal(new[] { 2 }, view.LedgersOpened);
    }

    [Fact]
    public async Task Cancel_payment_asks_a_reason_then_confirms_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        var row = view.PaymentsShown[0][0];
        view.ReasonAnswer = "  Chuyển khoản bị hoàn  ";

        await presenter.CancelPaymentAsync(row);

        Assert.Contains("Công nợ sẽ tăng lại", view.ReasonQuestions.Single(), StringComparison.Ordinal);
        Assert.Contains("cancel:1:Chuyển khoản bị hoàn", host.SupplierPayments.Calls);
        Assert.StartsWith("Đã hủy phiếu chi PC261110-0001", view.Successes.Single(), StringComparison.Ordinal);
        Assert.Equal(2, view.PaymentsShown.Count);
    }

    [Fact]
    public async Task Cancel_payment_backed_out_or_blank_does_nothing()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        var row = view.PaymentsShown[0][0];

        view.ReasonAnswer = null;
        await presenter.CancelPaymentAsync(row);
        view.ReasonAnswer = "   ";
        await presenter.CancelPaymentAsync(row);

        Assert.DoesNotContain(host.SupplierPayments.Calls, c => c.StartsWith("cancel", StringComparison.Ordinal));
        Assert.Empty(view.Successes);
    }

    [Fact]
    public async Task Cancel_of_a_cancelled_payment_or_without_permission_is_ignored_or_refused()
    {
        var (host, presenter, view) = Create(Permissions.PurchasesManage);
        using var _ = host;
        await presenter.LoadAsync(November);
        var cancelled = view.PaymentsShown[0].Single(p => p.IsCancelled);
        var valid = view.PaymentsShown[0].First(p => !p.IsCancelled);

        Assert.False(presenter.CanCancel(valid));
        await presenter.CancelPaymentAsync(cancelled);
        Assert.Empty(view.Warnings);
        await presenter.CancelPaymentAsync(valid);
        Assert.Single(view.Warnings);
        Assert.Empty(view.ReasonQuestions);
    }

    [Fact]
    public async Task Failed_cancel_warns_and_refreshes_the_stale_list()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        host.SupplierPayments.FailCancelWith = new Error("payment.cancelled", "Phiếu chi đã được hủy.");

        await presenter.CancelPaymentAsync(view.PaymentsShown[0][0]);

        Assert.Equal(new[] { "Phiếu chi đã được hủy." }, view.Warnings);
        Assert.Empty(view.Successes);
        Assert.Equal(2, view.PaymentsShown.Count);
    }

    [Fact]
    public async Task New_return_preselects_an_active_supplier_then_confirms_reloads_and_shows_the_tab()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.ReturnResult = Posted(140_000, 40_000, 100_000, 1_900_000);

        await presenter.NewReturnAsync(1);

        Assert.Equal(new int?[] { 1 }, view.ReturnEditors);
        var toast = view.Successes.Single();
        Assert.Contains("TN261121-0001", toast, StringComparison.Ordinal);
        Assert.Contains("cấn trừ nợ", toast, StringComparison.Ordinal);
        Assert.Contains("NCC hoàn", toast, StringComparison.Ordinal);
        Assert.Single(view.PayablesShown);
        Assert.Equal(1, view.ReturnsTabSelected);
    }

    [Fact]
    public async Task New_return_does_not_preselect_a_stopped_supplier_and_a_cancelled_editor_changes_nothing()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.NewReturnAsync(3);

        Assert.Equal(new int?[] { null }, view.ReturnEditors);
        Assert.Empty(view.Successes);
        Assert.Empty(view.PayablesShown);
        Assert.Equal(0, view.ReturnsTabSelected);
    }

    [Fact]
    public async Task New_return_needs_permission_and_reports_load_failures()
    {
        var (payer, payerPresenter, payerView) = Create(Permissions.SupplierPaymentsManage);
        using (payer)
        {
            await payerPresenter.NewReturnAsync(null);
            Assert.Single(payerView.Warnings);
            Assert.Empty(payerView.ReturnEditors);
        }

        var (host, presenter, view) = Create();
        using var _ = host;
        host.Suppliers.FailWith = Error.Forbidden();
        await presenter.NewReturnAsync(null);
        Assert.Equal(new[] { Error.Forbidden().Message }, view.Warnings);
        Assert.Empty(view.ReturnEditors);
    }

    [Fact]
    public async Task Opening_a_return_shows_its_detail_or_warns()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        await presenter.LoadAsync(November);
        var rows = view.ReturnsShown[0];

        await presenter.OpenReturnAsync(rows.Single(r => r.Id == 1));
        await presenter.OpenReturnAsync(rows.Single(r => r.Id == 2));

        Assert.Equal("TN261110-0001", view.ReturnDetails.Single().Code);
        Assert.Equal(new[] { "Không tìm thấy phiếu trả hàng." }, view.Warnings);
    }
}
