using FluentValidation;
using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Users;
using GroceryPOS.Application.Validation;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Sets a temporary password for another account (pre-filled with a random one, ↻ makes a new one). The account is
/// unlocked and must change the password at the next sign-in.
/// </summary>
internal sealed class ResetPasswordForm : BaseEditDialog
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UserDto _user;
    private readonly ModernTextBox _password;

    public ResetPasswordForm(IServiceScopeFactory scopeFactory, UserDto user)
        : base("Đặt lại mật khẩu", contentWidth: 376)
    {
        _scopeFactory = scopeFactory;
        _user = user;
        SaveText = "Đặt lại";
        ConfirmDiscard = false; // nothing typed by the user worth keeping: the password is generated

        AddField("Tài khoản", new ModernTextBox { Text = $"{user.Username} – {user.FullName}", ReadOnly = true, TabStop = false, IconGlyph = Glyphs.Contact }, track: false);
        _password = AddField("Mật khẩu tạm mới", new ModernTextBox
        {
            Text = PasswordPolicy.GenerateTemporary(),
            IconGlyph = Glyphs.Lock,
            TrailingGlyph = Glyphs.Refresh,
            HelperText = "Người dùng phải đổi mật khẩu này khi đăng nhập.",
        }, track: false);
        _password.TrailingIconClick += (_, _) => _password.Text = PasswordPolicy.GenerateTemporary();
    }

    protected override async Task<string?> SaveAsync()
    {
        var request = new ResetPasswordRequest(_password.Text, _password.Text);
        using var scope = _scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IValidator<ResetPasswordRequest>>().Check(request) is { IsFailure: true } invalid)
        {
            return invalid.ErrorMessageOrNull();
        }

        var result = await scope.ServiceProvider.GetRequiredService<IUserManagementService>().ResetPasswordAsync(_user.Id, _password.Text);
        if (result.IsFailure)
        {
            return result.Error.Message;
        }

        ShowInfo($"Mật khẩu tạm của \"{_user.Username}\": {_password.Text}\n\nTài khoản đã được mở khóa (nếu đang bị khóa).", Text);
        return null;
    }
}
