using FluentValidation;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.Application.Validation;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Sets a temporary password for another account (pre-filled with a random one, ↻ makes a new one). The account is
/// unlocked and must change the password at the next sign-in.
/// </summary>
internal sealed class ResetPasswordForm : ModernDialogForm
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UserDto _user;
    private readonly ModernTextBox _password;
    private readonly ModernButton _save;

    public ResetPasswordForm(IServiceScopeFactory scopeFactory, UserDto user)
    {
        _scopeFactory = scopeFactory;
        _user = user;

        Text = "Đặt lại mật khẩu";
        ClientSize = new Size(440, 250);

        var info = new Label
        {
            Text = $"Tài khoản: {user.Username} – {user.FullName}",
            AutoSize = true,
            Location = new Point(32, 16),
        };
        var caption = new Label { Text = "Mật khẩu tạm mới", AutoSize = true, Location = new Point(32, 52) };
        _password = new ModernTextBox
        {
            Location = new Point(32, 76),
            Width = 376,
            Text = PasswordPolicy.GenerateTemporary(),
            IconGlyph = Glyphs.Lock,
            TrailingGlyph = Glyphs.Refresh,
            HelperText = "Người dùng phải đổi mật khẩu này khi đăng nhập.",
        };
        _password.TrailingIconClick += (_, _) => _password.Text = PasswordPolicy.GenerateTemporary();

        ErrorLabel.Location = new Point(32, 150);
        ErrorLabel.Size = new Size(200, 64);

        _save = new ModernButton
        {
            Text = "Đặt lại",
            Variant = ButtonVariant.Primary,
            Location = new Point(248, 186),
            Size = new Size(160, 40),
        };
        _save.Click += async (_, _) => await SaveAsync();
        AcceptButton = _save;

        Controls.AddRange([info, caption, _password, _save]);
    }

    private async Task SaveAsync() => await RunSaveAsync(_save, async () =>
    {
        var request = new ResetPasswordRequest(_password.Text, _password.Text);
        using var scope = _scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IValidator<ResetPasswordRequest>>().Check(request) is { IsFailure: true } invalid)
        {
            return invalid;
        }

        var result = await scope.ServiceProvider.GetRequiredService<IUserManagementService>().ResetPasswordAsync(_user.Id, _password.Text);
        if (result.IsSuccess)
        {
            MessageBox.Show(this, $"Mật khẩu tạm của \"{_user.Username}\": {_password.Text}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        return result;
    });
}
