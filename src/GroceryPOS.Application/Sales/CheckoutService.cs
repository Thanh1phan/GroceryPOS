using System.Globalization;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Sales;

public interface ICheckoutService
{
    /// <summary>Creates and completes a sale: invoice number, stock deduction, loyalty earn/redeem, audit.</summary>
    Task<Result<CheckoutReceipt>> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>Voids a completed sale: returns stock, reverses loyalty points and spend, audits the reason.</summary>
    Task<Result> VoidAsync(int saleId, string reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// POS checkout use case. All validation happens before any product or customer is modified,
/// so a failed checkout never leaves half-applied changes in the unit of work.
/// </summary>
public sealed class CheckoutService : ICheckoutService
{
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _movements;
    private readonly ICustomerRepository _customers;
    private readonly IInvoiceNumberGenerator _invoiceNumbers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly SalesOptions _options;

    public CheckoutService(
        ISaleRepository sales,
        IProductRepository products,
        IStockMovementRepository movements,
        ICustomerRepository customers,
        IInvoiceNumberGenerator invoiceNumbers,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IAuditService audit,
        TimeProvider clock,
        SalesOptions? options = null)
    {
        _sales = sales;
        _products = products;
        _movements = movements;
        _customers = customers;
        _invoiceNumbers = invoiceNumbers;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _options = options ?? SalesOptions.Default;
    }

    public async Task<Result<CheckoutReceipt>> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_currentUser.Session is not { } session)
        {
            return AuthErrors.NotAuthenticated;
        }

        if (!_currentUser.HasPermission(Permissions.SalesCreate))
        {
            return Error.Forbidden();
        }

        if (request.HasDiscounts && !_currentUser.HasPermission(Permissions.SalesDiscount))
        {
            return SaleErrors.DiscountNotAllowed;
        }

        if (request.Lines.Count == 0)
        {
            return SaleErrors.EmptyCart;
        }

        if (request.Payments.Count == 0)
        {
            return SaleErrors.NoPayment;
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = (await _products.GetByIdsAsync(productIds, cancellationToken)).ToDictionary(p => p.Id);
        if (productIds.FirstOrDefault(id => !products.ContainsKey(id)) is var missing and > 0)
        {
            return SaleErrors.ProductNotFound(missing);
        }

        Customer? customer = null;
        if (request.CustomerId is { } customerId)
        {
            customer = await _customers.GetByIdAsync(customerId, cancellationToken);
            if (customer is null)
            {
                return SaleErrors.CustomerNotFound;
            }
        }

        var now = _clock.GetUtcNow();
        Sale sale;
        try
        {
            sale = await BuildSaleAsync(request, products, customer, session.UserId, now, cancellationToken);
            EnsureStockAvailable(sale, products);
            sale.Complete(now, _options.Loyalty);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        // Everything below is guaranteed to succeed: apply side effects.
        foreach (var (productId, quantity) in QuantitiesByProduct(sale))
        {
            _movements.Add(StockMovement.Apply(
                products[productId], StockMovementType.Sale, -quantity, now, session.UserId, sale.Code, allowNegative: true));
        }

        if (customer is not null)
        {
            if (sale.PointsRedeemed > 0)
            {
                customer.RedeemPoints(sale.PointsRedeemed, _options.Loyalty);
            }

            customer.RecordPurchase(sale.Total, _options.Loyalty);
        }

        _sales.Add(sale);
        _audit.Record(AuditActions.SaleCompleted, nameof(Sale), sale.Code, DescribeSale(sale));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var lowStock = productIds
            .Select(id => products[id])
            .Where(p => p.IsLowStock)
            .Select(p => new LowStockAlert(p.Id, p.Code, p.Name, p.StockQuantity, p.MinStockLevel))
            .ToList();

        return new CheckoutReceipt(
            sale.Id,
            sale.Code,
            now,
            sale.Total,
            sale.AmountPaid,
            sale.ChangeDue,
            sale.TotalDiscount,
            sale.VatTotal,
            sale.PointsEarned,
            sale.PointsRedeemed,
            customer?.LoyaltyPoints,
            lowStock);
    }

    public async Task<Result> VoidAsync(int saleId, string reason, CancellationToken cancellationToken = default)
    {
        if (_currentUser.Session is not { } session)
        {
            return AuthErrors.NotAuthenticated;
        }

        if (!_currentUser.HasPermission(Permissions.SalesVoid))
        {
            return Error.Forbidden("Bạn không có quyền hủy hóa đơn.");
        }

        var sale = await _sales.GetByIdAsync(saleId, cancellationToken);
        if (sale is null)
        {
            return SaleErrors.SaleNotFound;
        }

        var now = _clock.GetUtcNow();
        try
        {
            sale.Void(now, session.UserId, reason);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        var quantities = QuantitiesByProduct(sale);
        var products = (await _products.GetByIdsAsync(quantities.Keys, cancellationToken)).ToDictionary(p => p.Id);
        foreach (var (productId, quantity) in quantities)
        {
            // A product deleted since the sale simply gets no stock back.
            if (products.TryGetValue(productId, out var product))
            {
                _movements.Add(StockMovement.Apply(
                    product, StockMovementType.SaleVoid, quantity, now, session.UserId, sale.Code, allowNegative: true));
            }
        }

        if (sale.CustomerId is { } customerId
            && await _customers.GetByIdAsync(customerId, cancellationToken) is { } customer)
        {
            customer.ReverseSale(sale.Total, sale.PointsEarned, sale.PointsRedeemed);
        }

        _audit.Record(AuditActions.SaleVoided, nameof(Sale), sale.Code, $"{DescribeSale(sale)} – Lý do: {sale.VoidReason}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Sale> BuildSaleAsync(
        CheckoutRequest request,
        IReadOnlyDictionary<int, Product> products,
        Customer? customer,
        int cashierId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var code = await _invoiceNumbers.NextAsync(now, cancellationToken);
        var sale = new Sale(code, cashierId, now);

        foreach (var input in request.Lines)
        {
            var line = sale.AddItem(products[input.ProductId], input.Quantity);
            if (input.Discount is { IsNone: false } discount)
            {
                // A discounted line is never merged into: AddItem only merges into undiscounted lines.
                sale.SetLineDiscount(line, discount.ToDomain());
            }
        }

        if (request.InvoiceDiscount is { IsNone: false } invoiceDiscount)
        {
            sale.SetInvoiceDiscount(invoiceDiscount.ToDomain());
        }

        if (customer is not null)
        {
            sale.AttachCustomer(customer);
            if (request.PointsToRedeem > 0)
            {
                sale.RedeemPoints(request.PointsToRedeem, _options.Loyalty);
            }
        }
        else if (request.PointsToRedeem > 0)
        {
            throw new DomainException("sale.points", "Vui lòng chọn khách hàng trước khi đổi điểm.");
        }

        sale.SetNote(request.Note);
        foreach (var payment in request.Payments)
        {
            sale.AddPayment(payment.Method, payment.Amount, payment.Reference);
        }

        return sale;
    }

    private void EnsureStockAvailable(Sale sale, IReadOnlyDictionary<int, Product> products)
    {
        if (_options.AllowNegativeStock)
        {
            return;
        }

        foreach (var (productId, quantity) in QuantitiesByProduct(sale))
        {
            var product = products[productId];
            if (product.StockQuantity < quantity)
            {
                throw new DomainException(
                    "product.stock",
                    $"Không đủ tồn kho cho \"{product.Name}\" (còn {product.StockQuantity.ToString("0.###", Vnd.Culture)} {product.Unit}).");
            }
        }
    }

    private static Dictionary<int, decimal> QuantitiesByProduct(Sale sale) =>
        sale.Lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

    private static string DescribeSale(Sale sale) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Tổng {Vnd.Format(sale.Total)}, {sale.Lines.Count} dòng, giảm {Vnd.Format(sale.TotalDiscount)}");
}
