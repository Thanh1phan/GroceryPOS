using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Catalog;

public interface IProductImportExportAppService
{
    /// <summary>Exports every product (as shown on the product screen) to an .xlsx workbook. Requires <c>products.manage</c>.</summary>
    Task<Result<byte[]>> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts products from an .xlsx workbook, matched by code. Requires <c>products.manage</c>; if any matched
    /// row would change an existing product's cost/sell price, <c>prices.change</c> is also required for the
    /// whole file — rejected up front, before anything is written, when missing. Unknown categories fail that
    /// row rather than being created on the fly. Never silently drops a row: every failure is reported.
    /// </summary>
    Task<Result<ImportSummary>> ImportAsync(Stream xlsxFile, CancellationToken cancellationToken = default);
}

public sealed class ProductImportExportService : IProductImportExportAppService
{
    private readonly IProductService _products;
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categories;
    private readonly IProductImportExportService _transcoder;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;

    public ProductImportExportService(
        IProductService products,
        IProductRepository productRepository,
        ICategoryRepository categories,
        IProductImportExportService transcoder,
        ICurrentUser currentUser,
        IAuditService audit)
    {
        _products = products;
        _productRepository = productRepository;
        _categories = categories;
        _transcoder = transcoder;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<byte[]>> ExportAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var products = await _products.ListAsync(cancellationToken);
        if (products.IsFailure)
        {
            return products.Error;
        }

        var bytes = await _transcoder.ExportAsync(products.Value, cancellationToken);
        _audit.Record(AuditActions.ProductExported, details: $"{products.Value.Count} sản phẩm");
        return bytes;
    }

    public async Task<Result<ImportSummary>> ImportAsync(Stream xlsxFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xlsxFile);
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var rows = await _transcoder.ParseAsync(xlsxFile, cancellationToken);
        var existingByCode = (await _productRepository.ListAsync(cancellationToken))
            .ToDictionary(p => p.Code, p => p, StringComparer.OrdinalIgnoreCase);

        var changesPrice = rows.Any(r =>
            existingByCode.TryGetValue(r.Code.Trim(), out var existing) &&
            (r.CostPrice != existing.CostPrice || r.SellPrice != existing.SellPrice));
        if (changesPrice && !_currentUser.HasPermission(Permissions.PricesChange))
        {
            return ProductErrors.PriceChangeNotAllowed;
        }

        var categoriesByName = (await _categories.ListAsync(cancellationToken))
            .ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        var results = new List<ImportRowResult>();
        var created = 0;
        var updated = 0;
        foreach (var row in rows)
        {
            var result = await ImportRowAsync(row, existingByCode, categoriesByName, cancellationToken);
            results.Add(result);
            if (result.Outcome == ImportRowOutcome.Created)
            {
                created++;
            }
            else if (result.Outcome == ImportRowOutcome.Updated)
            {
                updated++;
            }
        }

        var failed = results.Count(r => r.Outcome == ImportRowOutcome.Failed);
        _audit.Record(AuditActions.ProductImported, details: $"Tạo mới {created}, cập nhật {updated}, lỗi {failed}");
        return new ImportSummary(created, updated, failed, results);
    }

    private async Task<ImportRowResult> ImportRowAsync(
        ProductImportRow row,
        IReadOnlyDictionary<string, Domain.Catalog.Product> existingByCode,
        IReadOnlyDictionary<string, Domain.Catalog.Category> categoriesByName,
        CancellationToken cancellationToken)
    {
        if (!categoriesByName.TryGetValue(row.CategoryName.Trim(), out var category))
        {
            return new ImportRowResult(row.RowNumber, row.Code, ImportRowOutcome.Failed, $"Danh mục \"{row.CategoryName}\" không tồn tại, vui lòng tạo danh mục trước.");
        }

        var request = new SaveProductRequest(row.Code, row.Barcode, row.Name, category.Id, row.Unit, row.CostPrice, row.SellPrice, row.VatRate, row.MinStockLevel);
        var existing = existingByCode.GetValueOrDefault(row.Code.Trim());

        var result = existing is null
            ? await _products.CreateAsync(request, cancellationToken)
            : await _products.UpdateAsync(existing.Id, request, cancellationToken);

        if (result.IsFailure)
        {
            return new ImportRowResult(row.RowNumber, row.Code, ImportRowOutcome.Failed, result.Error.Message);
        }

        if (result.Value.IsActive != row.IsActive)
        {
            await _products.SetActiveAsync(result.Value.Id, row.IsActive, cancellationToken);
        }

        return new ImportRowResult(row.RowNumber, row.Code, existing is null ? ImportRowOutcome.Created : ImportRowOutcome.Updated, null);
    }

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.ProductsManage)
            ? null
            : Error.Forbidden("Bạn không có quyền nhập/xuất dữ liệu sản phẩm.");
    }
}
