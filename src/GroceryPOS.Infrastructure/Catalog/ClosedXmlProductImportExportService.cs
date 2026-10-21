using System.Globalization;
using ClosedXML.Excel;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;

namespace GroceryPOS.Infrastructure.Catalog;

/// <summary>
/// Dumb Excel row transcoding for the product catalog: no business rules, just reading/writing cells.
/// The "Tồn kho" (stock) column is written on export for reference only and is never read back on
/// import — <see cref="ProductImportRow"/> has no stock field, so there is nothing to map it to.
/// </summary>
internal sealed class ClosedXmlProductImportExportService : IProductImportExportService
{
    private static readonly string[] Headers =
    {
        "Mã", "Tên", "Mã vạch", "Danh mục", "Đơn vị", "Giá vốn", "Giá bán", "Thuế VAT (%)", "Tồn kho", "Mức tồn tối thiểu", "Đang bán",
    };

    public Task<byte[]> ExportAsync(IReadOnlyList<ProductListItem> products, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(products);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sản phẩm");
        for (var i = 0; i < Headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = Headers[i];
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        var row = 2;
        foreach (var product in products)
        {
            sheet.Cell(row, 1).Value = product.Code;
            sheet.Cell(row, 2).Value = product.Name;
            sheet.Cell(row, 3).Value = product.Barcode ?? string.Empty;
            sheet.Cell(row, 4).Value = product.CategoryName;
            sheet.Cell(row, 5).Value = product.Unit;
            sheet.Cell(row, 6).Value = product.CostPrice;
            sheet.Cell(row, 7).Value = product.SellPrice;
            sheet.Cell(row, 8).Value = product.VatRate;
            sheet.Cell(row, 9).Value = product.StockQuantity;
            sheet.Cell(row, 10).Value = product.MinStockLevel;
            sheet.Cell(row, 11).Value = product.IsActive;
            row++;
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Task.FromResult(stream.ToArray());
    }

    public Task<IReadOnlyList<ProductImportRow>> ParseAsync(Stream xlsxFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xlsxFile);

        using var workbook = new XLWorkbook(xlsxFile);
        var sheet = workbook.Worksheets.First();

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in sheet.Row(1).CellsUsed())
        {
            var text = cell.GetString().Trim();
            if (text.Length > 0)
            {
                columns[text] = cell.Address.ColumnNumber;
            }
        }

        int RequiredColumn(string header) => columns.TryGetValue(header, out var index)
            ? index
            : throw new InvalidOperationException($"Thiếu cột \"{header}\" trong file nhập.");

        var codeColumn = RequiredColumn("Mã");
        var nameColumn = RequiredColumn("Tên");
        var categoryColumn = RequiredColumn("Danh mục");
        var unitColumn = RequiredColumn("Đơn vị");
        var costColumn = RequiredColumn("Giá vốn");
        var sellColumn = RequiredColumn("Giá bán");
        var vatColumn = RequiredColumn("Thuế VAT (%)");
        var barcodeColumn = columns.GetValueOrDefault("Mã vạch");
        var minStockColumn = columns.GetValueOrDefault("Mức tồn tối thiểu");
        var activeColumn = columns.GetValueOrDefault("Đang bán");

        var rows = new List<ProductImportRow>();
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var code = row.Cell(codeColumn).GetString().Trim();
            if (code.Length == 0)
            {
                continue;
            }

            var barcode = barcodeColumn > 0 ? row.Cell(barcodeColumn).GetString().Trim() : string.Empty;
            rows.Add(new ProductImportRow(
                row.RowNumber(),
                code,
                barcode.Length == 0 ? null : barcode,
                row.Cell(nameColumn).GetString().Trim(),
                row.Cell(categoryColumn).GetString().Trim(),
                row.Cell(unitColumn).GetString().Trim(),
                ReadDecimal(row.Cell(costColumn)),
                ReadDecimal(row.Cell(sellColumn)),
                ReadDecimal(row.Cell(vatColumn)),
                minStockColumn > 0 ? ReadDecimal(row.Cell(minStockColumn)) : 0,
                activeColumn == 0 || ReadBool(row.Cell(activeColumn))));
        }

        return Task.FromResult<IReadOnlyList<ProductImportRow>>(rows);
    }

    private static decimal ReadDecimal(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return 0;
        }

        if (cell.TryGetValue(out decimal value))
        {
            return value;
        }

        var text = cell.GetString().Trim();
        return text.Length == 0 ? 0 : decimal.Parse(text, CultureInfo.InvariantCulture);
    }

    private static bool ReadBool(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return true;
        }

        if (cell.TryGetValue(out bool value))
        {
            return value;
        }

        var text = cell.GetString().Trim();
        return text.Length == 0 || text is "Có" or "có" or "TRUE" or "true" or "1" or "x" or "X";
    }
}
