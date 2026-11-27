using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Sales;

/// <summary>The Trả hàng screen as seen by its presenter.</summary>
public interface IReturnView : IView
{
    /// <summary>Binds the invoice lines (null = no invoice) and puts the sheet's reason and refund method in the inputs.</summary>
    void ShowSheet(ReturnSheet? sheet);

    /// <summary>Header, refund card, message and button states; also repaints the line grid.</summary>
    void ShowState(ReturnScreenState state);

    /// <summary>Status line above the grid.</summary>
    void ShowStatus(string text, Tone tone);

    /// <summary>"Đã trả trước đó" card text.</summary>
    void ShowHistory(string text, Tone tone);

    void SetScanText(string text);

    /// <summary>Focuses the invoice box with its text selected (ready for the next scan).</summary>
    void FocusScan();

    void FocusLines();

    /// <summary>Quantity prompt; the entered quantity or null when the user backed out.</summary>
    decimal? AskQuantity(string prompt, string unit, decimal start);

    /// <summary>Print preview of the refund slip.</summary>
    void ShowSlip(string title, Func<int, IReadOnlyList<ReceiptLine>> layout, int paperWidthMm);

    /// <summary>Audible "no" for a rejected scan or a refund that cannot be made yet.</summary>
    void Beep();
}

/// <summary>
/// Trả hàng logic: load an invoice by code ("latest wins"), pick quantities / restock / reason / refund method on the
/// <see cref="ReturnSheet"/>, and make the refund. The cashier's open shift is re-checked before a cash refund (it
/// may have been opened on the POS screen meanwhile), a failed refund reloads the invoice while keeping the picks
/// (another counter may have returned the same goods), and leaving the screen with picked lines asks first.
/// </summary>
public sealed class ReturnPresenter : Presenter<IReturnView>
{
    public const string Title = "Trả hàng";

    private readonly ICurrentUser _currentUser;
    private SalesOptions _options = SalesOptions.Default;
    private ReturnSheet? _sheet;
    private bool? _hasOpenShift;
    private bool _submitting;
    private long _invoiceVersion;
    private long _shiftVersion;

    public ReturnPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    /// <summary>The invoice being returned (null before a scan and after a refund).</summary>
    public ReturnSheet? Sheet => _sheet;

    /// <summary>Whether the cashier has an open shift (null = unknown).</summary>
    public bool? HasOpenShift => _hasOpenShift;

    public bool IsSubmitting => _submitting;

    /// <summary>Lines are picked but not refunded yet.</summary>
    public bool HasPendingWork => _sheet is { SelectedLineCount: > 0 };

    public ReturnLineActions ActionsFor(ReturnSheetLine? line) => ReturnLineActions.For(_sheet, line, _submitting);

    protected override void OnAttached() => View.ShowState(ReturnScreenState.Empty);

    /// <summary>First show: reads the shift (and the store's return rules) and shows the return-window hint.</summary>
    public async Task StartAsync()
    {
        await RefreshShiftAsync();
        if (ViewAlive && _sheet is null)
        {
            View.ShowStatus(ReturnTexts.WindowHint(_options), Tone.Muted);
        }
    }

    /// <summary>F5: re-reads the shift and the loaded invoice, keeping what was picked.</summary>
    public async Task RefreshAsync()
    {
        if (_submitting)
        {
            return;
        }

        await RefreshShiftAsync();
        if (ViewAlive && _sheet is { } sheet)
        {
            await LoadAsync(sheet.Sale.Code, keepFrom: sheet, announce: true);
        }
    }

    /// <summary>Scan / Enter in the invoice box. Re-scanning the invoice already on screen keeps the picks.</summary>
    public async Task LoadInvoiceAsync(string? code)
    {
        code = code?.Trim() ?? string.Empty;
        if (code.Length == 0 || _submitting)
        {
            return;
        }

        var current = _sheet;
        var same = current is not null && code.Equals(current.Sale.Code, StringComparison.OrdinalIgnoreCase);
        if (!same && HasPendingWork
            && !View.Confirm($"Bỏ phiếu trả đang làm cho hóa đơn {current!.Sale.Code}?", Title, "Bỏ phiếu"))
        {
            return;
        }

        await LoadAsync(code, keepFrom: same ? current : null, announce: true);
    }

    // ---- Line actions ----------------------------------------------------------------------------

    public void Step(ReturnSheetLine? line, int delta) => Edit(line, (s, l) => s.Step(l.SaleLineId, delta));

    /// <summary>Space: everything left on the line, or nothing.</summary>
    public void ToggleLine(ReturnSheetLine? line) => Edit(line, (s, l) => s.ToggleLine(l.SaleLineId));

    /// <summary>F6: damaged goods are written off instead of restocked; marking a line damaged also picks it.</summary>
    public void ToggleRestock(ReturnSheetLine? line) => Edit(line, (s, l) =>
    {
        if (!l.IsSelected)
        {
            s.ToggleLine(l.SaleLineId);
        }

        s.SetRestock(l.SaleLineId, !l.Restock);
    });

