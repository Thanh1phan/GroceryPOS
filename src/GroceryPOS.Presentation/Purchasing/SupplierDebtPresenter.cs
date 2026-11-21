using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Purchasing;

/// <summary>The Công nợ NCC screen as seen by its presenter.</summary>
public interface ISupplierDebtView : IView
{
    void ShowPayables(IReadOnlyList<SupplierPayableDto> payables);

    void ShowPayments(IReadOnlyList<SupplierPaymentRow> rows);

    void ShowReturns(IReadOnlyList<SupplierReturnRow> rows);

    void ShowSummary(SupplierDebtSummary summary);

    /// <summary>Marks the "Từ" date as invalid (or clears it with null).</summary>
    void ShowPeriodError(string? message);

    /// <summary>Opens the payment dialog for <paramref name="payable"/>; the posted payment, or null when cancelled.</summary>
    SupplierPaymentDto? OpenPayment(SupplierPayableDto payable);

    /// <summary>Opens the debt ledger (sổ công nợ) of one supplier.</summary>
    void OpenLedger(int supplierId);

    /// <summary>Opens the supplier return editor; the posted return, or null when the user backed out.</summary>
    SupplierReturnReceipt? OpenReturnEditor(IReadOnlyList<StockItem> catalog, IReadOnlyList<SupplierDto> suppliers, int? supplierId);

    /// <summary>Shows a posted supplier return read-only.</summary>
    void OpenReturnDetail(SupplierReturnDto supplierReturn, TimeSpan storeOffset);

    /// <summary>Asks for a reason (one-click <paramref name="suggestions"/>, required); null when the user backed out.</summary>
    string? AskReason(string title, string message, IReadOnlyList<string> suggestions);

    /// <summary>Brings the "Trả hàng NCC" tab to the front (after a new return).</summary>
    void SelectReturnsTab();
}

/// <summary>
/// Công nợ NCC screen logic: the payables list (debt as of now), payments and returns of a period with
/// "latest wins" loading, the KPI cards, Thanh toán (re-reads the debt first), Sổ công nợ, Hủy phiếu chi with
/// a reason, and Trả hàng NCC (new return editor, view a posted return).
/// </summary>
public sealed class SupplierDebtPresenter : Presenter<ISupplierDebtView>
{
    public const string Title = "Công nợ NCC";

    /// <summary>One-click reasons offered when cancelling a payment.</summary>
    public static readonly IReadOnlyList<string> CancelReasons = ["Lập nhầm phiếu", "Chuyển khoản bị hoàn", "Sai số tiền"];

    private DocumentPeriod _period = DocumentPeriod.ThisMonth(DateTime.Today);
    private IReadOnlyList<SupplierPayableDto>? _payables;
    private IReadOnlyList<SupplierPaymentRow>? _payments;
    private IReadOnlyList<SupplierReturnRow>? _returns;
    private long _loadVersion;

