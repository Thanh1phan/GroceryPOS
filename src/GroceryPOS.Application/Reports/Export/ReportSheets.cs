using System.Globalization;

namespace GroceryPOS.Application.Reports.Export;

/// <summary>Who/when/where lines printed above every exported table.</summary>
public sealed record ReportHeader(string StoreName, string GeneratedBy, DateTime GeneratedAt)
{
    public IReadOnlyList<string> Lines(string? periodLine) =>
        periodLine is null
            ? [StoreName, GeneratedLine]
            : [StoreName, periodLine, GeneratedLine];

    private string GeneratedLine =>
        $"Người xuất: {GeneratedBy} – lúc {GeneratedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Turns report DTOs into <see cref="ReportSheet"/>s: Vietnamese headers, column kinds, row order and the totals
/// row. One builder per report so the screen and the export always show the same columns.
/// </summary>
public static class ReportSheets
{
    public const string TotalsLabel = "Tổng cộng";

    public static string PeriodLine(ReportPeriod period)
    {
        var from = period.From.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var to = period.To.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return period.From == period.To ? $"Ngày: {from}" : $"Từ ngày {from} đến ngày {to}";
    }

    public static string RankingName(ProductRanking ranking) => ranking switch
    {
        ProductRanking.Quantity => "số lượng",
        ProductRanking.Revenue => "doanh thu",
        ProductRanking.Profit => "lợi nhuận",
        _ => ranking.ToString(),
    };

    public static ReportSheet Revenue(RevenueReportDto report, ReportHeader header)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(header);

        ReportColumn[] columns =
        [
            new("Ngày", ReportColumnKind.Date, 12),
            new("Số HĐ", ReportColumnKind.Integer, 9),
            new("HĐ hủy", ReportColumnKind.Integer, 9),
            new("Tiền hàng", ReportColumnKind.Money, 15),
            new("Giảm giá", ReportColumnKind.Money, 13),
            new("Doanh số", ReportColumnKind.Money, 15),
            new("Phiếu trả", ReportColumnKind.Integer, 10),
            new("Hoàn trả", ReportColumnKind.Money, 13),
            new("Doanh thu thuần", ReportColumnKind.Money, 16),
            new("Thuế VAT", ReportColumnKind.Money, 13),
            new("DT chưa VAT", ReportColumnKind.Money, 15),
            new("Giá vốn", ReportColumnKind.Money, 15),
            new("Lợi nhuận gộp", ReportColumnKind.Money, 15),
            new("Tỷ suất LN", ReportColumnKind.Percent, 11),
            new("Hàng hủy (giá vốn)", ReportColumnKind.Money, 15),
            new("Tiền mặt", ReportColumnKind.Money, 15),
            new("Chuyển khoản", ReportColumnKind.Money, 15),
            new("Thẻ", ReportColumnKind.Money, 13),
            new("TB/hóa đơn", ReportColumnKind.Money, 13),
        ];

        static object?[] Row(RevenueRowDto r, object? first) =>
        [
            first, r.InvoiceCount, r.VoidedCount, r.GrossAmount, r.DiscountTotal, r.SalesTotal, r.ReturnCount,
            r.RefundTotal, r.NetRevenue, r.VatAmount, r.NetRevenueExclVat, r.CostOfGoods, r.GrossProfit,
            r.MarginPercent, r.WriteOffCost, r.CashCollected, r.TransferCollected, r.CardCollected, r.AverageBasket,
        ];

        return new ReportSheet(
            "Doanh thu",
            "BÁO CÁO DOANH THU",
            header.Lines(PeriodLine(report.Period)),
            columns,
            report.Days.Select(d => (IReadOnlyList<object?>)Row(d, d.Date)).ToList(),
            Row(report.Totals, TotalsLabel));
    }

