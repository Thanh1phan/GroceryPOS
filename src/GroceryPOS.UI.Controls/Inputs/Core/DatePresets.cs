namespace GroceryPOS.UI.Controls.Inputs.Core;

/// <summary>Inclusive range of store days.</summary>
public readonly record struct DateRange
{
    public DateRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.", nameof(to));
        }

        From = from;
        To = to;
    }

    public DateOnly From { get; }

    public DateOnly To { get; }

    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= From && date <= To;

    /// <summary>"19/10/2026" for a single day, otherwise "01/10/2026 – 19/10/2026".</summary>
    public override string ToString() =>
        From == To ? DateText.Format(From) : $"{DateText.Format(From)} – {DateText.Format(To)}";
}

/// <summary>Quick periods offered by report and list filters.</summary>
public enum DatePreset
{
    Today,
    Yesterday,
    Last7Days,
    ThisWeek,
    LastWeek,
    ThisMonth,
    LastMonth,
    Last30Days,
    ThisYear,
}

public static class DatePresets
{
    public static IReadOnlyList<DatePreset> All { get; } = Enum.GetValues<DatePreset>();

    public static string Label(DatePreset preset) => preset switch
    {
        DatePreset.Today => "Hôm nay",
        DatePreset.Yesterday => "Hôm qua",
        DatePreset.Last7Days => "7 ngày qua",
        DatePreset.ThisWeek => "Tuần này",
        DatePreset.LastWeek => "Tuần trước",
        DatePreset.ThisMonth => "Tháng này",
        DatePreset.LastMonth => "Tháng trước",
        DatePreset.Last30Days => "30 ngày qua",
        DatePreset.ThisYear => "Năm nay",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    /// <summary>Resolves a preset against <paramref name="today"/>. Weeks start on Monday; "this" periods end today.</summary>
    public static DateRange Resolve(DatePreset preset, DateOnly today)
    {
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        return preset switch
        {
            DatePreset.Today => new(today, today),
            DatePreset.Yesterday => new(today.AddDays(-1), today.AddDays(-1)),
            DatePreset.Last7Days => new(today.AddDays(-6), today),
            DatePreset.ThisWeek => new(monday, today),
            DatePreset.LastWeek => new(monday.AddDays(-7), monday.AddDays(-1)),
            DatePreset.ThisMonth => new(firstOfMonth, today),
            DatePreset.LastMonth => new(firstOfMonth.AddMonths(-1), firstOfMonth.AddDays(-1)),
            DatePreset.Last30Days => new(today.AddDays(-29), today),
            DatePreset.ThisYear => new(new DateOnly(today.Year, 1, 1), today),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };
    }

    /// <summary>The first preset that resolves to exactly <paramref name="range"/>, so a picker can highlight it.</summary>
    public static DatePreset? Match(DateRange range, DateOnly today)
    {
        foreach (var preset in All)
        {
            if (Resolve(preset, today) == range)
            {
                return preset;
            }
        }

        return null;
    }
}
