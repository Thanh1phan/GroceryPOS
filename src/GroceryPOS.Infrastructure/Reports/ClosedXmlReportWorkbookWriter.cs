using ClosedXML.Excel;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Reports.Export;

namespace GroceryPOS.Infrastructure.Reports;

/// <summary>
/// Writes report sheets to .xlsx: bold title, subtitle lines, a frozen header row with filter buttons, typed
/// cells (numbers stay numbers so the shop owner can keep calculating), VND/percent/date formats and a bold
/// totals row. Print setup: landscape, fit to one page wide, header row repeated on every page.
/// </summary>
internal sealed class ClosedXmlReportWorkbookWriter : IReportWorkbookWriter
{
    private const string MoneyFormat = "#,##0 \"₫\";-#,##0 \"₫\";0 \"₫\"";
    private const string IntegerFormat = "#,##0";
    private const string QuantityFormat = "#,##0.###";
    private const string PercentFormat = "0.0%";
    private const string DateFormat = "dd/mm/yyyy";

    public string FileExtension => ".xlsx";

    public Task<byte[]> WriteAsync(IReadOnlyList<ReportSheet> sheets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        if (sheets.Count == 0)
        {
            throw new ArgumentException("Không có báo cáo nào để xuất.", nameof(sheets));
        }

        using var workbook = new XLWorkbook();
        foreach (var sheet in sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(workbook.Worksheets.Add(UniqueName(workbook, sheet.Name)), sheet);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Task.FromResult(stream.ToArray());
    }

    private static string UniqueName(XLWorkbook workbook, string name)
    {
        var candidate = name;
        for (var i = 2; workbook.Worksheets.Contains(candidate); i++)
        {
            var suffix = $" ({i})";
            candidate = name[..Math.Min(name.Length, ReportSheet.MaxNameLength - suffix.Length)] + suffix;
        }

        return candidate;
    }

    private static void Write(IXLWorksheet ws, ReportSheet sheet)
    {
        var columnCount = sheet.Columns.Count;
        var row = 1;

        var title = ws.Cell(row, 1);
        title.Value = sheet.Title;
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;
        row++;

        foreach (var line in sheet.Subtitles)
        {
            ws.Cell(row, 1).Value = line;
            ws.Cell(row, 1).Style.Font.Italic = true;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#4B5563");
            row++;
        }

        row++; // blank line before the table
        var headerRow = row;
        for (var c = 0; c < columnCount; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = sheet.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E5E7EB");
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.Horizontal = sheet.Columns[c].Kind == ReportColumnKind.Text
                ? XLAlignmentHorizontalValues.Left
                : XLAlignmentHorizontalValues.Right;
        }

        foreach (var values in sheet.Rows)
        {
            row++;
            WriteRow(ws, row, sheet.Columns, values);
        }

        var lastDataRow = row;
        if (sheet.Rows.Count > 0)
        {
            ws.Range(headerRow, 1, lastDataRow, columnCount).SetAutoFilter();
        }

        if (sheet.Totals is { } totals)
        {
            row++;
            WriteRow(ws, row, sheet.Columns, totals);
            var range = ws.Range(row, 1, row, columnCount);
            range.Style.Font.Bold = true;
            range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            range.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
        }

        ws.Range(headerRow, 1, row, columnCount).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.SheetView.FreezeRows(headerRow);

        for (var c = 0; c < columnCount; c++)
        {
            var width = sheet.Columns[c].Width;
            if (width > 0)
            {
                ws.Column(c + 1).Width = width;
            }
            else
            {
                ws.Column(c + 1).AdjustToContents(headerRow, row);
            }
        }

        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);
        ws.PageSetup.Footer.Right.AddText("Trang ");
        ws.PageSetup.Footer.Right.AddText(XLHFPredefinedText.PageNumber);
        ws.PageSetup.Footer.Right.AddText("/");
        ws.PageSetup.Footer.Right.AddText(XLHFPredefinedText.NumberOfPages);
    }

    private static void WriteRow(IXLWorksheet ws, int row, IReadOnlyList<ReportColumn> columns, IReadOnlyList<object?> values)
    {
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = ws.Cell(row, c + 1);
            var value = values[c];
            if (value is null)
            {
                continue;
            }

            // A text value in a numeric column (e.g. "Tổng cộng" in the date column) is written as text.
            if (value is string text)
            {
                cell.Value = text;
                continue;
            }

            switch (columns[c].Kind)
            {
                case ReportColumnKind.Money:
                    cell.Value = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
                    cell.Style.NumberFormat.Format = MoneyFormat;
                    break;
                case ReportColumnKind.Integer:
                    cell.Value = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
                    cell.Style.NumberFormat.Format = IntegerFormat;
                    break;
                case ReportColumnKind.Quantity:
                    cell.Value = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
                    cell.Style.NumberFormat.Format = QuantityFormat;
                    break;
                case ReportColumnKind.Percent:
                    cell.Value = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) / 100m;
                    cell.Style.NumberFormat.Format = PercentFormat;
                    break;
                case ReportColumnKind.Date:
                    cell.Value = value switch
                    {
                        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                        DateTime dt => dt,
                        _ => Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture),
                    };
                    cell.Style.NumberFormat.Format = DateFormat;
                    break;
                default:
                    cell.Value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }
        }
    }
}
