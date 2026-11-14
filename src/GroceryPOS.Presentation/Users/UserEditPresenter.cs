using FluentValidation;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Users;

/// <summary>
/// The add/edit user dialog as seen by its presenter. Field names are <see cref="CreateUserRequest"/> property names
/// (<c>Username</c>, <c>FullName</c>, <c>RoleId</c>, <c>Password</c>).
/// </summary>
public interface IUserEditView : IView
{
    string Username { get; }

    string FullName { get; }

    /// <summary>The selected role, or null when nothing is selected.</summary>
    int? RoleId { get; }

    /// <summary>The temporary password (add only).</summary>
    string Password { get; }

    /// <summary>Fills the role list and selects <paramref name="selectedRoleId"/>; filling it is not an edit by the user.</summary>
    void ShowRoles(IReadOnlyList<RoleOptionDto> roles, int? selectedRoleId);

    /// <summary>Moves focus to the field for <paramref name="field"/>.</summary>
    void FocusField(string field);
}

/// <summary>
/// Add/edit user logic. On add, a temporary password is pre-filled and shown once saved so it can be handed over;
/// the user must change it at the first sign-in. On edit, the username is read-only and only name and role change.
/// The role list only offers roles the signed-in user may assign (plus the account's current role, so it still shows).
/// </summary>
public sealed class UserEditPresenter : Presenter<IUserEditView>
{
    public UserEditPresenter(IServiceScopeFactory scopeFactory)
        : base(scopeFactory)
    {
    }

    /// <summary>The account being edited, or null when adding.</summary>
    public UserDto? User { get; private set; }

    public bool IsNew => User is null;

    public string Title => User is null ? "Thêm người dùng" : $"Sửa người dùng – {User.Username}";

    /// <summary>Nobody changes their own role (it could lock themselves out of this screen).</summary>
    public bool RoleLocked => User is { IsCurrentUser: true };

    public string? RoleHelperText => RoleLocked ? "Bạn không thể tự đổi vai trò của mình." : null;

    public string? UsernameHelperText => IsNew ? "Chữ không dấu, số và . _ - (3–50 ký tự)" : null;

    /// <summary>Chooses what the dialog edits; call before the view is built.</summary>
    public UserEditPresenter For(UserDto? user)
    {
        User = user;
        return this;
    }

    /// <summary>A fresh random temporary password (pre-filled on add, ↻ makes a new one).</summary>
    public static string NewTemporaryPassword() => PasswordPolicy.GenerateTemporary();

    /// <summary>Roles the signed-in user may assign, plus <paramref name="currentRoleId"/> even when it is out of reach.</summary>
    public static IReadOnlyList<RoleOptionDto> SelectableRoles(IEnumerable<RoleOptionDto> roles, int? currentRoleId)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles.Where(r => r.CanAssign || r.Id == currentRoleId).ToList();
    }

    public static string CreatedMessage(string username, string password) =>
        $"Đã tạo tài khoản \"{username}\".\nMật khẩu tạm: {password}\n\nHãy đưa mật khẩu này cho người dùng; họ sẽ phải đổi ở lần đăng nhập đầu tiên.";

    /// <summary>Loads the role list into the view; returns the message for the error banner, or null.</summary>
    public async Task<string?> LoadRolesAsync()
    {
        var result = await UseAsync<IUserManagementService, Result<IReadOnlyList<RoleOptionDto>>>(
            users => users.ListRoleOptionsAsync());
        if (!ViewAlive)
        {
            return null;
        }

        if (result.IsFailure)
        {
            return result.Error.Message;
        }

        View.ShowRoles(SelectableRoles(result.Value, User?.RoleId), User?.RoleId);
        return null;
    }

    /// <summary>Validates and saves; returns the message for the dialog's error banner, or null when saved.</summary>
    public Task<string?> SaveAsync() => UseScopeAsync<string?>(async services =>
    {
        var users = services.GetRequiredService<IUserManagementService>();
        var roleId = View.RoleId ?? 0;
        var fullName = View.FullName.Trim();

        if (User is null)
        {
            var password = View.Password;
            var create = new CreateUserRequest(View.Username.Trim(), fullName, roleId, password, password);
            if (FormInput.FirstError(services.GetRequiredService<IValidator<CreateUserRequest>>(), create) is { } invalid)
            {
                View.FocusField(invalid.Field);
                return invalid.Message;
            }

            var created = await users.CreateAsync(create);
            if (created.IsFailure)
            {
                return created.Error.Message;
            }

            if (ViewAlive)
            {
                View.ShowInfo(CreatedMessage(created.Value.Username, password), Title);
            }

            return null;
        }

        var update = new UpdateUserRequest(fullName, roleId);
        if (FormInput.FirstError(services.GetRequiredService<IValidator<UpdateUserRequest>>(), update) is { } invalidUpdate)
        {
            View.FocusField(invalidUpdate.Field);
            return invalidUpdate.Message;
        }

        var updated = await users.UpdateAsync(User.Id, update);
        return updated.IsSuccess ? null : updated.Error.Message;
    });
}
