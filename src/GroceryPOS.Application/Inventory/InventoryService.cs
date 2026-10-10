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
using GroceryPOS.Domain.Inventory;

namespace GroceryPOS.Application.Inventory;

public interface IInventoryService
{
    /// <summary>Manual stock adjustment with a reason (damaged, expired, found...). Requires <c>inventory.adjust</c>.</summary>
    Task<Result<StockAdjustmentResult>> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a physical stock count: every product's stock is set to the counted quantity and each
    /// difference is written to the ledger under one count code (KK...). Requires <c>inventory.adjust</c>.
    /// </summary>
    Task<Result<StockCountResult>> CountAsync(StockCountRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stock card of one product for a period. Requires <c>inventory.view</c>.</summary>
    Task<Result<StockLedger>> GetLedgerAsync(int productId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);

    /// <summary>Active products at or below their minimum level, out-of-stock first. Requires <c>inventory.view</c>.</summary>
    Task<Result<IReadOnlyList<LowStockItem>>> GetLowStockAsync(CancellationToken cancellationToken = default);
}

public sealed class InventoryService : IInventoryService
{
    public const string StockCountPrefix = "KK";

    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _movements;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly SalesOptions _options;

    public InventoryService(
        IProductRepository products,
        IStockMovementRepository movements,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IAuditService audit,
        TimeProvider clock,
        SalesOptions? options = null)
    {
        _products = products;
        _movements = movements;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _options = options ?? SalesOptions.Default;
    }

    public async Task<Result<StockAdjustmentResult>> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize(Permissions.InventoryAdjust, "Bạn không có quyền điều chỉnh tồn kho.") is { } denied)
        {
            return denied;
        }

