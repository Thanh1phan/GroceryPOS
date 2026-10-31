namespace GroceryPOS.Application.Reports;

/// <summary>Totals of the 7-day trend on the dashboard.</summary>
/// <param name="NetRevenue">Doanh thu thuần 7 ngày.</param>
/// <param name="InvoiceCount">Hóa đơn 7 ngày.</param>
/// <param name="AveragePerDay">Doanh thu trung bình mỗi ngày (rounded to the đồng).</param>
/// <param name="BestDay">Day with the highest net revenue, or null when nothing was sold.</param>
public sealed record WeekSummary(decimal NetRevenue, int InvoiceCount, decimal AveragePerDay, DailyPointDto? BestDay);

/// <summary>
/// UI-free helpers that turn a <see cref="DashboardDto"/> into what the dashboard screen shows: Vietnamese day labels,
/// the hour window of the hourly chart, the peak hour and the week totals.
/// </summary>
public static class DashboardFigures
{
    /// <summary>Default opening hours of the hourly chart (06:00–22:00) – widened when sales happen outside them.</summary>
    public const int DefaultOpenHour = 6;

    public const int DefaultCloseHour = 22;

    private static readonly string[] DayNames = ["CN", "T2", "T3", "T4", "T5", "T6", "T7"];

    /// <summary>"Hôm nay", "Hôm qua", otherwise the short weekday and date: "T2 26/10", "CN 25/10".</summary>
    public static string DayLabel(DateOnly day, DateOnly today)
    {
        if (day == today)
        {
            return "Hôm nay";
        }

        if (day == today.AddDays(-1))
        {
            return "Hôm qua";
        }

        return $"{DayNames[(int)day.DayOfWeek]} {day:dd/MM}";
    }

    /// <summary>Full weekday name for tooltips: "Thứ hai, 26/10/2026", "Chủ nhật, 25/10/2026".</summary>
    public static string LongDayLabel(DateOnly day)
    {
        var name = day.DayOfWeek switch
        {
            DayOfWeek.Sunday => "Chủ nhật",
            DayOfWeek.Monday => "Thứ hai",
            DayOfWeek.Tuesday => "Thứ ba",
            DayOfWeek.Wednesday => "Thứ tư",
            DayOfWeek.Thursday => "Thứ năm",
            DayOfWeek.Friday => "Thứ sáu",
            _ => "Thứ bảy",
        };
        return $"{name}, {day:dd/MM/yyyy}";
    }

    /// <summary>
    /// Hours to chart: the store's usual hours (<paramref name="openHour"/> to <paramref name="closeHour"/>, inclusive),
    /// widened to include every hour that has sales, in hour order. Missing hours are filled with zero.
    /// </summary>
    public static IReadOnlyList<HourlyPointDto> HourWindow(
        IReadOnlyList<HourlyPointDto> hourly,
        int openHour = DefaultOpenHour,
        int closeHour = DefaultCloseHour)
    {
        ArgumentNullException.ThrowIfNull(hourly);
        openHour = Math.Clamp(openHour, 0, 23);
        closeHour = Math.Clamp(closeHour, openHour, 23);

        var active = hourly.Where(h => h.InvoiceCount > 0 || h.SalesTotal != 0).Select(h => h.Hour).ToList();
        var from = active.Count == 0 ? openHour : Math.Min(openHour, active.Min());
        var to = active.Count == 0 ? closeHour : Math.Max(closeHour, active.Max());
        var byHour = hourly.GroupBy(h => h.Hour).ToDictionary(g => g.Key, g => g.First());

        return Enumerable.Range(from, to - from + 1)
            .Select(hour => byHour.GetValueOrDefault(hour) ?? new HourlyPointDto(hour, 0, 0))
            .ToList();
    }

    /// <summary>Busiest hour by sales (earliest on a tie), or null before the first sale of the day.</summary>
    public static HourlyPointDto? PeakHour(IReadOnlyList<HourlyPointDto> hourly) =>
        hourly.Where(h => h.SalesTotal > 0)
            .OrderByDescending(h => h.SalesTotal)
            .ThenBy(h => h.Hour)
            .FirstOrDefault();

    /// <summary>"14h–15h".</summary>
    public static string HourRangeLabel(int hour) => $"{hour}h–{(hour + 1) % 24}h";

    public static WeekSummary Week(IReadOnlyList<DailyPointDto> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        var revenue = days.Sum(d => d.NetRevenue);
        var average = days.Count == 0 ? 0 : Math.Round(revenue / days.Count, 0, MidpointRounding.AwayFromZero);
        var best = days.Where(d => d.NetRevenue > 0)
            .OrderByDescending(d => d.NetRevenue)
            .ThenByDescending(d => d.Date)
            .FirstOrDefault();
        return new WeekSummary(revenue, days.Sum(d => d.InvoiceCount), average, best);
    }

    /// <summary>"Chào buổi sáng" before 11:00, "Chào buổi trưa" until 13:00, "Chào buổi chiều" until 18:00, else "Chào buổi tối".</summary>
    public static string Greeting(int hour) => hour switch
    {
        < 11 => "Chào buổi sáng",
        < 13 => "Chào buổi trưa",
        < 18 => "Chào buổi chiều",
        _ => "Chào buổi tối",
    };
}
