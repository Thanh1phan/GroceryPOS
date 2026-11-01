using GroceryPOS.UI.Controls.Inputs.Core;

namespace GroceryPOS.UI.Controls.Tests;

public sealed class DateRangeSelectionTests
{
    // Thursday 19/11/2026.
    private static readonly DateOnly Today = new(2026, 11, 19);

    private static DateOnly D(int month, int day, int year = 2026) => new(year, month, day);

    [Fact]
    public void Starts_on_the_requested_preset()
    {
        var selection = new DateRangeSelection(Today);

        Assert.Equal(DatePreset.ThisMonth, selection.Preset);
        Assert.Equal(new DateRange(D(11, 1), Today), selection.Range);
        Assert.Equal("Tháng này", selection.PresetLabel);
        Assert.Equal("Tháng này: 01/11/2026 – 19/11/2026", selection.Describe());
        Assert.False(selection.CanShiftForward);
    }

    [Fact]
    public void Selecting_a_preset_reports_changes_only_once()
    {
        var selection = new DateRangeSelection(Today, DatePreset.Today);

        Assert.True(selection.SelectPreset(DatePreset.LastWeek));
        Assert.Equal(new DateRange(D(11, 9), D(11, 15)), selection.Range);
        Assert.False(selection.SelectPreset(DatePreset.LastWeek));
    }

    [Fact]
    public void Custom_edits_rematch_presets_or_become_custom()
    {
        var selection = new DateRangeSelection(Today, DatePreset.Today);

        Assert.True(selection.SetFrom(D(11, 16)));
        Assert.Equal(DatePreset.ThisWeek, selection.Preset);

        Assert.True(selection.SetFrom(D(11, 3)));
        Assert.True(selection.IsCustom);
        Assert.Equal(DateRangeSelection.CustomLabel, selection.PresetLabel);
        Assert.Equal("03/11/2026 – 19/11/2026", selection.Describe());
    }

    [Fact]
    public void From_after_to_collapses_to_a_single_day()
    {
        var selection = new DateRangeSelection(Today, DatePreset.LastWeek);

        selection.SetFrom(D(11, 17));

        Assert.Equal(new DateRange(D(11, 17), D(11, 17)), selection.Range);
    }

    [Fact]
    public void To_before_from_collapses_to_a_single_day()
    {
        var selection = new DateRangeSelection(Today, DatePreset.ThisWeek);

        selection.SetTo(D(11, 10));

        Assert.Equal(new DateRange(D(11, 10), D(11, 10)), selection.Range);
    }

    [Fact]
    public void Dates_never_go_past_today()
    {
        var selection = new DateRangeSelection(Today, DatePreset.LastMonth);

        selection.SetTo(D(12, 31));
        Assert.Equal(Today, selection.Range.To);

        selection.SetFrom(D(12, 5));
        Assert.Equal(new DateRange(Today, Today), selection.Range);
        Assert.Equal(DatePreset.Today, selection.Preset);
    }

    [Fact]
    public void Long_ranges_are_cut_from_the_untouched_side()
    {
        var selection = new DateRangeSelection(Today, DatePreset.Today, maxDays: 31);

        selection.SetFrom(D(9, 1));
        Assert.Equal(new DateRange(D(9, 1), D(10, 1)), selection.Range);

        selection.SetTo(D(11, 19));
        Assert.Equal(new DateRange(D(10, 20), D(11, 19)), selection.Range);
        Assert.Equal(31, selection.Range.Days);
    }

    [Fact]
    public void SetRange_swaps_reversed_input_and_clamps()
    {
        var selection = new DateRangeSelection(Today, DatePreset.Today, maxDays: 10);

        selection.SetRange(D(11, 30), D(11, 1));

        Assert.Equal(new DateRange(D(11, 10), Today), selection.Range);
    }

    [Fact]
    public void Shifting_back_from_this_month_gives_whole_previous_months()
    {
        var selection = new DateRangeSelection(Today, DatePreset.ThisMonth);

        Assert.True(selection.Shift(-1));
        Assert.Equal(new DateRange(D(10, 1), D(10, 31)), selection.Range);
        Assert.Equal(DatePreset.LastMonth, selection.Preset);

        selection.Shift(-1);
        Assert.Equal(new DateRange(D(9, 1), D(9, 30)), selection.Range);

        selection.Shift(1);
        selection.Shift(1);
        Assert.Equal(new DateRange(D(11, 1), Today), selection.Range);
        Assert.False(selection.CanShiftForward);
        Assert.False(selection.Shift(1));
    }

    [Fact]
    public void Shifting_whole_weeks_and_years()
    {
        var week = new DateRangeSelection(Today, DatePreset.ThisWeek);
        week.Shift(-1);
        Assert.Equal(new DateRange(D(11, 9), D(11, 15)), week.Range);
        week.Shift(-1);
        Assert.Equal(new DateRange(D(11, 2), D(11, 8)), week.Range);

        var year = new DateRangeSelection(Today, DatePreset.ThisYear);
        year.Shift(-1);
        Assert.Equal(new DateRange(D(1, 1, 2025), D(12, 31, 2025)), year.Range);
    }

    [Fact]
    public void Shifting_a_day_count_range_keeps_its_length()
    {
        var selection = new DateRangeSelection(Today, DatePreset.Last7Days);

        selection.Shift(-1);

        Assert.Equal(new DateRange(D(11, 6), D(11, 12)), selection.Range);
        Assert.True(selection.IsCustom);
    }

    [Fact]
    public void Shifting_today_back_moves_one_day_even_on_a_month_start()
    {
        var firstOfMonth = new DateRangeSelection(D(11, 1), DatePreset.Today);

        firstOfMonth.Shift(-1);

        Assert.Equal(new DateRange(D(10, 31), D(10, 31)), firstOfMonth.Range);
        Assert.Equal(DatePreset.Yesterday, firstOfMonth.Preset);
    }

    [Fact]
    public void Rebase_moves_presets_and_keeps_custom_ranges()
    {
        var preset = new DateRangeSelection(Today, DatePreset.Today);
        Assert.True(preset.Rebase(Today.AddDays(1)));
        Assert.Equal(new DateRange(D(11, 20), D(11, 20)), preset.Range);

        var custom = new DateRangeSelection(Today, DatePreset.Today);
        custom.SetRange(D(11, 3), D(11, 5));
        custom.Rebase(Today.AddDays(1));
        Assert.Equal(new DateRange(D(11, 3), D(11, 5)), custom.Range);
    }

    [Fact]
    public void Max_days_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DateRangeSelection(Today, maxDays: 0));
    }
}
