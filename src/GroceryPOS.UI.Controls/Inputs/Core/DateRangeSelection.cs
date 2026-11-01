namespace GroceryPOS.UI.Controls.Inputs.Core;

/// <summary>
/// UI-free state of a date-range picker: a quick preset (Hôm nay, Tháng này, …) or a custom from/to pair,
/// kept valid on every edit. Rules:
/// <list type="bullet">
/// <item>Dates never go past <see cref="MaxDate"/> (today by default – reports do not look into the future).</item>
/// <item>Moving "from" past "to" (or "to" before "from") collapses the range to that single day.</item>
/// <item>A range longer than <see cref="MaxDays"/> is shortened from the side the user did not touch.</item>
/// <item>After a custom edit the matching preset is highlighted again, so 01/11 – 07/11 typed by hand still reads "Tuần này".</item>
/// <item><see cref="Shift"/> steps to the previous/next period of the same kind: whole years, whole months,
/// whole weeks (Monday-based) or otherwise the same number of days.</item>
/// </list>
/// All mutators return true when the range or the preset changed.
/// </summary>
public sealed class DateRangeSelection
{
    public const int DefaultMaxDays = 366;

    private DateOnly _today;

    public DateRangeSelection(DateOnly today, DatePreset initial = DatePreset.ThisMonth, int maxDays = DefaultMaxDays)
    {
        if (maxDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDays), maxDays, "Số ngày tối đa phải lớn hơn 0.");
        }

        _today = today;
        MaxDays = maxDays;
        Preset = initial;
        Range = DatePresets.Resolve(initial, today);
    }

    public DateOnly Today => _today;

    /// <summary>Latest selectable day; equals <see cref="Today"/>.</summary>
    public DateOnly MaxDate => _today;

    public int MaxDays { get; }

    public DateRange Range { get; private set; }

    /// <summary>The preset that resolves to <see cref="Range"/> today, or null for a custom ("Tùy chọn") range.</summary>
    public DatePreset? Preset { get; private set; }

    public bool IsCustom => Preset is null;

    /// <summary>"Tháng này" or "Tùy chọn".</summary>
    public string PresetLabel => Preset is { } preset ? DatePresets.Label(preset) : CustomLabel;

    public const string CustomLabel = "Tùy chọn";

    /// <summary>"Tháng này: 01/11/2026 – 30/11/2026" – used for report subtitles and export headers.</summary>
    public string Describe() => Preset is { } preset ? $"{DatePresets.Label(preset)}: {Range}" : Range.ToString();

    public bool CanShiftForward => Range.To < MaxDate;

    public bool SelectPreset(DatePreset preset)
    {
        var range = DatePresets.Resolve(preset, _today);
        var changed = range != Range || Preset != preset;
        Range = range;
        Preset = preset;
        return changed;
    }

    public bool SetFrom(DateOnly from)
    {
        from = Min(from, MaxDate);
        var to = Range.To < from ? from : Range.To;
        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            to = from.AddDays(MaxDays - 1);
        }

        return Apply(new DateRange(from, to));
    }

    public bool SetTo(DateOnly to)
    {
        to = Min(to, MaxDate);
        var from = Range.From > to ? to : Range.From;
        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            from = to.AddDays(-(MaxDays - 1));
        }

        return Apply(new DateRange(from, to));
    }

    /// <summary>Sets both ends at once (swapped if given backwards), clamped to <see cref="MaxDate"/> and <see cref="MaxDays"/>.</summary>
    public bool SetRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        to = Min(to, MaxDate);
        from = Min(from, to);
        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            from = to.AddDays(-(MaxDays - 1));
        }

        return Apply(new DateRange(from, to));
    }

    /// <summary>
    /// Steps one period back (<paramref name="direction"/> &lt; 0) or forward (&gt; 0). A forward step that would
    /// start after <see cref="MaxDate"/> does nothing; one that would end after it is cut at <see cref="MaxDate"/>.
    /// </summary>
    public bool Shift(int direction)
    {
        if (direction == 0 || (direction > 0 && !CanShiftForward))
        {
            return false;
        }

        var step = Math.Sign(direction);
        var (from, to) = (Range.From, Range.To);
        DateOnly newFrom;
        DateOnly newTo;

        if (IsWholeYears(from, to, out var years))
        {
            newFrom = from.AddYears(step * years);
            newTo = new DateOnly(newFrom.Year + years - 1, 12, 31);
        }
        else if (IsWholeMonths(from, to, out var months))
        {
            newFrom = from.AddMonths(step * months);
            newTo = EndOfMonth(newFrom.AddMonths(months - 1));
        }
        else if (IsWholeWeek(from, to))
        {
            newFrom = from.AddDays(step * 7);
            newTo = newFrom.AddDays(6);
        }
        else
        {
            var days = Range.Days;
            newFrom = from.AddDays(step * days);
            newTo = to.AddDays(step * days);
        }

        if (newFrom > MaxDate)
        {
            return false;
        }

        return Apply(new DateRange(newFrom, Min(newTo, MaxDate)));
    }

    /// <summary>
    /// Moves "today" (the app stayed open past midnight). A preset follows the new day; a custom range stays put
    /// unless it now ends after the new maximum.
    /// </summary>
    public bool Rebase(DateOnly today)
    {
        if (today == _today)
        {
            return false;
        }

        _today = today;
        if (Preset is { } preset)
        {
            Range = DatePresets.Resolve(preset, today);
            return true;
        }

        if (Range.To > MaxDate)
        {
            return SetRange(Range.From, MaxDate);
        }

        Preset = DatePresets.Match(Range, today);
        return Preset is not null;
    }

    private bool Apply(DateRange range)
    {
        var preset = DatePresets.Match(range, _today);
        var changed = range != Range || preset != Preset;
        Range = range;
        Preset = preset;
        return changed;
    }

    // "Whole" also accepts the running period of the Tuần này / Tháng này / Năm nay presets (cut short at
    // today), so stepping back from "this month" lands on the whole previous month.
    private bool IsWholeYears(DateOnly from, DateOnly to, out int years)
    {
        years = to.Year - from.Year + 1;
        return from.Month == 1 && from.Day == 1
            && ((to.Month == 12 && to.Day == 31) || (Preset == DatePreset.ThisYear && years == 1));
    }

    private bool IsWholeMonths(DateOnly from, DateOnly to, out int months)
    {
        months = (to.Year * 12 + to.Month) - (from.Year * 12 + from.Month) + 1;
        return from.Day == 1 && (to == EndOfMonth(to) || (Preset == DatePreset.ThisMonth && months == 1));
    }

    private bool IsWholeWeek(DateOnly from, DateOnly to)
    {
        if (from.DayOfWeek != DayOfWeek.Monday)
        {
            return false;
        }

        var days = to.DayNumber - from.DayNumber + 1;
        return days == 7 || (Preset == DatePreset.ThisWeek && days < 7);
    }

    private static DateOnly EndOfMonth(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;
}