    public SupplierDebtPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        CanPay = currentUser.HasPermission(Permissions.SupplierPaymentsManage);
        CanReturn = currentUser.HasPermission(Permissions.PurchasesManage);
    }

    /// <summary>Paying and cancelling payments need <c>suppliers.pay</c>.</summary>
    public bool CanPay { get; }

    /// <summary>The returns tab and new returns need <c>purchases.manage</c>.</summary>
    public bool CanReturn { get; }

    public DocumentPeriod Period => _period;

    public IReadOnlyList<SupplierPayableDto> Payables => _payables ?? [];

    public SupplierDebtSummary Summary => new(_payables, _payments, _returns, CanReturn);

    /// <summary>Thanh toán is offered only for a supplier the store still owes.</summary>
    public bool CanPayFor(int? supplierId) => CanPay && supplierId is { } id && Payables.Any(p => p.SupplierId == id);

    public bool CanCancel(SupplierPaymentRow? row) => CanPay && row is { IsCancelled: false };

    /// <summary>Reloads the payables and then the documents of <paramref name="period"/>.</summary>
    public Task LoadAsync(DocumentPeriod? period = null) => LoadCoreAsync(period ?? _period, withPayables: true);

    /// <summary>Reloads the documents only (the period changed).</summary>
    public Task FilterAsync(DocumentPeriod period) => LoadCoreAsync(period, withPayables: false);

    private async Task LoadCoreAsync(DocumentPeriod period, bool withPayables)
    {
        ArgumentNullException.ThrowIfNull(period);
        _period = period;
        var from = (period.From ?? DateTime.Today.AddDays(-30)).Date;
        var to = (period.To ?? DateTime.Today).Date.AddDays(1);
        var validPeriod = from < to;
        var version = ++_loadVersion; // an older load in flight must not overwrite this one
        View.ShowPeriodError(validPeriod ? null : "Từ ngày phải trước đến ngày.");
        if (!validPeriod && !withPayables)
        {
            return;
        }

        var loaded = await UseScopeAsync(async sp =>
        {
            var offset = StoreOffset(sp);
            var payments = sp.GetRequiredService<ISupplierPaymentService>();
            var payables = withPayables ? await payments.ListPayablesAsync() : null;
            Result<IReadOnlyList<SupplierPaymentDto>>? paid = null;
            Result<IReadOnlyList<SupplierReturnSummary>>? returned = null;
            if (validPeriod)
            {
                var filter = new SupplierDocumentFilter(new DateTimeOffset(from, offset), new DateTimeOffset(to, offset));
                paid = await payments.SearchAsync(filter);
                returned = CanReturn ? await sp.GetRequiredService<ISupplierReturnService>().SearchAsync(filter) : null;
            }

            return (offset, payables, paid, returned);
        });
        if (version != _loadVersion || !ViewAlive)
        {
            return;
        }

        if (loaded.payables is { } payables)
        {
            if (payables.IsSuccess)
            {
                _payables = payables.Value;
                View.ShowPayables(payables.Value);
            }
            else
            {
                View.ToastError($"Không tải được công nợ phải trả: {payables.Error.Message}", Title);
            }
        }

        if (loaded.paid is { } paid)
        {
            if (paid.IsSuccess)
            {
                _payments = paid.Value.Select(p => SupplierPaymentRow.From(p, loaded.offset)).ToList();
                View.ShowPayments(_payments);
            }
            else
            {
                _payments = null;
                View.ToastError($"Không tải được phiếu chi: {paid.Error.Message}", Title);
            }
        }

        if (loaded.returned is { } returned)
        {
            if (returned.IsSuccess)
            {
                _returns = returned.Value.Select(r => SupplierReturnRow.From(r, loaded.offset)).ToList();
                View.ShowReturns(_returns);
            }
            else
            {
                _returns = null;
                View.ToastError($"Không tải được phiếu trả hàng NCC: {returned.Error.Message}", Title);
            }
        }

        View.ShowSummary(Summary);
    }

    /// <summary>
    /// Thanh toán (F6): re-reads the supplier's debt (it may have changed on another machine), opens the payment
    /// dialog with the fresh balance, confirms with a toast and reloads.
    /// </summary>
    public async Task PayAsync(int? supplierId)
    {
        if (supplierId is not { } id)
        {
            return;
        }

        if (!CanPay)
        {
            View.ShowWarning("Bạn không có quyền thanh toán công nợ nhà cung cấp.", Title);
            return;
        }

        var fresh = await UseAsync<ISupplierPaymentService, Result<IReadOnlyList<SupplierPayableDto>>>(s => s.ListPayablesAsync());
        if (!ViewAlive)
        {
            return;
        }

        if (fresh.IsFailure)
        {
            View.ShowWarning(fresh.Error.Message, Title);
            return;
        }

        if (fresh.Value.FirstOrDefault(p => p.SupplierId == id) is not { } payable)
        {
            var name = Payables.FirstOrDefault(p => p.SupplierId == id)?.Name ?? "Nhà cung cấp này";
            View.ShowInfo($"{name} không còn công nợ (có thể vừa được thanh toán trên máy khác).", Title);
            await LoadAsync();
            return;
        }

        if (View.OpenPayment(payable) is not { } payment)
        {
            if (Payables.FirstOrDefault(p => p.SupplierId == id)?.DebtBalance != payable.DebtBalance)
            {
                await LoadAsync(); // the list showed a stale balance
            }

            return;
        }

        View.ToastSuccess(
            $"Phiếu chi {payment.Code}: {Vnd.Format(payment.Amount)} ({payment.MethodName}). Còn nợ {payment.SupplierName}: {Vnd.Format(payment.DebtAfter)}.",
            "Đã trả nợ nhà cung cấp");
        if (ViewAlive)
        {
            await LoadAsync();
        }
    }

    /// <summary>Sổ công nợ (F7).</summary>
    public void ShowLedger(int? supplierId)
    {
        if (supplierId is { } id)
        {
            View.OpenLedger(id);
        }
    }

    /// <summary>Hủy phiếu chi: asks for a reason, cancels (the debt goes back up), confirms and reloads.</summary>
    public async Task CancelPaymentAsync(SupplierPaymentRow? row)
    {
        if (row is not { IsCancelled: false })
        {
            return;
        }

        if (!CanPay)
        {
            View.ShowWarning("Bạn không có quyền hủy phiếu chi.", Title);
            return;
        }

        var reason = View.AskReason(
            "Hủy phiếu chi",
            $"Hủy phiếu chi {row.Code} ({row.SupplierName}, {Vnd.Format(row.Amount)})? Công nợ sẽ tăng lại {Vnd.Format(row.Amount)}.",
            CancelReasons);
        if (FormInput.Optional(reason) is not { } trimmed)
        {
            return;
        }

        var result = await UseAsync<ISupplierPaymentService, Result<SupplierPaymentDto>>(s => s.CancelAsync(row.Id, trimmed));
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, "Hủy phiếu chi");
            await LoadAsync(); // most failures mean the list is stale (already cancelled elsewhere)
            return;
        }

        View.ToastSuccess($"Đã hủy phiếu chi {row.Code}. Công nợ với {row.SupplierName} tăng lại {Vnd.Format(row.Amount)}.", Title);
        await LoadAsync();
    }

    /// <summary>
    /// Trả hàng NCC (F8): loads products and suppliers, opens the editor (pre-selecting <paramref name="supplierId"/>
    /// when it still trades), then confirms, reloads and shows the returns tab.
    /// </summary>
    public async Task NewReturnAsync(int? supplierId)
    {
        if (!CanReturn)
        {
            View.ShowWarning("Bạn không có quyền trả hàng nhà cung cấp.", Title);
            return;
        }

        var loaded = await UseScopeAsync(async sp =>
        {
            var stock = await sp.GetRequiredService<IInventoryService>().GetStockListAsync();
            var suppliers = await sp.GetRequiredService<ISupplierService>().ListAsync();
            return (stock, suppliers);
        });
        if (!ViewAlive)
        {
            return;
        }

        if (loaded.stock.IsFailure || loaded.suppliers.IsFailure)
        {
            View.ShowWarning((loaded.stock.IsFailure ? loaded.stock.Error : loaded.suppliers.Error).Message, "Trả hàng NCC");
            return;
        }

        var preselect = supplierId is { } id && loaded.suppliers.Value.Any(s => s.Id == id && s.IsActive) ? supplierId : null;
        if (View.OpenReturnEditor(loaded.stock.Value, loaded.suppliers.Value, preselect) is not { } posted)
        {
            return;
        }

        var ret = posted.Return;
        var settlement = (ret.DebtOffset, ret.RefundAmount) switch
        {
            ( > 0, > 0) => $"cấn trừ nợ {Vnd.Format(ret.DebtOffset)}, NCC hoàn {Vnd.Format(ret.RefundAmount)}",
            ( > 0, _) => $"cấn trừ nợ {Vnd.Format(ret.DebtOffset)}",
            (_, > 0) => $"NCC hoàn {Vnd.Format(ret.RefundAmount)}",
            _ => "chưa cấn trừ / hoàn tiền",
        };
        View.ToastSuccess(
            $"Phiếu {ret.Code} – {ret.SupplierName}: {Vnd.Format(ret.Total)} ({settlement}). Công nợ còn {Vnd.Format(posted.SupplierDebtBalance)}.",
            "Đã trả hàng nhà cung cấp");
        if (ViewAlive)
        {
            await LoadAsync();
            if (ViewAlive)
            {
                View.SelectReturnsTab();
            }
        }
    }

    /// <summary>Enter on the returns tab: shows the posted return.</summary>
    public async Task OpenReturnAsync(SupplierReturnRow? row)
    {
        if (row is null)
        {
            return;
        }

        var loaded = await UseScopeAsync(async sp =>
            (Offset: StoreOffset(sp), Result: await sp.GetRequiredService<ISupplierReturnService>().GetAsync(row.Id)));
        if (!ViewAlive)
        {
            return;
        }

        if (loaded.Result.IsFailure)
        {
            View.ShowWarning(loaded.Result.Error.Message, "Trả hàng NCC");
            return;
        }

        View.OpenReturnDetail(loaded.Result.Value, loaded.Offset);
    }

    private static TimeSpan StoreOffset(IServiceProvider services) =>
        (services.GetService<SalesOptions>() ?? SalesOptions.Default).StoreUtcOffset;
}
