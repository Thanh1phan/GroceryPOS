using GroceryPOS.UI.Controls.Inputs.Core;

namespace GroceryPOS.UI.Controls.Tests;

public class DateInputTests
{
    // Monday, 19 October 2026.
    private static readonly DateOnly Today = new(2026, 10, 19);

    [Theory]
    [InlineData("19/10/2026", 2026, 10, 19)]
    [InlineData("1/2/2026", 2026, 2, 1)]
    [InlineData("05-11-2026", 2026, 11, 5)]
    [InlineData("05.11.26", 2026, 11, 5)]
    [InlineData("25/12", 2026, 12, 25)]
    [InlineData("3", 2026, 10, 3)]
    [InlineData("29022028", 2028, 2, 29)]
    [InlineData("010126", 2026, 1, 1)]
    [InlineData(" 1 / 3 / 2025 ", 2025, 3, 1)]
    public void Parses_day_month_year(string text, int year, int month, int day)
    {
        Assert.True(DateText.TryParse(text, Today, out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("hôm nay", 0)]
    [InlineData("Hom Nay", 0)]
    [InlineData("nay", 0)]
    [InlineData("hôm qua", -1)]
    [InlineData("mai", 1)]
    [InlineData("+3", 3)]
    [InlineData("-7", -7)]
    public void Parses_shortcuts_relative_to_today(string text, int days)
    {
        Assert.True(DateText.TryParse(text, Today, out var date));
        Assert.Equal(Today.AddDays(days), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("31/02/2026")]
    [InlineData("29/02/2026")]
    [InlineData("0/1/2026")]
    [InlineData("12/13/2026")]
    [InlineData("1/1/202")]
    [InlineData("abc")]
    [InlineData("2026-10-19")]
    [InlineData("1/1/1800")]
    [InlineData("+99999")]
    [InlineData("1234567")]
    public void Rejects_invalid_dates(string text) =>
        Assert.False(DateText.TryParse(text, Today, out _));

    [Fact]
    public void Formats_as_dd_mm_yyyy() =>
        Assert.Equal("05/01/2026", DateText.Format(new DateOnly(2026, 1, 5)));

    [Theory]
    [InlineData(DatePreset.Today, "2026-10-19", "2026-10-19")]
    [InlineData(DatePreset.Yesterday, "2026-10-18", "2026-10-18")]
    [InlineData(DatePreset.Last7Days, "2026-10-13", "2026-10-19")]
    [InlineData(DatePreset.ThisWeek, "2026-10-19", "2026-10-19")]
    [InlineData(DatePreset.LastWeek, "2026-10-12", "2026-10-18")]
    [InlineData(DatePreset.ThisMonth, "2026-10-01", "2026-10-19")]
    [InlineData(DatePreset.LastMonth, "2026-09-01", "2026-09-30")]
    [InlineData(DatePreset.Last30Days, "2026-09-20", "2026-10-19")]
    [InlineData(DatePreset.ThisYear, "2026-01-01", "2026-10-19")]
    public void Resolves_presets(DatePreset preset, string from, string to)
    {
        var range = DatePresets.Resolve(preset, Today);

        Assert.Equal(DateOnly.Parse(from), range.From);
        Assert.Equal(DateOnly.Parse(to), range.To);
    }

    [Fact]
    public void Weeks_start_on_monday_even_on_sunday()
    {
        var sunday = new DateOnly(2026, 10, 25);

        Assert.Equal(new DateRange(new DateOnly(2026, 10, 19), sunday), DatePresets.Resolve(DatePreset.ThisWeek, sunday));
        Assert.Equal(new DateRange(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 18)), DatePresets.Resolve(DatePreset.LastWeek, sunday));
    }

    [Fact]
    public void Last_month_handles_january()
    {
        var range = DatePresets.Resolve(DatePreset.LastMonth, new DateOnly(2027, 1, 10));

        Assert.Equal(new DateRange(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)), range);
        Assert.Equal(31, range.Days);
    }

    [Fact]
    public void Match_finds_the_first_equal_preset()
    {
        // On a Monday, "today" and "this week" are the same range; the first one wins.
        Assert.Equal(DatePreset.Today, DatePresets.Match(new DateRange(Today, Today), Today));
        Assert.Equal(DatePreset.LastMonth, DatePresets.Match(new DateRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), Today));
        Assert.Null(DatePresets.Match(new DateRange(new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 30)), Today));
    }

    [Fact]
    public void Range_rejects_end_before_start_and_formats()
    {
        Assert.Throws<ArgumentException>(() => new DateRange(Today, Today.AddDays(-1)));
        Assert.Equal("19/10/2026", new DateRange(Today, Today).ToString());
        Assert.Equal("01/10/2026 – 19/10/2026", DatePresets.Resolve(DatePreset.ThisMonth, Today).ToString());
        Assert.True(DatePresets.Resolve(DatePreset.ThisMonth, Today).Contains(new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Every_preset_has_a_vietnamese_label() =>
        Assert.All(DatePresets.All, p => Assert.False(string.IsNullOrWhiteSpace(DatePresets.Label(p))));
}
