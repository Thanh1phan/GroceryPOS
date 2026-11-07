using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;

namespace GroceryPOS.Application.Tests;

public sealed class ReportPrintPlanTests
{
    // 1000 × 500 page; title block 100, continuation header 20, footer 20, header row 30, rows 20 at 100 %.
    private static readonly ReportPageMetrics Page = new(
        PrintableWidth: 1000, PrintableHeight: 500, CharWidth: 10, RowHeight: 20, HeaderRowHeight: 30,
        TitleBlockHeight: 100, PageHeaderHeight: 20, FooterHeight: 20, MinScale: 0.5);

    private static ReportSheet Sheet(int rows, bool totals = true, params ReportColumn[] columns)
    {
        if (columns.Length == 0)
        {
            columns = [new("Tên", ReportColumnKind.Text, 40), new("Số lượng", ReportColumnKind.Quantity, 20), new("Tiền", ReportColumnKind.Money, 20)];
        }

        var data = Enumerable.Range(0, rows)
            .Select(i => (IReadOnlyList<object?>)columns.Select((_, c) => (object?)(c == 0 ? $"Dòng {i}" : i)).ToArray())
            .ToList();
        object?[]? sum = totals ? columns.Select((_, c) => (object?)(c == 0 ? ReportSheets.TotalsLabel : 1)).ToArray() : null;
        return new ReportSheet("Thử", "BÁO CÁO THỬ", ["Cửa hàng"], columns, data, sum);
    }

    private static ReportColumn[] WideColumns(int moneyColumns) =>
        [new("Ngày", ReportColumnKind.Date, 10), .. Enumerable.Range(1, moneyColumns).Select(i => new ReportColumn($"C{i}", ReportColumnKind.Money, 15))];

    [Fact]
    public void Narrow_sheet_prints_at_full_size_in_one_band_with_totals_after_the_last_row()
    {
        var plan = ReportPrintPlan.Create(Sheet(30), Page);

        Assert.Equal(1, plan.Scale);
        Assert.Equal(1, plan.BandCount);
        Assert.Empty(plan.KeyColumns);
        Assert.Equal([400d, 200d, 200d], plan.ColumnWidths);

        // First page: (500 − 100 − 20 − 30) / 20 = 17 rows; later pages (500 − 20 − 20 − 30) / 20 = 21 rows.
        Assert.Equal(2, plan.Pages.Count);
        Assert.Equal((0, 17, false, true), (plan.Pages[0].FirstRow, plan.Pages[0].RowCount, plan.Pages[0].ShowTotals, plan.Pages[0].ShowTitleBlock));
        Assert.Equal((17, 13, true, false), (plan.Pages[1].FirstRow, plan.Pages[1].RowCount, plan.Pages[1].ShowTotals, plan.Pages[1].ShowTitleBlock));
        Assert.Null(plan.BandCaption(plan.Pages[0]));
    }

    [Fact]
    public void Totals_row_moves_to_its_own_page_when_the_last_page_is_full()
    {
        var plan = ReportPrintPlan.Create(Sheet(17 + 21), Page);

        Assert.Equal(3, plan.Pages.Count);
        Assert.False(plan.Pages[1].ShowTotals);
        Assert.Equal((38, 0, true), (plan.Pages[2].FirstRow, plan.Pages[2].RowCount, plan.Pages[2].ShowTotals));

        var withoutTotals = ReportPrintPlan.Create(Sheet(17 + 21, totals: false), Page);
        Assert.Equal(2, withoutTotals.Pages.Count);
    }

    [Fact]
    public void Wider_sheet_is_shrunk_to_the_page_width_and_fits_more_rows()
    {
        var plan = ReportPrintPlan.Create(Sheet(10, true, WideColumns(6)), Page); // 100 units = 1000 → fits exactly

        Assert.Equal(1, plan.Scale);
        var wider = ReportPrintPlan.Create(Sheet(60, true, WideColumns(9)), Page); // 145 units → scale 1000/1450

        Assert.Equal(1000d / 1450d, wider.Scale, 6);
        Assert.Equal(1000d, wider.ColumnWidths.Sum(), 6);
        Assert.Equal(1, wider.BandCount);
        Assert.Equal(1000d, wider.TableWidth(wider.Pages[0]), 6);

        // Rows shrink too: (500 − 100 − 20 − 30·s) / (20·s) on the first page.
        var s = wider.Scale;
        Assert.Equal((int)Math.Floor((500 - 100 - 20 - (30 * s)) / (20 * s)), wider.Pages[0].RowCount);
    }

    [Fact]
    public void Too_wide_sheet_is_split_into_bands_that_repeat_the_key_columns()
    {
        var plan = ReportPrintPlan.Create(Sheet(5, true, WideColumns(19)), Page); // 295 units at min scale 0.5 → 200 per band

        Assert.Equal(0.5, plan.Scale);
        Assert.Equal([0], plan.KeyColumns);
        Assert.Equal(2, plan.BandCount);
        Assert.Equal(2, plan.Pages.Count);

        var first = plan.Pages[0];
        var second = plan.Pages[1];
        Assert.Equal([0, .. Enumerable.Range(1, 12)], first.Columns); // 10 + 12 × 15 = 190 ≤ 200
        Assert.Equal([0, .. Enumerable.Range(13, 7)], second.Columns);
        Assert.True(second.ShowTitleBlock);
        Assert.True(first.ShowTotals && second.ShowTotals);
        Assert.Equal("Phần 1/2: C1 → C12", plan.BandCaption(first));
        Assert.Equal("Phần 2/2: C13 → C19", plan.BandCaption(second));
        Assert.All(plan.Pages, p => Assert.True(plan.TableWidth(p) <= Page.PrintableWidth + 0.001));
    }

