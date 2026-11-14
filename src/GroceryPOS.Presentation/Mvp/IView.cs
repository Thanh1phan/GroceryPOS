namespace GroceryPOS.Presentation.Mvp;

/// <summary>
/// What every MVP view can do for its presenter: user feedback only. Concrete views add their own
/// members (show items, open an editor, focus a field). Implemented by WinForms screens and dialogs,
/// and by plain fakes in tests – presenters never touch WinForms.
/// </summary>
public interface IView
{
    /// <summary>False once the view is closed or disposed; presenters stop updating it after awaits.</summary>
    bool IsAlive { get; }

    void ShowWarning(string message, string title);

    /// <summary>Shows an information box the user must acknowledge (e.g. a generated temporary password).</summary>
    void ShowInfo(string message, string title);

    /// <summary>
    /// Asks a yes/no question; true when the user chose <paramref name="confirmText"/>. <paramref name="danger"/>
    /// paints the confirm button red (actions that lock someone out or lose money).
    /// </summary>
    bool Confirm(string message, string title, string confirmText, bool danger = false);

    void ToastSuccess(string message, string? title = null);

    void ToastError(string message, string? title = null);
}
