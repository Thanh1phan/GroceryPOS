using GroceryPOS.Application.Catalog;

namespace GroceryPOS.Application.Abstractions;

/// <summary>
/// Dumb Excel row transcoding for the product catalog — no business rules (those live in
/// <see cref="Catalog.IProductImportExportAppService"/>). Implemented in Infrastructure with ClosedXML so
/// Application stays free of document-format dependencies.
/// </summary>
public interface IProductImportExportService
{
    Task<byte[]> ExportAsync(IReadOnlyList<ProductListItem> products, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductImportRow>> ParseAsync(Stream xlsxFile, CancellationToken cancellationToken = default);
}

/// <summary>
/// One parsed row of a product import sheet. Deliberately has no stock-quantity field: stock can never be
/// set via import, matching <see cref="SaveProductRequest"/>.
/// </summary>
public sealed record ProductImportRow(
    int RowNumber,
    string Code,
    string? Barcode,
    string Name,
    string CategoryName,
    string Unit,
    decimal CostPrice,
    decimal SellPrice,
    decimal VatRate,
    decimal MinStockLevel,
    bool IsActive);
