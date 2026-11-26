using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Reports;
using GroceryPOS.Presentation.Dashboard;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Reports;

/// <summary>The four tabs of Báo cáo, in screen order (same values as <see cref="Application.Reports.Export.ReportKind"/>).</summary>
public enum ReportTab
{
    Revenue = 0,
    BestSellers = 1,
    ProfitByCategory = 2,
    InventoryValue = 3,
}

/// <summary>One row of "Chi tiết theo ngày" (transfer and card shown together as "CK + thẻ").</summary>
public sealed record RevenueRow(
    DateTime Date,
    int InvoiceCount,
    decimal SalesTotal,
    decimal DiscountTotal,
    decimal RefundTotal,
    decimal NetRevenue,
    decimal VatAmount,
    decimal CostOfGoods,
    decimal GrossProfit,
    decimal MarginPercent,
    decimal Cash,
    decimal NonCash);

/// <summary>Number formats shared by the report tabs.</summary>
public static class ReportText
{
    /// <summary>"1,2 tỷ" / "1,2 tr" / "850 N" – compact bar labels.</summary>
    public static string ShortMoney(decimal amount) => Math.Abs(amount) switch
    {
        >= 1_000_000_000 => $"{(amount / 1_000_000_000).ToString("#,0.#", Vnd.Culture)} tỷ",
        >= 1_000_000 => $"{(amount / 1_000_000).ToString("#,0.#", Vnd.Culture)} tr",
        >= 1_000 => $"{(amount / 1_000).ToString("#,0", Vnd.Culture)} N",
        _ => amount.ToString("#,0", Vnd.Culture),
    };

    /// <summary>"12,5%" (one decimal at most); "0%" when <paramref name="whole"/> is zero.</summary>
    public static string Share(decimal part, decimal whole) =>
        whole == 0 ? "0%" : $"{Math.Round(part * 100 / whole, 1, MidpointRounding.AwayFromZero).ToString("0.#", Vnd.Culture)}%";

    /// <summary>"12,5" – percentages in notes.</summary>
    public static string Percent(decimal value) => value.ToString("0.0", Vnd.Culture);
}

