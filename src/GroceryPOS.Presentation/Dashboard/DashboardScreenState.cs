using System.Globalization;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Dashboard;

/// <summary>Greeting line and the "cập nhật lúc …" subtitle (amber or red when the figures could not be refreshed).</summary>
public sealed record DashboardHeader(string Greeting, string Subtitle, Tone SubtitleTone)
{
    public static DashboardHeader Loading { get; } = new("Tổng quan", "Đang tải số liệu…", Tone.Muted);

    /// <summary>"Chào buổi sáng, Nguyễn Văn A!" or just the greeting when the name is unknown.</summary>
    public static string GreetingFor(string? fullName, DateTime now) =>
        string.IsNullOrWhiteSpace(fullName) ? DashboardFigures.Greeting(now.Hour) : $"{DashboardFigures.Greeting(now.Hour)}, {fullName.Trim()}!";
}

/// <summary>
/// One KPI card. <paramref name="TrendPercent"/> / <paramref name="TrendComparedTo"/> show the ▲▼ trend when
/// <paramref name="ShowTrend"/> is set; <paramref name="Accent"/> colors the icon.
/// </summary>
public sealed record DashboardCard(
    string Caption,
    string Value,
    string? Note,
    Tone Accent,
    bool ShowTrend = false,
    decimal? TrendPercent = null,
    string? TrendComparedTo = null)
{
    public static DashboardCard Placeholder(string caption, Tone accent, string? note = null) => new(caption, "–", note, accent);
}

/// <summary>What the third card shows: gross profit (users with <c>reports.view</c>) or today's refunds (cashiers).</summary>
public enum DashboardProfitKind
{
    Profit,
    Refunds,
}

/// <summary>A bar of a dashboard chart (label under the bar, tooltip on hover).</summary>
public sealed record DashboardBar(string Label, decimal Value, string Tooltip);

/// <summary>A chart and the line under its card title; <paramref name="HighlightIndex"/> is -1 for none.</summary>
public sealed record DashboardChart(IReadOnlyList<DashboardBar> Bars, int HighlightIndex, string? Subtitle)
{
    public static DashboardChart Empty { get; } = new(Array.Empty<DashboardBar>(), -1, null);
}

/// <summary>Row of "Bán chạy hôm nay".</summary>
public sealed record DashboardTopRow(string Rank, string ProductName, string QuantityText, decimal Revenue);

/// <summary>Row of "Sắp hết hàng"; <see cref="StockTone"/> is red when out of stock, amber when below the minimum.</summary>
public sealed record DashboardLowRow(string ProductCode, string ProductName, decimal StockQuantity, decimal MinStockLevel)
{
    public Tone StockTone => StockQuantity <= 0 ? Tone.Danger : Tone.Warning;
}