    [Fact]
    public void Key_columns_are_the_leading_rank_and_text_columns()
    {
        ReportColumn[] columns =
        [
            new("Hạng", ReportColumnKind.Integer, 6), new("Mã", ReportColumnKind.Text, 10), new("Tên", ReportColumnKind.Text, 30),
            .. Enumerable.Range(1, 20).Select(i => new ReportColumn($"C{i}", ReportColumnKind.Money, 15)),
        ];

        var plan = ReportPrintPlan.Create(Sheet(3, true, columns), Page);

        Assert.Equal([0, 1, 2], plan.KeyColumns);
        Assert.All(plan.Pages, p => Assert.Equal([0, 1, 2], p.Columns.Take(3)));
        Assert.Equal(columns.Length, plan.Pages.SelectMany(p => p.Columns).Distinct().Count());
    }

    [Fact]
    public void Empty_sheet_prints_one_page()
    {
        var withTotals = ReportPrintPlan.Create(Sheet(0), Page);
        var page = Assert.Single(withTotals.Pages);
        Assert.Equal((0, true, true), (page.RowCount, page.ShowTotals, page.ShowTitleBlock));

        Assert.Single(ReportPrintPlan.Create(Sheet(0, totals: false), Page).Pages);
    }

    [Fact]
    public void Real_reports_on_a4_landscape()
    {
        var header = new ReportHeader("Tạp hóa Cô Ba", "Lê Văn C", new DateTime(2026, 11, 7, 20, 0, 0));
        var period = new ReportPeriod(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7));
        var row = new RevenueRowDto(period.From, 1, 0, 1, 0, 1, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0);
        var revenue = ReportSheets.Revenue(new RevenueReportDto(period, Enumerable.Repeat(row, 7).ToList(), row with { Date = null }), header);
        var best = ReportSheets.BestSellers(new BestSellerReportDto(period, ProductRanking.Quantity, [], 0, 0), header);

        var revenuePlan = ReportPrintPlan.Create(revenue, ReportPageMetrics.A4(landscape: true, revenue.Subtitles.Count));
        var bestPlan = ReportPrintPlan.Create(best, ReportPageMetrics.A4(landscape: true, best.Subtitles.Count));

        // 19 columns do not fit legibly: two bands, each starting with the date.
        Assert.Equal(2, revenuePlan.BandCount);
        Assert.All(revenuePlan.Pages, p => Assert.Equal(0, p.Columns[0]));
        Assert.Equal(1, bestPlan.BandCount);
        Assert.True(bestPlan.Scale > 0.99);
        Assert.Equal("Trang 2/5", ReportPrintPlan.PageLabel(2, 5));
    }

    [Fact]
    public void Rejects_an_empty_page()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportPrintPlan.Create(Sheet(1), Page with { PrintableWidth = 0 }));
    }
}

public sealed class ReportCellTextTests
{
    [Theory]
    [InlineData(ReportColumnKind.Money, 1_250_000.4, "1.250.000")]
    [InlineData(ReportColumnKind.Money, -35_000, "-35.000")]
    [InlineData(ReportColumnKind.Integer, 1234, "1.234")]
    [InlineData(ReportColumnKind.Quantity, 1.5, "1,5")]
    [InlineData(ReportColumnKind.Quantity, 12, "12")]
    [InlineData(ReportColumnKind.Percent, 12.46, "12,5 %")]
    public void Formats_numbers_for_vi_VN(ReportColumnKind kind, double value, string expected)
    {
        Assert.Equal(expected, ReportCellText.Format((decimal)value, kind));
    }

    [Fact]
    public void Formats_dates_text_and_blanks()
    {
        Assert.Equal("01/11/2026", ReportCellText.Format(new DateOnly(2026, 11, 1), ReportColumnKind.Date));
        Assert.Equal("01/11/2026", ReportCellText.Format(new DateTime(2026, 11, 1, 9, 30, 0), ReportColumnKind.Date));
        Assert.Equal("01/11/2026 09:30", ReportCellText.Format(new DateTime(2026, 11, 1, 9, 30, 0), ReportColumnKind.Text));
        Assert.Equal("Tổng cộng", ReportCellText.Format("Tổng cộng", ReportColumnKind.Date));
        Assert.Equal(string.Empty, ReportCellText.Format(null, ReportColumnKind.Money));
        Assert.Equal("7", ReportCellText.Format(7, ReportColumnKind.Integer));
    }

    [Fact]
    public void Aligns_numbers_right_dates_centre_text_left()
    {
        Assert.Equal(ReportCellAlignment.Right, ReportCellText.AlignmentOf(ReportColumnKind.Money));
        Assert.Equal(ReportCellAlignment.Right, ReportCellText.AlignmentOf(ReportColumnKind.Percent));
        Assert.Equal(ReportCellAlignment.Center, ReportCellText.AlignmentOf(ReportColumnKind.Date));
        Assert.Equal(ReportCellAlignment.Left, ReportCellText.AlignmentOf(ReportColumnKind.Text));
    }
}
