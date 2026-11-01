using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Tests;

public sealed class ReportExportTests
{
    private static readonly ReportPeriod November = new(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2));
    private static readonly ReportHeader Header = new("Tạp hóa Cô Ba", "Lê Văn C", new DateTime(2026, 11, 2, 21, 5, 0));

    private static RevenueRowDto RevenueRow(DateOnly? date, decimal sales, decimal profit) =>
        new(date, InvoiceCount: 4, VoidedCount: 1, GrossAmount: sales + 2_000, DiscountTotal: 2_000, SalesTotal: sales,
            ReturnCount: 0, RefundTotal: 0, NetRevenue: sales, VatAmount: sales / 11, NetRevenueExclVat: sales - sales / 11,
            CostOfGoods: sales - sales / 11 - profit, GrossProfit: profit, WriteOffCost: 0,
            CashCollected: sales, TransferCollected: 0, CardCollected: 0);

    private static RevenueReportDto Revenue() => new(
        November,
        [RevenueRow(November.From, 110_000, 20_000), RevenueRow(November.To, 220_000, 40_000)],
        RevenueRow(null, 330_000, 60_000));

    private static BestSellerReportDto BestSellers() => new(
        November,
        ProductRanking.Revenue,
        [
            new ProductSalesRowDto(1, 1, "SUA01", "Sữa tươi 1L", "hộp", 1, "Sữa", 6, 210_000, 200_000, 168_000, 32_000, 5, 70),
            new ProductSalesRowDto(2, 2, "MI01", "Mì tôm", "gói", 2, "Mì gói", 12, 60_000, 54_545, 42_000, 12_545, 3, 20),
        ],
        TotalRevenue: 300_000,
        ProductCount: 5);

    [Fact]
    public void Revenue_sheet_has_one_row_per_day_and_a_totals_row()
    {
        var sheet = ReportSheets.Revenue(Revenue(), Header);

        Assert.Equal("Doanh thu", sheet.Name);
        Assert.Equal("BÁO CÁO DOANH THU", sheet.Title);
        Assert.Equal(["Tạp hóa Cô Ba", "Từ ngày 01/11/2026 đến ngày 02/11/2026", "Người xuất: Lê Văn C – lúc 02/11/2026 21:05"], sheet.Subtitles);
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), sheet.Rows[0][0]);
        Assert.Equal(ReportSheets.TotalsLabel, sheet.Totals![0]);

        var profitColumn = IndexOf(sheet, "Lợi nhuận gộp");
        Assert.Equal(60_000m, sheet.Totals[profitColumn]);
        Assert.Equal(ReportColumnKind.Percent, sheet.Columns[IndexOf(sheet, "Tỷ suất LN")].Kind);
        Assert.Equal(ReportColumnKind.Date, sheet.Columns[0].Kind);
    }

    [Fact]
    public void Best_seller_totals_cover_listed_rows_and_share_of_all_revenue()
    {
        var sheet = ReportSheets.BestSellers(BestSellers(), Header, "Sữa");

        Assert.Contains("Xếp hạng theo doanh thu – 2/5 sản phẩm có bán – danh mục Sữa", sheet.Subtitles);
        Assert.Equal(270_000m, sheet.Totals![IndexOf(sheet, "Doanh thu")]);
        Assert.Equal(44_545m, sheet.Totals[IndexOf(sheet, "Lợi nhuận")]);
        Assert.Equal(90m, sheet.Totals[IndexOf(sheet, "Tỷ trọng DT")]); // 270k of 300k
        Assert.Equal("Tổng cộng (2 sản phẩm)", sheet.Totals[IndexOf(sheet, "Tên sản phẩm")]);
    }

    [Fact]
    public void Single_day_period_line_and_file_names()
    {
        var day = ReportPeriod.SingleDay(new DateOnly(2026, 11, 2));
        var now = new DateTime(2026, 11, 2, 21, 5, 0);

        Assert.Equal("Ngày: 02/11/2026", ReportSheets.PeriodLine(day));
        Assert.Equal("BaoCao_DoanhThu_20261102", ReportExportService.FileName([ReportKind.Revenue], day, now));
        Assert.Equal("BaoCao_20261101-20261102", ReportExportService.FileName([ReportKind.Revenue, ReportKind.BestSellers], November, now));
        Assert.Equal("BaoCao_TonKho_20261102-2105", ReportExportService.FileName([ReportKind.InventoryValue], November, now));
    }

    [Fact]
    public void Sheet_rejects_rows_that_do_not_match_the_columns()
    {
        ReportColumn[] columns = [new("A", ReportColumnKind.Text), new("B", ReportColumnKind.Money)];

        Assert.Throws<ArgumentException>(() => new ReportSheet("X", "T", [], columns, [new object?[] { "only one" }]));
        Assert.Throws<ArgumentException>(() => new ReportSheet("X", "T", [], columns, [], totals: [1]));
        Assert.Throws<ArgumentException>(() => new ReportSheet("Bad/Name", "T", [], columns, []));
        Assert.Throws<ArgumentException>(() => new ReportSheet(new string('x', 32), "T", [], columns, []));
    }

    [Fact]
    public async Task Export_writes_one_sheet_per_report_and_audits()
    {
        var (sut, writer, audit, uow) = Create(Permissions.ReportsView);

        var result = await sut.ExportAsync(new ReportExportRequest(
            [ReportKind.Revenue, ReportKind.BestSellers, ReportKind.Revenue], November, ProductRanking.Revenue));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("BaoCao_20261101-20261102.test", result.Value.FileName);
        Assert.Equal(["Doanh thu", "Bán chạy"], writer.Written!.Select(s => s.Name));
        Assert.Equal("Tạp hóa Cô Ba", writer.Written![0].Subtitles[0]);
        Assert.Contains("Người xuất: Lê Văn C", writer.Written![0].Subtitles[^1]);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.ReportExported, entry.Action);
        Assert.Contains("Doanh thu, Bán chạy", entry.Details);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task Export_requires_a_report_and_passes_report_errors_through()
    {
        var (sut, writer, audit, _) = Create(Permissions.ReportsView);
        var empty = await sut.ExportAsync(new ReportExportRequest([], November));
        Assert.Equal(ReportExportService.NothingSelected, empty.Error);

        var (denied, deniedWriter, deniedAudit, _) = Create(Permissions.SalesCreate);
        var result = await denied.ExportAsync(new ReportExportRequest([ReportKind.Revenue], November));

        Assert.True(result.IsFailure);
        Assert.Equal(ReportErrors.Denied, result.Error);
        Assert.Null(deniedWriter.Written);
        Assert.Empty(deniedAudit.Entries);
        Assert.Null(writer.Written);
        Assert.Empty(audit.Entries);
    }

    private static int IndexOf(ReportSheet sheet, string header)
    {
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            if (sheet.Columns[i].Header == header)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Missing column {header}.");
    }

    private static (ReportExportService Sut, CapturingWriter Writer, RecordingAuditService Audit, FakeUnitOfWork Uow) Create(params string[] permissions)
    {
        var clock = new FakeClock { Now = new DateTimeOffset(2026, 11, 2, 14, 5, 0, TimeSpan.Zero) };
        var session = new UserSessionStore();
        session.SignIn(new UserSession(3, "quanly", "Lê Văn C", SystemRoles.Manager, permissions.ToHashSet(), clock.Now));
        var writer = new CapturingWriter();
        var audit = new RecordingAuditService();
        var uow = new FakeUnitOfWork();
        var sut = new ReportExportService(
            new StubReports(session), writer, session, audit, uow, clock, new StoreProfile { Name = "Tạp hóa Cô Ba" });
        return (sut, writer, audit, uow);
    }

    private sealed class CapturingWriter : IReportWorkbookWriter
    {
        public IReadOnlyList<ReportSheet>? Written { get; private set; }

        public string FileExtension => ".test";

        public Task<byte[]> WriteAsync(IReadOnlyList<ReportSheet> sheets, CancellationToken cancellationToken = default)
        {
            Written = sheets;
            return Task.FromResult(new byte[] { 1, 2, 3 });
        }
    }

    private sealed class StubReports(ICurrentUser user) : IReportService
    {
        private Result<T> Guard<T>(Func<T> value) =>
            user.HasPermission(Permissions.ReportsView) ? value() : ReportErrors.Denied;

        public Task<Result<RevenueReportDto>> GetRevenueAsync(ReportPeriod period, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guard(Revenue));

        public Task<Result<BestSellerReportDto>> GetBestSellersAsync(BestSellerQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guard(BestSellers));

        public Task<Result<ProfitReportDto>> GetProfitByCategoryAsync(ReportPeriod period, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<InventoryValueReportDto>> GetInventoryValueAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<DashboardDto>> GetDashboardAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

public sealed class ReportFiguresTests
{
    private static RevenueRowDto Day(DateOnly date, decimal net, int invoices = 1) =>
        new(date, invoices, 0, net, 0, net, 0, 0, net, 0, net, net / 2, net / 2, 0, net, 0, 0);

    private static RevenueReportDto Report(DateOnly from, DateOnly to, Func<DateOnly, decimal> net)
    {
        var days = new List<RevenueRowDto>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            days.Add(Day(d, net(d)));
        }

        return new RevenueReportDto(new ReportPeriod(from, to), days, Day(from, days.Sum(x => x.NetRevenue)) with { Date = null });
    }

    [Fact]
    public void Short_periods_chart_one_bar_per_day()
    {
        var report = Report(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), d => d.Day * 1_000);

        var buckets = ReportFigures.RevenueBuckets(report);

        Assert.Equal(7, buckets.Count);
        Assert.Equal("01/11", buckets[0].Label);
        Assert.Equal(7_000m, buckets[^1].NetRevenue);
    }

    [Fact]
    public void Long_periods_chart_one_bar_per_month_clipped_to_the_period()
    {
        var report = Report(new DateOnly(2026, 8, 15), new DateOnly(2026, 11, 2), _ => 1_000);

        var buckets = ReportFigures.RevenueBuckets(report);

        Assert.Equal(["T8/2026", "T9/2026", "T10/2026", "T11/2026"], buckets.Select(b => b.Label));
        Assert.Equal(17_000m, buckets[0].NetRevenue); // 15–31/08
        Assert.Equal(new DateOnly(2026, 8, 15), buckets[0].From);
        Assert.Equal(30, buckets[1].InvoiceCount);
        Assert.Equal(new DateOnly(2026, 11, 2), buckets[^1].To);
    }

    [Fact]
    public void Previous_period_and_change()
    {
        var previous = ReportFigures.PreviousPeriod(new ReportPeriod(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7)));

        Assert.Equal(new ReportPeriod(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 31)), previous);
        Assert.Equal(25m, ReportFigures.ChangePercent(125_000, 100_000));
        Assert.Equal(-33.3m, ReportFigures.ChangePercent(200, 300));
        Assert.Null(ReportFigures.ChangePercent(100, 0));
    }

    [Fact]
    public void Best_day_ignores_days_without_sales()
    {
        var report = Report(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), d => d.Day == 2 ? 50_000 : 0);
        Assert.Equal(new DateOnly(2026, 11, 2), ReportFigures.BestDay(report)!.Date);

        var empty = Report(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), _ => 0);
        Assert.Null(ReportFigures.BestDay(empty));
    }
}
