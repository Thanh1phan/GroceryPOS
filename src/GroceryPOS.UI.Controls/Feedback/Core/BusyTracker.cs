namespace GroceryPOS.UI.Controls.Feedback.Core;

/// <summary>
/// Decides when a loading overlay is on screen. Nested operations share one overlay (reference-counted);
/// the overlay appears only after <see cref="ShowDelay"/> so quick loads never flash, and once shown it
/// stays at least <see cref="MinimumVisible"/> so it never flickers. Time comes from the caller
/// (a <c>Stopwatch</c> in the control), which keeps the rules testable.
/// </summary>
public sealed class BusyTracker
{
    private readonly List<(long Id, string? Message)> _operations = new();
    private long _nextId = 1;
    private TimeSpan _busySince;
    private TimeSpan _shownAt;

    public BusyTracker(TimeSpan showDelay, TimeSpan minimumVisible)
    {
        if (showDelay < TimeSpan.Zero || minimumVisible < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(showDelay), "Thời gian không được âm.");
        }

        ShowDelay = showDelay;
        MinimumVisible = minimumVisible;
    }

    /// <summary>Default rules: show after 250 ms, keep for at least 400 ms.</summary>
    public static BusyTracker CreateDefault() => new(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(400));

    public TimeSpan ShowDelay { get; }

    public TimeSpan MinimumVisible { get; }

    public bool IsBusy => _operations.Count > 0;

    public int Depth => _operations.Count;

    public bool IsVisible { get; private set; }

    /// <summary>Message of the most recent running operation that has one ("Đang xuất Excel…").</summary>
    public string? Message => _operations.LastOrDefault(o => !string.IsNullOrWhiteSpace(o.Message)).Message;

    /// <summary>Starts an operation; pass the returned id to <see cref="End"/>.</summary>
    public long Begin(TimeSpan now, string? message = null)
    {
        if (_operations.Count == 0 && !IsVisible)
        {
            _busySince = now;
        }

        var id = _nextId++;
        _operations.Add((id, message));
        Evaluate(now);
        return id;
    }

    /// <summary>Ends an operation. Unknown ids (already ended) are ignored.</summary>
    public void End(long id, TimeSpan now)
    {
        _operations.RemoveAll(o => o.Id == id);
        Evaluate(now);
    }

    /// <summary>Re-evaluates visibility at <paramref name="now"/> and returns it.</summary>
    public bool Evaluate(TimeSpan now)
    {
        if (IsBusy && !IsVisible && now - _busySince >= ShowDelay)
        {
            IsVisible = true;
            _shownAt = now;
        }
        else if (!IsBusy && IsVisible && now - _shownAt >= MinimumVisible)
        {
            IsVisible = false;
        }

        return IsVisible;
    }

    /// <summary>How long until visibility can change on its own (null when nothing is pending); the host's timer interval.</summary>
    public TimeSpan? NextChangeIn(TimeSpan now)
    {
        if (IsBusy && !IsVisible)
        {
            return Max(TimeSpan.Zero, ShowDelay - (now - _busySince));
        }

        if (!IsBusy && IsVisible)
        {
            return Max(TimeSpan.Zero, MinimumVisible - (now - _shownAt));
        }

        return null;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

/// <summary>Indeterminate spinner arc: a rotating arc whose length grows and shrinks.</summary>
public static class SpinnerGeometry
{
    public const float MinSweep = 30f;
    public const float MaxSweep = 270f;

    /// <summary>One full rotation of the whole spinner.</summary>
    public static readonly TimeSpan RotationPeriod = TimeSpan.FromMilliseconds(1400);

    /// <summary>One grow/shrink cycle of the arc.</summary>
    public static readonly TimeSpan SweepPeriod = TimeSpan.FromMilliseconds(1800);

    /// <summary>Start angle (degrees, 0–360) and sweep of the arc at time <paramref name="t"/>.</summary>
    public static (float Start, float Sweep) ArcAt(TimeSpan t)
    {
        var rotation = Fraction(t, RotationPeriod) * 360d;
        var phase = Fraction(t, SweepPeriod);
        var wave = (1 - Math.Cos(phase * 2 * Math.PI)) / 2; // 0 → 1 → 0, eased at the ends
        var sweep = MinSweep + (MaxSweep - MinSweep) * wave;
        return ((float)(rotation % 360d), (float)sweep);
    }

    private static double Fraction(TimeSpan t, TimeSpan period)
    {
        var f = t.TotalMilliseconds % period.TotalMilliseconds / period.TotalMilliseconds;
        return f < 0 ? f + 1 : f;
    }
}
