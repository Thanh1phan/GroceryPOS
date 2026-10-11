using System.Globalization;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;

namespace GroceryPOS.Application.Purchasing;

public interface IGoodsReceiptService
{
    /// <summary>Creates or rewrites a draft receipt (no effect on stock). Requires <c>purchases.manage</c>.</summary>
    Task<Result<GoodsReceiptDto>> SaveDraftAsync(SaveGoodsReceiptRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a draft: stock in through the ledger, weighted-average cost, optional new shelf prices,
    /// unpaid amount added to supplier debt, audited. Requires <c>purchases.manage</c>.
    /// </summary>
    Task<Result<GoodsReceiptCompletion>> CompleteAsync(int receiptId, CancellationToken cancellationToken = default);

    /// <summary>Discards a draft receipt. Requires <c>purchases.manage</c>.</summary>
    Task<Result> CancelAsync(int receiptId, string? reason, CancellationToken cancellationToken = default);

    Task<Result<GoodsReceiptDto>> GetAsync(int receiptId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GoodsReceiptSummary>>> SearchAsync(GoodsReceiptFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Low-stock products with a suggested quantity, to pre-fill a new receipt. Requires <c>purchases.manage</c>.</summary>
    Task<Result<IReadOnlyList<ReorderSuggestion>>> SuggestReorderAsync(CancellationToken cancellationToken = default);
}

public sealed class GoodsReceiptService : IGoodsReceiptService
{
    public const string CodePrefix = "PN";

    private readonly IGoodsReceiptRepository _receipts;
    private readonly ISupplierRepository _suppliers;
    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _movements;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly SalesOptions _options;

    public GoodsReceiptService(
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

    public async Task<Result<GoodsReceiptDto>> SaveDraftAsync(SaveGoodsReceiptRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize() is { } denied)
        {
            return denied;
        }

        if (request.Lines.Count == 0)
        {
            return PurchasingErrors.EmptyReceipt;
        }

        if (request.Lines.Any(l => l.NewSellPrice is not null) && !_currentUser.HasPermission(Permissions.PricesChange))
        {
            return PurchasingErrors.PriceChangeNotAllowed;
        }

        var supplier = await _suppliers.GetByIdAsync(request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return PurchasingErrors.SupplierNotFound;
        }

        if (!supplier.IsActive)
        {
            return PurchasingErrors.SupplierInactive(supplier.Name);
        }

        var ids = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = (await _products.GetByIdsAsync(ids, cancellationToken)).ToDictionary(p => p.Id);
        if (ids.FirstOrDefault(id => !products.ContainsKey(id)) is var missing and > 0)
        {
            return PurchasingErrors.ProductNotFound(missing);
        }

        GoodsReceipt receipt;
        var isNew = request.Id is null;
        if (isNew)
        {
            var now = _clock.GetUtcNow();
            receipt = new GoodsReceipt(await NextCodeAsync(now, cancellationToken), supplier.Id, _currentUser.Session!.UserId, now);
        }
        else
        {
            var existing = await _receipts.GetByIdAsync(request.Id!.Value, cancellationToken);
            if (existing is null)
            {
                return PurchasingErrors.ReceiptNotFound;
            }

            receipt = existing;
        }

        try
        {
            // A rejected edit is never saved: the unit of work is only committed on success.
            receipt.ChangeSupplier(supplier.Id);
            receipt.SetReference(request.SupplierInvoiceNo, request.Note);
            receipt.ClearLines();
            foreach (var line in request.Lines)
            {
                receipt.AddLine(products[line.ProductId], line.Quantity, line.UnitCost, line.DiscountAmount, line.NewSellPrice);
            }

            receipt.SetDiscount(request.DiscountAmount);
            receipt.SetPaidAmount(request.PaidAmount);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        if (receipt.DiscountAmount > receipt.Subtotal)
        {
            return PurchasingErrors.DiscountTooLarge;
        }

        if (receipt.PaidAmount > receipt.Total)
        {
            return PurchasingErrors.OverPaid;
        }

        if (isNew)
        {
            _receipts.Add(receipt);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(receipt, supplier);
    }

    public async Task<Result<GoodsReceiptCompletion>> CompleteAsync(int receiptId, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var receipt = await _receipts.GetByIdAsync(receiptId, cancellationToken);
        if (receipt is null)
        {
            return PurchasingErrors.ReceiptNotFound;
        }

        if (receipt.Lines.Any(l => l.NewSellPrice is not null) && !_currentUser.HasPermission(Permissions.PricesChange))
        {
            return PurchasingErrors.PriceChangeNotAllowed;
        }

        var supplier = await _suppliers.GetByIdAsync(receipt.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return PurchasingErrors.SupplierNotFound;
        }

        if (!supplier.IsActive)
        {
            return PurchasingErrors.SupplierInactive(supplier.Name);
        }

        var ids = receipt.Lines.Select(l => l.ProductId).ToList();
        var products = (await _products.GetByIdsAsync(ids, cancellationToken)).ToDictionary(p => p.Id);
        if (ids.FirstOrDefault(id => !products.ContainsKey(id)) is var missing and > 0)
        {
            return PurchasingErrors.ProductNotFound(missing);
        }

        IReadOnlyList<GoodsReceiptPosting> postings;
        var userId = _currentUser.Session!.UserId;
        try
        {
            // GoodsReceipt.Complete checks every rule before touching any product.
            postings = receipt.Complete(products, _clock.GetUtcNow(), userId);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        foreach (var posting in postings)
        {
            _movements.Add(posting.Movement);
            if (posting.SellPriceChange is { HasChanged: true } change)
            {
                _audit.Record(
                    AuditActions.PriceChanged,
                    nameof(Product),
                    posting.Product.Code,
                    $"{posting.Product.Name}: giá bán {Vnd.Format(change.OldSellPrice)} → {Vnd.Format(change.NewSellPrice)} (phiếu nhập {receipt.Code})");
            }
        }

        supplier.AddDebt(receipt.DebtAmount);
        _audit.Record(
            AuditActions.GoodsReceived,
            nameof(GoodsReceipt),
            receipt.Code,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{supplier.Name}: {receipt.Lines.Count} sản phẩm, tổng {Vnd.Format(receipt.Total)}, đã trả {Vnd.Format(receipt.PaidAmount)}, công nợ {Vnd.Format(receipt.DebtAmount)}"));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var lines = postings
            .Select(p => new GoodsReceiptPostedLine(
                p.Product.Id,
                p.Product.Code,
                p.Product.Name,
                p.Movement.Quantity,
                p.Movement.BalanceAfter,
                p.CostChange.OldCostPrice,
                p.CostChange.NewCostPrice,
                p.Product.SellPrice,
                p.SellPriceChange?.HasChanged ?? false))
            .ToList();
        return new GoodsReceiptCompletion(receipt.Code, receipt.Total, receipt.PaidAmount, receipt.DebtAmount, supplier.DebtBalance, lines);
    }

    public async Task<Result> CancelAsync(int receiptId, string? reason, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var receipt = await _receipts.GetByIdAsync(receiptId, cancellationToken);
        if (receipt is null)
        {
            return PurchasingErrors.ReceiptNotFound;
        }

        try
        {
            receipt.Cancel(_clock.GetUtcNow(), _currentUser.Session!.UserId, reason);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _audit.Record(AuditActions.GoodsReceiptCancelled, nameof(GoodsReceipt), receipt.Code, receipt.CancelReason);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<GoodsReceiptDto>> GetAsync(int receiptId, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var receipt = await _receipts.GetByIdAsync(receiptId, cancellationToken);
        if (receipt is null)
        {
            return PurchasingErrors.ReceiptNotFound;
        }

        var supplier = await _suppliers.GetByIdAsync(receipt.SupplierId, cancellationToken);
        return ToDto(receipt, supplier);
    }

    public async Task<Result<IReadOnlyList<GoodsReceiptSummary>>> SearchAsync(GoodsReceiptFilter filter, CancellationToken cancellationToken = default)
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

        var receipts = await _receipts.SearchAsync(filter, cancellationToken);
        var suppliers = (await _suppliers.ListAsync(cancellationToken)).ToDictionary(s => s.Id, s => s.Name);
        IReadOnlyList<GoodsReceiptSummary> items = receipts
            .OrderByDescending(r => r.ReceiptDate)
            .Select(r => new GoodsReceiptSummary(
                r.Id,
                r.Code,
                r.ReceiptDate,
                r.Status,
                r.Status.DisplayName(),
                r.SupplierId,
                suppliers.GetValueOrDefault(r.SupplierId, string.Empty),
                r.Lines.Count,
                r.Total,
                r.PaidAmount,
                r.Status == GoodsReceiptStatus.Completed ? r.DebtAmount : 0))
            .ToList();
        return Result.Success(items);
    }

    public async Task<Result<IReadOnlyList<ReorderSuggestion>>> SuggestReorderAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var products = await _products.ListLowStockAsync(cancellationToken);
        IReadOnlyList<ReorderSuggestion> items = products
            .Where(p => p.IsActive && p.IsLowStock)
            .Select(p => new ReorderSuggestion(
                p.Id, p.Code, p.Name, p.Unit, p.StockQuantity, p.MinStockLevel, SuggestedQuantity(p), p.CostPrice))
            .Where(s => s.SuggestedQuantity > 0)
            .OrderByDescending(s => s.StockQuantity <= 0)
            .ThenByDescending(s => s.EstimatedAmount)
            .ThenBy(s => s.ProductName, StringComparer.Create(Vnd.Culture, ignoreCase: true))
            .ToList();
        return Result.Success(items);
    }

    /// <summary>Refill to twice the minimum level (or at least back to zero), in whole units.</summary>
    public static decimal SuggestedQuantity(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        var target = Math.Max(product.MinStockLevel * 2, 0);
        return Math.Max(0, Math.Ceiling(target - product.StockQuantity));
    }

    private async Task<string> NextCodeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var prefix = DailyCode.PrefixFor(CodePrefix, now.ToOffset(_options.StoreUtcOffset));
        var last = await _receipts.GetLastCodeWithPrefixAsync(prefix, cancellationToken);
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
            : Error.Forbidden("Bạn không có quyền nhập hàng.");
    }

    private static GoodsReceiptDto ToDto(GoodsReceipt receipt, Supplier? supplier) => new(
        receipt.Id,
        receipt.Code,
        receipt.Status,
        receipt.Status.DisplayName(),
        receipt.ReceiptDate,
        receipt.SupplierId,
        supplier?.Name ?? string.Empty,
        receipt.SupplierInvoiceNo,
        receipt.Note,
        receipt.Subtotal,
        receipt.DiscountAmount,
        receipt.Total,
        receipt.PaidAmount,
        receipt.DebtAmount,
        receipt.CompletedAt,
        receipt.CancelReason,
        receipt.Lines
            .Select(l => new GoodsReceiptLineDto(
                l.ProductId,
                l.ProductCode,
                l.ProductName,
                l.Unit,
                l.Quantity,
                l.UnitCost,
                l.DiscountAmount,
                l.AllocatedDiscount,
                l.FinalAmount,
                l.EffectiveUnitCost,
                l.NewSellPrice))
            .ToList());
}
