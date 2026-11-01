using System.Globalization;

namespace GroceryPOS.Application.Reports;

/// <summary>One bar of the revenue chart: a day, or a whole month for long periods.</summary>
public sealed record RevenueBucket(string Label, DateOnly From, DateOnly To, decimal NetRevenue, decimal GrossProfit, int InvoiceCount);

/// <summary>Presentation helpers for the report screens (UI-free so they can be tested).</summary>
public static class ReportFigures
{
    /// <summary>Periods longer than this are charted per month instead of per day.</summary>
    public const int MaxDailyBars = 62;

    /// <summary>
    /// Chart bars for the revenue report: one per day ("03/11"), or for periods over <see cref="MaxDailyBars"/> days
    /// one per calendar month ("T11/2026"), clipped to the period.
    /// </summary>
    public static IReadOnlyList<RevenueBucket> RevenueBuckets(RevenueReportDto report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var days = report.Days.Where(d => d.Date is not null).OrderBy(d => d.Date).ToList();

        if (report.Period.Days <= MaxDailyBars)
        {
            return days
                .Select(d => new RevenueBucket(
                    d.Date!.Value.ToString("dd/MM", CultureInfo.InvariantCulture),
                    d.Date.Value,
                    d.Date.Value,
                    d.NetRevenue,
                    d.GrossProfit,
                    d.InvoiceCount))
                .ToList();
        }

        return days
            .GroupBy(d => (d.Date!.Value.Year, d.Date.Value.Month))
            .Select(g => new RevenueBucket(
                $"T{g.Key.Month}/{g.Key.Year}",
                g.Min(d => d.Date!.Value),
                g.Max(d => d.Date!.Value),
                g.Sum(d => d.NetRevenue),
                g.Sum(d => d.GrossProfit),
                g.Sum(d => d.InvoiceCount)))
            .ToList();
    }

    /// <summary>The period of the same length right before <paramref name="period"/> – for "so với kỳ trước".</summary>
    public static ReportPeriod PreviousPeriod(ReportPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        var to = period.From.AddDays(-1);
        return new ReportPeriod(to.AddDays(-(period.Days - 1)), to);
    }

    /// <summary>Change in percent (1 decimal), or null when there is nothing to compare with.</summary>
    public static decimal? ChangePercent(decimal current, decimal previous) =>
        previous <= 0 ? null : Math.Round((current - previous) * 100 / previous, 1, MidpointRounding.AwayFromZero);

    /// <summary>The best day of a revenue report (most net revenue), or null without sales.</summary>
    public static RevenueRowDto? BestDay(RevenueReportDto report) =>
        report.Days.Where(d => d.NetRevenue > 0).OrderByDescending(d => d.NetRevenue).ThenBy(d => d.Date).FirstOrDefault();
}
