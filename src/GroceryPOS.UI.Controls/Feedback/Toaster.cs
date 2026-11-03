using System.Diagnostics;
using System.Runtime.CompilerServices;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Feedback.Core;

namespace GroceryPOS.UI.Controls.Feedback;

/// <summary>
/// Non-blocking toast notifications ("Đã lưu sản phẩm", "Đã xuất Excel – Mở file"). Toasts stack in a
/// corner of the window that contains the given control, disappear on their own (hovering pauses them),
/// and identical toasts are merged into one with a "×n" counter. Use them for results the user does
/// not have to acknowledge; anything that needs a decision belongs in <see cref="ModernDialog"/>.
/// </summary>
/// <example><code>
/// Toaster.Success(this, "Đã lưu sản phẩm SUA01.");
/// Toaster.Show(this, new ToastRequest(FeedbackKind.Success, $"Đã xuất {name}", "Xuất Excel", ActionText: "Mở file"), () => Open(path));
/// </code></example>
public static class Toaster
{
    private static readonly ConditionalWeakTable<Form, ToastHost> Hosts = new();

    /// <summary>Where toasts appear in new windows.</summary>
    public static ToastPosition Position { get; set; } = ToastPosition.BottomRight;

    public static void Success(Control owner, string message, string? title = null) =>
        Show(owner, new ToastRequest(FeedbackKind.Success, message, title));

    public static void Info(Control owner, string message, string? title = null) =>
        Show(owner, new ToastRequest(FeedbackKind.Info, message, title));

    public static void Warning(Control owner, string message, string? title = null) =>
        Show(owner, new ToastRequest(FeedbackKind.Warning, message, title));

    public static void Error(Control owner, string message, string? title = null) =>
        Show(owner, new ToastRequest(FeedbackKind.Error, message, title));

    /// <summary>
    /// Shows a toast in the window that hosts <paramref name="owner"/> (or the active window when the control
    /// is not on a form yet). <paramref name="onAction"/> runs when the action link is clicked.
    /// Safe to call from any thread. Returns null when there is no window to show it in.
    /// </summary>
    public static Toast? Show(Control owner, ToastRequest request, Action? onAction = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        if (owner.InvokeRequired)
        {
            return (Toast?)owner.Invoke(() => Show(owner, request, onAction));
        }

        var form = owner as Form ?? owner.FindForm() ?? Form.ActiveForm;
        if (form is null || form.IsDisposed)
        {
            return null;
        }

        var host = Hosts.GetValue(form, f => new ToastHost(f));
        return host.Show(request, onAction);
    }

    /// <summary>Removes every toast from the window that hosts <paramref name="owner"/> (e.g. on sign-out).</summary>
    public static void Clear(Control owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var form = owner as Form ?? owner.FindForm();
        if (form is not null && Hosts.TryGetValue(form, out var host))
        {
            host.Clear();
        }
    }

    /// <summary>Owns the queue and the toast controls of one window.</summary>
    private sealed class ToastHost
    {
        private const int MarginDesign = 16;
        private const int GapDesign = 8;
        private const int TickMs = 40;

        private readonly Form _form;
        private readonly ToastQueue _queue = new();
        private readonly Dictionary<long, ToastView> _views = new();
        private readonly Dictionary<long, Action> _actions = new();
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = TickMs };
        private readonly Stopwatch _clock = new();
        private int _laidOutVersion = -1;

        public ToastHost(Form form)
        {
            _form = form;
            _timer.Tick += (_, _) => Tick();
            _form.Resize += (_, _) => Layout(force: true);
            _form.DpiChanged += (_, _) => Layout(force: true);
            _form.Disposed += (_, _) =>
            {
                _timer.Dispose();
                Hosts.Remove(_form);
            };
        }

        public Toast Show(ToastRequest request, Action? onAction)
        {
            var toast = _queue.Enqueue(request);
            if (onAction is not null)
            {
                _actions[toast.Id] = onAction;
            }

            Sync();
            if (!_timer.Enabled)
            {
                _clock.Restart();
                _timer.Start();
            }

            return toast;
        }

        public void Clear()
        {
            _queue.Clear();
            _actions.Clear();
            Sync();
        }

        private void Tick()
        {
            var elapsed = _clock.Elapsed;
            _clock.Restart();
            _queue.IsPaused = IsPointerOverToasts();
            foreach (var expired in _queue.Advance(elapsed))
            {
                _actions.Remove(expired.Id);
            }

            Sync();
            foreach (var view in _views.Values)
            {
                view.Invalidate(); // progress line
            }

            if (_queue.Visible.Count == 0)
            {
                _timer.Stop();
            }
        }

        private void Dismiss(long id)
        {
            _queue.Dismiss(id);
            _actions.Remove(id);
            Sync();
        }

        /// <summary>Creates views for newly visible toasts, removes views of gone ones and re-lays out.</summary>
        private void Sync()
        {
            if (_form.IsDisposed)
            {
                return;
            }

            var visibleIds = _queue.Visible.Select(t => t.Id).ToHashSet();
            foreach (var id in _views.Keys.Where(id => !visibleIds.Contains(id)).ToList())
            {
                var view = _views[id];
                _views.Remove(id);
                _form.Controls.Remove(view);
                view.Dispose();
            }

            foreach (var toast in _queue.Visible)
            {
                if (_views.ContainsKey(toast.Id))
                {
                    continue;
                }

                var view = new ToastView(toast) { Visible = false };
                var id = toast.Id;
                view.CloseClicked += (_, _) => Dismiss(id);
                view.ActionClicked += (_, _) =>
                {
                    var action = _actions.GetValueOrDefault(id);
                    Dismiss(id);
                    action?.Invoke();
                };
                _views.Add(toast.Id, view);
                _form.Controls.Add(view);
            }

            Layout(force: false);
        }

        private void Layout(bool force)
        {
            if (!force && _laidOutVersion == _queue.Version)
            {
                return;
            }

            _laidOutVersion = _queue.Version;
            if (_views.Count == 0 || _form.WindowState == FormWindowState.Minimized)
            {
                return;
            }

            var client = _form.ClientSize;
            var margin = _form.ScaleDpi(MarginDesign);
            var views = _queue.Visible.Select(t => _views[t.Id]).ToList();
            var width = ToastLayout.Width(client.Width, views[0].PreferredWidth, margin, _form.ScaleDpi(200));
            var sizes = views.Select(v => new Size(width, v.MeasureHeight(width))).ToList();
            var bounds = ToastLayout.Arrange(client, sizes, Position, margin, _form.ScaleDpi(GapDesign));

            _form.SuspendLayout();
            for (var i = 0; i < views.Count; i++)
            {
                var view = views[i];
                view.UpdateContent(contentChanged: true);
                if (bounds[i].IsEmpty)
                {
                    view.Visible = false;
                    continue;
                }

                view.Bounds = bounds[i];
                view.Visible = true;
                view.BringToFront();
            }

            _form.ResumeLayout(false);
        }

        private bool IsPointerOverToasts()
        {
            if (_views.Count == 0 || !_form.Visible)
            {
                return false;
            }

            var point = _form.PointToClient(Control.MousePosition);
            return _views.Values.Any(v => v.Visible && v.Bounds.Contains(point));
        }
    }
}