    public static ReportSheet BestSellers(BestSellerReportDto report, ReportHeader header, string? categoryName = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(header);

        var lines = header.Lines(PeriodLine(report.Period)).ToList();
        lines.Add($"Xếp hạng theo {RankingName(report.RankBy)} – {report.Rows.Count}/{report.ProductCount} sản phẩm có bán"
            + (categoryName is null ? string.Empty : $" – danh mục {categoryName}"));

        ReportColumn[] columns =
        [
            new("Hạng", ReportColumnKind.Integer, 7),
            new("Mã SP", ReportColumnKind.Text, 12),
            new("Tên sản phẩm", ReportColumnKind.Text, 36),
            new("ĐVT", ReportColumnKind.Text, 8),
            new("Danh mục", ReportColumnKind.Text, 18),
            new("Số lượng", ReportColumnKind.Quantity, 11),
            new("Số HĐ", ReportColumnKind.Integer, 9),
            new("Doanh thu", ReportColumnKind.Money, 15),
            new("Giá vốn", ReportColumnKind.Money, 15),
            new("Lợi nhuận", ReportColumnKind.Money, 15),
            new("Tỷ suất LN", ReportColumnKind.Percent, 11),
            new("Tỷ trọng DT", ReportColumnKind.Percent, 11),
        ];

        var rows = report.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Rank, r.ProductCode, r.ProductName, r.Unit, r.CategoryName, r.Quantity, r.InvoiceCount,
            r.Revenue, r.Cost, r.Profit, r.MarginPercent, r.RevenueSharePercent,
        }).ToList();

        var revenue = report.Rows.Sum(r => r.Revenue);
        var cost = report.Rows.Sum(r => r.Cost);
        var profit = report.Rows.Sum(r => r.Profit);
        var exclVat = report.Rows.Sum(r => r.RevenueExclVat);
        object?[] totals =
        [
            null, null, $"{TotalsLabel} ({report.Rows.Count} sản phẩm)", null, null, null, null,
            revenue, cost, profit, ReportMath.Percent(profit, exclVat), ReportMath.Percent(revenue, report.TotalRevenue),
        ];

        return new ReportSheet("Bán chạy", "BÁO CÁO HÀNG BÁN CHẠY", lines, columns, rows, totals);
    }

    public static ReportSheet ProfitByCategory(ProfitReportDto report, ReportHeader header)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(header);

        ReportColumn[] columns =
        [
            new("Danh mục", ReportColumnKind.Text, 26),
            new("Số SP có bán", ReportColumnKind.Integer, 12),
            new("Doanh thu", ReportColumnKind.Money, 16),
            new("DT chưa VAT", ReportColumnKind.Money, 16),
            new("Giá vốn", ReportColumnKind.Money, 16),
            new("Lợi nhuận gộp", ReportColumnKind.Money, 16),
            new("Tỷ suất LN", ReportColumnKind.Percent, 11),
            new("Tỷ trọng DT", ReportColumnKind.Percent, 11),
        ];

        static object?[] Row(CategoryProfitRowDto r, string name) =>
            [name, r.ProductCount, r.Revenue, r.RevenueExclVat, r.Cost, r.Profit, r.MarginPercent, r.RevenueSharePercent];

        return new ReportSheet(
            "Lợi nhuận",
            "BÁO CÁO LỢI NHUẬN THEO DANH MỤC",
            header.Lines(PeriodLine(report.Period)),
            columns,
            report.Rows.Select(r => (IReadOnlyList<object?>)Row(r, r.CategoryName)).ToList(),
            Row(report.Totals, TotalsLabel));
    }

    public static ReportSheet InventoryValue(InventoryValueReportDto report, ReportHeader header, DateTime generatedAtLocal)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(header);

        ReportColumn[] columns =
        [
            new("Danh mục", ReportColumnKind.Text, 26),
            new("Số SP", ReportColumnKind.Integer, 9),
            new("Giá trị vốn", ReportColumnKind.Money, 17),
            new("Giá trị bán", ReportColumnKind.Money, 17),
            new("Giá trị bán chưa VAT", ReportColumnKind.Money, 17),
            new("Lãi dự kiến", ReportColumnKind.Money, 16),
            new("Hết hàng", ReportColumnKind.Integer, 10),
            new("Sắp hết", ReportColumnKind.Integer, 10),
            new("Tồn âm", ReportColumnKind.Integer, 10),
        ];

        static object?[] Row(InventoryValueRowDto r, string name) =>
        [
            name, r.ProductCount, r.CostValue, r.RetailValue, r.RetailValueExclVat, r.PotentialProfit,
            r.OutOfStockCount, r.LowStockCount, r.NegativeStockCount,
        ];

        var at = generatedAtLocal.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        return new ReportSheet(
            "Tồn kho",
            "BÁO CÁO GIÁ TRỊ TỒN KHO",
            header.Lines($"Thời điểm: {at}"),
            columns,
            report.Rows.Select(r => (IReadOnlyList<object?>)Row(r, r.CategoryName)).ToList(),
            Row(report.Totals, TotalsLabel));
    }
}