/// <summary>
/// Tab Doanh thu: four KPI cards (net revenue with the trend against the previous period of the same length, invoices,
/// gross profit, refunds &amp; voids), the daily – or, for long periods, monthly – chart and the day table (newest first).
/// </summary>
public sealed record RevenueBoard(
    DashboardCard NetRevenue,
    DashboardCard Invoices,
    DashboardCard Profit,
    DashboardCard Refunds,
    string ChartTitle,
    string? ChartSubtitle,
    IReadOnlyList<DashboardBar> Bars,
    bool ShowBarValues,
    IReadOnlyList<RevenueRow> Rows)
{
    public const string NetRevenueCaption = "Doanh thu thuần";
    public const string InvoicesCaption = "Số hóa đơn";
    public const string ProfitCaption = "Lợi nhuận gộp";
    public const string RefundsCaption = "Hoàn trả & hủy";

    /// <summary>Bar values are printed above the bars only up to this many bars (they overlap beyond).</summary>
    public const int MaxLabelledBars = 14;

    /// <summary>All cards "–", no bars, no rows (before the first load and after a failed one).</summary>
    public static RevenueBoard Empty { get; } = new(
        DashboardCard.Placeholder(NetRevenueCaption, Tone.Success),
        DashboardCard.Placeholder(InvoicesCaption, Tone.Info),
        DashboardCard.Placeholder(ProfitCaption, Tone.Info),
        DashboardCard.Placeholder(RefundsCaption, Tone.Warning),
        "Doanh thu thuần theo ngày",
        null,
        Array.Empty<DashboardBar>(),
        false,
        Array.Empty<RevenueRow>());

    /// <param name="report">The selected period.</param>
    /// <param name="previous">The period right before it (null when it could not be loaded: no trend then).</param>
    public static RevenueBoard From(RevenueReportDto report, RevenueReportDto? previous)
    {
        ArgumentNullException.ThrowIfNull(report);
        var totals = report.Totals;
        var before = previous?.Totals;

        var netRevenue = new DashboardCard(
            NetRevenueCaption,
            Vnd.Format(totals.NetRevenue),
            before is null || before.NetRevenue <= 0 ? $"Doanh số {Vnd.Format(totals.SalesTotal)}" : null,
            Tone.Success,
            ShowTrend: true,
            TrendPercent: before is null ? null : ReportFigures.ChangePercent(totals.NetRevenue, before.NetRevenue),
            TrendComparedTo: "kỳ trước");

        var invoices = new DashboardCard(
            InvoicesCaption,
            totals.InvoiceCount.ToString("#,0", Vnd.Culture),
            totals.InvoiceCount == 0 ? "Chưa có hóa đơn" : $"TB {Vnd.Format(totals.AverageBasket)} / hóa đơn",
            Tone.Info);

        var profit = new DashboardCard(
            ProfitCaption,
            Vnd.Format(totals.GrossProfit),
            $"Tỷ suất {ReportText.Percent(totals.MarginPercent)}% · giá vốn {Vnd.Format(totals.CostOfGoods)}",
            totals.GrossProfit < 0 ? Tone.Danger : Tone.Info);

        var refunds = new DashboardCard(
            RefundsCaption,
            Vnd.Format(totals.RefundTotal),
            $"{totals.ReturnCount.ToString("#,0", Vnd.Culture)} phiếu trả · {totals.VoidedCount.ToString("#,0", Vnd.Culture)} HĐ hủy",
            Tone.Warning);

        var buckets = ReportFigures.RevenueBuckets(report);
        var bars = buckets
            .Select(b => new DashboardBar(
                b.Label,
                b.NetRevenue,
                (b.From == b.To ? DashboardFigures.LongDayLabel(b.From) : $"{b.From:dd/MM/yyyy} – {b.To:dd/MM/yyyy}")
                + $"\nDoanh thu thuần: {Vnd.Format(b.NetRevenue)}\nLợi nhuận: {Vnd.Format(b.GrossProfit)}\nHóa đơn: {b.InvoiceCount.ToString("#,0", Vnd.Culture)}"))
            .ToList();

        var best = ReportFigures.BestDay(report);
        var subtitle = best is null
            ? "Không có doanh thu"
            : $"Tổng {Vnd.Format(totals.NetRevenue)} · TB {Vnd.Format(Vnd.Round(totals.NetRevenue / report.Period.Days))}/ngày · cao nhất {best.Date:dd/MM} ({Vnd.Format(best.NetRevenue)})";

        var rows = report.Days
            .Where(d => d.Date is not null)
            .OrderByDescending(d => d.Date)
            .Select(d => new RevenueRow(
                d.Date!.Value.ToDateTime(TimeOnly.MinValue),
                d.InvoiceCount,
                d.SalesTotal,
                d.DiscountTotal,
                d.RefundTotal,
                d.NetRevenue,
                d.VatAmount,
                d.CostOfGoods,
                d.GrossProfit,
                d.MarginPercent,
                d.CashCollected,
                d.TransferCollected + d.CardCollected))
            .ToList();

        return new RevenueBoard(
            netRevenue,
            invoices,
            profit,
            refunds,
            report.Period.Days > ReportFigures.MaxDailyBars ? "Doanh thu thuần theo tháng" : "Doanh thu thuần theo ngày",
            subtitle,
            bars,
            bars.Count <= MaxLabelledBars,
            rows);
    }
}

/// <summary>Tab Hàng bán chạy: the ranked rows and "N/M sản phẩm có bán · chiếm x% doanh thu".</summary>
public sealed record BestSellerBoard(IReadOnlyList<ProductSalesRowDto> Rows, string? Subtitle)
{
    public static BestSellerBoard Empty { get; } = new(Array.Empty<ProductSalesRowDto>(), null);

    public static BestSellerBoard From(BestSellerReportDto report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Rows.Count == 0)
        {
            return Empty;
        }

        var shown = report.Rows.Sum(r => r.Revenue);
        return new BestSellerBoard(
            report.Rows.ToList(),
            $"{report.Rows.Count.ToString("#,0", Vnd.Culture)}/{report.ProductCount.ToString("#,0", Vnd.Culture)} sản phẩm có bán · chiếm {ReportText.Share(shown, report.TotalRevenue)} doanh thu ({Vnd.Format(shown)})");
    }
}

/// <summary>Tab Lợi nhuận theo danh mục: rows by profit (highest first), the top-12 chart and the totals line.</summary>
public sealed record ProfitBoard(IReadOnlyList<CategoryProfitRowDto> Rows, IReadOnlyList<DashboardBar> Bars, string? Subtitle)
{
    /// <summary>Categories beyond this many are in the table only.</summary>
    public const int MaxBars = 12;

    public static ProfitBoard Empty { get; } = new(Array.Empty<CategoryProfitRowDto>(), Array.Empty<DashboardBar>(), null);

    public static ProfitBoard From(ProfitReportDto report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = report.Rows.OrderByDescending(r => r.Profit).ThenBy(r => r.CategoryName, StringComparer.CurrentCulture).ToList();
        if (rows.Count == 0)
        {
            return Empty;
        }

        var bars = rows
            .Take(MaxBars)
            .Select(r => new DashboardBar(
                r.CategoryName,
                r.Profit,
                $"{r.CategoryName}\nDoanh thu: {Vnd.Format(r.Revenue)}\nLợi nhuận: {Vnd.Format(r.Profit)} ({ReportText.Percent(r.MarginPercent)}%)"))
            .ToList();
        var subtitle = $"Tổng {Vnd.Format(report.Totals.Profit)} · tỷ suất {ReportText.Percent(report.Totals.MarginPercent)}% trên {Vnd.Format(report.Totals.RevenueExclVat)} chưa VAT";
        if (rows.Count > MaxBars)
        {
            subtitle += $" · biểu đồ {MaxBars}/{rows.Count} danh mục";
        }

        return new ProfitBoard(rows, bars, subtitle);
    }
}

/// <summary>Tab Giá trị tồn kho: four KPI cards and the rows by cost value (highest first). Not tied to the period.</summary>
public sealed record InventoryBoard(
    DashboardCard CostValue,
    DashboardCard RetailValue,
    DashboardCard PotentialProfit,
    DashboardCard StockAlerts,
    IReadOnlyList<InventoryValueRowDto> Rows)
{
    public const string CostCaption = "Giá trị tồn (giá vốn)";
    public const string RetailCaption = "Giá trị tồn (giá bán)";
    public const string PotentialCaption = "Lãi dự kiến";
    public const string AlertsCaption = "Hết / sắp hết hàng";

    public static InventoryBoard Empty { get; } = new(
        DashboardCard.Placeholder(CostCaption, Tone.Info),
        DashboardCard.Placeholder(RetailCaption, Tone.Success),
        DashboardCard.Placeholder(PotentialCaption, Tone.Info),
        DashboardCard.Placeholder(AlertsCaption, Tone.Warning),
        Array.Empty<InventoryValueRowDto>());

    public static InventoryBoard From(InventoryValueReportDto report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var t = report.Totals;
        return new InventoryBoard(
            new DashboardCard(CostCaption, Vnd.Format(t.CostValue), $"{t.ProductCount.ToString("#,0", Vnd.Culture)} sản phẩm", Tone.Info),
            new DashboardCard(RetailCaption, Vnd.Format(t.RetailValue), $"Chưa VAT {Vnd.Format(t.RetailValueExclVat)}", Tone.Success),
            new DashboardCard(PotentialCaption, Vnd.Format(t.PotentialProfit), "Nếu bán hết theo giá hiện tại", t.PotentialProfit < 0 ? Tone.Danger : Tone.Info),
            new DashboardCard(
                AlertsCaption,
                $"{t.OutOfStockCount.ToString("#,0", Vnd.Culture)} / {t.LowStockCount.ToString("#,0", Vnd.Culture)}",
                t.NegativeStockCount > 0 ? $"{t.NegativeStockCount.ToString("#,0", Vnd.Culture)} SP tồn âm – kiểm tra phiếu nhập" : "Không có tồn âm",
                t.OutOfStockCount > 0 ? Tone.Danger : Tone.Warning),
            report.Rows.OrderByDescending(r => r.CostValue).ToList());
    }
}
