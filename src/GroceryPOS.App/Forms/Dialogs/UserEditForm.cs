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
/// Add/edit dialog for a user account. On add, a temporary password is pre-filled (shown so it can be handed over);
/// the user must change it at the first sign-in. On edit, the username is read-only and only name and role change.
/// The role list only offers roles the signed-in user may assign.
/// </summary>
internal sealed class UserEditForm : BaseEditDialog
{
    private const string RoleKey = "role";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UserDto? _user;
    private readonly ModernTextBox _username;
    private readonly ModernTextBox _fullName;
    private readonly ModernComboBox _role;
    private readonly ModernTextBox? _password;

    public UserEditForm(IServiceScopeFactory scopeFactory, UserDto? user)
        : base(user is null ? "Thêm người dùng" : "Sửa người dùng", contentWidth: 416)
    {
        _scopeFactory = scopeFactory;
        _user = user;

        _username = AddField("Tên đăng nhập", new ModernTextBox
        {
            Text = user?.Username ?? string.Empty,
            ReadOnly = user is not null,
            PlaceholderText = "vd: thungan2",
            IconGlyph = Glyphs.Contact,
            HelperText = user is null ? "Chữ không dấu, số và . _ - (3–50 ký tự)" : null,
        });
        _fullName = AddField("Họ tên", new ModernTextBox { Text = user?.FullName ?? string.Empty, PlaceholderText = "Họ và tên" });
        _role = AddField("Vai trò", new ModernComboBox
        {
            DisplayMember = nameof(RoleOptionDto.Name),
            ValueMember = nameof(RoleOptionDto.Id),
            Enabled = user is not { IsCurrentUser: true },
            HelperText = user is { IsCurrentUser: true } ? "Bạn không thể tự đổi vai trò của mình." : null,
        }, trackAs: RoleKey);

        if (user is null)
        {
            _password = AddField("Mật khẩu tạm", new ModernTextBox
            {
                Text = PasswordPolicy.GenerateTemporary(),
                IconGlyph = Glyphs.Lock,
                TrailingGlyph = Glyphs.Refresh,
                HelperText = "Mật khẩu tạm – người dùng phải đổi ở lần đăng nhập đầu tiên.",
            }, track: false);
            _password.TrailingIconClick += (_, _) => _password.Text = PasswordPolicy.GenerateTemporary();
        }

        Load += async (_, _) => await LoadRolesAsync();
    }

    private async Task LoadRolesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IUserManagementService>().ListRoleOptionsAsync();
        if (!IsAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            ShowFormError(result.Error.Message);
            return;
        }

        // Keep the user's current role in the list even when it is out of reach, so it still shows.
        _role.DataSource = result.Value.Where(r => r.CanAssign || r.Id == _user?.RoleId).ToList();
        _role.SelectedValue = _user?.RoleId;
        Changes.AcceptChange(RoleKey); // filling the list is not an edit by the user
    }

    protected override async Task<string?> SaveAsync()
    {
        var roleId = _role.SelectedValue is int id ? id : 0;
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserManagementService>();

        if (_user is null)
        {
            var request = new CreateUserRequest(_username.Text, _fullName.Text.Trim(), roleId, _password!.Text, _password.Text);
            if (scope.ServiceProvider.GetRequiredService<IValidator<CreateUserRequest>>().Check(request) is { IsFailure: true } invalid)
            {
                return invalid.ErrorMessageOrNull();
            }

            var created = await users.CreateAsync(request);
            if (created.IsFailure)
            {
                return created.Error.Message;
            }

            ShowInfo(
                $"Đã tạo tài khoản \"{created.Value.Username}\".\nMật khẩu tạm: {_password.Text}\n\nHãy đưa mật khẩu này cho người dùng; họ sẽ phải đổi ở lần đăng nhập đầu tiên.",
                Text);
            return null;
        }

        var update = new UpdateUserRequest(_fullName.Text.Trim(), roleId);
        if (scope.ServiceProvider.GetRequiredService<IValidator<UpdateUserRequest>>().Check(update) is { IsFailure: true } invalidUpdate)
        {
            return invalidUpdate.ErrorMessageOrNull();
        }

        var updated = await users.UpdateAsync(_user.Id, update);
        return updated.ErrorMessageOrNull();
    }
}
