using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Sales;

namespace GroceryPOS.Presentation.Tests;

public sealed class PosPresenterTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    private static readonly PosProductDto Milk = new(1, "SUA01", "8934673000011", "Sữa tươi 1L", "hộp", 32_000, 8, 50, false);
    private static readonly PosProductDto Noodles = new(2, "MI01", "8934563000022", "Mì Hảo Hảo", "gói", 4_500, 8, 200, false);
    private static readonly PosProductDto Noodles2 = new(3, "MI02", null, "Mì Omachi", "gói", 8_000, 8, 100, false);

    private sealed class FakePosView : RecordingView, IPosView
    {
        public List<PosBoard> Boards { get; } = new();
        public List<PosShiftBar> ShiftBars { get; } = new();
        public List<(string Text, Tone Tone)> Statuses { get; } = new();
        public List<int> ParkedCounts { get; } = new();
        public string Phone { get; private set; } = string.Empty;
        public string? PhoneError { get; private set; }
        public string ScanText { get; set; } = string.Empty;
        public PosCartLine? Selected { get; private set; }
        public List<string> Focus { get; } = new();
        public List<CustomerDisplayState> Displays { get; } = new();
        public int Beeps { get; private set; }
        public List<string> Dialogs { get; } = new();

        public decimal? QuantityAnswer { get; set; }
        public (bool, DiscountInput?) DiscountAnswer { get; set; } = (false, null);
        public PosProductDto? SearchAnswer { get; set; }
        public PosProductDto? PriceCheckAnswer { get; set; }
        public Func<ShiftSummaryDto?>? OpenShiftAnswer { get; set; }
        public (bool, string?) ParkAnswer { get; set; } = (true, "Chị Lan");
        public Func<(Guid?, int)> ParkedAnswer { get; set; } = () => (null, 0);
        public string? ReprintAnswer { get; set; }
        public bool PrintAnswer { get; set; }
        public List<ReceiptData> Receipts { get; } = new();

        public PosBoard Board => Boards[^1];

        public (string Text, Tone Tone) Status => Statuses[^1];

        public void ShowBoard(PosBoard board) => Boards.Add(board);

        public void SelectLine(PosCartLine line) => Selected = line;

        public void ShowShift(PosShiftBar bar) => ShiftBars.Add(bar);

        public void ShowStatus(string text, Tone tone) => Statuses.Add((text, tone));

        public void ShowParkedCount(int count) => ParkedCounts.Add(count);

        public void SetPhone(string text) => Phone = text;

        public void ShowPhoneError(string? message) => PhoneError = message;

        public void RestoreScan(string text)
        {
            if (ScanText.Length == 0)
            {
                ScanText = text;
            }
        }

        public void ClearScan() => ScanText = string.Empty;

        public void FocusScan() => Focus.Add("scan");

        public void FocusPay() => Focus.Add("pay");

        public void ShowCustomerDisplay(CustomerDisplayState state) => Displays.Add(state);

        public decimal? AskQuantity(string name, string unit, decimal start)
        {
            Dialogs.Add($"quantity:{name}|{unit}|{start}");
            return QuantityAnswer;
        }

        public (bool Ok, DiscountInput? Discount) AskDiscount(string title, decimal baseAmount, DiscountInput? current)
        {
            Dialogs.Add($"discount:{title}|{baseAmount}");
            return DiscountAnswer;
        }

        public PosProductDto? SearchProduct(string term)
        {
            Dialogs.Add($"search:{term}");
            return SearchAnswer;
        }

        public PosProductDto? PriceCheck()
        {
            Dialogs.Add("price-check");
            return PriceCheckAnswer;
        }

        public ShiftSummaryDto? OpenShift(decimal suggestedFloat, string? cashierName)
        {
            Dialogs.Add($"open-shift:{suggestedFloat}|{cashierName}");
            return OpenShiftAnswer?.Invoke();
        }

        public (bool Ok, string? Label) AskParkLabel(int lineCount, decimal total, string defaultLabel)
        {
            Dialogs.Add($"park:{lineCount}|{total}|{defaultLabel}");
            return ParkAnswer;
        }

        public (Guid? Chosen, int Remaining) PickParkedOrder(bool cartBusy)
        {
            Dialogs.Add($"parked:{cartBusy}");
            return ParkedAnswer();
        }

        public string? AskReprintCode(string? lastCode)
        {
            Dialogs.Add($"reprint:{lastCode}");
            return ReprintAnswer;
        }

        public bool ShowReceipt(ReceiptData receipt, int paperWidthMm)
        {
            Receipts.Add(receipt);
            return PrintAnswer;
        }

        public void Beep() => Beeps++;
    }

    private static ShiftSummaryDto OpenShift(string code = "CA261128-0001") => new(
        7, code, 1, "Thu ngân An", ShiftStatus.Open, "Đang mở", new DateTimeOffset(2026, 11, 28, 7, 0, 0, Vn), null,
        500_000, new ShiftTotals(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0, 0, 500_000, null, null, null, null, null, null, []);

    private static (TestHost Host, PosPresenter Presenter, FakePosView View) Create(
        bool shiftOpen = true, bool requireShift = true, bool canDiscount = true)
    {
        var host = canDiscount ? new TestHost(Permissions.SalesDiscount) : new TestHost();
        host.CurrentUser.Session = new UserSession(1, "an", "Thu ngân An", "Thu ngân", new HashSet<string>(), DateTimeOffset.UnixEpoch);
        host.SalesOptions = SalesOptions.Default with { RequireOpenShift = requireShift };
        host.Shifts.Current = new CurrentShiftDto(shiftOpen ? OpenShift() : null, 300_000);
        foreach (var product in new[] { Milk, Noodles, Noodles2 })
        {
            host.PosLookup.ByCode[product.Code] = product;
            if (product.Barcode is { } barcode)
            {
                host.PosLookup.ByCode[barcode] = product;
            }

            host.PosLookup.Catalog.Add(product);
        }

        var presenter = host.Get<PosPresenter>();
        var view = new FakePosView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    private static async Task<(TestHost Host, PosPresenter Presenter, FakePosView View)> CreateStartedAsync(
        bool shiftOpen = true, bool requireShift = true, bool canDiscount = true)
    {
        var created = Create(shiftOpen, requireShift, canDiscount);
        await created.Presenter.StartAsync();
        return created;
    }

    // ---- Start & shift -------------------------------------------------------------------------

    [Fact]
    public async Task Start_shows_the_open_shift_and_the_parked_count()
    {
        var (_, _, view) = Create();
        Assert.Equal(PosShiftBar.Checking, view.ShiftBars[0]);
        Assert.Equal((PosTexts.Ready, Tone.Muted), view.Status);

        var (_, presenter, started) = await CreateStartedAsync();

        Assert.Equal(0, started.ParkedCounts[^1]);
        Assert.Equal("Ca CA261128-0001 · mở lúc 07:00 28/11 · tiền đầu ca 500.000 ₫", started.ShiftBars[^1].Text);
        Assert.False(started.ShiftBars[^1].ShowOpenButton);
        Assert.Empty(started.Dialogs);
        Assert.NotNull(presenter.Shift);
    }

    [Fact]
    public async Task Start_without_a_shift_offers_to_open_one_with_the_suggested_float()
    {
        var (_, presenter, view) = Create(shiftOpen: false);
        view.OpenShiftAnswer = () => OpenShift("CA261128-0002");

        await presenter.StartAsync();

        Assert.Equal("open-shift:300000|Thu ngân An", Assert.Single(view.Dialogs));
        Assert.Equal("CA261128-0002", presenter.Shift!.Code);
        Assert.Equal(("Đã mở ca CA261128-0002 với tiền đầu ca 500.000 ₫.", Tone.Success), view.Status);
        Assert.False(view.ShiftBars[^1].ShowOpenButton);
        Assert.Equal("scan", view.Focus[^1]);
    }

    [Fact]
    public async Task Declining_to_open_a_shift_blocks_payment_and_shows_the_open_button()
    {
        var (_, presenter, view) = Create(shiftOpen: false);

        await presenter.StartAsync();
        await presenter.ScanAsync("SUA01");

        Assert.True(view.ShiftBars[^1].ShowOpenButton);
        Assert.Equal(Tone.Warning, view.ShiftBars[^1].Tone);
        Assert.Equal(PosBoard.ShiftMissingBlocker, view.Board.Blocker);
        Assert.False(view.Board.CanPay);
    }

    [Fact]
    public async Task A_store_without_required_shifts_does_not_ask_to_open_one()
    {
        var (_, _, view) = await CreateStartedAsync(shiftOpen: false, requireShift: false);
        Assert.Empty(view.Dialogs);
        Assert.Equal(new PosShiftBar("Chưa mở ca.", Tone.Muted, true), view.ShiftBars[^1]);
    }

    [Fact]
    public async Task Open_shift_button_shows_a_shift_opened_elsewhere_without_the_dialog()
    {
        var (host, presenter, view) = Create(shiftOpen: false);
        await presenter.StartAsync(); // declined
        view.Dialogs.Clear();
        host.Shifts.Current = new CurrentShiftDto(OpenShift("CA261128-0009"), 0);

        await presenter.OpenShiftAsync();

        Assert.Empty(view.Dialogs);
        Assert.Equal("CA261128-0009", presenter.Shift!.Code);
    }

    [Fact]
    public async Task A_shift_that_cannot_be_read_is_shown_and_the_button_reports_why()
    {
        var (host, presenter, view) = Create(shiftOpen: false);
        host.Shifts.FailCurrentWith = Error.Forbidden("Bạn không có quyền mở ca.");

        await presenter.StartAsync();
        Assert.Equal(new PosShiftBar("Không thể bán hàng: Bạn không có quyền mở ca.", Tone.Danger, false), view.ShiftBars[^1]);
        Assert.Empty(view.Dialogs);

        await presenter.OpenShiftAsync();
        Assert.Equal("Bạn không có quyền mở ca.", Assert.Single(view.Warnings));
    }

    // ---- Scanning ------------------------------------------------------------------------------

    [Fact]
    public async Task Scanning_a_barcode_adds_and_selects_the_line()
    {
        var (_, presenter, view) = await CreateStartedAsync();

        await presenter.ScanAsync("8934673000011");
        await presenter.ScanAsync("3*MI01");

        Assert.Equal(2, presenter.Cart.Lines.Count);
        Assert.Equal(3, presenter.Cart.Lines[1].Quantity);
        Assert.Same(presenter.Cart.Lines[1], view.Selected);
        Assert.Equal(("Đã thêm Mì Hảo Hảo × 3 – 13.500 ₫", Tone.Success), view.Status);
        Assert.Equal("Mì Hảo Hảo", view.Displays[^1].Lines[^1].Name);
        Assert.True(view.Board.CanPay);
    }

    [Fact]
    public async Task An_unknown_scan_beeps_and_comes_back_into_the_scan_box()
    {
        var (_, presenter, view) = await CreateStartedAsync();

        await presenter.ScanAsync(" 999 ");

        Assert.True(presenter.Cart.IsEmpty);
        Assert.Equal(1, view.Beeps);
        Assert.Equal(Tone.Danger, view.Status.Tone);
        Assert.Equal("999", view.ScanText);
    }

    [Fact]
    public async Task A_failed_scan_does_not_overwrite_the_next_code_already_being_scanned()
    {
        var (_, presenter, view) = await CreateStartedAsync();
        view.ScanText = "89346"; // the scanner already started the next code

        await presenter.ScanAsync("999");

        Assert.Equal("89346", view.ScanText);
    }

    [Fact]
    public async Task A_name_with_one_match_is_added_and_several_open_the_search()
    {
        var (_, presenter, view) = await CreateStartedAsync();

        await presenter.ScanAsync("sữa");
        Assert.Equal("SUA01", Assert.Single(presenter.Cart.Lines).Code);

        view.SearchAnswer = null;
        await presenter.ScanAsync("2*mì");
        Assert.Equal("search:mì", view.Dialogs[^1]);
        Assert.Equal("2*mì", view.ScanText); // backing out of the list keeps what was typed

        view.ScanText = string.Empty;
        view.SearchAnswer = Noodles2;
        await presenter.ScanAsync("2*mì");
        Assert.Equal(2, presenter.Cart.Lines.Single(l => l.Code == "MI02").Quantity);
        Assert.Equal(string.Empty, view.ScanText);
    }

    [Fact]
    public async Task Scans_are_added_in_the_order_they_were_scanned()
    {
        var (host, presenter, view) = await CreateStartedAsync();
        var slow = new TaskCompletionSource();
        host.PosLookup.BeforeScanReturns["SUA01"] = () => slow.Task;

        var first = presenter.ScanAsync("SUA01");
        var second = presenter.ScanAsync("MI01");
        Assert.True(presenter.Cart.IsEmpty);

        slow.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(["SUA01", "MI01"], presenter.Cart.Lines.Select(l => l.Code));
    }

    [Fact]
    public async Task Paying_right_after_a_scan_waits_for_that_item()
    {
        var (host, presenter, _) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");
        var slow = new TaskCompletionSource();
        host.PosLookup.BeforeScanReturns["MI01"] = () => slow.Task;

        var scan = presenter.ScanAsync("MI01");
        var pay = presenter.CheckoutAsync();
        Assert.Empty(host.Checkout.Requests);

        slow.SetResult();
        await Task.WhenAll(scan, pay);

        Assert.Equal(2, Assert.Single(host.Checkout.Requests).Lines.Count);
        Assert.True(presenter.Cart.IsEmpty);
    }

    [Fact]
    public async Task Price_check_can_add_the_product()
    {
        var (_, presenter, view) = await CreateStartedAsync();
        view.PriceCheckAnswer = Milk;

        presenter.PriceCheck();

        Assert.Equal("SUA01", Assert.Single(presenter.Cart.Lines).Code);
        Assert.Equal("scan", view.Focus[^1]);
    }

    // ---- Lines ---------------------------------------------------------------------------------

    [Fact]
    public async Task Line_actions_fall_back_to_the_last_line_when_nothing_is_selected()
    {
        var (_, presenter, view) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");
        await presenter.ScanAsync("MI01");
        view.QuantityAnswer = 5;

        presenter.EditQuantity(null);
        presenter.Step(presenter.Cart.Lines[0], 1);

        Assert.Equal("quantity:Mì Hảo Hảo|gói|1", view.Dialogs[^1]);
        Assert.Equal(5, presenter.Cart.Lines[1].Quantity);
        Assert.Equal(2, presenter.Cart.Lines[0].Quantity);

        presenter.RemoveLine(null);
        Assert.Equal(("Đã xóa Mì Hảo Hảo.", Tone.Muted), view.Status);
        Assert.Single(presenter.Cart.Lines);
    }

    [Fact]
    public async Task Discounts_need_the_permission()
    {
        var (_, presenter, view) = await CreateStartedAsync(canDiscount: false);
        await presenter.ScanAsync("SUA01");

        presenter.EditLineDiscount(null);
        presenter.EditInvoiceDiscount();

        Assert.DoesNotContain(view.Dialogs, d => d.StartsWith("discount", StringComparison.Ordinal));
        Assert.Equal((PosTexts.NoDiscountPermission, Tone.Danger), view.Status);
        Assert.False(view.Board.CanDiscountLines);
        Assert.False(view.Board.CanDiscountInvoice);
    }

    [Fact]
    public async Task Line_and_invoice_discounts_are_applied_from_the_dialog()
    {
        var (_, presenter, view) = await CreateStartedAsync();
        await presenter.ScanAsync("2*SUA01");
        view.DiscountAnswer = (true, DiscountInput.Amount(4_000));

        presenter.EditLineDiscount(null);
        view.DiscountAnswer = (true, DiscountInput.Percent(10));
        presenter.EditInvoiceDiscount();

        Assert.Equal("discount:Giảm giá – Sữa tươi 1L|64000", view.Dialogs[0]);
        Assert.Equal("discount:Giảm giá hóa đơn|60000", view.Dialogs[1]);
        Assert.Equal(54_000, presenter.Cart.Total);
        Assert.Equal("−6.000 ₫ (10%)", view.Board.InvoiceDiscount);
    }

    // ---- Customer ------------------------------------------------------------------------------

    [Fact]
    public async Task Members_are_found_by_phone_and_an_empty_box_detaches()
    {
        var (host, presenter, view) = await CreateStartedAsync();
        host.PosLookup.CustomersByPhone["0901234567"] = new Customer("KH000001", "Chị Lan", "0901234567");

        await presenter.FindCustomerAsync("0909999999");
        Assert.NotNull(view.PhoneError);
        Assert.Null(presenter.Cart.Customer);

        await presenter.FindCustomerAsync("0901234567");
        Assert.Null(view.PhoneError);
        Assert.Equal("Chị Lan", presenter.Cart.Customer!.Name);
        Assert.Equal(("Khách hàng: Chị Lan (0 điểm)", Tone.Success), view.Status);
        Assert.True(view.Board.HasMember);

        await presenter.FindCustomerAsync("  ");
        Assert.Null(presenter.Cart.Customer);
        Assert.Equal("Khách lẻ", view.Board.CustomerInfo);
    }

    // ---- Checkout ------------------------------------------------------------------------------

    [Fact]
    public async Task Paying_clears_the_cart_thanks_the_customer_and_shows_the_receipt()
    {
        var (host, presenter, view) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");
        host.Checkout.Total = 32_000;
        host.Checkout.PointsEarned = 3;
        host.Checkout.LowStock.Add(new LowStockAlert(1, "SUA01", "Sữa tươi 1L", 2, 5));
        presenter.SetCashTendered(50_000);
        view.SetPhone("0901234567");

        await presenter.CheckoutAsync();

        var request = Assert.Single(host.Checkout.Requests);
        Assert.Equal(50_000, request.Payments.Single().Amount);
        Assert.True(presenter.Cart.IsEmpty);
        Assert.Equal(string.Empty, view.Phone);
        Assert.Equal("HD261128-0001", presenter.LastInvoiceCode);
        Assert.Equal(("✔ HD261128-0001: 32.000 ₫ · Tiền thừa 18.000 ₫ · +3 điểm · Sắp hết: Sữa tươi 1L", Tone.Success), view.Status);
        Assert.Equal(["sale:1"], host.SaleReceipts.Calls);
        Assert.Single(view.Receipts);
        Assert.Equal(CustomerDisplayMode.ThankYou, view.Displays[^1].Mode);
        Assert.Equal(CustomerDisplayMode.ThankYou, presenter.DisplayState.Mode);
        Assert.False(presenter.IsBusy);
        Assert.Equal("scan", view.Focus[^1]);
    }

    [Fact]
    public async Task The_thank_you_screen_stays_until_the_next_scan()
    {
        var (_, presenter, _) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");
        await presenter.CheckoutAsync();
        presenter.SetPaymentMethod(PaymentMethod.BankTransfer); // re-renders an empty cart

        Assert.Equal(CustomerDisplayMode.ThankYou, presenter.DisplayState.Mode);

        await presenter.ScanAsync("MI01");
        Assert.NotEqual(CustomerDisplayMode.ThankYou, presenter.DisplayState.Mode);
    }

    [Fact]
    public async Task Paying_without_a_shift_rereads_it_when_it_was_opened_elsewhere()
    {
        var (host, presenter, view) = Create(shiftOpen: false);
        await presenter.StartAsync(); // declined the dialog
        await presenter.ScanAsync("SUA01");
        host.Shifts.Current = new CurrentShiftDto(OpenShift(), 0); // opened on Ca làm việc
        view.Dialogs.Clear();

        await presenter.CheckoutAsync();

        Assert.Empty(view.Dialogs);
        Assert.Single(host.Checkout.Requests);
    }

    [Fact]
    public async Task Paying_without_a_shift_offers_to_open_it_and_goes_on_paying()
    {
        var (host, presenter, view) = Create(shiftOpen: false);
        await presenter.StartAsync();
        await presenter.ScanAsync("SUA01");

        await presenter.CheckoutAsync(); // declined again
        Assert.Empty(host.Checkout.Requests);
        Assert.Equal(("Chưa mở ca – vui lòng mở ca trước khi thanh toán.", Tone.Danger), view.Status);
        Assert.Equal(1, view.Beeps);

        view.OpenShiftAnswer = () => OpenShift();
        await presenter.CheckoutAsync();
        Assert.Single(host.Checkout.Requests);
    }

    [Fact]
    public async Task A_shift_closed_elsewhere_turns_the_bar_back_to_open_shift()
    {
        var (host, presenter, view) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");
        host.Checkout.FailWith = SaleErrors.NoOpenShift;

        await presenter.CheckoutAsync();

        Assert.Null(presenter.Shift);
        Assert.True(view.ShiftBars[^1].ShowOpenButton);
        Assert.Equal(SaleErrors.NoOpenShift.Message, Assert.Single(view.Warnings));
        Assert.False(presenter.Cart.IsEmpty);
        Assert.False(view.Board.CanPay);
    }

    [Fact]
    public async Task A_cart_that_cannot_be_paid_beeps_with_the_reason()
    {
        var (host, presenter, view) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");
        presenter.SetCashTendered(20_000);

        await presenter.CheckoutAsync();

        Assert.Empty(host.Checkout.Requests);
        Assert.Equal(("Khách còn thiếu 12.000 ₫.", Tone.Danger), view.Status);
        Assert.Equal(1, view.Beeps);
    }

    [Fact]
    public async Task Reprint_uses_the_last_invoice_and_reports_printing()
    {
        var (host, presenter, view) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");
        await presenter.CheckoutAsync();
        host.SaleReceipts.Calls.Clear();
        view.Receipts.Clear();

        await presenter.ReprintAsync(); // backed out
        Assert.Equal("reprint:HD261128-0001", view.Dialogs[^1]);
        Assert.Empty(host.SaleReceipts.Calls);

        view.ReprintAnswer = " HD261128-0001 ";
        view.PrintAnswer = true;
        await presenter.ReprintAsync();
        Assert.Equal(["reprint:HD261128-0001"], host.SaleReceipts.Calls);
        Assert.True(Assert.Single(view.Receipts).IsReprint);
        Assert.Equal(("Đã in lại hóa đơn HD261128-0001.", Tone.Muted), view.Status);
    }

    // ---- Cancel & leave ------------------------------------------------------------------------

    [Fact]
    public async Task Cancel_sale_asks_in_red_and_clears_the_cart()
    {
        var (_, presenter, view) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");

        view.ConfirmAnswer = false;
        presenter.CancelSale();
        Assert.False(presenter.Cart.IsEmpty);

        view.ConfirmAnswer = true;
        presenter.CancelSale();
        Assert.True(presenter.Cart.IsEmpty);
        Assert.Equal(2, view.DangerQuestions.Count);
        Assert.Equal(("Đã hủy đơn hàng.", Tone.Muted), view.Status);
    }

    [Fact]
    public async Task Leaving_with_an_unpaid_cart_asks_first()
    {
        var (_, presenter, view) = await CreateStartedAsync();
        Assert.True(presenter.CanLeave());
        Assert.Empty(view.Questions);

        await presenter.ScanAsync("SUA01");
        view.ConfirmAnswer = false;
        Assert.False(presenter.CanLeave());
        Assert.StartsWith("Đơn hàng đang bán (1 sản phẩm, 32.000 ₫) chưa thanh toán.", Assert.Single(view.DangerQuestions));

        view.ConfirmAnswer = true;
        Assert.True(presenter.CanLeave());
    }

    // ---- Parked orders -------------------------------------------------------------------------

    [Fact]
    public async Task Parking_puts_the_cart_on_hold_and_updates_the_count()
    {
        var (host, presenter, view) = await CreateStartedAsync();

        await presenter.ParkAsync();
        Assert.Equal(("Giỏ hàng đang trống – không có gì để lưu tạm.", Tone.Muted), view.Status);
        Assert.Empty(view.Dialogs);

        await presenter.ScanAsync("SUA01");
        await presenter.ParkAsync();

        Assert.Equal("park:1|32000|Đơn tạm", view.Dialogs[^1]);
        Assert.True(presenter.Cart.IsEmpty);
        Assert.Equal(1, host.ParkedOrders.Count);
        Assert.Equal(1, view.ParkedCounts[^1]);
        Assert.Equal("Chị Lan · 32.000 ₫", Assert.Single(view.Successes));
        Assert.Equal(("Đã lưu tạm \"Chị Lan\" (0 mặt hàng). Nhấn F11 để gọi lại.", Tone.Success), view.Status);
    }

    [Fact]
    public async Task Resuming_parks_the_cart_in_progress_first_and_reports_changes()
    {
        var (host, presenter, view) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");
        await presenter.ParkAsync();
        await presenter.ScanAsync("MI01");
        var id = host.ParkedOrders.IdAt(0);
        host.ParkedOrders.ResumeNotices.Add("Giá Sữa tươi 1L đã đổi.");
        view.ParkedAnswer = () => (id, 1);

        await presenter.ShowParkedAsync();

        Assert.Equal("parked:True", view.Dialogs[^1]);
        Assert.Equal("SUA01", Assert.Single(presenter.Cart.Lines).Code);
        Assert.Equal(1, host.ParkedOrders.Count); // the noodles cart is now parked
        Assert.Equal("Giá Sữa tươi 1L đã đổi.", Assert.Single(view.Warnings));
        Assert.Equal(Tone.Warning, view.Status.Tone);
        Assert.Equal(1, view.ParkedCounts[^1]);
    }

    [Fact]
    public async Task A_cart_that_cannot_be_parked_is_kept_instead_of_resuming()
    {
        var (host, presenter, view) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");
        await presenter.ParkAsync();
        await presenter.ScanAsync("MI01");
        host.ParkedOrders.FailParkWith = Error.Validation("Đã đạt tối đa 10 đơn tạm.");
        view.ParkedAnswer = () => (host.ParkedOrders.IdAt(0), 1);

        await presenter.ShowParkedAsync();

        Assert.Equal("MI01", Assert.Single(presenter.Cart.Lines).Code);
        Assert.DoesNotContain("resume", host.ParkedOrders.Calls);
        Assert.Equal("Đã đạt tối đa 10 đơn tạm.", Assert.Single(view.Warnings));
    }

    // ---- Tender --------------------------------------------------------------------------------

    [Fact]
    public async Task Quick_cash_sets_the_tender_and_moves_to_pay()
    {
        var (_, presenter, view) = await CreateStartedAsync();
        await presenter.ScanAsync("SUA01");

        presenter.QuickCash(50_000);

        Assert.Equal(50_000, presenter.Cart.CashTendered);
        Assert.Equal("18.000 ₫", view.Board.Change);
        Assert.Equal("pay", view.Focus[^1]);
    }

    [Fact]
    public async Task Points_beyond_the_bill_fall_back_to_the_maximum()
    {
        var (host, presenter, view) = await CreateStartedAsync();
        var lan = new Customer("KH000001", "Chị Lan", "0901234567");
        lan.RecordPurchase(10_000_000, host.SalesOptions.Loyalty);
        host.PosLookup.CustomersByPhone["0901234567"] = lan;
        await presenter.ScanAsync("MI01");
        await presenter.FindCustomerAsync("0901234567");

        presenter.RedeemPoints(1_000_000);

        Assert.Equal(presenter.Cart.MaxRedeemablePoints, presenter.Cart.PointsToRedeem);
        Assert.True(presenter.Cart.PointsToRedeem > 0);
        Assert.Equal(presenter.Cart.PointsToRedeem, view.Board.PointsToRedeem);
    }
}
