using System.Globalization;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Sales;

public interface IReturnService
{
    /// <summary>Loads an invoice by code (scanned from the receipt) with what can still be returned per line.</summary>
    Task<Result<ReturnableSaleDto>> GetReturnableAsync(string saleCode, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer return: refund, restock (or write off), loyalty reversal, audit.</summary>
    Task<Result<ReturnReceipt>> CreateAsync(CreateReturnRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns already made against an invoice, oldest first.</summary>
    Task<Result<IReadOnlyList<SaleReturnSummaryDto>>> ListBySaleAsync(int saleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Customer returns (khách trả hàng, phiếu <c>TH{yyMMdd}-{0001}</c>). Cashiers can take returns within
/// <see cref="SalesOptions.ReturnWindowDays"/>; older invoices need the manager permission <c>sales.void</c>.
/// </summary>
public sealed class ReturnService : IReturnService
{
    private readonly ISaleRepository _sales;
    private readonly ISaleReturnRepository _returns;
    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _movements;
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly SalesOptions _options;
    private readonly IShiftRepository? _shifts;

    public ReturnService(
        ISaleRepository sales,
        ISaleReturnRepository returns,
        IProductRepository products,
        IStockMovementRepository movements,
        ICustomerRepository customers,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IAuditService audit,
        TimeProvider clock,
        SalesOptions? options = null,
        IShiftRepository? shifts = null)
    {
        _sales = sales;
        _returns = returns;
        _products = products;
        _movements = movements;
        _customers = customers;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _options = options ?? SalesOptions.Default;
        _shifts = shifts;
    }

    public async Task<Result<ReturnableSaleDto>> GetReturnableAsync(string saleCode, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return error;
        }

        var code = (saleCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            return Error.Validation("Vui lòng nhập số hóa đơn.");
        }

        var sale = await _sales.GetByCodeAsync(code, cancellationToken);
        if (sale is null)
        {
            return SaleErrors.SaleNotFound;
        }

        if (CheckReturnable(sale) is { } notReturnable && notReturnable != ReturnErrors.AlreadyFullyReturned)
        {
            return notReturnable;
        }

        string? customerName = null;
        if (sale.CustomerId is { } customerId)
        {
            customerName = (await _customers.GetByIdAsync(customerId, cancellationToken))?.Name;
        }

        var withinWindow = IsWithinWindow(sale);
        var lines = sale.Lines
            .Select(l => new ReturnableLineDto(
                l.Id,
                l.ProductId,
                l.ProductCode,
                l.ProductName,
                l.Unit,
                l.Quantity,
                l.ReturnedQuantity,
                l.RemainingQuantity,
                l.FinalAmount,
                l.FinalAmount - l.RefundedAmount))
            .ToList();

        return new ReturnableSaleDto(
            sale.Id,
            sale.Code,
            sale.SaleDate,
            sale.CustomerId,
            customerName,
            sale.Total,
            sale.RefundedTotal,
            sale.PointsEarned,
            sale.PointsRedeemed,
            withinWindow,
            CanReturn: !sale.IsFullyReturned && (withinWindow || _currentUser.HasPermission(Permissions.SalesVoid)),
            lines,
            sale.PointsReversed,
            sale.RedeemedPointsRestored);
    }

    public async Task<Result<ReturnReceipt>> CreateAsync(CreateReturnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize() is { } error)
        {
            return error;
        }

        var session = _currentUser.Session!;
        var items = request.Lines.Where(l => l.Quantity != 0).ToList();
        if (items.Count == 0)
        {
            return ReturnErrors.EmptyReturn;
        }

        var sale = await _sales.GetByIdAsync(request.SaleId, cancellationToken);
        if (sale is null)
        {
            return SaleErrors.SaleNotFound;
        }

        if (CheckReturnable(sale) is { } notReturnable)
        {
            return notReturnable;
        }

        if (!IsWithinWindow(sale) && !_currentUser.HasPermission(Permissions.SalesVoid))
        {
            return ReturnErrors.WindowExpired(_options.ReturnWindowDays);
        }

        // Cash comes out of the drawer, so it must land in an open shift's count (like checkout).
        if (request.RefundMethod == PaymentMethod.Cash && _options.RequireOpenShift && _shifts is not null
            && await _shifts.GetOpenByUserAsync(session.UserId, cancellationToken) is null)
        {
            return ReturnErrors.NoOpenShiftForCashRefund;
        }

        var returnItems = new List<ReturnItem>(items.Count);
        foreach (var item in items)
        {
            var line = sale.Lines.FirstOrDefault(l => l.Id == item.SaleLineId);
            if (line is null)
            {
                return ReturnErrors.LineNotFound(item.SaleLineId);
            }

            returnItems.Add(new ReturnItem(line, item.Quantity, item.Restock));
        }

        var now = _clock.GetUtcNow();
        var code = await NextCodeAsync(now, cancellationToken);
        SaleReturn saleReturn;
        try
        {
            // Validates everything before the sale is changed; a rejected return leaves the sale untouched.
            saleReturn = SaleReturn.Create(code, sale, session.UserId, now, request.Reason, request.RefundMethod, returnItems);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        var restock = saleReturn.Lines
            .Where(l => l.Restock)
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => (Quantity: g.Sum(l => l.Quantity), UnitCost: g.First().UnitCost));
        var products = (await _products.GetByIdsAsync(restock.Keys, cancellationToken)).ToDictionary(p => p.Id);
        foreach (var (productId, (quantity, unitCost)) in restock)
        {
            // A product deleted since the sale simply gets no stock back.
            if (products.TryGetValue(productId, out var product))
            {
                _movements.Add(StockMovement.Apply(
                    product,
                    StockMovementType.CustomerReturn,
                    quantity,
                    now,
                    session.UserId,
                    saleReturn.Code,
                    note: $"Trả từ hóa đơn {sale.Code}",
                    allowNegative: true,
                    unitCost: unitCost));
            }
        }

        int? pointsBalance = null;
        string? customerName = null;
        if (sale.CustomerId is { } customerId
            && await _customers.GetByIdAsync(customerId, cancellationToken) is { } customer)
        {
            customer.ReverseSale(saleReturn.RefundTotal, saleReturn.PointsReversed, saleReturn.PointsRestored);
            pointsBalance = customer.LoyaltyPoints;
            customerName = customer.Name;
        }

        _returns.Add(saleReturn);
        _audit.Record(AuditActions.SaleReturned, nameof(SaleReturn), saleReturn.Code, Describe(saleReturn));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ReturnReceipt(
            saleReturn.Id,
            saleReturn.Code,
            sale.Code,
            now,
            saleReturn.RefundTotal,
            saleReturn.RefundMethod,
            saleReturn.PointsReversed,
            saleReturn.PointsRestored,
            pointsBalance,
            sale.IsFullyReturned,
            saleReturn.WrittenOffCost,
            saleReturn.Lines
                .Select(l => new ReturnedLineDto(l.ProductCode, l.ProductName, l.Unit, l.Quantity, l.RefundAmount, l.Restock))
                .ToList(),
            saleReturn.Reason,
            customerName);
    }

    public async Task<Result<IReadOnlyList<SaleReturnSummaryDto>>> ListBySaleAsync(int saleId, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return error;
        }

        var returns = await _returns.ListBySaleAsync(saleId, cancellationToken);
        return returns
            .Select(r => new SaleReturnSummaryDto(r.Id, r.Code, r.ReturnedAt, r.RefundTotal, r.RefundMethod, r.Reason, r.Lines.Count))
            .ToList();
    }

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.ReturnsCreate)
            ? null
            : Error.Forbidden("Bạn không có quyền trả hàng.");
    }

    private static Error? CheckReturnable(Sale sale) => sale.Status switch
    {
        SaleStatus.Voided => ReturnErrors.SaleVoided,
        SaleStatus.Draft => ReturnErrors.SaleNotCompleted,
        _ when sale.IsFullyReturned => ReturnErrors.AlreadyFullyReturned,
        _ => null,
    };

    /// <summary>Compares store calendar days, so a sale at 21:00 can still be returned on the 7th evening after.</summary>
    private bool IsWithinWindow(Sale sale)
    {
        var today = _clock.GetUtcNow().ToOffset(_options.StoreUtcOffset).Date;
        var saleDay = sale.SaleDate.ToOffset(_options.StoreUtcOffset).Date;
        return (today - saleDay).TotalDays <= _options.ReturnWindowDays;
    }

    private async Task<string> NextCodeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var prefix = DailyCode.PrefixFor(_options.ReturnPrefix, now.ToOffset(_options.StoreUtcOffset));
        var last = await _returns.GetLastCodeWithPrefixAsync(prefix, cancellationToken);
        return DailyCode.Next(prefix, last);
    }

    private static string Describe(SaleReturn saleReturn) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Trả hàng HĐ {saleReturn.SaleCode}: hoàn {Vnd.Format(saleReturn.RefundTotal)} ({SaleLabels.Of(saleReturn.RefundMethod)}), " +
            $"{saleReturn.Lines.Count} dòng, hủy hàng {Vnd.Format(saleReturn.WrittenOffCost)} – Lý do: {saleReturn.Reason}");
}
