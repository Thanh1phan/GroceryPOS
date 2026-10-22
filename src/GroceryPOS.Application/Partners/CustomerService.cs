using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Application.Partners;

public interface ICustomerService
{
    /// <summary>All customers (active and inactive), by name. Requires <c>customers.view</c>.</summary>
    Task<Result<IReadOnlyList<CustomerDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Requires <c>customers.view</c>.</summary>
    Task<Result<CustomerDto>> GetAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>customers.manage</c>. Gets the next <c>KH000001</c> code; the phone must not belong to another customer.</summary>
    Task<Result<CustomerDto>> CreateAsync(SaveCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>customers.manage</c>. Points and total spent only change through sales, voids and returns.</summary>
    Task<Result<CustomerDto>> UpdateAsync(int customerId, SaveCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>customers.manage</c>. The points are kept; an inactive member is not found at the counter.</summary>
    Task<Result> DeactivateAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>customers.manage</c>.</summary>
    Task<Result> ActivateAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loyalty history (purchases, voids, returns), newest first, with the balance after each entry replayed from the
    /// documents; points no document explains appear as an opening entry. Requires <c>customers.view</c>.
    /// </summary>
    Task<Result<CustomerLoyaltyHistoryDto>> GetLoyaltyHistoryAsync(int customerId, CancellationToken cancellationToken = default);
}

public sealed class CustomerService : ICustomerService
{
    public const string CodePrefix = "KH";
    public const int CodeWidth = 6;

    private readonly ICustomerRepository _customers;
    private readonly ISaleRepository _sales;
    private readonly ISaleReturnRepository _returns;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;

    public CustomerService(
        ICustomerRepository customers,
        ISaleRepository sales,
        ISaleReturnRepository returns,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IAuditService audit)
    {
        _customers = customers;
        _sales = sales;
        _returns = returns;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<IReadOnlyList<CustomerDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.CustomersView, "Bạn không có quyền xem khách hàng.") is { } denied)
        {
            return denied;
        }

        var customers = await _customers.ListAsync(cancellationToken);
        IReadOnlyList<CustomerDto> items = customers.OrderBy(c => c.Name, PartnerSort.Names).Select(ToDto).ToList();
        return Result.Success(items);
    }

