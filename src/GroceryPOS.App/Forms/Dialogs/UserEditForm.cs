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
/// Add/edit dialog for a user account. On add, a temporary password is pre-filled (shown so it can be handed over);
/// the user must change it at the first sign-in. On edit, the username is read-only and only name and role change.
/// The role list only offers roles the signed-in user may assign.
/// </summary>
internal sealed class UserEditForm : ModernDialogForm
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UserDto? _user;
    private readonly ModernTextBox _username;
    private readonly ModernTextBox _fullName;
    private readonly ModernComboBox _role;
    private readonly ModernTextBox? _password;
    private readonly ModernButton _save;

    public UserEditForm(IServiceScopeFactory scopeFactory, UserDto? user)
    {
        _scopeFactory = scopeFactory;
        _user = user;

        Text = user is null ? "Thêm người dùng" : "Sửa người dùng";
        ClientSize = new Size(480, user is null ? 420 : 340);

        const int left = 32;
        const int width = 416;

        Label Caption(string text, int y) => new() { Text = text, AutoSize = true, Location = new Point(left, y) };

        _username = new ModernTextBox
        {
            Location = new Point(left, 40),
            Width = width,
            Text = user?.Username ?? string.Empty,
            ReadOnly = user is not null,
            PlaceholderText = "vd: thungan2",
            IconGlyph = Glyphs.Contact,
            HelperText = user is null ? "Chữ không dấu, số và . _ - (3–50 ký tự)" : null,
        };
        _fullName = new ModernTextBox { Location = new Point(left, 120), Width = width, Text = user?.FullName ?? string.Empty, PlaceholderText = "Họ và tên" };
        _role = new ModernComboBox
        {
            Location = new Point(left, 200),
            Width = width,
            DisplayMember = nameof(RoleOptionDto.Name),
            ValueMember = nameof(RoleOptionDto.Id),
            Enabled = user is not { IsCurrentUser: true },
            HelperText = user is { IsCurrentUser: true } ? "Bạn không thể tự đổi vai trò của mình." : null,
        };

        Controls.AddRange([Caption("Tên đăng nhập", 16), _username, Caption("Họ tên", 96), _fullName, Caption("Vai trò", 176), _role]);

        var bottom = 260;
        if (user is null)
        {
            _password = new ModernTextBox
            {
                Location = new Point(left, 280),
                Width = width,
                Text = PasswordPolicy.GenerateTemporary(),
                IconGlyph = Glyphs.Lock,
                TrailingGlyph = Glyphs.Refresh,
                HelperText = "Mật khẩu tạm – người dùng phải đổi ở lần đăng nhập đầu tiên.",
            };
            _password.TrailingIconClick += (_, _) => _password.Text = PasswordPolicy.GenerateTemporary();
            Controls.AddRange([Caption("Mật khẩu tạm", 256), _password]);
            bottom = 340;
        }

        ErrorLabel.Location = new Point(left, bottom);
        ErrorLabel.Size = new Size(240, 64);

        _save = new ModernButton
        {
            Text = "Lưu",
            Variant = ButtonVariant.Primary,
            Location = new Point(left + width - 160, bottom + 8),
            Size = new Size(160, 40),
        };
        _save.Click += async (_, _) => await SaveAsync();
        AcceptButton = _save;
        Controls.Add(_save);

        Load += async (_, _) => await LoadRolesAsync();
    }

    private async Task LoadRolesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IUserManagementService>().ListRoleOptionsAsync();
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            ErrorLabel.Text = result.Error.Message;
            ErrorLabel.Visible = true;
            return;
        }

        // Keep the user's current role in the list even when it is out of reach, so it still shows.
        _role.DataSource = result.Value.Where(r => r.CanAssign || r.Id == _user?.RoleId).ToList();
        _role.SelectedValue = _user?.RoleId;
    }

    private async Task SaveAsync() => await RunSaveAsync(_save, async () =>
    {
        var roleId = _role.SelectedValue is int id ? id : 0;
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserManagementService>();

        if (_user is null)
        {
            var request = new CreateUserRequest(_username.Text, _fullName.Text.Trim(), roleId, _password!.Text, _password.Text);
            if (scope.ServiceProvider.GetRequiredService<IValidator<CreateUserRequest>>().Check(request) is { IsFailure: true } invalid)
            {
                return invalid;
            }

            var created = await users.CreateAsync(request);
            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            MessageBox.Show(this,
                $"Đã tạo tài khoản \"{created.Value.Username}\".\nMật khẩu tạm: {_password.Text}\n\nHãy đưa mật khẩu này cho người dùng; họ sẽ phải đổi ở lần đăng nhập đầu tiên.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Result.Success();
        }

        var update = new UpdateUserRequest(_fullName.Text.Trim(), roleId);
        if (scope.ServiceProvider.GetRequiredService<IValidator<UpdateUserRequest>>().Check(update) is { IsFailure: true } invalidUpdate)
        {
            return invalidUpdate;
        }

        var updated = await users.UpdateAsync(_user.Id, update);
        return updated.IsSuccess ? Result.Success() : Result.Failure(updated.Error);
    });
}