/// <summary>Everything the dashboard shows from one <see cref="DashboardDto"/> (the shift card comes separately).</summary>
public sealed record DashboardBoard(
    DashboardHeader Header,
    DashboardCard Revenue,
    DashboardCard Invoices,
    DashboardCard Profit,
    DashboardProfitKind ProfitKind,
    DashboardChart Week,
    DashboardChart Hours,
    IReadOnlyList<DashboardTopRow> TopProducts,
    string? TopSubtitle,
    IReadOnlyList<DashboardLowRow> LowStock,
    string LowSubtitle)
{
    public const string RevenueCaption = "Doanh thu hôm nay";
    public const string InvoicesCaption = "Hóa đơn hôm nay";
    public const string ProfitCaption = "Lợi nhuận gộp hôm nay";
    public const string RefundCaption = "Tiền hoàn trả hôm nay";

    /// <param name="data">Today's figures.</param>
    /// <param name="fullName">Signed-in user's name for the greeting (may be null).</param>
    /// <param name="now">Local time of this refresh (greeting, "cập nhật lúc", current-hour highlight).</param>
    public static DashboardBoard From(DashboardDto data, string? fullName, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(data);
        var header = new DashboardHeader(
            DashboardHeader.GreetingFor(fullName, now),
            $"{DashboardFigures.LongDayLabel(data.Today)} · cập nhật lúc {Time(now)}",
            Tone.Muted);

        var revenue = new DashboardCard(
            RevenueCaption,
            Vnd.Format(data.NetRevenueToday),
            data.RevenueChangePercent is null ? $"Hôm qua: {Vnd.Format(data.NetRevenueYesterday)}" : null,
            Tone.Success,
            ShowTrend: true,
            TrendPercent: data.RevenueChangePercent,
            TrendComparedTo: "hôm qua");

        var invoices = new DashboardCard(
            InvoicesCaption,
            data.InvoiceCountToday.ToString("#,0", Vnd.Culture),
            data.InvoiceCountToday == 0 ? "Chưa có hóa đơn" : $"TB {Vnd.Format(data.AverageBasketToday)} / hóa đơn",
            Tone.Info);

        var (profit, kind) = data.GrossProfitToday is { } gross
            ? (new DashboardCard(
                ProfitCaption,
                Vnd.Format(gross),
                data.RefundTotalToday > 0 ? $"Đã hoàn trả {Vnd.Format(data.RefundTotalToday)}" : "Sau giá vốn, trước chi phí",
                gross < 0 ? Tone.Danger : Tone.Info), DashboardProfitKind.Profit)
            : (new DashboardCard(
                RefundCaption,
                Vnd.Format(data.RefundTotalToday),
                "Khách trả hàng",
                data.RefundTotalToday > 0 ? Tone.Warning : Tone.Normal), DashboardProfitKind.Refunds);

        return new DashboardBoard(
            header,
            revenue,
            invoices,
            profit,
            kind,
            WeekChart(data),
            HourChart(data, now.Hour),
            data.TopProductsToday
                .Select((p, i) => new DashboardTopRow(
                    (i + 1).ToString(Vnd.Culture),
                    p.ProductName,
                    $"{p.Quantity.ToString("#,0.###", Vnd.Culture)} {p.Unit}".TrimEnd(),
                    p.Revenue))
                .ToList(),
            data.TopProductsToday.Count == 0 ? null : $"Top {data.TopProductsToday.Count} theo doanh thu",
            data.LowStockItems.Select(i => new DashboardLowRow(i.ProductCode, i.ProductName, i.StockQuantity, i.MinStockLevel)).ToList(),
            LowStockSubtitle(data));
    }

    /// <summary>"14:05" whatever the machine's culture.</summary>
    public static string Time(DateTime time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static DashboardChart WeekChart(DashboardDto data)
    {
        var week = DashboardFigures.Week(data.Last7Days);
        var bars = data.Last7Days
            .Select(d => new DashboardBar(
                DashboardFigures.DayLabel(d.Date, data.Today),
                d.NetRevenue,
                $"{DashboardFigures.LongDayLabel(d.Date)}\nDoanh thu: {Vnd.Format(d.NetRevenue)}\nHóa đơn: {d.InvoiceCount:#,0}"))
            .ToList();
        var todayIndex = data.Last7Days.ToList().FindIndex(d => d.Date == data.Today);
        var subtitle = week.BestDay is null
            ? "Chưa có doanh thu"
            : $"Tổng {Vnd.Format(week.NetRevenue)} · TB {Vnd.Format(week.AveragePerDay)}/ngày · cao nhất {DashboardFigures.DayLabel(week.BestDay.Date, data.Today)}";
        return new DashboardChart(bars, todayIndex, subtitle);
    }

    private static DashboardChart HourChart(DashboardDto data, int currentHour)
    {
        var hours = DashboardFigures.HourWindow(data.TodayByHour);
        var bars = hours
            .Select(h => new DashboardBar(
                $"{h.Hour}h",
                h.SalesTotal,
                $"{DashboardFigures.HourRangeLabel(h.Hour)}\nDoanh số: {Vnd.Format(h.SalesTotal)}\nHóa đơn: {h.InvoiceCount:#,0}"))
            .ToList();
        var peak = DashboardFigures.PeakHour(data.TodayByHour);
        var subtitle = peak is null
            ? "Chưa có hóa đơn hôm nay"
            : $"Đông khách nhất {DashboardFigures.HourRangeLabel(peak.Hour)} · {Vnd.Format(peak.SalesTotal)} ({peak.InvoiceCount:#,0} HĐ)";
        return new DashboardChart(bars, hours.ToList().FindIndex(h => h.Hour == currentHour), subtitle);
    }

    private static string LowStockSubtitle(DashboardDto data)
    {
        var total = data.OutOfStockCount + data.LowStockCount;
        if (total == 0)
        {
            return "Tồn kho ổn định";
        }

        var text = $"{data.OutOfStockCount:#,0} hết hàng · {data.LowStockCount:#,0} sắp hết";
        return total > data.LowStockItems.Count ? $"{text} (hiện {data.LowStockItems.Count:#,0})" : text;
    }
}

/// <summary>The "Ca của bạn" card (users with <c>shifts.manage</c> or <c>shifts.supervise</c>).</summary>
public static class DashboardShiftCard
{
    public const string Caption = "Ca của bạn";

    public static DashboardCard Loading { get; } = DashboardCard.Placeholder(Caption, Tone.Warning);

    public static DashboardCard Failed { get; } = DashboardCard.Placeholder(Caption, Tone.Warning, "Không tải được thông tin ca");

    /// <param name="current">The user's drawer.</param>
    /// <param name="storeOffset">Store UTC offset for the opening time.</param>
    /// <param name="now">Current time, to remind about a shift left open too long.</param>
    public static DashboardCard From(CurrentShiftDto current, TimeSpan storeOffset, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Current is not { } shift)
        {
            return new DashboardCard(Caption, "Chưa mở ca", "Mở ca trước khi bán hàng", Tone.Warning);
        }

        var openFor = now - shift.OpenedAt;
        var openedAt = shift.OpenedAt.ToOffset(storeOffset);
        if (openFor >= Shifts.CurrentShiftState.LongOpenThreshold)
        {
            return new DashboardCard(
                $"Ca {shift.Code}",
                Vnd.Format(shift.ExpectedCash),
                $"Đã mở {(int)openFor.TotalHours} giờ (từ {openedAt.ToString("HH:mm dd/MM", CultureInfo.InvariantCulture)}) · nhớ kết ca",
                Tone.Warning);
        }

        return new DashboardCard(
            $"Ca {shift.Code}",
            Vnd.Format(shift.ExpectedCash),
            $"Tiền mặt trong két · mở lúc {openedAt.ToString("HH:mm", CultureInfo.InvariantCulture)}",
            Tone.Success);
    }
}