    public async Task<Result<CustomerDto>> GetAsync(int customerId, CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.CustomersView, "Bạn không có quyền xem khách hàng.") is { } denied)
        {
            return denied;
        }

        var customer = await _customers.GetByIdAsync(customerId, cancellationToken);
        return customer is null ? PartnerErrors.CustomerNotFound(customerId) : ToDto(customer);
    }

    public async Task<Result<CustomerDto>> CreateAsync(SaveCustomerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize(Permissions.CustomersManage, "Bạn không có quyền quản lý khách hàng.") is { } denied)
        {
            return denied;
        }

        var code = SequentialCode.Next(CodePrefix, CodeWidth, await _customers.ListCodesWithPrefixAsync(CodePrefix, cancellationToken));
        Customer customer;
        try
        {
            customer = new Customer(code, request.Name);
            customer.Update(request.Name, request.Phone, request.Email, request.Address);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        if (await PhoneTakenAsync(customer.Phone, excludeId: null, cancellationToken))
        {
            return PartnerErrors.DuplicateCustomerPhone;
        }

        _customers.Add(customer);
        _audit.Record(AuditActions.CustomerCreated, nameof(Customer), customer.Code, customer.Name);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(customer);
    }

    public async Task<Result<CustomerDto>> UpdateAsync(int customerId, SaveCustomerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize(Permissions.CustomersManage, "Bạn không có quyền quản lý khách hàng.") is { } denied)
        {
            return denied;
        }

        var customer = await _customers.GetByIdAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return PartnerErrors.CustomerNotFound(customerId);
        }

        string? phone;
        try
        {
            phone = PhoneNumber.NormalizeOptional(request.Phone);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        if (await PhoneTakenAsync(phone, excludeId: customer.Id, cancellationToken))
        {
            return PartnerErrors.DuplicateCustomerPhone;
        }

        var before = Describe(customer);
        try
        {
            customer.Update(request.Name, request.Phone, request.Email, request.Address);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        var after = Describe(customer);
        if (before != after)
        {
            _audit.Record(AuditActions.CustomerUpdated, nameof(Customer), customer.Code, $"{before} → {after}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(customer);
    }

    public Task<Result> DeactivateAsync(int customerId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(customerId, isActive: false, cancellationToken);

    public Task<Result> ActivateAsync(int customerId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(customerId, isActive: true, cancellationToken);

    public async Task<Result<CustomerLoyaltyHistoryDto>> GetLoyaltyHistoryAsync(int customerId, CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.CustomersView, "Bạn không có quyền xem khách hàng.") is { } denied)
        {
            return denied;
        }

        var customer = await _customers.GetByIdAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return PartnerErrors.CustomerNotFound(customerId);
        }

        var sales = await _sales.ListByCustomerAsync(customerId, cancellationToken);
        var returns = await _returns.ListByCustomerAsync(customerId, cancellationToken);
        var saleCodes = sales.ToDictionary(s => s.Id, s => s.Code);

        // Each document with the balance rule the domain applied to it (Customer.RecordPurchase / RedeemPoints / ReverseSale):
        // reversals never take the balance below zero, so the history is replayed forwards rather than subtracted backwards.
        var raw = new List<(DateTimeOffset Date, int Order, LoyaltyEntryDto Entry, Func<int, int> Apply)>();
        foreach (var sale in sales.Where(s => s.CompletedAt is not null))
        {
            var description = sale.PointsRedeemed > 0 ? $"Mua hàng, đổi {sale.PointsRedeemed:N0} điểm" : "Mua hàng";
            var earned = sale.PointsEarned;
            var redeemed = sale.PointsRedeemed;
            raw.Add((sale.CompletedAt!.Value, 0, new LoyaltyEntryDto(
                    sale.CompletedAt.Value, LoyaltyEntryKind.Purchase, sale.Code, sale.Total, earned, redeemed, 0, 0, description),
                b => Math.Max(0, b - redeemed) + earned));

            if (sale.VoidedAt is { } voidedAt)
            {
                raw.Add((voidedAt, 2, new LoyaltyEntryDto(
                        voidedAt, LoyaltyEntryKind.Void, sale.Code, -sale.Total, 0, 0, 0, 0,
                        string.IsNullOrWhiteSpace(sale.VoidReason) ? "Hủy hóa đơn" : $"Hủy hóa đơn: {sale.VoidReason}"),
                    b => Math.Max(0, b - earned) + redeemed));
            }
        }

        foreach (var saleReturn in returns)
        {
            var invoice = saleCodes.TryGetValue(saleReturn.SaleId, out var code) ? code : $"#{saleReturn.SaleId}";
            var reversed = saleReturn.PointsReversed;
            var restored = saleReturn.PointsRestored;
            raw.Add((saleReturn.ReturnedAt, 1, new LoyaltyEntryDto(
                    saleReturn.ReturnedAt, LoyaltyEntryKind.Return, saleReturn.Code, -saleReturn.RefundTotal, 0, 0, 0, 0,
                    $"Trả hàng (HĐ {invoice})"),
                b => Math.Max(0, b - reversed) + restored));
        }

        var chronological = raw.OrderBy(r => r.Date).ThenBy(r => r.Order).ToList();
        int Replay(int opening) => chronological.Aggregate(opening, (balance, r) => r.Apply(balance));

        // Points that no document explains (e.g. imported from an old system) show up as an opening balance.
        var opening = Math.Max(0, customer.LoyaltyPoints - Replay(0));
        var entries = new List<LoyaltyEntryDto>(chronological.Count + 1);
        var running = opening;
        if (opening > 0)
        {
            var openingDate = chronological.Count > 0 ? chronological[0].Date : customer.CreatedAt;
            entries.Add(new LoyaltyEntryDto(
                openingDate, LoyaltyEntryKind.Opening, customer.Code, 0, 0, 0, opening, opening, "Điểm đầu kỳ (ngoài chứng từ)"));
        }

        foreach (var (_, _, entry, apply) in chronological)
        {
            var after = apply(running);
            entries.Add(entry with { PointsChange = after - running, BalanceAfter = after });
            running = after;
        }

        entries.Reverse(); // newest first

        var invoiceCount = sales.Count(s => s.CompletedAt is not null && s.VoidedAt is null);
        return new CustomerLoyaltyHistoryDto(ToDto(customer), invoiceCount, entries);
    }

    private async Task<Result> SetActiveAsync(int customerId, bool isActive, CancellationToken cancellationToken)
    {
        if (Authorize(Permissions.CustomersManage, "Bạn không có quyền quản lý khách hàng.") is { } denied)
        {
            return denied;
        }

        var customer = await _customers.GetByIdAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return PartnerErrors.CustomerNotFound(customerId);
        }

        if (customer.IsActive == isActive)
        {
            return Result.Success();
        }

        if (isActive)
        {
            customer.Activate();
        }
        else
        {
            customer.Deactivate();
        }

        _audit.Record(AuditActions.CustomerStatusChanged, nameof(Customer), customer.Code, isActive ? "Kích hoạt" : "Ngừng hoạt động");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<bool> PhoneTakenAsync(string? phone, int? excludeId, CancellationToken cancellationToken)
    {
        if (phone is null)
        {
            return false;
        }

        var owner = await _customers.GetByPhoneAsync(phone, cancellationToken);
        return owner is not null && owner.Id != excludeId;
    }

    private static string Describe(Customer c) =>
        string.Join(" | ", new[] { c.Name, c.Phone, c.Email, c.Address }.Select(v => v ?? "-"));

    internal static CustomerDto ToDto(Customer c) =>
        new(c.Id, c.Code, c.Name, c.Phone, c.Email, c.Address, c.LoyaltyPoints, c.TotalSpent, c.IsActive);

    private Error? Authorize(string permission, string deniedMessage)
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(permission) ? null : Error.Forbidden(deniedMessage);
    }
}
