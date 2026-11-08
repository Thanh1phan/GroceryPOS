using GroceryPOS.UI.Controls.Feedback;

namespace GroceryPOS.UI.Controls.Forms;

/// <summary>
/// Base class for the screens hosted in the main window (and for larger composite panels): fills its host,
/// loads its data once when first shown, reloads on F5, and offers the same feedback helpers as
/// <see cref="BaseForm"/>. Theming comes from the host form, which tracks the theme for all its children.
/// </summary>
public class BaseUserControl : UserControl
{
    private bool _loaded;

    public BaseUserControl()
    {
        Dock = DockStyle.Fill;
    }

    /// <summary>False once the screen is closing or disposed (the user navigated away); check after every <c>await</c>.</summary>
    protected bool IsAlive => !IsDisposed && !Disposing;

    /// <summary>True when the screen reloads on F5 (default). Screens with their own F5 handling turn it off.</summary>
    protected bool RefreshOnF5 { get; set; } = true;

    protected bool IsBusy => LoadingOverlay.IsBusy(this);

    /// <summary>(Re)loads the screen's data. Called on first show and on F5.</summary>
    public virtual Task RefreshDataAsync() => Task.CompletedTask;

    /// <summary>Called once, the first time the screen is shown. Default: <see cref="RefreshDataAsync"/>.</summary>
    protected virtual Task OnFirstLoadAsync() => RefreshDataAsync();

    /// <summary>
    /// Fire-and-forget reload (after a dialog closed, after a toggle). Exceptions are not lost: they surface
    /// through the WinForms synchronization context to the app's global exception handler.
    /// </summary>
    protected async void RequestRefresh()
    {
        if (IsAlive)
        {
            await RefreshDataAsync();
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (DesignMode || _loaded)
        {
            return;
        }

        _loaded = true;
        await OnFirstLoadAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (RefreshOnF5 && keyData == Keys.F5 && !IsBusy)
        {
            RequestRefresh();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

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
