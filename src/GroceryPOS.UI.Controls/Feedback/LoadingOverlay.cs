using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Feedback.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Feedback;

/// <summary>
/// Covers a screen or panel while it loads or saves: the content stays visible but dimmed (a snapshot of
/// it is painted under a scrim), a spinner card shows what is happening, and mouse/keyboard input to the
/// covered controls is blocked. Quick operations never flash it (<see cref="BusyTracker"/>: shown after
/// 250 ms, kept at least 400 ms) and nested operations share one overlay.
/// </summary>
/// <example><code>
/// var report = await LoadingOverlay.RunAsync(this, () => service.GetDailyRevenueAsync(range), "Đang tải báo cáo…");
/// using (LoadingOverlay.Begin(_grid, "Đang lưu…")) { await SaveAsync(); }
/// </code></example>
[ToolboxItem(false)]
public sealed class LoadingOverlay : Control, IThemeable
{
    private const string DefaultMessage = "Đang xử lý…";
    private static readonly ConditionalWeakTable<Control, LoadingOverlay> Overlays = new();

    private readonly Control _target;
    private readonly BusyTracker _tracker = BusyTracker.CreateDefault();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _timer = new();
    private Bitmap? _snapshot;
    private Control? _focusBefore;

    private LoadingOverlay(Control target)
    {
        _target = target;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.Selectable,
            true);
        Visible = false;
        TabStop = false;
        Cursor = Cursors.WaitCursor;
        AccessibleRole = AccessibleRole.Animation;
        _timer.Tick += (_, _) => OnTick();
        _target.Resize += (_, _) => Bounds = _target.ClientRectangle;
        _target.Disposed += (_, _) =>
        {
            _timer.Dispose();
            Overlays.Remove(_target);
        };
        _target.Controls.Add(this);
    }

    /// <summary>Runs <paramref name="work"/> with the overlay over <paramref name="target"/>.</summary>
    public static async Task RunAsync(Control target, Func<Task> work, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        using (Begin(target, message))
        {
            await work();
        }
    }

    /// <summary>Runs <paramref name="work"/> with the overlay over <paramref name="target"/> and returns its result.</summary>
    public static async Task<T> RunAsync<T>(Control target, Func<Task<T>> work, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        using (Begin(target, message))
        {
            return await work();
        }
    }

    /// <summary>Marks <paramref name="target"/> busy until the returned handle is disposed (UI thread only).</summary>
    public static IDisposable Begin(Control target, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsDisposed || target.Disposing)
        {
            return NullScope.Instance;
        }

        var overlay = Overlays.GetValue(target, t => new LoadingOverlay(t));
        var id = overlay.BeginOperation(message);
        return new Scope(overlay, id);
    }

    /// <summary>True while <paramref name="target"/> has a running operation (shown or not yet).</summary>
    public static bool IsBusy(Control target) =>
        target is not null && Overlays.TryGetValue(target, out var overlay) && overlay._tracker.IsBusy;

    void IThemeable.ApplyTheme(Theme theme) => Invalidate();

    private long BeginOperation(string? message)
    {
        var id = _tracker.Begin(_clock.Elapsed, message);
        Sync(); // may already be visible (nested call) – refreshes the message
        return id;
    }

    private void EndOperation(long id)
    {
        if (IsDisposed)
        {
            return;
        }

        _tracker.End(id, _clock.Elapsed);
        Sync();
    }

    private void OnTick()
    {
        _tracker.Evaluate(_clock.Elapsed);
        Sync();
        if (Visible)
        {
            Invalidate(); // spinner frame
        }
    }

    /// <summary>Brings the control in line with the tracker and schedules the next check.</summary>
    private void Sync()
    {
        var shouldShow = _tracker.Evaluate(_clock.Elapsed);
        if (shouldShow && !Visible)
        {
            ShowOverlay();
        }
        else if (!shouldShow && Visible)
        {
            HideOverlay();
        }

        AccessibleName = _tracker.Message ?? DefaultMessage;
        if (Visible)
        {
            _timer.Interval = 33; // ~30 fps spinner
            _timer.Start();
            Invalidate();
        }
        else if (_tracker.NextChangeIn(_clock.Elapsed) is { } next)
        {
            _timer.Interval = Math.Max(1, (int)Math.Ceiling(next.TotalMilliseconds));
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void ShowOverlay()
    {
        Bounds = _target.ClientRectangle;
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        TakeSnapshot();
        _focusBefore = FindFocused(_target);
        Visible = true;
        BringToFront();
        if (_focusBefore is not null)
        {
            Focus(); // keeps keystrokes (scanner input, Enter) away from the covered controls
        }
    }

    private void HideOverlay()
    {
        Visible = false;
        _snapshot?.Dispose();
        _snapshot = null;
        if (_focusBefore is { IsDisposed: false, CanFocus: true })
        {
            _focusBefore.Focus();
        }

        _focusBefore = null;
    }

    private void TakeSnapshot()
    {
        _snapshot?.Dispose();
        _snapshot = null;
        if (_target.Width <= 0 || _target.Height <= 0)
        {
            return;
        }

        try
        {
            var bitmap = new Bitmap(_target.Width, _target.Height);
            _target.DrawToBitmap(bitmap, new Rectangle(Point.Empty, _target.Size));
            _snapshot = bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            // Some native controls cannot render to a bitmap; the plain scrim is enough.
        }
    }

    protected override bool IsInputKey(Keys keyData) => true; // swallow Tab/arrows while busy

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = e.SuppressKeyPress = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var c = theme.Colors;
        g.Clear(SurfaceColors.EffectiveBackColor(this, theme));
        if (_snapshot is not null)
        {
            // The snapshot covers the target's whole window (borders included); shift it so its client area lines up.
            var windowOrigin = _target.Parent is { } parent ? parent.PointToScreen(_target.Location) : _target.Location;
            var clientOrigin = _target.PointToScreen(Point.Empty);
            g.DrawImageUnscaled(_snapshot, windowOrigin.X - clientOrigin.X, windowOrigin.Y - clientOrigin.Y);
        }

        var scrim = theme.IsDark ? Color.FromArgb(150, 0, 0, 0) : Color.FromArgb(150, c.Background);
        using (var brush = new SolidBrush(scrim))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        g.UseHighQuality();
        var message = _tracker.Message ?? DefaultMessage;
        var font = ThemeFonts.Get(FontRole.Body, FontStyle.Bold);
        var pad = this.ScaleDpi(20);
        var spinner = this.ScaleDpi(36);
        var maxText = Math.Max(this.ScaleDpi(120), Math.Min(this.ScaleDpi(320), Width - (4 * pad)));
        var textSize = TextRenderer.MeasureText(message, font, new Size(maxText, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var cardWidth = Math.Max(spinner, textSize.Width) + (2 * pad);
        var cardHeight = pad + spinner + this.ScaleDpi(12) + textSize.Height + pad;
        var card = new Rectangle((Width - cardWidth) / 2, (Height - cardHeight) / 2, cardWidth, cardHeight);

        g.FillRoundedRectangle(c.Surface, card, this.ScaleDpi(theme.Spacing.RadiusLg));
        g.DrawRoundedRectangle(c.Border, card, this.ScaleDpi(theme.Spacing.RadiusLg));

        var ring = new Rectangle(card.Left + ((card.Width - spinner) / 2), card.Top + pad, spinner, spinner);
        var stroke = Math.Max(2f, this.ScaleDpi(4));
        var inner = RectangleF.Inflate(ring, -stroke / 2f, -stroke / 2f);
        using (var track = new Pen(ColorMath.Blend(c.Surface, c.Primary, 0.18), stroke))
        {
            g.DrawEllipse(track, inner);
        }

        var (start, sweep) = SpinnerGeometry.ArcAt(_clock.Elapsed);
        using (var arc = new Pen(c.Primary, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawArc(arc, inner, start - 90f, sweep);
        }

        var textBounds = new Rectangle(card.Left + pad, ring.Bottom + this.ScaleDpi(12), card.Width - (2 * pad), textSize.Height);
        TextRenderer.DrawText(g, message, font, textBounds, ColorMath.EnsureContrast(c.TextPrimary, c.Surface),
            TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _snapshot?.Dispose();
            _snapshot = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>The focused control inside <paramref name="root"/>, so focus can return there afterwards.</summary>
    private static Control? FindFocused(Control root)
    {
        Control? active = root.FindForm()?.ActiveControl;
        while (active is ContainerControl { ActiveControl: { } inner })
        {
            active = inner;
        }

        return active is not null && active != root && root.Contains(active) ? active : null;
    }

    private sealed class Scope : IDisposable
    {
        private LoadingOverlay? _overlay;
        private readonly long _id;

        public Scope(LoadingOverlay overlay, long id)
        {
            _overlay = overlay;
            _id = id;
        }

        public void Dispose()
        {
            var overlay = Interlocked.Exchange(ref _overlay, null);
            if (overlay is null)
            {
                return;
            }

            if (overlay.InvokeRequired)
            {
                overlay.BeginInvoke(() => overlay.EndOperation(_id));
            }
            else
            {
                overlay.EndOperation(_id);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