    /// <summary>F4 / Enter on a line: quantity prompt, capped at what is still returnable.</summary>
    public void EditQuantity(ReturnSheetLine? line) => Edit(line, (s, l) =>
    {
        var remaining = l.Line.RemainingQuantity;
        var start = l.IsSelected ? l.Quantity : remaining;
        var quantity = View.AskQuantity(
            $"{l.Line.ProductName} – còn trả được {Qty(remaining)} {l.Line.Unit}",
            l.Line.Unit,
            start);
        if (quantity is not { } value || !ReferenceEquals(s, _sheet))
        {
            return;
        }

        if (value > remaining)
        {
            View.ShowStatus($"Chỉ còn trả được tối đa {Qty(remaining)} {l.Line.Unit}.", Tone.Warning);
        }

        s.SetQuantity(l.SaleLineId, value);
    });

    /// <summary>F7: every remaining unit of the invoice.</summary>
    public void ReturnAll()
    {
        if (_sheet is { CanReturnAnything: true } sheet && !_submitting)
        {
            sheet.ReturnAll();
        }
    }

    /// <summary>Bỏ chọn: clears the picked quantities (reason and method stay).</summary>
    public void ClearPicks()
    {
        if (_sheet is { } sheet && !_submitting)
        {
            sheet.Clear();
        }
    }

    public void SetReason(string? reason)
    {
        if (_sheet is { } sheet && !_submitting)
        {
            sheet.SetReason(reason);
        }
    }

    public void SetRefundMethod(PaymentMethod method)
    {
        if (_sheet is { } sheet && !_submitting && sheet.RefundMethod != method)
        {
            sheet.SetRefundMethod(method);
        }
    }

    // ---- Refund ------------------------------------------------------------------------------------

    /// <summary>F9 Hoàn tiền.</summary>
    public async Task SubmitAsync()
    {
        if (_sheet is not { } sheet || _submitting)
        {
            return;
        }

        if (sheet.Blocker is not null)
        {
            View.Beep();
            return;
        }

        // The shift may have been opened (POS screen) or closed (Kết ca hộ) since it was last read.
        if (sheet.RefundMethod == PaymentMethod.Cash && _options.RequireOpenShift)
        {
            await RefreshShiftAsync();
            if (!ViewAlive || !ReferenceEquals(sheet, _sheet) || _submitting)
            {
                return;
            }
        }

        if (ReturnScreenState.ShiftBlocker(sheet, _options, _hasOpenShift) is { } shiftBlocker)
        {
            View.Beep();
            View.ShowStatus(shiftBlocker, Tone.Warning);
            return;
        }

        var request = sheet.BuildRequest();
        if (request.IsFailure)
        {
            View.Beep();
            return;
        }

        if (!View.Confirm($"{sheet.Summary} – hóa đơn {sheet.Sale.Code}?", Title, "Hoàn tiền", danger: true))
        {
            return;
        }

        _submitting = true;
        Render();
        (Result<ReturnReceipt> Result, StoreProfile Store, SalesOptions Options) done;
        try
        {
            done = await UseScopeAsync(async sp => (
                await sp.GetRequiredService<IReturnService>().CreateAsync(request.Value),
                sp.GetService<StoreProfile>() ?? StoreProfile.Default,
                sp.GetService<SalesOptions>() ?? SalesOptions.Default));
        }
        finally
        {
            _submitting = false;
        }

        if (!ViewAlive)
        {
            return;
        }

        if (done.Result.IsFailure)
        {
            View.Beep();
            View.ShowStatus(done.Result.Error.Message, Tone.Danger);
            Render();

            // Refresh what may have changed elsewhere (shift closed, goods returned at another counter), keeping the picks.
            await RefreshShiftAsync();
            if (ViewAlive && ReferenceEquals(sheet, _sheet))
            {
                await LoadAsync(sheet.Sale.Code, keepFrom: sheet, announce: false);
            }

            return;
        }

        var slip = done.Result.Value;
        ++_invoiceVersion; // a scan still in flight must not bring the old invoice back
        SetSheet(null);
        View.SetScanText(string.Empty);
        View.ShowHistory(string.Empty, Tone.Muted);
        View.ShowStatus(ReturnTexts.Done(slip), Tone.Success);

        var cashier = _currentUser.Session?.FullName;
        var store = done.Store;
        var offset = done.Options.StoreUtcOffset;
        View.ShowSlip(
            $"Phiếu trả hàng {slip.Code}",
            columns => ReturnSlipFormatter.Format(slip, store, cashier, columns, offset),
            store.ReceiptPaperWidthMm);
        if (ViewAlive)
        {
            View.FocusScan();
        }
    }

