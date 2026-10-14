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

public interface ISupplierReturnService
{
    /// <summary>Loads a completed goods receipt by code with what can still be returned per product.</summary>
    Task<Result<ReturnableReceiptDto>> GetReturnableAsync(string receiptCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns goods to a supplier (phiếu <c>TN{yyMMdd}-{0001}</c>): stock out through the ledger, refund and/or
    /// debt offset, audited. Requires <c>purchases.manage</c>.
    /// </summary>
    Task<Result<SupplierReturnReceipt>> CreateAsync(CreateSupplierReturnRequest request, CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnDto>> GetAsync(int returnId, CancellationToken cancellationToken = default);

    /// <summary>Returns in a period, newest first.</summary>
    Task<Result<IReadOnlyList<SupplierReturnSummary>>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default);
}

/// <summary>Goods returned to suppliers (trả hàng nhà cung cấp).</summary>
public sealed class SupplierReturnService : ISupplierReturnService
{
    public const string CodePrefix = "TN";

    private readonly ISupplierReturnRepository _returns;
    private readonly IGoodsReceiptRepository _receipts;
    private readonly ISupplierRepository _suppliers;
    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _movements;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly SalesOptions _options;

    public SupplierReturnService(
        ISupplierReturnRepository returns,
        IGoodsReceiptRepository receipts,
        ISupplierRepository suppliers,
        IProductRepository products,
        IStockMovementRepository movements,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IAuditService audit,
        TimeProvider clock,
        SalesOptions? options = null)
    {
        _returns = returns;
        _receipts = receipts;
        _suppliers = suppliers;
        _products = products;
        _movements = movements;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _options = options ?? SalesOptions.Default;
    }

    public async Task<Result<ReturnableReceiptDto>> GetReturnableAsync(string receiptCode, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(receiptCode))
        {
            return PurchasingErrors.ReceiptNotFound;
        }

        var receipt = await _receipts.GetByCodeAsync(receiptCode.Trim().ToUpperInvariant(), cancellationToken);
        if (receipt is null)
        {
            return PurchasingErrors.ReceiptNotFound;
        }

        if (receipt.Status != GoodsReceiptStatus.Completed)
        {
            return Error.Conflict($"Phiếu nhập {receipt.Code} {receipt.Status.DisplayName().ToLowerInvariant()}, không thể trả hàng theo phiếu này.");
        }

        var supplier = await _suppliers.GetByIdAsync(receipt.SupplierId, cancellationToken);
        var returned = await ReturnedQuantitiesAsync(receipt.Id, cancellationToken);
        var products = (await _products.GetByIdsAsync(receipt.Lines.Select(l => l.ProductId).ToList(), cancellationToken))
            .ToDictionary(p => p.Id);

        var lines = receipt.Lines
            .Select(l => new ReturnableReceiptLineDto(
                l.ProductId,
                l.ProductCode,
                l.ProductName,
                l.Unit,
                l.Quantity,
                returned.GetValueOrDefault(l.ProductId),
                products.TryGetValue(l.ProductId, out var p) ? p.StockQuantity : 0,
                l.EffectiveUnitCost))
            .ToList();
        return new ReturnableReceiptDto(
            receipt.Id, receipt.Code, receipt.ReceiptDate, receipt.SupplierId, supplier?.Name ?? string.Empty, supplier?.DebtBalance ?? 0, lines);
    }

    public async Task<Result<SupplierReturnReceipt>> CreateAsync(CreateSupplierReturnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize() is { } denied)
        {
            return denied;
        }

        if (request.Lines.Count == 0)
        {
            return new Error("supplier_return.empty", "Vui lòng chọn sản phẩm cần trả.");
        }

        var supplier = await _suppliers.GetByIdAsync(request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return PurchasingErrors.SupplierNotFound;
        }

        GoodsReceipt? receipt = null;
        IReadOnlyDictionary<int, decimal> alreadyReturned = new Dictionary<int, decimal>();
        if (request.GoodsReceiptId is { } receiptId)
        {
            receipt = await _receipts.GetByIdAsync(receiptId, cancellationToken);
            if (receipt is null)
            {
                return PurchasingErrors.ReceiptNotFound;
            }

            alreadyReturned = await ReturnedQuantitiesAsync(receipt.Id, cancellationToken);
        }

        var ids = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = (await _products.GetByIdsAsync(ids, cancellationToken)).ToDictionary(p => p.Id);
        if (ids.FirstOrDefault(id => !products.ContainsKey(id)) is var missing and > 0)
        {
            return PurchasingErrors.ProductNotFound(missing);
        }