        var product = await _products.GetByIdAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return InventoryErrors.ProductNotFound(request.ProductId);
        }

        StockMovement movement;
        try
        {
            // Manual adjustments may never push stock below zero (sales may, see SalesOptions).
            movement = StockMovement.Apply(
                product,
                StockMovementType.Adjustment,
                request.Quantity,
                _clock.GetUtcNow(),
                _currentUser.Session!.UserId,
                reason: request.Reason,
                note: request.Note);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _movements.Add(movement);
        _audit.Record(
            AuditActions.StockAdjusted,
            nameof(Product),
            product.Code,
            $"{product.Name}: {FormatSigned(movement.Quantity)} {product.Unit} ({request.Reason.DisplayName()}), "
            + $"tồn {FormatQty(movement.BalanceBefore)} → {FormatQty(movement.BalanceAfter)}, giá trị {Vnd.Format(movement.CostValue)}"
            + (movement.Note is null ? string.Empty : $" – {movement.Note}"));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new StockAdjustmentResult(
            product.Id,
            product.Code,
            product.Name,
            movement.Quantity,
            movement.BalanceAfter,
            movement.CostValue,
            product.IsLowStock);
    }

    public async Task<Result<StockCountResult>> CountAsync(StockCountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize(Permissions.InventoryAdjust, "Bạn không có quyền kiểm kê kho.") is { } denied)
        {
            return denied;
        }

        if (request.Lines.Count == 0)
        {
            return InventoryErrors.EmptyCount;
        }

        var ids = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = (await _products.GetByIdsAsync(ids, cancellationToken)).ToDictionary(p => p.Id);
        if (ids.FirstOrDefault(id => !products.ContainsKey(id)) is var missing and > 0)
        {
            return InventoryErrors.ProductNotFound(missing);
        }

        // Validate everything before touching any product.
        var seen = new HashSet<int>();
        foreach (var line in request.Lines)
        {
            var product = products[line.ProductId];
            if (!seen.Add(line.ProductId))
            {
                return InventoryErrors.DuplicateCountLine(product.Name);
            }

            if (line.CountedQuantity < 0)
            {
                return InventoryErrors.NegativeCount(product.Name);
            }
        }

        var now = _clock.GetUtcNow();
        var code = StockCountCode(now);
        var userId = _currentUser.Session!.UserId;
        var results = new List<StockCountLineResult>(request.Lines.Count);

        foreach (var line in request.Lines)
        {
            var product = products[line.ProductId];
            var counted = Math.Round(line.CountedQuantity, StockMovement.QuantityDecimals, MidpointRounding.AwayFromZero);
            results.Add(new StockCountLineResult(
                product.Id, product.Code, product.Name, product.Unit, product.StockQuantity, counted, product.CostPrice));

            var variance = counted - product.StockQuantity;
            if (variance != 0)
            {
                _movements.Add(StockMovement.Apply(
                    product, StockMovementType.StockCount, variance, now, userId, code, note: request.Note, allowNegative: true));
            }
        }

        var result = new StockCountResult(code, now, results);
        _audit.Record(
            AuditActions.StockCounted,
            "StockCount",
            code,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{results.Count} sản phẩm, lệch {result.MismatchCount}, thiếu {Vnd.Format(-result.ShortageValue)}, thừa {Vnd.Format(result.SurplusValue)}"));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<Result<StockLedger>> GetLedgerAsync(
        int productId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.InventoryView) is { } denied)
        {
            return denied;
        }

        if (from is { } f && to is { } t && f >= t)
        {
            return InventoryErrors.InvalidPeriod;
        }

        var product = await _products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return InventoryErrors.ProductNotFound(productId);
        }

        var movements = await _movements.ListByProductAsync(productId, from, to, cancellationToken);
        var entries = movements
            .Select(m => new StockLedgerEntry(
                m.Id,
                m.Timestamp,
                m.Type,
                m.Type.DisplayName(),
                m.ReferenceCode,
                m.Quantity > 0 ? m.Quantity : 0,
                m.Quantity < 0 ? -m.Quantity : 0,
                m.BalanceAfter,
                m.UnitCost,
                m.Reason?.DisplayName(),
                m.Note))
            .ToList();

        var totalIn = entries.Sum(e => e.QuantityIn);
        var totalOut = entries.Sum(e => e.QuantityOut);

        // Closing balance = current stock minus everything that happened after the period.
        var closing = product.StockQuantity;
        if (to is { } end)
        {
            var later = await _movements.ListByProductAsync(productId, end, null, cancellationToken);
            closing -= later.Sum(m => m.Quantity);
        }

        var opening = closing - totalIn + totalOut;

        return new StockLedger(product.Id, product.Code, product.Name, product.Unit, opening, totalIn, totalOut, closing, entries);
    }

    public async Task<Result<IReadOnlyList<LowStockItem>>> GetLowStockAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.InventoryView) is { } denied)
        {
            return denied;
        }

        var products = await _products.ListLowStockAsync(cancellationToken);
        IReadOnlyList<LowStockItem> items = products
            .Where(p => p.IsActive && p.IsLowStock)
            .Select(p => new LowStockItem(p.Id, p.Code, p.Name, p.Unit, p.StockQuantity, p.MinStockLevel, p.CostPrice))
            .OrderByDescending(i => i.IsOutOfStock)
            .ThenByDescending(i => i.Shortage)
            .ThenBy(i => i.ProductName, StringComparer.Create(Vnd.Culture, ignoreCase: true))
            .ToList();
        return Result.Success(items);
    }

    /// <summary>Stock count code in store-local time, e.g. KK261010-173005.</summary>
    public string StockCountCode(DateTimeOffset now) =>
        StockCountPrefix + now.ToOffset(_options.StoreUtcOffset).ToString("yyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private Error? Authorize(string permission, string? deniedMessage = null)
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(permission)
            ? null
            : deniedMessage is null ? Error.Forbidden() : Error.Forbidden(deniedMessage);
    }

    private static string FormatQty(decimal quantity) => quantity.ToString("0.###", Vnd.Culture);

    private static string FormatSigned(decimal quantity) => (quantity > 0 ? "+" : string.Empty) + FormatQty(quantity);
}
