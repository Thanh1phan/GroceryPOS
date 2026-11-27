using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Sales;

namespace GroceryPOS.Presentation.Tests;

public sealed class ReturnPresenterTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    private sealed class FakeReturnView : RecordingView, IReturnView
    {
        public List<ReturnSheet?> Sheets { get; } = new();
        public List<ReturnScreenState> States { get; } = new();
        public List<(string Text, Tone Tone)> Statuses { get; } = new();
        public List<(string Text, Tone Tone)> Histories { get; } = new();
        public string ScanText { get; private set; } = string.Empty;
        public List<string> Focus { get; } = new();
        public int Beeps { get; private set; }
        public List<string> QuantityPrompts { get; } = new();
        public decimal? QuantityAnswer { get; set; }
        public List<(string Title, Func<int, IReadOnlyList<ReceiptLine>> Layout, int Paper)> Slips { get; } = new();

        public ReturnScreenState State => States[^1];

        public (string Text, Tone Tone) Status => Statuses[^1];

        public void ShowSheet(ReturnSheet? sheet) => Sheets.Add(sheet);

        public void ShowState(ReturnScreenState state) => States.Add(state);

        public void ShowStatus(string text, Tone tone) => Statuses.Add((text, tone));

        public void ShowHistory(string text, Tone tone) => Histories.Add((text, tone));

        public void SetScanText(string text) => ScanText = text;

        public void FocusScan() => Focus.Add("scan");

        public void FocusLines() => Focus.Add("lines");

        public decimal? AskQuantity(string prompt, string unit, decimal start)
        {
            QuantityPrompts.Add($"{prompt}|{unit}|{start}");
            return QuantityAnswer;
        }

        public void ShowSlip(string title, Func<int, IReadOnlyList<ReceiptLine>> layout, int paperWidthMm) =>
            Slips.Add((title, layout, paperWidthMm));

        public void Beep() => Beeps++;
    }

    private static ReturnableLineDto Line(int id, string code, decimal sold, decimal returned = 0, decimal unit = 10_000) =>
        new(id, id, code, $"Sản phẩm {code}", "gói", sold, returned, sold - returned, sold * unit, (sold - returned) * unit);

    private static ReturnableSaleDto Sale(
        string code = "HD261127-0001",
        int id = 1,
        bool withinWindow = true,
        bool canReturn = true,
        decimal milkReturned = 0) => new(
        id, code, new DateTimeOffset(2026, 11, 27, 8, 15, 0, Vn), null, null, 50_000, milkReturned * 10_000, 0, 0,
        withinWindow, canReturn,
        [Line(id * 10 + 1, "SUA01", 3, milkReturned), Line(id * 10 + 2, "MI01", 2)]);

    private static ShiftSummaryDto OpenShift() => new(
        7, "CA261127-0001", 1, "Thu ngân An", ShiftStatus.Open, "Đang mở", new DateTimeOffset(2026, 11, 27, 7, 0, 0, Vn), null,
        500_000, new ShiftTotals(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0, 0, 500_000, null, null, null, null, null, null, []);

    private static (TestHost Host, ReturnPresenter Presenter, FakeReturnView View) Create(bool shiftOpen = true)
    {
        var host = new TestHost();
        host.CurrentUser.Session = new UserSession(1, "an", "Thu ngân An", "Thu ngân", new HashSet<string>(), DateTimeOffset.UnixEpoch);
        host.Shifts.Current = new CurrentShiftDto(shiftOpen ? OpenShift() : null, 0);
        host.Returns.Sales["HD261127-0001"] = Sale();
        host.Returns.Sales["HD261127-0002"] = Sale("HD261127-0002", id: 2);
        var presenter = host.Get<ReturnPresenter>();
        var view = new FakeReturnView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    private static async Task<(TestHost Host, ReturnPresenter Presenter, FakeReturnView View)> CreateLoadedAsync(bool shiftOpen = true)
    {
        var (host, presenter, view) = Create(shiftOpen);
        await presenter.StartAsync();
        await presenter.LoadInvoiceAsync("hd261127-0001");
        return (host, presenter, view);
    }

    private static void PickMilk(ReturnPresenter presenter, decimal quantity = 3, string reason = "Khách đổi ý")
    {
        var sheet = presenter.Sheet!;
        sheet.SetQuantity(sheet.Lines[0].SaleLineId, quantity);
        presenter.SetReason(reason);
    }

    [Fact]
    public async Task Start_reads_the_shift_and_shows_the_return_window_hint()
    {
        var (host, presenter, view) = Create();
        host.SalesOptions = SalesOptions.Default with { ReturnWindowDays = 3 };

        await presenter.StartAsync();

        Assert.True(presenter.HasOpenShift);
        Assert.Equal(("Khách được đổi trả trong 3 ngày kể từ ngày mua.", Tone.Muted), view.Status);
        Assert.False(view.State.HasInvoice);
        Assert.False(view.State.CanSubmit);
    }

    [Fact]
    public async Task Loading_an_invoice_binds_its_lines_history_and_focuses_the_grid()
    {
        var (host, presenter, view) = Create();
        host.Returns.History[1] = [new SaleReturnSummaryDto(3, "TH261126-0001", new DateTimeOffset(2026, 11, 26, 2, 0, 0, TimeSpan.Zero), 10_000, PaymentMethod.Cash, "Hàng lỗi", 1)];

        await presenter.LoadInvoiceAsync("  hd261127-0001 ");

        Assert.Equal("HD261127-0001", presenter.Sheet!.Sale.Code);
        Assert.Same(presenter.Sheet, view.Sheets[^1]);
        Assert.Equal("HD261127-0001", view.ScanText);
        Assert.StartsWith("TH261126-0001 · 26/11 09:00 · 10.000", view.Histories[^1].Text);
        Assert.Equal(Tone.Muted, view.Status.Tone);
        Assert.StartsWith("Chọn sản phẩm khách trả", view.Status.Text);
        Assert.Equal("lines", view.Focus[^1]);
        Assert.Equal("Trong hạn đổi trả", view.State.BadgeText);
        Assert.Equal("Chọn sản phẩm và số lượng khách trả.", view.State.Message);
    }

    [Fact]
    public async Task An_unknown_invoice_beeps_and_keeps_the_scan_box_ready()
    {
        var (_, presenter, view) = await CreateLoadedAsync();
        var loaded = presenter.Sheet;

        await presenter.LoadInvoiceAsync("HD000000-9999");

        Assert.Equal(1, view.Beeps);
        Assert.Equal(("Không tìm thấy hóa đơn HD000000-9999.", Tone.Danger), view.Status);
        Assert.Equal("scan", view.Focus[^1]);
        Assert.Same(loaded, presenter.Sheet);
    }

    [Fact]
    public async Task Scanning_another_invoice_with_picked_lines_asks_first()
    {
        var (host, presenter, view) = await CreateLoadedAsync();
        PickMilk(presenter);
        view.ConfirmAnswer = false;

        await presenter.LoadInvoiceAsync("HD261127-0002");

        Assert.Equal("Bỏ phiếu trả đang làm cho hóa đơn HD261127-0001?", Assert.Single(view.Questions));
        Assert.Equal("HD261127-0001", presenter.Sheet!.Sale.Code);
        Assert.DoesNotContain("load:HD261127-0002", host.Returns.Calls);

        view.ConfirmAnswer = true;
        await presenter.LoadInvoiceAsync("HD261127-0002");
        Assert.Equal("HD261127-0002", presenter.Sheet!.Sale.Code);
        Assert.Equal(0, presenter.Sheet.SelectedLineCount);
    }

    [Fact]
    public async Task Rescanning_the_same_invoice_keeps_the_picks_reason_and_method()
    {
        var (_, presenter, view) = await CreateLoadedAsync();
        PickMilk(presenter, 2);
        presenter.SetRefundMethod(PaymentMethod.BankTransfer);
        var before = presenter.Sheet;

        await presenter.LoadInvoiceAsync("HD261127-0001");

        Assert.Empty(view.Questions);
        Assert.NotSame(before, presenter.Sheet);
        Assert.Equal(2, presenter.Sheet!.Lines[0].Quantity);
        Assert.Equal("Khách đổi ý", presenter.Sheet.Reason);
        Assert.Equal(PaymentMethod.BankTransfer, presenter.Sheet.RefundMethod);
    }

    [Fact]
    public async Task The_latest_scan_wins_when_an_older_one_answers_last()
    {
        var (host, presenter, view) = Create();
        var gate = new TaskCompletionSource();
        host.Returns.BeforeLoadReturns["HD261127-0001"] = () => gate.Task;

        var slow = presenter.LoadInvoiceAsync("HD261127-0001");
        await presenter.LoadInvoiceAsync("HD261127-0002");
        gate.SetResult();
        await slow;

        Assert.Equal("HD261127-0002", presenter.Sheet!.Sale.Code);
        Assert.Equal("HD261127-0002", view.ScanText);
    }

    [Fact]
    public async Task A_failed_history_load_is_reported_in_the_card_but_the_invoice_still_loads()
    {
        var (host, presenter, view) = Create();
        host.Returns.FailHistoryWith = new Error("db", "Mất kết nối");

        await presenter.LoadInvoiceAsync("HD261127-0001");

        Assert.NotNull(presenter.Sheet);
        Assert.Equal(("Không tải được các phiếu trả trước đó: Mất kết nối", Tone.Danger), view.Histories[^1]);
    }

    [Fact]
    public async Task Marking_a_line_damaged_picks_it_and_flips_the_button_caption()
    {
        var (_, presenter, view) = await CreateLoadedAsync();
        var milk = presenter.Sheet!.Lines[0];

        presenter.ToggleRestock(milk);

        Assert.Equal(3, milk.Quantity);
        Assert.False(milk.Restock);
        Assert.Equal("Nhập lại kho (F6)", presenter.ActionsFor(milk).RestockCaption);
        Assert.Equal("30.000 ₫", view.State.WrittenOff.Replace(' ', ' '));

        presenter.ToggleRestock(milk);
        Assert.True(milk.Restock);
        Assert.Equal("Hàng hỏng (F6)", presenter.ActionsFor(milk).RestockCaption);
    }

    [Fact]
    public async Task Lines_of_an_invoice_past_the_return_window_cannot_be_picked()
    {
        var (host, presenter, _) = Create();
        host.Returns.Sales["HD261101-0001"] = Sale("HD261101-0001", id: 3, withinWindow: false, canReturn: false);
        await presenter.LoadInvoiceAsync("HD261101-0001");
        var line = presenter.Sheet!.Lines[0];

        presenter.ToggleLine(line);
        presenter.Step(line, 1);
        presenter.ReturnAll();

        Assert.Equal(0, presenter.Sheet.SelectedLineCount);
        Assert.False(presenter.ActionsFor(line).CanEdit);
    }

    [Fact]
    public async Task Quantity_prompt_caps_at_what_is_left_and_says_so()
    {
        var (_, presenter, view) = await CreateLoadedAsync();
        var milk = presenter.Sheet!.Lines[0];
        view.QuantityAnswer = 5;

        presenter.EditQuantity(milk);

        Assert.Equal("Sản phẩm SUA01 – còn trả được 3 gói|gói|3", Assert.Single(view.QuantityPrompts));
        Assert.Equal(3, milk.Quantity);
        Assert.Equal(("Chỉ còn trả được tối đa 3 gói.", Tone.Warning), view.Status);

        view.QuantityAnswer = null;
        presenter.EditQuantity(milk);
        Assert.Equal(3, milk.Quantity);
    }

    [Fact]
    public async Task Submit_with_a_blocker_only_beeps()
    {
        var (host, presenter, view) = await CreateLoadedAsync();
        presenter.Sheet!.SetQuantity(presenter.Sheet.Lines[0].SaleLineId, 1); // no reason yet

        await presenter.SubmitAsync();

        Assert.Equal(1, view.Beeps);
        Assert.Empty(view.Questions);
        Assert.Empty(host.Returns.Created);
    }

    [Fact]
    public async Task A_cash_refund_rechecks_the_shift_opened_meanwhile_on_the_pos_screen()
    {
        var (host, presenter, view) = await CreateLoadedAsync(shiftOpen: false);
        PickMilk(presenter);
        Assert.False(view.State.CanSubmit);
        Assert.StartsWith("Bạn chưa mở ca", view.State.Message);

        host.Shifts.Current = new CurrentShiftDto(OpenShift(), 0);
        await presenter.SubmitAsync();

        Assert.Single(host.Returns.Created);
        Assert.True(presenter.HasOpenShift);
    }

    [Fact]
    public async Task A_cash_refund_without_a_shift_is_refused_with_the_reason()
    {
        var (host, presenter, view) = await CreateLoadedAsync(shiftOpen: false);
        PickMilk(presenter);

        await presenter.SubmitAsync();

        Assert.Equal(1, view.Beeps);
        Assert.StartsWith("Bạn chưa mở ca", view.Status.Text);
        Assert.Empty(host.Returns.Created);
    }

    [Fact]
    public async Task A_transfer_refund_does_not_need_a_shift()
    {
        var (host, presenter, view) = await CreateLoadedAsync(shiftOpen: false);
        PickMilk(presenter);
        presenter.SetRefundMethod(PaymentMethod.BankTransfer);
        Assert.True(view.State.CanSubmit);

        await presenter.SubmitAsync();

        Assert.Equal(PaymentMethod.BankTransfer, Assert.Single(host.Returns.Created).RefundMethod);
    }

    [Fact]
    public async Task Declining_the_confirmation_makes_no_refund()
    {
        var (host, presenter, view) = await CreateLoadedAsync();
        PickMilk(presenter);
        view.ConfirmAnswer = false;

        await presenter.SubmitAsync();

        Assert.Equal("Hoàn 30.000 ₫ tiền mặt cho 1 sản phẩm – hóa đơn HD261127-0001?", Assert.Single(view.DangerQuestions).Replace(' ', ' '));
        Assert.Empty(host.Returns.Created);
        Assert.NotNull(presenter.Sheet);
    }

    [Fact]
    public async Task A_successful_refund_clears_the_screen_and_previews_the_slip()
    {
        var (host, presenter, view) = await CreateLoadedAsync();
        PickMilk(presenter, 2);
        presenter.ToggleRestock(presenter.Sheet!.Lines[0]);

        await presenter.SubmitAsync();

        var request = Assert.Single(host.Returns.Created);
        Assert.Equal(new ReturnLineInput(11, 2, Restock: false), Assert.Single(request.Lines));
        Assert.Equal("Khách đổi ý", request.Reason);
        Assert.Null(presenter.Sheet);
        Assert.Null(view.Sheets[^1]);
        Assert.Equal(string.Empty, view.ScanText);
        Assert.Equal(Tone.Success, view.Status.Tone);
        Assert.StartsWith("✔ TH261127-0001: hoàn 20.000", view.Status.Text);
        var slip = Assert.Single(view.Slips);
        Assert.Equal("Phiếu trả hàng TH261127-0001", slip.Title);
        Assert.Equal(80, slip.Paper);
        Assert.Contains(slip.Layout(48), l => l.Text.Contains("Thu ngân An", StringComparison.Ordinal));
        Assert.Equal("scan", view.Focus[^1]);
        Assert.False(presenter.IsSubmitting);
    }

    [Fact]
    public async Task A_failed_refund_reloads_the_invoice_keeping_the_picks_and_flags_reduced_lines()
    {
        var (host, presenter, view) = await CreateLoadedAsync();
        PickMilk(presenter, 3);
        host.Returns.FailCreateWith = Error.Conflict("Số lượng trả vượt quá số còn lại.");
        host.Returns.DuringCreate = () => host.Returns.Sales["HD261127-0001"] = Sale(milkReturned: 2); // another counter took 2

        await presenter.SubmitAsync();

        Assert.Equal(1, view.Beeps);
        Assert.Equal(1, presenter.Sheet!.Lines[0].Quantity);
        Assert.Equal("Khách đổi ý", presenter.Sheet.Reason);
        Assert.Equal(Tone.Warning, view.Status.Tone);
        Assert.Contains("đã giảm số lượng trả ở 1 dòng", view.Status.Text);
        Assert.True(view.State.CanSubmit);
    }

    [Fact]
    public async Task A_failed_refund_with_nothing_changed_keeps_the_error_on_screen()
    {
        var (host, presenter, view) = await CreateLoadedAsync();
        PickMilk(presenter, 1);
        host.Returns.FailCreateWith = new Error("db", "Không lưu được phiếu trả.");

        await presenter.SubmitAsync();

        Assert.Equal(("Không lưu được phiếu trả.", Tone.Danger), view.Status);
        Assert.Equal(1, presenter.Sheet!.Lines[0].Quantity);
        Assert.Equal(2, host.Returns.Calls.Count(c => c.Equals("load:HD261127-0001", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Leaving_with_picked_lines_asks_and_can_be_refused()
    {
        var (_, presenter, view) = await CreateLoadedAsync();
        Assert.True(presenter.CanLeave());
        Assert.Empty(view.Questions);

        PickMilk(presenter);
        view.ConfirmAnswer = false;
        Assert.False(presenter.CanLeave());
        Assert.Equal("Phiếu trả cho hóa đơn HD261127-0001 chưa hoàn tiền. Bỏ phiếu này?", Assert.Single(view.Questions));
    }

    [Fact]
    public async Task Reset_asks_when_lines_are_picked_then_clears_everything()
    {
        var (_, presenter, view) = await CreateLoadedAsync();
        PickMilk(presenter);

        view.ConfirmAnswer = false;
        presenter.Reset();
        Assert.NotNull(presenter.Sheet);

        view.ConfirmAnswer = true;
        presenter.Reset();
        Assert.Null(presenter.Sheet);
        Assert.Equal(string.Empty, view.ScanText);
        Assert.False(view.State.HasInvoice);
        Assert.StartsWith("Khách được đổi trả trong 7 ngày", view.Status.Text);
    }

    [Fact]
    public async Task Refresh_rereads_shift_and_invoice_without_losing_the_picks()
    {
        var (host, presenter, _) = await CreateLoadedAsync();
        PickMilk(presenter, 2);
        host.Shifts.Current = new CurrentShiftDto(null, 0);

        await presenter.RefreshAsync();

        Assert.False(presenter.HasOpenShift);
        Assert.Equal(2, presenter.Sheet!.Lines[0].Quantity);
        Assert.Equal(2, host.Returns.Calls.Count(c => c.Equals("load:HD261127-0001", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Nothing_reaches_a_closed_view()
    {
        var (_, presenter, view) = Create();
        view.IsAlive = false;
        var shown = view.Sheets.Count;

        await presenter.LoadInvoiceAsync("HD261127-0001");

        Assert.Equal(shown, view.Sheets.Count);
        Assert.Null(presenter.Sheet);
    }
}
