using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Catalog;

public interface IProductService
{
    /// <summary>Every product (active and inactive), with category name and current stock. Requires <c>products.view</c>.</summary>
    Task<Result<IReadOnlyList<ProductListItem>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Requires <c>products.view</c>.</summary>
    Task<Result<ProductDetail>> GetAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>products.manage</c>.</summary>
    Task<Result<ProductDetail>> CreateAsync(SaveProductRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requires <c>products.manage</c>; changing <see cref="SaveProductRequest.CostPrice"/>/<see cref="SaveProductRequest.SellPrice"/>
    /// also requires <c>prices.change</c>. A real price change is recorded to the price-history ledger.
    /// </summary>
    Task<Result<ProductDetail>> UpdateAsync(int productId, SaveProductRequest request, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>products.manage</c>.</summary>
    Task<Result> SetActiveAsync(int productId, bool isActive, CancellationToken cancellationToken = default);
}

public sealed class ProductService : IProductService
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly IPriceHistoryRepository _priceHistory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public ProductService(
        IProductRepository products,
        ICategoryRepository categories,
        IPriceHistoryRepository priceHistory,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IAuditService audit,
        TimeProvider clock)
    {
        _products = products;
        _categories = categories;
        _priceHistory = priceHistory;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ProductListItem>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.ProductsView) is { } denied)
        {
            return denied;
        }

        var products = await _products.ListAsync(cancellationToken);
        IReadOnlyList<ProductListItem> items = products.Select(ToListItem).ToList();
        return Result.Success(items);
    }

    public async Task<Result<ProductDetail>> GetAsync(int productId, CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.ProductsView) is { } denied)
        {
            return denied;
        }

        var product = await _products.GetByIdAsync(productId, cancellationToken);
        return product is null ? ProductErrors.NotFound(productId) : ToDetail(product);
    }

    public async Task<Result<ProductDetail>> CreateAsync(SaveProductRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize(Permissions.ProductsManage, "Bạn không có quyền thêm sản phẩm.") is { } denied)
        {
            return denied;
        }

        var category = await _categories.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category is null)
        {
            return ProductErrors.CategoryNotFound;
        }

        if (await _products.GetByCodeAsync(request.Code, cancellationToken) is not null)
        {
            return ProductErrors.DuplicateCode(request.Code);
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode) && await _products.GetByBarcodeAsync(request.Barcode, cancellationToken) is not null)
        {
            return ProductErrors.DuplicateBarcode(request.Barcode);
        }

        Product product;
        try
        {
            product = new Product(request.Code, request.Name, request.CategoryId, request.Unit, request.CostPrice, request.SellPrice, request.VatRate);
            product.SetBarcode(request.Barcode);
            product.SetMinStockLevel(request.MinStockLevel);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _products.Add(product);
        _audit.Record(AuditActions.ProductCreated, nameof(Product), product.Code, product.Name);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDetail(product, category.Name);
    }

    public async Task<Result<ProductDetail>> UpdateAsync(int productId, SaveProductRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize(Permissions.ProductsManage, "Bạn không có quyền sửa sản phẩm.") is { } denied)
        {
            return denied;
        }

        var product = await _products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound(productId);
        }

        var category = await _categories.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category is null)
        {
            return ProductErrors.CategoryNotFound;
        }

        if (await _products.GetByCodeAsync(request.Code, cancellationToken) is { } byCode && byCode.Id != productId)
        {
            return ProductErrors.DuplicateCode(request.Code);
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode)
            && await _products.GetByBarcodeAsync(request.Barcode, cancellationToken) is { } byBarcode && byBarcode.Id != productId)
        {
            return ProductErrors.DuplicateBarcode(request.Barcode);
        }

        var priceChanged = request.CostPrice != product.CostPrice || request.SellPrice != product.SellPrice;
        if (priceChanged && !_currentUser.HasPermission(Permissions.PricesChange))
        {
            return ProductErrors.PriceChangeNotAllowed;
        }

        try
        {
            product.UpdateInfo(request.Name, request.CategoryId, request.Unit);
            product.SetBarcode(request.Barcode);
            product.ChangeVatRate(request.VatRate);
            product.SetMinStockLevel(request.MinStockLevel);

            if (priceChanged)
            {
                var change = product.ChangePrices(request.CostPrice, request.SellPrice);
                if (PriceHistory.From(product, change, _clock.GetUtcNow(), _currentUser.Session!.UserId, PriceChangeSource.ManualEdit) is { } history)
                {
                    _priceHistory.Add(history);
                    _audit.Record(
                        AuditActions.PriceChanged,
                        nameof(Product),
                        product.Code,
                        $"Giá vốn {change.OldCostPrice:0} → {change.NewCostPrice:0}, giá bán {change.OldSellPrice:0} → {change.NewSellPrice:0}");
                }
            }
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _audit.Record(AuditActions.ProductUpdated, nameof(Product), product.Code, product.Name);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDetail(product, category.Name);
    }

    public async Task<Result> SetActiveAsync(int productId, bool isActive, CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.ProductsManage, "Bạn không có quyền sửa sản phẩm.") is { } denied)
        {
            return denied;
        }

        var product = await _products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound(productId);
        }

        if (isActive)
        {
            product.Activate();
        }
        else
        {
            product.Deactivate();
        }

        _audit.Record(AuditActions.ProductStatusChanged, nameof(Product), product.Code, isActive ? "Bán lại" : "Ngừng bán");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static ProductListItem ToListItem(Product product) => new(
        product.Id, product.Code, product.Barcode, product.Name, product.CategoryId, product.Category?.Name ?? string.Empty,
        product.Unit, product.CostPrice, product.SellPrice, product.VatRate, product.StockQuantity, product.MinStockLevel, product.IsActive);

    private static ProductDetail ToDetail(Product product, string? categoryName = null) => new(
        product.Id, product.Code, product.Barcode, product.Name, product.CategoryId, categoryName ?? product.Category?.Name ?? string.Empty,
        product.Unit, product.CostPrice, product.SellPrice, product.VatRate, product.StockQuantity, product.MinStockLevel, product.IsActive);

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