        var now = _clock.GetUtcNow();
        var userId = _currentUser.Session!.UserId;
        var debtBefore = supplier.DebtBalance;
        SupplierReturnPosting posting;
        try
        {
            // SupplierReturn.Create checks every rule before touching stock or debt.
            posting = SupplierReturn.Create(
                await NextCodeAsync(now, cancellationToken),
                supplier,
                receipt,
                alreadyReturned,
                request.Lines.Select(l => new SupplierReturnItem(l.ProductId, l.Quantity, l.UnitPrice)).ToList(),
                products,
                request.RefundAmount,
                request.RefundMethod,
                request.Reason,
                request.Note,
                now,
                userId);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        var ret = posting.Return;
        _returns.Add(ret);
        foreach (var movement in posting.Movements)
        {
            _movements.Add(movement);
        }

        _audit.Record(
            AuditActions.SupplierReturned,
            nameof(SupplierReturn),
            ret.Code,
            $"{supplier.Name}: trả {ret.Lines.Count} sản phẩm, giá trị {Vnd.Format(ret.Total)} (giá vốn {Vnd.Format(ret.CostValue)}), "
            + $"nhận lại {Vnd.Format(ret.RefundAmount)}, cấn trừ công nợ {Vnd.Format(ret.DebtOffset)} ({Vnd.Format(debtBefore)} → {Vnd.Format(supplier.DebtBalance)})"
            + (ret.GoodsReceiptCode is null ? string.Empty : $", theo phiếu nhập {ret.GoodsReceiptCode}")
            + $". Lý do: {ret.Reason}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var stockAfter = posting.Movements.ToDictionary(m => m.ProductId, m => m.BalanceAfter);
        return new SupplierReturnReceipt(ToDto(ret, supplier.Name), supplier.DebtBalance, stockAfter);
    }

    public async Task<Result<SupplierReturnDto>> GetAsync(int returnId, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var ret = await _returns.GetByIdAsync(returnId, cancellationToken);
        if (ret is null)
        {
            return SupplierDebtErrors.ReturnNotFound;
        }

        var supplier = await _suppliers.GetByIdAsync(ret.SupplierId, cancellationToken);
        return ToDto(ret, supplier?.Name ?? string.Empty);
    }

    public async Task<Result<IReadOnlyList<SupplierReturnSummary>>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (Authorize() is { } denied)
        {
            return denied;
        }

        if (filter.From is { } f && filter.To is { } t && f >= t)
        {
            return PurchasingErrors.InvalidPeriod;
        }

        var returns = await _returns.SearchAsync(filter, cancellationToken);
        var names = (await _suppliers.ListAsync(cancellationToken)).ToDictionary(s => s.Id, s => s.Name);
        IReadOnlyList<SupplierReturnSummary> items = returns
            .OrderByDescending(r => r.ReturnDate)
            .Select(r => new SupplierReturnSummary(
                r.Id,
                r.Code,
                r.ReturnDate,
                r.SupplierId,
                names.GetValueOrDefault(r.SupplierId, string.Empty),
                r.GoodsReceiptCode,
                r.Lines.Count,
                r.Total,
                r.RefundAmount,
                r.DebtOffset))
            .ToList();
        return Result.Success(items);
    }

    private async Task<Dictionary<int, decimal>> ReturnedQuantitiesAsync(int receiptId, CancellationToken cancellationToken) =>
        (await _returns.ListByGoodsReceiptAsync(receiptId, cancellationToken))
            .SelectMany(r => r.Lines)
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

    private async Task<string> NextCodeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var prefix = DailyCode.PrefixFor(CodePrefix, now.ToOffset(_options.StoreUtcOffset));
        var last = await _returns.GetLastCodeWithPrefixAsync(prefix, cancellationToken);
        return DailyCode.Next(prefix, last);
    }

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.PurchasesManage)
            ? null
            : Error.Forbidden("Bạn không có quyền trả hàng nhà cung cấp.");
    }

    private static SupplierReturnDto ToDto(SupplierReturn ret, string supplierName) => new(
        ret.Id,
        ret.Code,
        ret.ReturnDate,
        ret.SupplierId,
        supplierName,
        ret.GoodsReceiptCode,
        ret.Reason,
        ret.Note,
        ret.Total,
        ret.RefundAmount,
        ret.RefundMethod is { } m ? SaleLabels.Of(m) : null,
        ret.DebtOffset,
        ret.CostValue,
        ret.CostDifference,
        ret.Lines
            .Select(l => new SupplierReturnLineDto(
                l.ProductId, l.ProductCode, l.ProductName, l.Unit, l.Quantity, l.UnitPrice, l.Amount, l.CostPrice, l.CostValue))
            .ToList());
}
