using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Sales;

/// <summary>The Bán hàng screen as seen by its presenter. Dialogs stay in the view; the presenter only asks.</summary>
public interface IPosView : IView
{
    /// <summary>Cart grid (keeping the selected line), member card, totals, tender section and button states.</summary>
    void ShowBoard(PosBoard board);

    void SelectLine(PosCartLine line);

    void ShowShift(PosShiftBar bar);

    /// <summary>Status line above the cart.</summary>
    void ShowStatus(string text, Tone tone);

    void ShowParkedCount(int count);

    void SetPhone(string text);

    /// <summary>Red message under the phone box; null clears it.</summary>
    void ShowPhoneError(string? message);

    /// <summary>Puts a scan that could not be sold back into the scan box – unless the cashier already scanned on.</summary>
    void RestoreScan(string text);

    void ClearScan();

    /// <summary>Focuses the scan box with its text selected (ready for the next item).</summary>
    void FocusScan();

    void FocusPay();

    /// <summary>Pushes the customer-facing display (ignored while that window is closed).</summary>
    void ShowCustomerDisplay(CustomerDisplayState state);

    decimal? AskQuantity(string name, string unit, decimal start);

    /// <summary>Discount prompt; Ok = false when the user backed out, Discount = null removes the discount.</summary>
    (bool Ok, DiscountInput? Discount) AskDiscount(string title, decimal baseAmount, DiscountInput? current);

    /// <summary>F3 product search; the chosen product or null.</summary>
    PosProductDto? SearchProduct(string term);

    /// <summary>F12 price check; the product the cashier chose to sell, or null.</summary>
    PosProductDto? PriceCheck();

    /// <summary>Open-shift dialog; the opened shift or null when the cashier backed out.</summary>
    ShiftSummaryDto? OpenShift(decimal suggestedFloat, string? cashierName);

    /// <summary>F10 label prompt; Ok = false when the cashier backed out.</summary>
    (bool Ok, string? Label) AskParkLabel(int lineCount, decimal total, string defaultLabel);

    /// <summary>F11 parked-orders list; the order to resume (or null) and how many orders are left parked.</summary>
    (Guid? Chosen, int Remaining) PickParkedOrder(bool cartBusy);

    /// <summary>Ctrl+P invoice number prompt (pre-filled); null when the cashier backed out.</summary>
    string? AskReprintCode(string? lastCode);

    /// <summary>Receipt print preview; true when it was printed.</summary>
    bool ShowReceipt(ReceiptData receipt, int paperWidthMm);

    /// <summary>Audible "no" for a scan that found nothing or a payment that cannot be made.</summary>
    void Beep();
}

/// <summary>
/// Bán hàng logic around the UI-free <see cref="PosCart"/>: scanning (one scan at a time, in the order scanned), line and
/// invoice edits, member and points, tender, checkout with receipt preview, reprint, park/resume, price check, the
/// cashier's shift and the customer-facing display. A payment waits for scans still being looked up, a missing shift is
/// re-read (and offered to open) before paying, and leaving the screen with an unpaid cart asks first.
/// </summary>
public sealed class PosPresenter : Presenter<IPosView>
{
    public const string Title = "Bán hàng";

    private readonly ICurrentUser _currentUser;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SalesOptions _options;
    private readonly StoreProfile _store;
    private ShiftSummaryDto? _shift;
    private string? _shiftProblem;
    private bool _busy;
    private string? _lastInvoiceCode;
    private PosCartLine? _lastTouched;
    private CustomerDisplayState _display;
    private bool _showingThanks;
    private long _shiftVersion;

    public PosPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        using (var scope = scopeFactory.CreateScope())
        {
            _options = scope.ServiceProvider.GetService<SalesOptions>() ?? SalesOptions.Default;
            _store = scope.ServiceProvider.GetService<StoreProfile>() ?? StoreProfile.Default;
        }

