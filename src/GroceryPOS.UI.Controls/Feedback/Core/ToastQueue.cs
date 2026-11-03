namespace GroceryPOS.UI.Controls.Feedback.Core;

/// <summary>What to show in a toast. <paramref name="Duration"/> null picks <see cref="ToastDurations.For"/>; <see cref="TimeSpan.Zero"/> keeps it until closed.</summary>
public sealed record ToastRequest(
    FeedbackKind Kind,
    string Message,
    string? Title = null,
    TimeSpan? Duration = null,
    string? ActionText = null);

/// <summary>A toast in the queue (visible or waiting). Mutable state is owned by <see cref="ToastQueue"/>.</summary>
public sealed class Toast
{
    internal Toast(long id, ToastRequest request, TimeSpan duration)
    {
        Id = id;
        Kind = request.Kind;
        Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        Message = request.Message.Trim();
        ActionText = string.IsNullOrWhiteSpace(request.ActionText) ? null : request.ActionText.Trim();
        Duration = duration;
        Remaining = duration;
        Repeat = 1;
    }

    public long Id { get; }

    public FeedbackKind Kind { get; }

    public string? Title { get; }

    public string Message { get; }

    /// <summary>Optional link-style button ("Mở file", "Xem").</summary>
    public string? ActionText { get; }

    /// <summary>Display time; <see cref="TimeSpan.Zero"/> = stays until the user closes it.</summary>
    public TimeSpan Duration { get; }

    public TimeSpan Remaining { get; internal set; }

    /// <summary>How many times the same toast was raised while it was still on screen (shown as "×3").</summary>
    public int Repeat { get; internal set; }

    public bool IsSticky => Duration <= TimeSpan.Zero;

    /// <summary>Share of the display time left, 1 → 0 (always 1 for sticky toasts); drives the progress line.</summary>
    public double Progress => IsSticky ? 1d : Math.Clamp(Remaining.TotalMilliseconds / Duration.TotalMilliseconds, 0d, 1d);

    internal bool SameContentAs(ToastRequest request) =>
        Kind == request.Kind
        && string.Equals(Title, string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(), StringComparison.Ordinal)
        && string.Equals(Message, request.Message.Trim(), StringComparison.Ordinal);
}

/// <summary>Default display times: short for confirmations, longer for problems and long texts.</summary>
public static class ToastDurations
{
    /// <summary>Upper bound for automatic durations.</summary>
    public static readonly TimeSpan Max = TimeSpan.FromSeconds(15);

    public static TimeSpan For(FeedbackKind kind, string? message)
    {
        var baseSeconds = kind switch
        {
            FeedbackKind.Success => 3.5,
            FeedbackKind.Warning => 6,
            FeedbackKind.Error or FeedbackKind.Danger => 8,
            _ => 4.5,
        };

        // Roughly 15 characters per second of reading time beyond the first 60 characters.
        var extraChars = Math.Max(0, (message?.Length ?? 0) - 60);
        var total = TimeSpan.FromSeconds(baseSeconds + extraChars / 15d);
        return total > Max ? Max : total;
    }
}

/// <summary>
/// UI-free toast queue: at most <see cref="MaxVisible"/> toasts on screen, the rest wait in order.
/// A toast identical to one already queued restarts that toast and bumps its <see cref="Toast.Repeat"/>
/// instead of stacking copies (e.g. scanning an unknown barcode five times). Time is pushed in through
/// <see cref="Advance"/>, so the host decides the clock and tests stay deterministic.
/// </summary>
public sealed class ToastQueue
{
    private readonly List<Toast> _visible = new();
    private readonly LinkedList<Toast> _pending = new();
    private long _nextId = 1;

    public ToastQueue(int maxVisible = 3, int maxPending = 20)
    {
        if (maxVisible < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxVisible), maxVisible, "Phải hiển thị ít nhất 1 thông báo.");
        }

        if (maxPending < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPending), maxPending, "Không được âm.");
        }

        MaxVisible = maxVisible;
        MaxPending = maxPending;
    }

    public int MaxVisible { get; }

    public int MaxPending { get; }

    /// <summary>Toasts on screen, oldest first.</summary>
    public IReadOnlyList<Toast> Visible => _visible;

    public int PendingCount => _pending.Count;

    /// <summary>While paused (pointer over the stack) no toast counts down.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Changes whenever toasts are added, removed or repeated, so the host knows to re-layout.</summary>
    public int Version { get; private set; }

    public Toast Enqueue(ToastRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Nội dung thông báo không được để trống.", nameof(request));
        }

        var duplicate = _visible.FirstOrDefault(t => t.SameContentAs(request))
            ?? _pending.FirstOrDefault(t => t.SameContentAs(request));
        if (duplicate is not null)
        {
            duplicate.Repeat++;
            duplicate.Remaining = duplicate.Duration;
            Version++;
            return duplicate;
        }

        var duration = request.Duration ?? ToastDurations.For(request.Kind, request.Message);
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var toast = new Toast(_nextId++, request, duration);
        if (_visible.Count < MaxVisible)
        {
            _visible.Add(toast);
        }
        else
        {
            _pending.AddLast(toast);
            TrimPending();
        }

        Version++;
        return toast;
    }

    /// <summary>Closes a toast (close button, action clicked). Returns false when it is no longer queued.</summary>
    public bool Dismiss(long id)
    {
        var index = _visible.FindIndex(t => t.Id == id);
        if (index >= 0)
        {
            _visible.RemoveAt(index);
            Promote();
            Version++;
            return true;
        }

        for (var node = _pending.First; node is not null; node = node.Next)
        {
            if (node.Value.Id == id)
            {
                _pending.Remove(node);
                Version++;
                return true;
            }
        }

        return false;
    }

    /// <summary>Counts visible toasts down by <paramref name="elapsed"/>; returns the ones that expired.</summary>
    public IReadOnlyList<Toast> Advance(TimeSpan elapsed)
    {
        if (IsPaused || elapsed <= TimeSpan.Zero || _visible.Count == 0)
        {
            return Array.Empty<Toast>();
        }

        List<Toast>? expired = null;
        foreach (var toast in _visible)
        {
            if (toast.IsSticky)
            {
                continue;
            }

            toast.Remaining -= elapsed;
            if (toast.Remaining <= TimeSpan.Zero)
            {
                toast.Remaining = TimeSpan.Zero;
                (expired ??= new List<Toast>()).Add(toast);
            }
        }

        if (expired is null)
        {
            return Array.Empty<Toast>();
        }

        _visible.RemoveAll(expired.Contains);
        Promote();
        Version++;
        return expired;
    }

    public void Clear()
    {
        if (_visible.Count == 0 && _pending.Count == 0)
        {
            return;
        }

        _visible.Clear();
        _pending.Clear();
        Version++;
    }

    private void Promote()
    {
        while (_visible.Count < MaxVisible && _pending.First?.Value is { } next)
        {
            _pending.RemoveFirst();
            next.Remaining = next.Duration; // the clock starts when it becomes visible
            _visible.Add(next);
        }
    }

    /// <summary>Drops the oldest waiting toasts beyond the limit, keeping errors as long as possible.</summary>
    private void TrimPending()
    {
        while (_pending.Count > MaxPending)
        {
            var victim = _pending.FirstOrDefault(t => t.Kind is not (FeedbackKind.Error or FeedbackKind.Danger)) ?? _pending.First!.Value;
            _pending.Remove(victim);
        }
    }
}
