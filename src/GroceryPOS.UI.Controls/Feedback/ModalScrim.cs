using GroceryPOS.UI.Controls.Feedback.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Feedback;

/// <summary>
/// Dims the owner window while a modal dialog is open, so it is obvious the dialog needs an answer first.
/// It is a borderless, semi-transparent, non-activating window laid exactly over the owner.
/// </summary>
public sealed class ModalScrim : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private readonly Form _target;

    private ModalScrim(Form target)
    {
        _target = target;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        var scrim = FeedbackPalette.Scrim(ThemeManager.Current);
        BackColor = Color.FromArgb(255, scrim.R, scrim.G, scrim.B);
        Opacity = scrim.A / 255d;
        Bounds = target.Bounds;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow;
            return cp;
        }
    }

    /// <summary>
    /// Shows <paramref name="dialog"/> modally over a dimmed <paramref name="owner"/>. Falls back to a plain
    /// <see cref="Form.ShowDialog(IWin32Window)"/> when the owner window is hidden, minimised or not a form.
    /// </summary>
    public static DialogResult ShowDialog(IWin32Window? owner, Form dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var ownerForm = ResolveOwner(owner);
        if (ownerForm is null || !ownerForm.Visible || ownerForm.WindowState == FormWindowState.Minimized || ownerForm.Opacity < 1)
        {
            return ownerForm is null ? dialog.ShowDialog() : dialog.ShowDialog(ownerForm);
        }

        using var scrim = new ModalScrim(ownerForm);
        scrim.Show(ownerForm);
        try
        {
            if (dialog.StartPosition == FormStartPosition.CenterParent)
            {
                // CenterParent is relative to the owner, not the scrim window.
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = CenterOver(ownerForm.Bounds, dialog.Size);
            }

            return dialog.ShowDialog(ownerForm);
        }
        finally
        {
            scrim.Close();
            if (!ownerForm.IsDisposed)
            {
                ownerForm.Activate();
            }
        }
    }

    internal static Form? ResolveOwner(IWin32Window? owner) => owner switch
    {
        Form form => form,
        Control control => control.FindForm(),
        _ => Form.ActiveForm,
    };

    internal static Point CenterOver(Rectangle area, Size size)
    {
        var screen = Screen.FromRectangle(area).WorkingArea;
        var x = area.Left + ((area.Width - size.Width) / 2);
        var y = area.Top + ((area.Height - size.Height) / 2);
        x = Math.Clamp(x, screen.Left, Math.Max(screen.Left, screen.Right - size.Width));
        y = Math.Clamp(y, screen.Top, Math.Max(screen.Top, screen.Bottom - size.Height));
        return new Point(x, y);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _target.Move += FollowTarget;
        _target.Resize += FollowTarget;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _target.Move -= FollowTarget;
        _target.Resize -= FollowTarget;
        base.OnFormClosed(e);
    }

    private void FollowTarget(object? sender, EventArgs e) => Bounds = _target.Bounds;
}