        Cart = new PosCart(_options.Loyalty);
        Cart.Changed += (_, _) => Render();
        _display = CustomerDisplayState.Welcome(_store.Name);
    }

    public PosCart Cart { get; }

    public SalesOptions Options => _options;

    /// <summary>A payment or reprint is running: the view ignores shortcut keys meanwhile.</summary>
    public bool IsBusy => _busy;

    public bool CanDiscount => _currentUser.HasPermission(Permissions.SalesDiscount);

    /// <summary>The cashier's open shift (null = none or unknown).</summary>
    public ShiftSummaryDto? Shift => _shift;

    public string? LastInvoiceCode => _lastInvoiceCode;

    /// <summary>What the customer-facing display should show now (used when the window is opened).</summary>
    public CustomerDisplayState DisplayState => _display;

    private bool ShiftMissing => _options.RequireOpenShift && _shift is null;

    protected override void OnAttached()
    {
        View.ShowShift(PosShiftBar.Checking);
        View.ShowStatus(PosTexts.Ready, Tone.Muted);
        Render();
    }

    /// <summary>First show: parked-order count, then the shift (offering to open one when the store requires it).</summary>
    public async Task StartAsync()
    {
        await RefreshParkedCountAsync();
        if (ViewAlive)
        {
            await LoadShiftAsync(promptToOpen: true);
        }
    }

    // ---- Shift -------------------------------------------------------------------------------------

    /// <summary>Mở ca: re-reads the shift first – it may have been opened on Ca làm việc meanwhile.</summary>
    public async Task OpenShiftAsync()
    {
        if (!_busy)
        {
            await LoadShiftAsync(promptToOpen: true, reportFailure: true);
        }
    }

    /// <returns>True when a shift is open afterwards.</returns>
    private async Task<bool> LoadShiftAsync(bool promptToOpen, bool reportFailure = false)
    {
        var version = ++_shiftVersion;
        var result = await UseAsync<IShiftService, Result<CurrentShiftDto>>(s => s.GetCurrentAsync());
        if (version != _shiftVersion || !ViewAlive)
        {
            return _shift is not null;
        }

        _shiftProblem = result.IsFailure ? result.Error.Message : null;
        _shift = result.IsSuccess ? result.Value.Current : null;
        RenderShift();
        if (result.IsFailure)
        {
            if (reportFailure)
            {
                View.ShowWarning(result.Error.Message, "Mở ca");
            }

            return false;
        }

        if (_shift is not null || !_options.RequireOpenShift || !promptToOpen)
        {
            return _shift is not null;
        }

        var opened = View.OpenShift(result.Value.SuggestedOpeningFloat, _currentUser.Session?.FullName);
        if (!ViewAlive)
        {
            return false;
        }

        if (opened is not null)
        {
            ++_shiftVersion;
            _shift = opened;
            View.ShowStatus($"Đã mở ca {opened.Code} với tiền đầu ca {Vnd.Format(opened.OpeningFloat)}.", Tone.Success);
            RenderShift();
        }

        View.FocusScan();
        return opened is not null;
    }

    private void RenderShift()
    {
        View.ShowShift(PosShiftBar.From(_shift, _shiftProblem, _options));
        Render();
    }

    // ---- Scan & search -----------------------------------------------------------------------------

    /// <summary>
    /// Enter in the scan box (the view has already emptied it so the next scan can start). Scans are looked up one at
    /// a time in the order scanned: a barcode / code first, then a name search (one hit is added, several open the list).
    /// </summary>
    public async Task ScanAsync(string? input)
    {
        var (quantity, text) = PosCart.ParseScan(input);
        if (text.Length == 0)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (!ViewAlive)
            {
                return;
            }

            var lookup = await UseAsync<IPosLookupService, (Result<PosProductDto> Found, IReadOnlyList<PosProductDto> Matches)>(async s =>
            {
                var found = await s.FindForScanAsync(text);
                return (found, found.IsSuccess ? [] : await s.SearchAsync(text, take: 2));
            });
            if (!ViewAlive)
            {
                return;
            }

            if (lookup.Found.IsSuccess)
            {
                AddToCart(lookup.Found.Value, quantity);
            }
            else if (lookup.Matches.Count == 1)
            {
                AddToCart(lookup.Matches[0], quantity);
            }
            else if (lookup.Matches.Count > 1)
            {
                Search(text, quantity, restoreOnCancel: input!.Trim());
            }
            else
            {
                View.ShowStatus(lookup.Found.Error.Message, Tone.Danger);
                View.Beep();
                View.RestoreScan(input!.Trim());
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>F3 / search icon: product list for <paramref name="term"/> (a "3*" prefix keeps its quantity).</summary>
    public void OpenSearch(string? term)
    {
        if (_busy)
        {
            return;
        }

        var (quantity, text) = PosCart.ParseScan(term);
        Search(text, quantity);
    }

    private void Search(string text, decimal quantity, string? restoreOnCancel = null)
    {
        var product = View.SearchProduct(text);
        if (!ViewAlive)
        {
            return;
        }

        if (product is not null && AddToCart(product, quantity))
        {
            View.ClearScan();
        }
        else if (product is null && restoreOnCancel is not null)
        {
            View.RestoreScan(restoreOnCancel);
        }

        if (ViewAlive)
        {
            View.FocusScan();
        }
    }

    /// <summary>F12: price and stock without selling; the cashier may still add the product.</summary>
    public void PriceCheck()
    {
        if (_busy)
        {
            return;
        }

        if (View.PriceCheck() is { } product && ViewAlive)
        {
            AddToCart(product, 1);
        }

        if (ViewAlive)
        {
            View.FocusScan();
        }
    }

    private bool AddToCart(PosProductDto product, decimal quantity)
    {
        PosCartLine? line = null;
        if (!TryCart(() => line = Cart.Add(product, quantity)) || line is null)
        {
            return false;
        }

        _lastTouched = line;
        View.SelectLine(line);
        PushDisplay();
        var (text, tone) = PosTexts.Added(line);
        View.ShowStatus(text, tone);
        return true;
    }

    // ---- Line actions ------------------------------------------------------------------------------

    /// <summary>The line an action applies to: the selected one, else the last one scanned.</summary>
    public PosCartLine? Target(PosCartLine? selected) =>
        selected is not null && Cart.Lines.Contains(selected) ? selected : Cart.Lines.LastOrDefault();

    public void Step(PosCartLine? selected, decimal delta)
    {
        if (_busy || Target(selected) is not { } line)
        {
            return;
        }

        _lastTouched = line;
        TryCart(() => Cart.Increment(line, delta));
        if (Cart.Lines.Contains(line))
        {
            View.SelectLine(line);
        }
        else
        {
            View.ShowStatus($"Đã xóa {line.Name}.", Tone.Muted);
        }
    }

    /// <summary>F4 / double-click: quantity prompt.</summary>
    public void EditQuantity(PosCartLine? selected)
    {
        if (_busy || Target(selected) is not { } line)
        {
            return;
        }

        if (View.AskQuantity(line.Name, line.Unit, line.Quantity) is { } quantity && ViewAlive && Cart.Lines.Contains(line))
        {
            _lastTouched = line;
            TryCart(() => Cart.SetQuantity(line, quantity));
            View.SelectLine(line);
        }

        FocusScanIfAlive();
    }

    /// <summary>F6: discount on one line (needs <c>sales.discount</c>).</summary>
    public void EditLineDiscount(PosCartLine? selected)
    {
        if (_busy)
        {
            return;
        }

        if (!CanDiscount)
        {
            View.ShowStatus(PosTexts.NoDiscountPermission, Tone.Danger);
            return;
        }

        if (Target(selected) is not { } line)
        {
            return;
        }

        var (ok, discount) = View.AskDiscount($"Giảm giá – {line.Name}", line.GrossAmount, line.Discount);
        if (ok && ViewAlive && Cart.Lines.Contains(line))
        {
            TryCart(() => Cart.SetLineDiscount(line, discount));
            View.SelectLine(line);
        }

        FocusScanIfAlive();
    }

    /// <summary>Delete: removes the line.</summary>
    public void RemoveLine(PosCartLine? selected)
    {
        if (!_busy && Target(selected) is { } line && TryCart(() => Cart.Remove(line)))
        {
            View.ShowStatus($"Đã xóa {line.Name}.", Tone.Muted);
        }
    }

    // ---- Customer & points -------------------------------------------------------------------------

    /// <summary>Enter in the phone box: attaches the member (an empty box detaches).</summary>
    public async Task FindCustomerAsync(string? phone)
    {
        if (_busy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            View.ShowPhoneError(null);
            Cart.DetachCustomer();
            return;
        }

        var result = await UseAsync<IPosLookupService, Result<GroceryPOS.Domain.Partners.Customer>>(s => s.FindCustomerByPhoneAsync(phone));
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowPhoneError(result.Error.Message);
            return;
        }

        View.ShowPhoneError(null);
        Cart.AttachCustomer(PosCustomer.From(result.Value));
        View.ShowStatus($"Khách hàng: {result.Value.Name} ({result.Value.LoyaltyPoints.ToString("#,0", Vnd.Culture)} điểm)", Tone.Success);
        View.FocusScan();
    }

    public void DetachCustomer()
    {
        Cart.DetachCustomer();
        View.SetPhone(string.Empty);
        View.ShowPhoneError(null);
    }

    /// <summary>Points box: more than the bill allows falls back to the maximum instead of an error.</summary>
    public void RedeemPoints(int points)
    {
        if (points == Cart.PointsToRedeem)
        {
            return;
        }

        if (!TryCart(() => Cart.RedeemPoints(points)))
        {
            TryCart(() => Cart.RedeemPoints(Cart.MaxRedeemablePoints));
        }
    }

    public void UseMaxPoints() => RedeemPoints(Cart.MaxRedeemablePoints);

    // ---- Invoice discount & tender -----------------------------------------------------------------

    /// <summary>F7: discount on the whole invoice (needs <c>sales.discount</c>).</summary>
    public void EditInvoiceDiscount()
    {
        if (_busy)
        {
            return;
        }

        if (!CanDiscount)
        {
            View.ShowStatus(PosTexts.NoDiscountPermission, Tone.Danger);
            return;
        }

        if (Cart.IsEmpty)
        {
            return;
        }

        var (ok, discount) = View.AskDiscount("Giảm giá hóa đơn", Cart.Subtotal, Cart.InvoiceDiscount);
        if (ok && ViewAlive && !Cart.IsEmpty)
        {
            TryCart(() => Cart.SetInvoiceDiscount(discount));
        }

        FocusScanIfAlive();
    }

    public void SetPaymentMethod(PaymentMethod method)
    {
        if (method != Cart.PaymentMethod)
        {
            TryCart(() => Cart.SetPaymentMethod(method));
        }
    }

    public void SetCashTendered(decimal? amount)
    {
        if (amount != Cart.CashTendered)
        {
            TryCart(() => Cart.SetCashTendered(amount));
        }
    }

    /// <summary>Quick-cash button: the amount the customer handed over, then focus Thanh toán.</summary>
    public void QuickCash(decimal amount)
    {
        SetCashTendered(amount);
        View.FocusPay();
    }

    public void SetPaymentReference(string? reference)
    {
        if (reference != Cart.PaymentReference)
        {
            Cart.SetPaymentReference(reference);
        }
    }

    // ---- Checkout ----------------------------------------------------------------------------------

    /// <summary>
    /// F9 Thanh toán. Waits for scans still being looked up (so the last item scanned is on the bill), re-reads a missing
    /// shift and offers to open one, then pays and shows the receipt.
    /// </summary>
    public async Task CheckoutAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        Render();
        await _gate.WaitAsync();
        try
        {
            if (!ViewAlive)
            {
                return;
            }

            if (ShiftMissing)
            {
                // The shift may have been opened on Ca làm việc meanwhile; otherwise offer to open it now and go on paying.
                _busy = false;
                var open = await LoadShiftAsync(promptToOpen: true);
                _busy = true;
                if (!ViewAlive)
                {
                    return;
                }

                if (!open)
                {
                    View.ShowStatus("Chưa mở ca – vui lòng mở ca trước khi thanh toán.", Tone.Danger);
                    View.Beep();
                    return;
                }
            }

            var request = Cart.BuildRequest();
            if (request.IsFailure)
            {
                View.ShowStatus(request.Error.Message, Tone.Danger);
                View.Beep();
                return;
            }

            var result = await UseAsync<ICheckoutService, Result<CheckoutReceipt>>(s => s.CheckoutAsync(request.Value));
            if (!ViewAlive)
            {
                return;
            }

            if (result.IsFailure)
            {
                View.ShowStatus(result.Error.Message, Tone.Danger);
                if (result.Error == SaleErrors.NoOpenShift)
                {
                    // Closed elsewhere (e.g. "Kết ca hộ" by a manager): back to "Mở ca".
                    ++_shiftVersion;
                    _shift = null;
                    _shiftProblem = null;
                    RenderShift();
                }

                View.ShowWarning(result.Error.Message, "Thanh toán");
                return;
            }

            var receipt = result.Value;
            _lastInvoiceCode = receipt.Code;
            _lastTouched = null;
            _showingThanks = true;
            _display = CustomerDisplayState.ThankYou(_store.Name, receipt.Total, receipt.AmountPaid, receipt.Change, receipt.PointsEarned);
            View.ShowCustomerDisplay(_display);
            Cart.Clear();
            View.SetPhone(string.Empty);
            View.ShowPhoneError(null);
            View.ShowStatus(PosTexts.Paid(receipt), Tone.Success);
            if (_store.PreviewReceiptAfterCheckout)
            {
                var printout = await UseAsync<IReceiptService, Result<ReceiptData>>(s => s.GetForSaleAsync(receipt.SaleId));
                if (!ViewAlive)
                {
                    return;
                }

                if (printout.IsFailure)
                {
                    View.ShowWarning(printout.Error.Message, "In hóa đơn");
                }
                else
                {
                    View.ShowReceipt(printout.Value, _store.ReceiptPaperWidthMm);
                }
            }
        }
        finally
        {
            _busy = false;
            _gate.Release();
            if (ViewAlive)
            {
                Render();
                View.FocusScan();
            }
        }
    }

    /// <summary>Ctrl+P: reprint an invoice (pre-filled with this counter's last sale), marked BẢN IN LẠI.</summary>
    public async Task ReprintAsync()
    {
        if (_busy)
        {
            return;
        }

        var code = View.AskReprintCode(_lastInvoiceCode)?.Trim();
        if (string.IsNullOrEmpty(code) || !ViewAlive)
        {
            FocusScanIfAlive();
            return;
        }

        _busy = true;
        try
        {
            var result = await UseAsync<IReceiptService, Result<ReceiptData>>(s => s.ReprintAsync(code));
            if (!ViewAlive)
            {
                return;
            }

            if (result.IsFailure)
            {
                View.ShowStatus(result.Error.Message, Tone.Danger);
                return;
            }

            if (View.ShowReceipt(result.Value, _store.ReceiptPaperWidthMm) && ViewAlive)
            {
                View.ShowStatus($"Đã in lại hóa đơn {result.Value.Code}.", Tone.Muted);
            }
        }
        finally
        {
            _busy = false;
            FocusScanIfAlive();
        }
    }

    /// <summary>Esc Hủy đơn: clears the cart after a red confirmation.</summary>
    public void CancelSale()
    {
        if (_busy)
        {
            return;
        }

        if (!Cart.IsEmpty
            && View.Confirm($"Hủy đơn hàng đang bán ({Cart.Lines.Count} sản phẩm)? Giỏ hàng sẽ bị xóa.", "Hủy đơn", "Hủy đơn", danger: true))
        {
            ClearSale();
            View.ShowStatus("Đã hủy đơn hàng.", Tone.Muted);
        }

        FocusScanIfAlive();
    }

    /// <summary>Before switching screens / signing out / closing: an unpaid cart is only dropped when the cashier agrees.</summary>
    public bool CanLeave()
    {
        if (_busy)
        {
            View.ShowInfo("Đang thanh toán, vui lòng đợi trong giây lát.", Title);
            return false;
        }

        return Cart.IsEmpty || View.Confirm(PosTexts.LeaveQuestion(Cart), Title, "Bỏ đơn", danger: true);
    }

    // ---- Parked orders -----------------------------------------------------------------------------

    /// <summary>F10 Lưu tạm: puts the cart on hold so the next customer can be served.</summary>
    public async Task ParkAsync()
    {
        if (_busy)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (!ViewAlive)
            {
                return;
            }

            if (Cart.IsEmpty)
            {
                View.ShowStatus("Giỏ hàng đang trống – không có gì để lưu tạm.", Tone.Muted);
                return;
            }

            var (ok, label) = View.AskParkLabel(Cart.Lines.Count, Cart.Total, Cart.Customer?.Name ?? "Đơn tạm");
            if (ok && ViewAlive)
            {
                await ParkCurrentAsync(label);
            }
        }
        finally
        {
            _gate.Release();
            FocusScanIfAlive();
        }
    }

    /// <summary>F11 Đơn chờ: resume a parked order; a cart in progress is parked first so nothing is lost.</summary>
    public async Task ShowParkedAsync()
    {
        if (_busy)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (!ViewAlive)
            {
                return;
            }

            var (chosen, remaining) = View.PickParkedOrder(cartBusy: !Cart.IsEmpty);
            if (!ViewAlive)
            {
                return;
            }

            View.ShowParkedCount(remaining);
            if (chosen is not { } id || (!Cart.IsEmpty && !await ParkCurrentAsync(null)) || !ViewAlive)
            {
                return;
            }

            var result = await UseAsync<IParkedOrderService, Result<ParkedOrderResume>>(s => s.ResumeAsync(id, Cart));
            if (!ViewAlive)
            {
                return;
            }

            if (result.IsFailure)
            {
                View.ShowWarning(result.Error.Message, "Gọi lại đơn");
            }
            else
            {
                var resumed = result.Value;
                View.SetPhone(Cart.Customer?.Phone ?? string.Empty);
                View.ShowPhoneError(null);
                View.ShowStatus(
                    $"Đã gọi lại \"{resumed.Order.Label}\" ({resumed.RestoredLines} mặt hàng).",
                    resumed.HasNotices ? Tone.Warning : Tone.Success);
                if (resumed.HasNotices)
                {
                    View.ShowWarning(string.Join("\n", resumed.Notices), "Đơn đã thay đổi từ lúc lưu tạm");
                }
            }

            await RefreshParkedCountAsync();
        }
        finally
        {
            _gate.Release();
            FocusScanIfAlive();
        }
    }

    /// <returns>False (with the reason shown) when the cart could not be parked.</returns>
    private async Task<bool> ParkCurrentAsync(string? label)
    {
        var result = await UseAsync<IParkedOrderService, Result<ParkedOrder>>(s => s.ParkAsync(Cart, label));
        if (!ViewAlive)
        {
            return false;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, "Lưu tạm đơn");
            return false;
        }

        // The service clears the cart; make sure the member box and display follow.
        _lastTouched = null;
        View.SetPhone(string.Empty);
        View.ShowPhoneError(null);
        View.ShowStatus($"Đã lưu tạm \"{result.Value.Label}\" ({result.Value.LineCount} mặt hàng). Nhấn F11 để gọi lại.", Tone.Success);
        View.ToastSuccess($"{result.Value.Label} · {Vnd.Format(result.Value.Total)}", "Đã lưu tạm đơn");
        await RefreshParkedCountAsync();
        return true;
    }

    private async Task RefreshParkedCountAsync()
    {
        var orders = await UseAsync<IParkedOrderService, IReadOnlyList<ParkedOrder>>(s => s.ListAsync());
        if (ViewAlive)
        {
            View.ShowParkedCount(orders.Count);
        }
    }

    // ---- Internals ---------------------------------------------------------------------------------

    private void ClearSale()
    {
        _lastTouched = null;
        Cart.Clear();
        View.SetPhone(string.Empty);
        View.ShowPhoneError(null);
    }

    /// <summary>Runs a cart change, turning domain rule violations into the status line instead of exceptions.</summary>
    private bool TryCart(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (DomainException ex)
        {
            if (ViewAlive)
            {
                View.ShowStatus(ex.Message, Tone.Danger);
            }

            return false;
        }
    }

    private void FocusScanIfAlive()
    {
        if (ViewAlive)
        {
            View.FocusScan();
        }
    }

    private void PushDisplay()
    {
        if (!Cart.IsEmpty)
        {
            _showingThanks = false;
        }
        else if (_showingThanks)
        {
            return; // keep "Cảm ơn quý khách / tiền thừa" until the next scan
        }

        _display = CustomerDisplayState.FromCart(Cart, _store.Name, _lastTouched is { } t && Cart.Lines.Contains(t) ? t : null);
        View.ShowCustomerDisplay(_display);
    }

    private void Render()
    {
        if (!ViewAlive)
        {
            return;
        }

        PushDisplay();
        View.ShowBoard(PosBoard.From(Cart, CanDiscount, ShiftMissing, _busy));
    }
}
