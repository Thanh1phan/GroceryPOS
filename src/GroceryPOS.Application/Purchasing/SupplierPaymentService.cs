using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Purchasing;

public interface ISupplierPaymentService
{
    /// <summary>Pays supplier debt (phiếu chi <c>PC{yyMMdd}-{0001}</c>). Requires <c>suppliers.pay</c>.</summary>
    Task<Result<SupplierPaymentDto>> PayAsync(SupplierPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cancels a mistaken payment; the debt comes back. Requires <c>suppliers.pay</c>.</summary>
    Task<Result<SupplierPaymentDto>> CancelAsync(int paymentId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Suppliers with an outstanding balance, largest first. Requires <c>suppliers.pay</c> or <c>purchases.manage</c>.</summary>
    Task<Result<IReadOnlyList<SupplierPayableDto>>> ListPayablesAsync(CancellationToken cancellationToken = default);

    /// <summary>Payments in a period (cancelled ones included), newest first.</summary>
    Task<Result<IReadOnlyList<SupplierPaymentDto>>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Debt ledger of one supplier for [<paramref name="from"/>, <paramref name="to"/>): receipts on credit, payments,
    /// cancelled payments and returns offset against debt, with opening/closing and running balances.
    /// </summary>
    Task<Result<SupplierDebtLedgerDto>> GetLedgerAsync(int supplierId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Supplier payables (công nợ nhà cung cấp): payments, cancellations, payables list and debt ledger.</summary>
public sealed class SupplierPaymentService : ISupplierPaymentService
{
    public const string CodePrefix = "PC";

    private readonly ISupplierPaymentRepository _payments;
    private readonly ISupplierRepository _suppliers;
    private readonly IGoodsReceiptRepository _receipts;
    private readonly ISupplierReturnRepository _returns;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly SalesOptions _options;

    public SupplierPaymentService(
        ISupplierPaymentRepository payments,
        ISupplierRepository suppliers,
        IGoodsReceiptRepository receipts,
        ISupplierReturnRepository returns,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IAuditService audit,
        TimeProvider clock,
        SalesOptions? options = null)
    {
        _payments = payments;
        _suppliers = suppliers;
        _receipts = receipts;
        _returns = returns;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _options = options ?? SalesOptions.Default;
    }

    public async Task<Result<SupplierPaymentDto>> PayAsync(SupplierPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (AuthorizePay() is { } denied)
        {
            return denied;
        }

        var supplier = await _suppliers.GetByIdAsync(request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return PurchasingErrors.SupplierNotFound;
        }

        if (supplier.DebtBalance <= 0)
        {
            return SupplierDebtErrors.NoDebt;
        }

        var now = _clock.GetUtcNow();
        SupplierPayment payment;
        try
        {
            payment = SupplierPayment.Pay(
                await NextCodeAsync(now, cancellationToken),
                supplier,
                request.Amount,
                request.Method,
                now,
                _currentUser.Session!.UserId,
                request.Reference,
                request.Note);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _payments.Add(payment);
        _audit.Record(
            AuditActions.SupplierPaid,
            nameof(SupplierPayment),
            payment.Code,
            $"{supplier.Name}: trả {Vnd.Format(payment.Amount)} ({SaleLabels.Of(payment.Method)}), công nợ {Vnd.Format(payment.DebtBefore)} → {Vnd.Format(payment.DebtAfter)}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(payment, supplier.Name);
    }

    public async Task<Result<SupplierPaymentDto>> CancelAsync(int paymentId, string? reason, CancellationToken cancellationToken = default)
    {
        if (AuthorizePay() is { } denied)
        {
            return denied;
        }

        var payment = await _payments.GetByIdAsync(paymentId, cancellationToken);
        if (payment is null)
        {
            return SupplierDebtErrors.PaymentNotFound;
        }

        var supplier = await _suppliers.GetByIdAsync(payment.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return PurchasingErrors.SupplierNotFound;
        }

        try
        {
            payment.Cancel(supplier, _clock.GetUtcNow(), _currentUser.Session!.UserId, reason);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _audit.Record(
            AuditActions.SupplierPaymentCancelled,
            nameof(SupplierPayment),
            payment.Code,
            $"{supplier.Name}: hủy phiếu chi {Vnd.Format(payment.Amount)}, công nợ hiện tại {Vnd.Format(supplier.DebtBalance)}. Lý do: {payment.CancelReason}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(payment, supplier.Name);
    }

    public async Task<Result<IReadOnlyList<SupplierPayableDto>>> ListPayablesAsync(CancellationToken cancellationToken = default)
    {
        if (AuthorizeView() is { } denied)
        {
            return denied;
        }

        var suppliers = await _suppliers.ListAsync(cancellationToken);
        IReadOnlyList<SupplierPayableDto> items = suppliers
            .Where(s => s.DebtBalance > 0)
            .OrderByDescending(s => s.DebtBalance)
            .ThenBy(s => s.Name, StringComparer.Create(Vnd.Culture, ignoreCase: true))
            .Select(s => new SupplierPayableDto(s.Id, s.Code, s.Name, s.Phone, s.DebtBalance))
            .ToList();
        return Result.Success(items);
    }

    public async Task<Result<IReadOnlyList<SupplierPaymentDto>>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (AuthorizeView() is { } denied)
        {
            return denied;
        }

        if (filter.From is { } f && filter.To is { } t && f >= t)
        {
            return PurchasingErrors.InvalidPeriod;
        }

        var payments = await _payments.SearchAsync(filter, cancellationToken);
        var names = await SupplierNamesAsync(cancellationToken);
        IReadOnlyList<SupplierPaymentDto> items = payments
            .OrderByDescending(p => p.PaidAt)
            .Select(p => ToDto(p, names.GetValueOrDefault(p.SupplierId, string.Empty)))
            .ToList();
        return Result.Success(items);
    }

    public async Task<Result<SupplierDebtLedgerDto>> GetLedgerAsync(
        int supplierId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (AuthorizeView() is { } denied)
        {
            return denied;
        }

        if (from >= to)
        {
            return PurchasingErrors.InvalidPeriod;
        }

        var supplier = await _suppliers.GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null)
        {
            return PurchasingErrors.SupplierNotFound;
        }

        // Every change to Supplier.DebtBalance comes from one of these documents, so the ledger is rebuilt
        // backwards from the current balance: closing = current − changes after the period.
        var changes = await LoadChangesSinceAsync(supplier.Id, from, cancellationToken);
        var after = changes.Where(c => c.Timestamp >= to).Sum(c => c.Increase - c.Decrease);
        var inPeriod = changes.Where(c => c.Timestamp < to).ToList();
        var closing = supplier.DebtBalance - after;
        var opening = closing - inPeriod.Sum(c => c.Increase - c.Decrease);

        var balance = opening;
        var entries = new List<SupplierDebtEntry>(inPeriod.Count);
        foreach (var change in inPeriod)
        {
            balance += change.Increase - change.Decrease;
            entries.Add(change with { Balance = balance });
        }

        return new SupplierDebtLedgerDto(
            supplier.Id,
            supplier.Name,
            from,
            to,
            opening,
            entries.Sum(e => e.Increase),
            entries.Sum(e => e.Decrease),
            closing,
            entries);
    }

    /// <summary>All debt changes of a supplier at or after <paramref name="since"/>, in time order (balance not set).</summary>
    private async Task<List<SupplierDebtEntry>> LoadChangesSinceAsync(int supplierId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var changes = new List<SupplierDebtEntry>();

        var receipts = await _receipts.SearchAsync(
            new GoodsReceiptFilter(SupplierId: supplierId, Status: GoodsReceiptStatus.Completed), cancellationToken);
        foreach (var receipt in receipts.Where(r => r.CompletedAt >= since && r.DebtAmount > 0))
        {
            changes.Add(Entry(
                receipt.CompletedAt!.Value,
                SupplierDebtEntryKind.GoodsReceipt,
                receipt.Code,
                $"Nhập hàng {Vnd.Format(receipt.Total)}, đã trả {Vnd.Format(receipt.PaidAmount)}",
                increase: receipt.DebtAmount));
        }

        var payments = await _payments.ListActivityAsync(supplierId, since, cancellationToken);
        foreach (var payment in payments)
        {
            if (payment.PaidAt >= since)
            {
                changes.Add(Entry(
                    payment.PaidAt,
                    SupplierDebtEntryKind.Payment,
                    payment.Code,
                    $"Trả nợ ({SaleLabels.Of(payment.Method)})" + (payment.Note is null ? string.Empty : $" – {payment.Note}"),
                    decrease: payment.Amount));
            }

            if (payment is { IsCancelled: true, CancelledAt: { } cancelledAt } && cancelledAt >= since)
            {
                changes.Add(Entry(
                    cancelledAt,
                    SupplierDebtEntryKind.PaymentCancelled,
                    payment.Code,
                    $"Hủy phiếu chi: {payment.CancelReason}",
                    increase: payment.Amount));
            }
        }

        var returns = await _returns.SearchAsync(new SupplierDocumentFilter(From: since, SupplierId: supplierId), cancellationToken);
        foreach (var ret in returns.Where(r => r.DebtOffset > 0))
        {
            changes.Add(Entry(
                ret.ReturnDate,
                SupplierDebtEntryKind.SupplierReturn,
                ret.Code,
                $"Trả hàng {Vnd.Format(ret.Total)}" + (ret.RefundAmount > 0 ? $", nhận lại {Vnd.Format(ret.RefundAmount)}" : string.Empty) + $" – {ret.Reason}",
                decrease: ret.DebtOffset));
        }

        return changes
            .OrderBy(c => c.Timestamp)
            .ThenBy(c => c.Kind)
            .ThenBy(c => c.DocumentCode, StringComparer.Ordinal)
            .ToList();
    }

    private static SupplierDebtEntry Entry(
        DateTimeOffset timestamp,
        SupplierDebtEntryKind kind,
        string code,
        string description,
        decimal increase = 0,
        decimal decrease = 0) =>
        new(timestamp, kind, SupplierDebtLabels.Of(kind), code, description, increase, decrease, 0);

    private async Task<Dictionary<int, string>> SupplierNamesAsync(CancellationToken cancellationToken) =>
        (await _suppliers.ListAsync(cancellationToken)).ToDictionary(s => s.Id, s => s.Name);

    private async Task<string> NextCodeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var prefix = DailyCode.PrefixFor(CodePrefix, now.ToOffset(_options.StoreUtcOffset));
        var last = await _payments.GetLastCodeWithPrefixAsync(prefix, cancellationToken);
        return DailyCode.Next(prefix, last);
    }

    private Error? AuthorizePay()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.SupplierPaymentsManage) ? null : SupplierDebtErrors.PaymentForbidden;
    }

    private Error? AuthorizeView()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.SupplierPaymentsManage) || _currentUser.HasPermission(Permissions.PurchasesManage)
            ? null
            : SupplierDebtErrors.ViewForbidden;
    }

    private static SupplierPaymentDto ToDto(SupplierPayment payment, string supplierName) => new(
        payment.Id,
        payment.Code,
        payment.SupplierId,
        supplierName,
        payment.Amount,
        payment.Method,
        SaleLabels.Of(payment.Method),
        payment.PaidAt,
        payment.Reference,
        payment.Note,
        payment.DebtBefore,
        payment.DebtAfter,
        payment.IsCancelled,
        payment.CancelledAt,
        payment.CancelReason);
}
