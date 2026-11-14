using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Users;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Sets a temporary password for another account (pre-filled with a random one, ↻ makes a new one) – passive MVP
/// view of <see cref="ResetPasswordPresenter"/>.
/// </summary>
internal sealed class ResetPasswordForm : BaseEditDialog, IResetPasswordView
{
    private readonly ResetPasswordPresenter _presenter;
    private readonly ModernTextBox _password;

    public ResetPasswordForm(ResetPasswordPresenter presenter)
        : base(ResetPasswordPresenter.Title, contentWidth: 376)
    {
        _presenter = presenter;
        SaveText = "Đặt lại";
        ConfirmDiscard = false; // nothing typed by the user worth keeping: the password is generated

        AddField("Tài khoản", new ModernTextBox { Text = presenter.AccountText, ReadOnly = true, TabStop = false, IconGlyph = Glyphs.Contact }, track: false);
        _password = AddField("Mật khẩu tạm mới", new ModernTextBox
        {
            Text = UserEditPresenter.NewTemporaryPassword(),
            IconGlyph = Glyphs.Lock,
            TrailingGlyph = Glyphs.Refresh,
            HelperText = "Người dùng phải đổi mật khẩu này khi đăng nhập.",
        }, track: false);
        _password.TrailingIconClick += (_, _) => _password.Text = UserEditPresenter.NewTemporaryPassword();

        _presenter.Attach(this);
    }

    string IResetPasswordView.Password => _password.Text;

    bool IView.IsAlive => IsAlive;

    protected override Task<string?> SaveAsync() => _presenter.SaveAsync();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
