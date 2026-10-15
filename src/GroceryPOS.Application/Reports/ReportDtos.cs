using GroceryPOS.Application.Sales;

namespace GroceryPOS.Application.Reports;

/// <summary>A range of store calendar days, both ends inclusive.</summary>
public sealed record ReportPeriod(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public static ReportPeriod SingleDay(DateOnly day) => new(day, day);
}

/// <summary>
/// Sales figures for one store day (or a whole period for the totals row). All amounts VND, VAT-inclusive
/// unless named <c>ExclVat</c>.
/// <para>
/// Sales count on the day they were completed; voided invoices are left out entirely (only counted in
/// <see cref="VoidedCount"/>). Customer returns reduce the day the goods came back, so a past day's
/// figures never change because of a later return.
/// </para>
/// </summary>
public sealed record RevenueRowDto(
    DateOnly? Date,
    int InvoiceCount,
    int VoidedCount,
    decimal GrossAmount,
    decimal DiscountTotal,
    decimal SalesTotal,
    int ReturnCount,
    decimal RefundTotal,
    decimal NetRevenue,
    decimal VatAmount,
    decimal NetRevenueExclVat,
    decimal CostOfGoods,
    decimal GrossProfit,
    decimal WriteOffCost,
    decimal CashCollected,
    decimal TransferCollected,
    decimal CardCollected)
{
    /// <summary>Giá trị trung bình mỗi hóa đơn.</summary>
    public decimal AverageBasket => InvoiceCount == 0 ? 0 : Math.Round(SalesTotal / InvoiceCount, 0, MidpointRounding.AwayFromZero);

    /// <summary>Gross margin on revenue excluding VAT, in percent (1 decimal).</summary>
    public decimal MarginPercent => ReportMath.Percent(GrossProfit, NetRevenueExclVat);
}

/// <summary>Báo cáo doanh thu: one row per day of the period (days without sales included) plus totals.</summary>
public sealed record RevenueReportDto(ReportPeriod Period, IReadOnlyList<RevenueRowDto> Days, RevenueRowDto Totals);

public enum ProductRanking
{
    /// <summary>By quantity sold (net of returns).</summary>
    Quantity = 0,

    /// <summary>By net revenue.</summary>
    Revenue = 1,

    /// <summary>By gross profit.</summary>
    Profit = 2,
}

public sealed record BestSellerQuery(
    ReportPeriod Period,
    ProductRanking RankBy = ProductRanking.Quantity,
    int Top = 20,
    int? CategoryId = null);

/// <summary>One product's sales in a period, net of the returns taken in that period.</summary>
public sealed record ProductSalesRowDto(
    int Rank,
    int ProductId,
    string ProductCode,
    string ProductName,
    string Unit,
    int CategoryId,
    string CategoryName,
    decimal Quantity,
    decimal Revenue,
    decimal RevenueExclVat,
    decimal Cost,
    decimal Profit,
    int InvoiceCount,
    decimal RevenueSharePercent)
{
    public decimal MarginPercent => ReportMath.Percent(Profit, RevenueExclVat);
}

/// <summary>Báo cáo hàng bán chạy. <see cref="TotalRevenue"/> covers all products of the period (before the top cut).</summary>
public sealed record BestSellerReportDto(
    ReportPeriod Period,
    ProductRanking RankBy,
    IReadOnlyList<ProductSalesRowDto> Rows,
    decimal TotalRevenue,
    int ProductCount);

public sealed record CategoryProfitRowDto(
    int CategoryId,
    string CategoryName,
    int ProductCount,
    decimal Revenue,
    decimal RevenueExclVat,
    decimal Cost,
    decimal Profit,
    decimal RevenueSharePercent)
{
    public decimal MarginPercent => ReportMath.Percent(Profit, RevenueExclVat);
}

/// <summary>Báo cáo lợi nhuận theo danh mục; <see cref="Totals"/> has CategoryId 0.</summary>
public sealed record ProfitReportDto(ReportPeriod Period, IReadOnlyList<CategoryProfitRowDto> Rows, CategoryProfitRowDto Totals);

/// <summary>
/// Stock value of one category at cost and at shelf price. Products with negative stock (sold before
/// the goods receipt was entered) add nothing to the value and are counted in <see cref="NegativeStockCount"/>.
/// </summary>
public sealed record InventoryValueRowDto(
    int CategoryId,
    string CategoryName,
    int ProductCount,
    decimal CostValue,
    decimal RetailValue,
    decimal RetailValueExclVat,
    int OutOfStockCount,
    int LowStockCount,
    int NegativeStockCount)
{
    /// <summary>Lãi dự kiến: what the stock would earn if sold at the current shelf price.</summary>
    public decimal PotentialProfit => RetailValueExclVat - CostValue;
}

/// <summary>Báo cáo giá trị tồn kho at <see cref="GeneratedAt"/>.</summary>
public sealed record InventoryValueReportDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<InventoryValueRowDto> Rows,
    InventoryValueRowDto Totals);

public sealed record DailyPointDto(DateOnly Date, decimal NetRevenue, int InvoiceCount);

public sealed record HourlyPointDto(int Hour, decimal SalesTotal, int InvoiceCount);

public sealed record DashboardTopProductDto(int ProductId, string ProductCode, string ProductName, string Unit, decimal Quantity, decimal Revenue);

/// <summary>
/// Trang tổng quan. Profit figures are only filled in for users with <c>reports.view</c>
/// (cashiers see the dashboard but not margins).
/// </summary>
public sealed record DashboardDto(
    DateOnly Today,
    decimal NetRevenueToday,
    int InvoiceCountToday,
    decimal AverageBasketToday,
    decimal RefundTotalToday,
    decimal? GrossProfitToday,
    decimal NetRevenueYesterday,
    decimal? RevenueChangePercent,
    IReadOnlyList<DailyPointDto> Last7Days,
    IReadOnlyList<HourlyPointDto> TodayByHour,
    IReadOnlyList<DashboardTopProductDto> TopProductsToday,
    int OutOfStockCount,
    int LowStockCount,
    IReadOnlyList<LowStockAlert> LowStockItems);

internal static class ReportMath
{
    /// <summary><paramref name="part"/> / <paramref name="whole"/> × 100, 1 decimal; 0 when the whole is 0.</summary>
    public static decimal Percent(decimal part, decimal whole) =>
        whole == 0 ? 0 : Math.Round(part * 100 / whole, 1, MidpointRounding.AwayFromZero);
}
