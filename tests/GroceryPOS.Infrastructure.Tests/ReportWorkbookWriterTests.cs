using ClosedXML.Excel;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.Infrastructure.Reports;

namespace GroceryPOS.Infrastructure.Tests;

public sealed class ReportWorkbookWriterTests
{
    private static ReportSheet Sheet(string name) => new(
        name,
        "BÁO CÁO DOANH THU",
        ["Tạp hóa Cô Ba", "Ngày: 02/11/2026"],
        [
            new ReportColumn("Ngày", ReportColumnKind.Date),
            new ReportColumn("Số HĐ", ReportColumnKind.Integer, 9),
            new ReportColumn("Số lượng", ReportColumnKind.Quantity, 10),
            new ReportColumn("Doanh thu", ReportColumnKind.Money, 15),
            new ReportColumn("Tỷ suất LN", ReportColumnKind.Percent, 10),
        ],
        [
            new object?[] { new DateOnly(2026, 11, 1), 4, 1.5m, 110_000m, 18.2m },
            new object?[] { new DateOnly(2026, 11, 2), 6, 2m, 220_000m, 20m },
        ],
        ["Tổng cộng", 10, 3.5m, 330_000m, 19.4m]);

    [Fact]
    public async Task Writes_typed_cells_with_title_header_and_totals()
    {
        var bytes = await new ClosedXmlReportWorkbookWriter().WriteAsync([Sheet("Doanh thu")]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheet("Doanh thu");
        Assert.Equal("BÁO CÁO DOANH THU", ws.Cell(1, 1).GetString());
        Assert.Equal("Tạp hóa Cô Ba", ws.Cell(2, 1).GetString());

        // Title, 2 subtitles, blank line → header on row 5.
        Assert.Equal("Ngày", ws.Cell(5, 1).GetString());
        Assert.Equal("Doanh thu", ws.Cell(5, 4).GetString());

        Assert.Equal(new DateTime(2026, 11, 1), ws.Cell(6, 1).GetDateTime());
        Assert.Equal(110_000d, ws.Cell(6, 4).GetDouble());
        Assert.Contains("₫", ws.Cell(6, 4).Style.NumberFormat.Format);
        Assert.Equal(0.182d, ws.Cell(6, 5).GetDouble(), 6);
        Assert.Equal("0.0%", ws.Cell(6, 5).Style.NumberFormat.Format);
        Assert.Equal(1.5d, ws.Cell(6, 3).GetDouble());

        Assert.Equal("Tổng cộng", ws.Cell(8, 1).GetString());
        Assert.True(ws.Cell(8, 4).Style.Font.Bold);
        Assert.Equal(330_000d, ws.Cell(8, 4).GetDouble());
        Assert.Equal(5, ws.SheetView.SplitRow);
    }

    [Fact]
    public async Task Duplicate_sheet_names_get_a_suffix()
    {
        var bytes = await new ClosedXmlReportWorkbookWriter().WriteAsync([Sheet("Doanh thu"), Sheet("Doanh thu")]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["Doanh thu", "Doanh thu (2)"], workbook.Worksheets.Select(w => w.Name));
    }

    [Fact]
    public async Task Refuses_an_empty_export()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new ClosedXmlReportWorkbookWriter().WriteAsync([]));
    }
}
