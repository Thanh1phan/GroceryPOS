using System.ComponentModel;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Forms;

/// <summary>
/// Base class for every window of the app: follows the current theme (light/dark switches live), uses the
/// theme's body font, can close on Esc, and offers the shared feedback helpers – loading overlay, themed
/// message boxes and toasts – so derived forms never call <c>MessageBox</c> or wire the theme themselves.
/// </summary>
public class BaseForm : Form
{
    private bool _themeTracked;

    public BaseForm()
    {
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterParent;
        Font = ThemeFonts.Get(FontRole.Body);
        BackColor = ThemeManager.Current.Colors.Background;
        ForeColor = ThemeManager.Current.Colors.TextPrimary;
    }

    /// <summary>When true, Esc closes the form with <see cref="DialogResult.Cancel"/> (dialogs, viewers).</summary>
    [Category("Behavior")]
    [DefaultValue(false)]
    public bool CloseOnEscape { get; set; }

    /// <summary>False once the form is closing or disposed; check after every <c>await</c> before touching controls.</summary>
    protected bool IsAlive => !IsDisposed && !Disposing;

    /// <summary>True while an operation started with <see cref="RunBusyAsync(Func{Task}, string?)"/> is running.</summary>
    protected bool IsBusy => LoadingOverlay.IsBusy(this);

    protected override void OnLoad(EventArgs e)
    {
        if (!DesignMode && !_themeTracked)
        {
            _themeTracked = true;
            ThemeManager.Track(this);
        }

        base.OnLoad(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (CloseOnEscape && keyData == Keys.Escape && !IsBusy)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Runs <paramref name="work"/> under a loading overlay (shown only when it takes longer than a moment).</summary>
    protected Task RunBusyAsync(Func<Task> work, string? message = null) => LoadingOverlay.RunAsync(this, work, message);

    protected Task<T> RunBusyAsync<T>(Func<Task<T>> work, string? message = null) => LoadingOverlay.RunAsync(this, work, message);

    protected void ShowInfo(string message, string title = "Thông báo") => ModernDialog.Info(this, message, title);

    protected void ShowWarning(string message, string title = "Cảnh báo") => ModernDialog.Warning(this, message, title);

    protected void ShowError(string message, string title = "Lỗi", string? details = null) => ModernDialog.Error(this, message, title, details);

    protected bool Confirm(string message, string title, string confirmText = "Đồng ý", string cancelText = "Hủy", bool danger = false) =>
        ModernDialog.Confirm(this, message, title, confirmText, cancelText, danger);

    protected void ToastSuccess(string message, string? title = null) => Toaster.Success(this, message, title);

    protected void ToastInfo(string message, string? title = null) => Toaster.Info(this, message, title);

    protected void ToastWarning(string message, string? title = null) => Toaster.Warning(this, message, title);

    protected void ToastError(string message, string? title = null) => Toaster.Error(this, message, title);
}