    /// <summary>Esc / Làm lại: drops the invoice (asks when lines are picked).</summary>
    public void Reset(bool confirm = true)
    {
        if (_submitting)
        {
            return;
        }

        if (_sheet is null)
        {
            ++_invoiceVersion;
            View.SetScanText(string.Empty);
            View.FocusScan();
            return;
        }

        if (confirm && HasPendingWork && !View.Confirm("Bỏ phiếu trả hàng đang làm?", Title, "Bỏ phiếu"))
        {
            return;
        }

        ++_invoiceVersion;
        SetSheet(null);
        View.SetScanText(string.Empty);
        View.ShowHistory(string.Empty, Tone.Muted);
        View.ShowStatus(ReturnTexts.WindowHint(_options), Tone.Muted);
        View.FocusScan();
    }

    /// <summary>Before switching screens / signing out: a refund being saved or picked lines keep the user here unless they agree.</summary>
    public bool CanLeave()
    {
        if (_submitting)
        {
            View.ShowInfo("Đang hoàn tiền, vui lòng đợi trong giây lát.", Title);
            return false;
        }

        return !HasPendingWork
            || View.Confirm($"Phiếu trả cho hóa đơn {_sheet!.Sale.Code} chưa hoàn tiền. Bỏ phiếu này?", Title, "Bỏ phiếu");
    }

    // ---- Internals ---------------------------------------------------------------------------------

    private async Task LoadAsync(string code, ReturnSheet? keepFrom, bool announce)
    {
        var version = ++_invoiceVersion;
        var loaded = await UseScopeAsync(async sp =>
        {
            var options = sp.GetService<SalesOptions>() ?? SalesOptions.Default;
            var service = sp.GetRequiredService<IReturnService>();
            var sale = await service.GetReturnableAsync(code);
            Result<IReadOnlyList<SaleReturnSummaryDto>>? history = sale.IsSuccess ? await service.ListBySaleAsync(sale.Value.SaleId) : null;
            return (options, sale, history);
        });
        if (version != _invoiceVersion || !ViewAlive || _submitting)
        {
            return;
        }

        _options = loaded.options;
        if (loaded.sale.IsFailure)
        {
            View.Beep();
            View.ShowStatus(loaded.sale.Error.Message, Tone.Danger);
            View.FocusScan();
            return;
        }

        var sale = loaded.sale.Value;
        var sheet = new ReturnSheet(sale);
        var reduced = keepFrom is not null && keepFrom.Sale.SaleId == sale.SaleId ? sheet.CopySelectionFrom(keepFrom) : 0;
        SetSheet(sheet);
        View.SetScanText(sale.Code);

        if (loaded.history is { IsSuccess: true } history)
        {
            View.ShowHistory(ReturnTexts.History(history.Value, _options.StoreUtcOffset), Tone.Muted);
        }
        else
        {
            View.ShowHistory($"Không tải được các phiếu trả trước đó: {loaded.history?.Error.Message}", Tone.Danger);
        }

        if (reduced > 0)
        {
            View.ShowStatus(
                $"Hóa đơn vừa được trả bớt ở nơi khác – đã giảm số lượng trả ở {reduced} dòng theo số còn lại. Vui lòng kiểm tra lại.",
                Tone.Warning);
        }
        else if (announce)
        {
            var (text, tone) = ReturnTexts.DescribeInvoice(sale, _options);
            View.ShowStatus(text, tone);
        }

        if (announce)
        {
            View.FocusLines();
        }
    }

    private async Task RefreshShiftAsync()
    {
        var version = ++_shiftVersion;
        var loaded = await UseScopeAsync(async sp => (
            Options: sp.GetService<SalesOptions>() ?? SalesOptions.Default,
            Result: await sp.GetRequiredService<IShiftService>().GetCurrentAsync()));
        if (version != _shiftVersion || !ViewAlive)
        {
            return;
        }

        _options = loaded.Options;
        _hasOpenShift = loaded.Result.IsSuccess ? loaded.Result.Value.Current is not null : null;
        Render();
    }

    private void Edit(ReturnSheetLine? line, Action<ReturnSheet, ReturnSheetLine> action)
    {
        if (_sheet is { CanReturnAnything: true } sheet && !_submitting
            && line is { IsExhausted: false } && sheet.Lines.Contains(line))
        {
            action(sheet, line);
        }
    }

    private void SetSheet(ReturnSheet? sheet)
    {
        if (_sheet is not null)
        {
            _sheet.Changed -= OnSheetChanged;
        }

        _sheet = sheet;
        if (sheet is not null)
        {
            sheet.Changed += OnSheetChanged;
        }

        View.ShowSheet(sheet);
        Render();
    }

    private void OnSheetChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        if (ViewAlive)
        {
            View.ShowState(ReturnScreenState.From(_sheet, _options, _hasOpenShift, _submitting));
        }
    }

    private static string Qty(decimal value) => value.ToString("#,0.###", Vnd.Culture);
}
