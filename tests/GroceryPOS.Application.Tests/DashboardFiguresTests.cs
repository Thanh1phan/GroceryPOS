using GroceryPOS.Application.Reports;

namespace GroceryPOS.Application.Tests;

public class DashboardFiguresTests
{
    private static readonly DateOnly Today = new(2026, 10, 31); // Saturday

    [Theory]
    [InlineData(2026, 10, 31, "Hôm nay")]
    [InlineData(2026, 10, 30, "Hôm qua")]
    [InlineData(2026, 10, 26, "T2 26/10")]
    [InlineData(2026, 10, 25, "CN 25/10")]
    public void Day_labels_are_vietnamese(int year, int month, int day, string expected) =>
        Assert.Equal(expected, DashboardFigures.DayLabel(new DateOnly(year, month, day), Today));

    [Fact]
    public void Long_day_label_names_the_weekday() =>
        Assert.Equal("Chủ nhật, 25/10/2026", DashboardFigures.LongDayLabel(new DateOnly(2026, 10, 25)));

    [Fact]
    public void Hour_window_defaults_to_opening_hours_when_nothing_sold()
    {
        var window = DashboardFigures.HourWindow(Hours());

        Assert.Equal(17, window.Count);
        Assert.Equal(6, window[0].Hour);
        Assert.Equal(22, window[^1].Hour);
    }

    [Fact]
    public void Hour_window_widens_to_sales_outside_opening_hours_and_fills_gaps()
    {
        var hourly = Hours((5, 120_000m), (23, 40_000m));
        hourly.RemoveAll(h => h.Hour == 12);

        var window = DashboardFigures.HourWindow(hourly);

        Assert.Equal(5, window[0].Hour);
        Assert.Equal(23, window[^1].Hour);
        Assert.Equal(Enumerable.Range(5, 19), window.Select(h => h.Hour));
        Assert.Equal(120_000m, window[0].SalesTotal);
        Assert.Equal(0, window.Single(h => h.Hour == 12).SalesTotal);
    }

    [Fact]
    public void Peak_hour_is_the_busiest_and_earliest_on_a_tie()
    {
        Assert.Null(DashboardFigures.PeakHour(Hours()));
        var peak = DashboardFigures.PeakHour(Hours((9, 300_000m), (17, 500_000m), (18, 500_000m)));
        Assert.Equal(17, peak!.Hour);
        Assert.Equal("17h–18h", DashboardFigures.HourRangeLabel(peak.Hour));
        Assert.Equal("23h–0h", DashboardFigures.HourRangeLabel(23));
    }

    [Fact]
    public void Week_summary_totals_and_best_day()
    {
        var days = Enumerable.Range(0, 7)
            .Select(i => new DailyPointDto(Today.AddDays(i - 6), i == 3 ? 900_000m : 100_001m, i == 3 ? 9 : 1))
            .ToList();

        var week = DashboardFigures.Week(days);

        Assert.Equal(1_500_006m, week.NetRevenue);
        Assert.Equal(15, week.InvoiceCount);
        Assert.Equal(214_287m, week.AveragePerDay);
        Assert.Equal(Today.AddDays(-3), week.BestDay!.Date);
    }

    [Fact]
    public void Week_without_sales_has_no_best_day()
    {
        var week = DashboardFigures.Week([new DailyPointDto(Today, 0, 0)]);
        Assert.Null(week.BestDay);
        Assert.Equal(0, week.AveragePerDay);
    }

    [Theory]
    [InlineData(7, "Chào buổi sáng")]
    [InlineData(12, "Chào buổi trưa")]
    [InlineData(15, "Chào buổi chiều")]
    [InlineData(20, "Chào buổi tối")]
    public void Greeting_follows_the_time_of_day(int hour, string expected) =>
        Assert.Equal(expected, DashboardFigures.Greeting(hour));

    private static List<HourlyPointDto> Hours(params (int Hour, decimal Sales)[] sales) =>
        Enumerable.Range(0, 24)
            .Select(h => sales.FirstOrDefault(s => s.Hour == h) is { Sales: > 0 } s ? new HourlyPointDto(h, s.Sales, 1) : new HourlyPointDto(h, 0, 0))
            .ToList();
}
