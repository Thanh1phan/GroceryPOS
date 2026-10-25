using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Sales.Receipts;

public interface IReceiptService
{
    /// <summary>Receipt for a sale that was just paid (printed right after checkout).</summary>
    Task<Result<ReceiptData>> GetForSaleAsync(int saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reprint by invoice code (bản in lại). The copy is marked as a reprint, shows the print time and
    /// is audited, so a reprinted receipt can never pass for a second sale.
    /// </summary>
    Task<Result<ReceiptData>> ReprintAsync(string invoiceCode, CancellationToken cancellationToken = default);
}

public sealed class ReceiptService : IReceiptService
{
    private readonly ISaleRepository _sales;
    private readonly IUserRepository _users;
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly StoreProfile _store;
    private readonly SalesOptions _options;

    public ReceiptService(
        ISaleRepository sales,
        IUserRepository users,
        ICustomerRepository customers,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IAuditService audit,
        TimeProvider clock,
        StoreProfile? store = null,
        SalesOptions? options = null)
    {
        _sales = sales;
        _users = users;
        _customers = customers;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _store = store ?? StoreProfile.Default;
        _options = options ?? SalesOptions.Default;
    }

    public async Task<Result<ReceiptData>> GetForSaleAsync(int saleId, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var sale = await _sales.GetByIdAsync(saleId, cancellationToken);
        if (sale is null || sale.IsDraft)
        {
            return Error.NotFound("Không tìm thấy hóa đơn.");
        }

        return await BuildAsync(sale, reprint: false, cancellationToken);
    }

    public async Task<Result<ReceiptData>> ReprintAsync(string invoiceCode, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(invoiceCode))
        {
            return Error.Validation("Vui lòng nhập số hóa đơn.");
        }

        var sale = await _sales.GetByCodeAsync(invoiceCode.Trim().ToUpperInvariant(), cancellationToken);
        if (sale is null || sale.IsDraft)
        {
            return Error.NotFound($"Không tìm thấy hóa đơn {invoiceCode.Trim().ToUpperInvariant()}.");
        }

        var receipt = await BuildAsync(sale, reprint: true, cancellationToken);
        _audit.Record(AuditActions.ReceiptReprinted, nameof(Sale), sale.Code, $"In lại hóa đơn {sale.Code} ({Formatting.Vnd.Format(sale.Total)})");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return receipt;
    }

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.SalesCreate) || _currentUser.HasPermission(Permissions.ReportsView)
            ? null
            : Error.Forbidden();
    }

    private async Task<ReceiptData> BuildAsync(Sale sale, bool reprint, CancellationToken cancellationToken)
    {
        var cashier = await _users.GetByIdAsync(sale.CashierId, cancellationToken);
        var member = await BuildMemberAsync(sale, reprint, cancellationToken);

        var items = sale.Lines
            .Select(l => new ReceiptItem(
                l.ProductName,
                l.Unit,
                l.Quantity,
                l.UnitPrice,
                l.GrossAmount,
                DiscountLabel(l.Discount),
                l.LineDiscountAmount,
                l.NetAmount))
            .ToList();

        var payments = sale.Payments
            .Select(p => new ReceiptPayment(SaleLabels.Of(p.Method), p.Amount, p.Reference))
            .ToList();

        var issuedAt = (sale.CompletedAt ?? sale.SaleDate).ToOffset(_options.StoreUtcOffset);
        var now = _clock.GetUtcNow().ToOffset(_options.StoreUtcOffset);

        return new ReceiptData(
            _store,
            sale.Code,
            issuedAt,
            cashier?.FullName ?? $"#{sale.CashierId}",
            sale.Status,
            items,
            sale.GrossAmount,
            sale.LineDiscountTotal,
            DiscountLabel(sale.InvoiceDiscount),
            sale.InvoiceDiscountAmount,
            sale.PointsDiscountAmount,
            sale.Total,
            payments,
            sale.AmountPaid,
            sale.ChangeDue,
            sale.VatBreakdown(),
            member,
            sale.Note,
            reprint,
            reprint ? now : null);
    }

    private async Task<ReceiptMember?> BuildMemberAsync(Sale sale, bool reprint, CancellationToken cancellationToken)
    {
        if (sale.CustomerId is not { } customerId)
        {
            return null;
        }

        var customer = sale.Customer ?? await _customers.GetByIdAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        // The current balance only describes this invoice right after checkout; a reprint days later would mislead.
        return new ReceiptMember(
            customer.Name,
            MaskPhone(customer.Phone),
            sale.PointsEarned,
            sale.PointsRedeemed,
            reprint ? null : customer.LoyaltyPoints);
    }

    /// <summary>"0901234567" → "0901***567": enough for the customer to recognise, useless to anyone who finds the slip.</summary>
    public static string? MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = phone.Trim();
        return digits.Length < 7 ? digits : string.Concat(digits.AsSpan(0, 4), "***", digits.AsSpan(digits.Length - 3));
    }

    private static string? DiscountLabel(Discount discount) => discount.Kind switch
    {
        _ when discount.IsNone => null,
        DiscountKind.Percent => $"Giảm {discount.Value.ToString("0.##", Formatting.Vnd.Culture)}%",
        _ => "Giảm giá",
    };
}
