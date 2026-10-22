using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Application.Partners;

public interface ISupplierService
{
    /// <summary>
    /// All suppliers (active and inactive), by name. Requires <c>suppliers.manage</c>, <c>purchases.manage</c>
    /// or <c>suppliers.pay</c> (goods receipts and payments need the list to pick from).
    /// </summary>
    Task<Result<IReadOnlyList<SupplierDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Same permissions as <see cref="ListAsync"/>.</summary>
    Task<Result<SupplierDto>> GetAsync(int supplierId, CancellationToken cancellationToken = default);

    /// <summary>Next free automatic code (<c>NCC0001</c>) to pre-fill the add dialog. Requires <c>suppliers.manage</c>.</summary>
    Task<Result<string>> SuggestCodeAsync(CancellationToken cancellationToken = default);

    /// <summary>Requires <c>suppliers.manage</c>. An empty code gets the next <c>NCC0001</c> code.</summary>
    Task<Result<SupplierDto>> CreateAsync(SaveSupplierRequest request, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>suppliers.manage</c>. The code never changes (it is printed on receipts and payments).</summary>
    Task<Result<SupplierDto>> UpdateAsync(int supplierId, SaveSupplierRequest request, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>suppliers.manage</c>. Refused while the store still owes the supplier money.</summary>
    Task<Result> DeactivateAsync(int supplierId, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>suppliers.manage</c>.</summary>
    Task<Result> ActivateAsync(int supplierId, CancellationToken cancellationToken = default);
}

public sealed class SupplierService : ISupplierService
{
    public const string CodePrefix = "NCC";
    public const int CodeWidth = 4;

    private const string DeniedMessage = "Bạn không có quyền quản lý nhà cung cấp.";

    private readonly ISupplierRepository _suppliers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;

    public SupplierService(ISupplierRepository suppliers, IUnitOfWork unitOfWork, ICurrentUser currentUser, IAuditService audit)
    {
        _suppliers = suppliers;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<IReadOnlyList<SupplierDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (AuthorizeRead() is { } denied)
        {
            return denied;
        }

        var suppliers = await _suppliers.ListAsync(cancellationToken);
        IReadOnlyList<SupplierDto> items = suppliers
            .OrderBy(s => s.Name, PartnerSort.Names)
            .Select(ToDto)
            .ToList();
        return Result.Success(items);
    }

    public async Task<Result<SupplierDto>> GetAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        if (AuthorizeRead() is { } denied)
        {
            return denied;
        }

        var supplier = await _suppliers.GetByIdAsync(supplierId, cancellationToken);
        return supplier is null ? PartnerErrors.SupplierNotFound(supplierId) : ToDto(supplier);
    }

    public async Task<Result<string>> SuggestCodeAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.SuppliersManage) is { } denied)
        {
            return denied;
        }

        return await NextCodeAsync(cancellationToken);
    }

    public async Task<Result<SupplierDto>> CreateAsync(SaveSupplierRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize(Permissions.SuppliersManage) is { } denied)
        {
            return denied;
        }

        var code = string.IsNullOrWhiteSpace(request.Code)
            ? await NextCodeAsync(cancellationToken)
            : request.Code.Trim().ToUpperInvariant();
        if (await _suppliers.GetByCodeAsync(code, cancellationToken) is not null)
        {
            return PartnerErrors.DuplicateSupplierCode;
        }

        Supplier supplier;
        try
        {
            supplier = new Supplier(code, request.Name);
            supplier.Update(request.Name, request.ContactPerson, request.Phone, request.Email, request.Address, request.TaxCode);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _suppliers.Add(supplier);
        _audit.Record(AuditActions.SupplierCreated, nameof(Supplier), supplier.Code, supplier.Name);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(supplier);
    }

    public async Task<Result<SupplierDto>> UpdateAsync(int supplierId, SaveSupplierRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize(Permissions.SuppliersManage) is { } denied)
        {
            return denied;
        }

        var supplier = await _suppliers.GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null)
        {
            return PartnerErrors.SupplierNotFound(supplierId);
        }

        var before = Describe(supplier);
        try
        {
            supplier.Update(request.Name, request.ContactPerson, request.Phone, request.Email, request.Address, request.TaxCode);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        var after = Describe(supplier);
        if (before != after)
        {
            _audit.Record(AuditActions.SupplierUpdated, nameof(Supplier), supplier.Code, $"{before} → {after}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(supplier);
    }

    public Task<Result> DeactivateAsync(int supplierId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(supplierId, isActive: false, cancellationToken);

    public Task<Result> ActivateAsync(int supplierId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(supplierId, isActive: true, cancellationToken);

    private async Task<Result> SetActiveAsync(int supplierId, bool isActive, CancellationToken cancellationToken)
    {
        if (Authorize(Permissions.SuppliersManage) is { } denied)
        {
            return denied;
        }

        var supplier = await _suppliers.GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null)
        {
            return PartnerErrors.SupplierNotFound(supplierId);
        }

        if (supplier.IsActive == isActive)
        {
            return Result.Success();
        }

        if (!isActive && supplier.DebtBalance > 0)
        {
            return PartnerErrors.SupplierHasDebt(supplier.DebtBalance);
        }

        if (isActive)
        {
            supplier.Activate();
        }
        else
        {
            supplier.Deactivate();
        }

        _audit.Record(AuditActions.SupplierStatusChanged, nameof(Supplier), supplier.Code, isActive ? "Giao dịch lại" : "Ngừng giao dịch");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<string> NextCodeAsync(CancellationToken cancellationToken) =>
        SequentialCode.Next(CodePrefix, CodeWidth, await _suppliers.ListCodesWithPrefixAsync(CodePrefix, cancellationToken));

    private static string Describe(Supplier s) =>
        string.Join(" | ", new[] { s.Name, s.ContactPerson, s.Phone, s.Email, s.Address, s.TaxCode }.Select(v => v ?? "-"));

    internal static SupplierDto ToDto(Supplier s) =>
        new(s.Id, s.Code, s.Name, s.ContactPerson, s.Phone, s.Email, s.Address, s.TaxCode, s.DebtBalance, s.IsActive);

    private Error? AuthorizeRead()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.SuppliersManage)
               || _currentUser.HasPermission(Permissions.PurchasesManage)
               || _currentUser.HasPermission(Permissions.SupplierPaymentsManage)
            ? null
            : Error.Forbidden("Bạn không có quyền xem nhà cung cấp.");
    }

    private Error? Authorize(string permission)
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(permission) ? null : Error.Forbidden(DeniedMessage);
    }
}

/// <summary>Vietnamese name ordering shared by the partner lists.</summary>
internal static class PartnerSort
{
    public static readonly StringComparer Names = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("vi-VN"), ignoreCase: true);
}
