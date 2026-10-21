using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Catalog;

public interface ICategoryService
{
    /// <summary>All categories (active and inactive). Requires <c>products.view</c>.</summary>
    Task<Result<IReadOnlyList<CategoryDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Requires <c>categories.manage</c>.</summary>
    Task<Result<CategoryDto>> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>categories.manage</c>.</summary>
    Task<Result<CategoryDto>> UpdateAsync(int categoryId, SaveCategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>categories.manage</c>. No hard delete: a category with products can never be removed.</summary>
    Task<Result> DeactivateAsync(int categoryId, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>categories.manage</c>.</summary>
    Task<Result> ActivateAsync(int categoryId, CancellationToken cancellationToken = default);
}

public sealed class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;

    public CategoryService(
        ICategoryRepository categories,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IAuditService audit)
    {
        _categories = categories;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<IReadOnlyList<CategoryDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.ProductsView) is { } denied)
        {
            return denied;
        }

        var categories = await _categories.ListAsync(cancellationToken);
        IReadOnlyList<CategoryDto> items = categories.Select(ToDto).ToList();
        return Result.Success(items);
    }

    public async Task<Result<CategoryDto>> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize(Permissions.CategoriesManage, "Bạn không có quyền quản lý danh mục.") is { } denied)
        {
            return denied;
        }

        if (await NameExistsAsync(request.Name, excludeId: null, cancellationToken))
        {
            return CategoryErrors.DuplicateName;
        }

        Category category;
        try
        {
            category = new Category(request.Name, request.Description);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _categories.Add(category);
        _audit.Record(AuditActions.CategoryCreated, nameof(Category), category.Id.ToString(), category.Name);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(category);
    }

    public async Task<Result<CategoryDto>> UpdateAsync(int categoryId, SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize(Permissions.CategoriesManage, "Bạn không có quyền quản lý danh mục.") is { } denied)
        {
            return denied;
        }

        var category = await _categories.GetByIdAsync(categoryId, cancellationToken);
        if (category is null)
        {
            return CategoryErrors.NotFound(categoryId);
        }

        if (await NameExistsAsync(request.Name, excludeId: categoryId, cancellationToken))
        {
            return CategoryErrors.DuplicateName;
        }

        try
        {
            category.Update(request.Name, request.Description);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _audit.Record(AuditActions.CategoryUpdated, nameof(Category), category.Id.ToString(), category.Name);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(category);
    }

    public Task<Result> DeactivateAsync(int categoryId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(categoryId, isActive: false, cancellationToken);

    public Task<Result> ActivateAsync(int categoryId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(categoryId, isActive: true, cancellationToken);

    private async Task<Result> SetActiveAsync(int categoryId, bool isActive, CancellationToken cancellationToken)
    {
        if (Authorize(Permissions.CategoriesManage, "Bạn không có quyền quản lý danh mục.") is { } denied)
        {
            return denied;
        }

        var category = await _categories.GetByIdAsync(categoryId, cancellationToken);
        if (category is null)
        {
            return CategoryErrors.NotFound(categoryId);
        }

        if (isActive)
        {
            category.Activate();
        }
        else
        {
            category.Deactivate();
        }

        _audit.Record(AuditActions.CategoryStatusChanged, nameof(Category), category.Id.ToString(), isActive ? "Kích hoạt" : "Ngừng dùng");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        var categories = await _categories.ListAsync(cancellationToken);
        return categories.Any(c => c.Id != excludeId && string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static CategoryDto ToDto(Category category) => new(category.Id, category.Name, category.Description, category.IsActive);

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
}
